// JOSCRAFT multiplayer – authoritative host logic. Runs inside the host's game process on the main thread.
// NAVRH: runtime/Net/NetServer.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Per-player record persisted in the HOST save (SavedGame.RemotePlayers). Key = SHA-256(token), never the token.</summary>
public sealed class RemotePlayerSave
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Character { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public int Health { get; set; } = 20;
    public int SelectedSlot { get; set; }
    public ItemStack[] Slots { get; set; } = Enumerable.Range(0, 36).Select(_ => new ItemStack()).ToArray();
    public ItemStack[] Crafting { get; set; } = Enumerable.Range(0, 9).Select(_ => new ItemStack()).ToArray();
    public ItemStack Carried {get;set;}=new();
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Validation used by GameSession.Read (HOOK) – same strictness as the host's own slots.</summary>
    public static bool Valid(RemotePlayerSave? p)
    {
        if (p == null || p.Key.Length != 64 || !p.Key.All(Uri.IsHexDigit) || p.Name.Length > NetProtocol.MaxNameLength) return false;
        if (p.Character < 0 || p.Character >= 11 || p.Health is < 1 or > 20 || p.SelectedSlot is < 0 or > 8) return false;
        if (!new[] { p.X, p.Y, p.Z, p.Yaw, p.Pitch }.All(float.IsFinite)) return false;
        bool Stack(ItemStack? s) => s != null && BlockCatalog.IsValid((BlockType)s.Type) && s.Count >= 0 && s.Count <= 64 && (s.Count == 0) == (s.Type == 0);
        return p.Slots is { Length: 36 } && p.Slots.All(Stack) && p.Crafting is { Length: 9 } && p.Crafting.All(Stack)&&Stack(p.Carried);
    }
}

sealed class FurnaceStacks : IFurnaceStacks
{
    readonly FurnaceSave f; public FurnaceStacks(FurnaceSave f) { this.f = f; }
    public ItemStack Get(int i) => i == 0 ? f.Input : i == 1 ? f.Fuel : f.Output;
    public void Set(int i, ItemStack s) { if (i == 0) f.Input = s; else if (i == 1) f.Fuel = s; else f.Output = s; }
    public FurnaceSave Data => f;
}

public sealed class NetPlayer
{
    public enum Phase { Handshake, Syncing, Playing, Leaving }
    public readonly long PeerId; public Phase State = Phase.Handshake; public readonly double ConnectedAt;
    public byte Slot; public string Key = ""; public string Name = ""; public int Character;
    public V3 Pos; public float Heading, Yaw, Pitch; public byte Anim, Epoch; public double LastMotionAt; public uint LastMotionSeq;
    public readonly InventoryModel Inv = new(); public int SentInvVersion = -1; public bool SentInvOpen;
    public int Health = 20; public double HurtUntil, AttackUntil, RespawnAllowedAt;
    public C3? MiningCell; public double MiningStart; public int MiningTool; public bool FinishPending;
    public Vector3I? Station;
    public readonly Queue<byte[]> Outbox = new();
    public readonly RateLimiter Intents = new(40, 80), Motion = new(40, 60), Edits = new(12, 20);
    public readonly StrikeCounter Strikes = new();
    public RemoteAvatar? Avatar;
    public NetPlayer(long peer, double now) { PeerId = peer; ConnectedAt = now; }
    public V3 Eye => Pos + new V3(0, NetRules.EyeHeight, 0);
}

public sealed partial class NetServer : IDisposable
{
    readonly NetSession session; readonly BlockWorld world; public readonly int Port;
    readonly Dictionary<long, NetPlayer> peers = new();
    readonly Dictionary<string, RemotePlayerSave> known = new(StringComparer.Ordinal);
    readonly Dictionary<WoodPickup, uint> pickupIds = new();
    readonly Dictionary<uint, WoodPickup> pickups = new();
    readonly Dictionary<uint, Vector3> pickupSent = new();
    readonly Dictionary<Creature, uint> creatureIds = new();
    readonly Dictionary<uint, Creature> creatures = new();
    uint worldSeq, nextNetId = 1; double motionTimer, reconcileTimer, autosaveTimer; int pickupCursor, creatureCursor;
    public const double AutosaveSeconds = 180;
    public IEnumerable<NetPlayer> Players => peers.Values.Where(p => p.State is NetPlayer.Phase.Syncing or NetPlayer.Phase.Playing);
    public uint WorldSeq => worldSeq;
    double Now => session.Now;

    public NetServer(NetSession session, BlockWorld world, int port)
    {
        this.session = session; this.world = world; Port = port;
        foreach (var r in world.NetLoadedRemotePlayers) if (RemotePlayerSave.Valid(r)) known[r.Key] = r;
        world.Terrain.OverlayChanged += OnOverlayChanged;world.Shapes.Changed+=OnShapeChanged;
        Reconcile();
        GD.Print($"[NET] Hostitel naslouchá na portu {port}, protokol {NetProtocol.ProtocolVersion}, verze {NetSession.GameVersion}.");
    }
    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(world.Terrain)) { world.Terrain.OverlayChanged -= OnOverlayChanged;world.Shapes.Changed-=OnShapeChanged; world.Terrain.SetExtraStreamingCenters(Array.Empty<Vector3>()); }
        foreach (var p in peers.Values) p.Avatar?.QueueFree();
    }

    // =====================================================================================
    // Connection lifecycle
    // =====================================================================================
    public void OnPeerConnected(long id)
    {
        peers[id] = new NetPlayer(id, Now);
        session.PacketPeer(id)?.SetTimeout(4, 3000, 12000);
        GD.Print($"[NET] Nové spojení (čeká na ověření). Spojení: {peers.Count}.");
    }
    public void OnPeerDisconnected(long id)
    {
        if (!peers.Remove(id, out var p)) return;CloseChest(p);
        if (p.State is NetPlayer.Phase.Syncing or NetPlayer.Phase.Playing or NetPlayer.Phase.Leaving && p.Key.Length > 0)
        {
            known[p.Key] = Export(p);
            if (p.Slot > 0) Broadcast(new NetWriter(Msg.PlayerLeft).U8(p.Slot).ToArray(), NetProtocol.ChControl);
            GD.Print($"[NET] Hráč #{p.Slot} se odpojil.");
        }
        p.Avatar?.QueueFree();
    }

    void Reject(NetPlayer p, RejectCode code, string text)
    {
        session.Send(p.PeerId, new NetWriter(Msg.Reject).U8((byte)code).Str(text, 200).Str(NetSession.GameVersion, 32).ToArray(), NetProtocol.ChControl);
        p.State = NetPlayer.Phase.Leaving; session.DisconnectPeerLater(p.PeerId);
        GD.Print($"[NET] Spojení odmítnuto: {code}.");
    }
    void Kick(NetPlayer p, string text)
    {
        if (p.State == NetPlayer.Phase.Leaving) return;
        session.Send(p.PeerId, new NetWriter(Msg.Shutdown).U8((byte)ShutdownReason.Kicked).Str(text, 200).ToArray(), NetProtocol.ChControl);
        if (p.Key.Length > 0 && p.State != NetPlayer.Phase.Handshake) known[p.Key] = Export(p);
        p.State = NetPlayer.Phase.Leaving; session.DisconnectPeerLater(p.PeerId);
        GD.Print($"[NET] Hráč #{p.Slot} odpojen serverem.");
    }
    void Strike(NetPlayer p, string why)
    {
        GD.Print($"[NET] Neplatná zpráva od #{p.Slot}: {why}");
        if (p.Strikes.Add(Now)) Kick(p, "Hra přijala příliš mnoho neplatných zpráv. Odpojeno.");
    }

    public void BroadcastShutdown(ShutdownReason reason, string text)
    {
        foreach (var p in peers.Values.ToArray())
        {
            if (p.Key.Length > 0 && p.State != NetPlayer.Phase.Handshake) known[p.Key] = Export(p);
            session.Send(p.PeerId, new NetWriter(Msg.Shutdown).U8((byte)reason).Str(text, 200).ToArray(), NetProtocol.ChControl);
        }
        Dispose();
    }

    // =====================================================================================
    // Packet dispatch
    // =====================================================================================
    public void OnPacket(long from, int channel, byte[] packet)
    {
        if (!peers.TryGetValue(from, out var p) || p.State == NetPlayer.Phase.Leaving) return;
        var type = NetReader.TypeOf(packet);
        try
        {
            if (p.State == NetPlayer.Phase.Handshake)
            {
                if (type != Msg.Hello || channel != NetProtocol.ChControl) { Reject(p, RejectCode.BadHello, "Neplatné připojení."); return; }
                HandleHello(p, packet); return;
            }
            if (type == Msg.Motion)
            {
                if (channel != NetProtocol.ChMotion) { Strike(p, "channel"); return; }
                if (p.State == NetPlayer.Phase.Playing) HandleMotion(p, packet); return;
            }
            if (channel != NetProtocol.ChControl) { Strike(p, "channel"); return; }
            if (!p.Intents.Allow(Now)) { Strike(p, "rate"); return; }   // legit clients stay far below 40 intents/s
            if (type == Msg.Leave) { p.State = NetPlayer.Phase.Leaving; known[p.Key] = Export(p); CloseChest(p);session.DisconnectPeerLater(p.PeerId); return; }
            if (p.State != NetPlayer.Phase.Playing) { Strike(p, "not playing"); return; }
            var r = new NetReader(packet);
            switch (type)
            {
                case Msg.MineStart: { var c = r.Cell(); r.End(); MineStart(p, new C3(c.X, c.Y, c.Z)); break; }
                case Msg.MineCancel: r.End(); p.MiningCell = null; p.FinishPending = false; break;
                case Msg.MineFinish: { var c = r.Cell(); r.End(); MineFinish(p, new C3(c.X, c.Y, c.Z)); break; }
                case Msg.Place: { var t = r.Cell(); var pl = r.Cell(); int slot = r.U8(); r.End(); Place(p, new C3(t.X, t.Y, t.Z), new C3(pl.X, pl.Y, pl.Z), slot); break; }
                case Msg.SelectHotbar: { int s = r.U8(); r.End(); if (!p.Inv.SelectHotbar(s)) Strike(p, "hotbar"); break; }
                case Msg.UiClick: { int area = r.U8(), index = r.U8(); bool right = r.Bool(); r.End(); if (!p.Inv.ValidSlot(area, index) && p.Inv.IsOpen) Strike(p, "slot"); else p.Inv.Click(area, index, right); if(world.IsQa&&p.Inv.Chest!=null)GD.Print($"NET_QA_CHEST area={area} slot={index} stored={p.Inv.Chest.Slots[0]} carry={p.Inv.Carried}"); break; }
                case Msg.UiDrag:
                    {
                        bool single = r.Bool(); int n = r.U8(); if (n is < 1 or > 72) throw new NetFormatException("drag count");
                        var targets = new List<(int, int)>(n); for (int i = 0; i < n; i++) targets.Add((r.U8(), r.U8())); r.End();
                        p.Inv.Drag(targets, single); break;
                    }
                case Msg.MoveSlot: { int a = r.U8(), b = r.U8(); r.End(); p.Inv.Move(a, b); break; }
                case Msg.Craft: r.End(); p.Inv.CraftOnce(); break;
                case Msg.ChooseRecipe: { int c = r.U8(); r.End(); p.Inv.ChooseRecipe(c); break; }
                case Msg.FillRecipe:{int index=r.U16();r.End();p.Inv.FillRecipe(index);break;}
                case Msg.OpenInventory: r.End(); if (!p.Inv.IsOpen) { p.Station = null; p.Inv.Open(2, null); } break;
                case Msg.CloseInventory: r.End(); CloseChest(p); if (!p.Inv.Close()) Feedback(p, "Pro zásoby není místo – zůstaly ve výrobní mřížce."); p.Station = null; break;
                case Msg.UseBlock: { var c = r.Cell(); r.End(); UseBlock(p, new C3(c.X, c.Y, c.Z)); break; }
                case Msg.Interact: { uint id = r.U32(); r.End(); Interact(p, id); break; }
                case Msg.Attack: { uint id = r.U32(); r.End(); Attack(p, id); break; }
                case Msg.RequestRespawn: r.End(); if (Now >= p.RespawnAllowedAt) { p.RespawnAllowedAt = Now + 3; Teleport(p, SpawnFor(p.Slot), Msg.Respawn); } break;
                default: Strike(p, "unknown " + (int)type); break;
            }
        }
        catch (NetFormatException e) { Strike(p, e.Message); }
    }

    // =====================================================================================
    // Handshake + snapshot
    // =====================================================================================
    bool WorldReady => world.CharacterReady && world.Terrain.PendingChunkCount == 0;

    void HandleHello(NetPlayer p, byte[] packet)
    {
        var r = new NetReader(packet);
        int protocol = r.U16();
        if (protocol != NetProtocol.ProtocolVersion) { Reject(p, RejectCode.ProtocolMismatch, $"Jiná síťová verze (hostitel {NetProtocol.ProtocolVersion}, ty {protocol}). Aktualizujte obě hry na stejnou verzi."); return; }
        string version = r.Str(32); string name = NetNames.Sanitize(r.Str(NetProtocol.MaxNameLength));
        var token = r.Bytes(NetProtocol.TokenBytes).ToArray(); int character = r.U8(); r.End();
        if (version != NetSession.GameVersion) { Reject(p, RejectCode.VersionMismatch, $"Hostitel má verzi {NetSession.GameVersion}, ty máš {version}. Aktualizujte obě hry na stejnou verzi."); return; }
        if (token.Length != NetProtocol.TokenBytes || character >= Viewer.Ids.Length) { Reject(p, RejectCode.BadHello, "Neplatné údaje hráče."); return; }
        if (!WorldReady) { Reject(p, RejectCode.WorldNotReady, "Hostitel ještě načítá svět. Zkus to za chvíli."); return; }
        string key = NetIdentity.KeyOf(token);
        // Reconnect with the same identity while the old connection is still timing out: the NEW connection wins.
        // The old one is exported first and then ignored, so the inventory exists exactly once.
        foreach (var old in peers.Values.Where(o => o != p && o.Key == key && o.State is NetPlayer.Phase.Syncing or NetPlayer.Phase.Playing).ToArray())
        {
            known[key] = Export(old);
            if (old.Slot > 0) Broadcast(new NetWriter(Msg.PlayerLeft).U8(old.Slot).ToArray(), NetProtocol.ChControl);
            Kick(old, "Připojil ses znovu z jiného spojení."); old.Avatar?.QueueFree(); old.Avatar = null; old.Key = "";
        }
        var used = Players.Select(o => o.Slot).ToHashSet();
        byte slot = 0; for (byte s = 1; s < NetProtocol.MaxPlayers; s++) if (!used.Contains(s)) { slot = s; break; }
        if (slot == 0) { Reject(p, RejectCode.Full, $"Hra je plná (nejvýše {NetProtocol.MaxPlayers} hráči)."); return; }

        p.Key = key; p.Name = name; p.Slot = slot; p.Character = character;
        if (known.TryGetValue(key, out var saved))
        {
            p.Pos = new V3(saved.X, saved.Y, saved.Z); p.Yaw = saved.Yaw; p.Pitch = saved.Pitch; p.Health = saved.Health;
            p.Inv.Load(saved.Slots, saved.SelectedSlot, saved.Crafting);
            p.Inv.RestoreCarried(saved.Carried);
            if (!NetRules.InsideWorld((int)MathF.Floor(p.Pos.X), (int)MathF.Floor(p.Pos.Z)) || p.Pos.Y < 0) p.Pos = SpawnFor(slot);
        }
        else p.Pos = SpawnFor(slot);
        p.LastMotionAt = Now;
        Admit(p);
    }

    V3 SpawnFor(int slot)
    {
        var s = world.Terrain.SpawnPosition; float x = s.X + (slot % 2 == 1 ? 1.5f : -1.5f) * ((slot + 1) / 2), z = s.Z;
        return new V3(x, world.Terrain.HeightAt(x, z) + .30f, z);
    }

    void Admit(NetPlayer p)
    {
        Reconcile(); // pickups/creatures known up to the current sequence
        var trees = world.Terrain.TreeExclusions;
        var edits = world.Terrain.GetChanges().Select(e => new EditDto(e.X, e.Y, e.Z, e.Type)).ToList();
        var pickupDtos = pickups.Select(kv => PickupDto(kv.Value, kv.Key)).ToList();
        var creatureDtos = creatures.Select(kv => CreatureDto(kv.Value, kv.Key)).ToList();
        float tod = world.Ecology != null && GodotObject.IsInstanceValid(world.Ecology) ? world.Ecology.TimeOfDay : 0;
        var welcome = new WelcomeDto(p.Slot, world.Terrain.Seed, worldSeq, trees.Length, edits.Count, pickupDtos.Count, creatureDtos.Count, tod,
            (byte)p.Character, p.Pos.X, p.Pos.Y, p.Pos.Z, p.Yaw, p.Pitch, p.Health, NetSession.GameVersion);
        p.State = NetPlayer.Phase.Syncing;
        session.Send(p.PeerId, WorldCodec.Welcome(welcome), NetProtocol.ChWorld);
        foreach (var b in WorldCodec.TreeBatches(trees)) p.Outbox.Enqueue(b);
        foreach (var b in WorldCodec.EditBatches(edits)) p.Outbox.Enqueue(b);
        foreach (var b in WorldCodec.PickupBatches(pickupDtos)) p.Outbox.Enqueue(b);
        foreach (var b in WorldCodec.CreatureBatches(creatureDtos)) p.Outbox.Enqueue(b);
        foreach(var b in ShapeCodec.Batches(world.Shapes.States.Values.ToArray()))p.Outbox.Enqueue(b);
        p.Outbox.Enqueue(WorldCodec.SnapEnd(worldSeq));
        // Roster: host (slot 0) and everybody else to the newcomer; newcomer to everybody else.
        session.Send(p.PeerId, Joined(0, session.LocalName, world.CurrentCharacter), NetProtocol.ChControl);
        foreach (var o in Players) if (o != p) { session.Send(p.PeerId, Joined(o.Slot, o.Name, o.Character), NetProtocol.ChControl); session.Send(o.PeerId, Joined(p.Slot, p.Name, p.Character), NetProtocol.ChControl); }
        p.Avatar = RemoteAvatar.Create(world, p.Slot, p.Name, p.Character); p.Avatar.SetTarget(ToGodot(p.Pos), p.Heading, 0, true);
        SendInventory(p, true); SendHealth(p);
        GD.Print($"[NET] Hráč #{p.Slot} přijat; snímek {edits.Count} úprav, {pickupDtos.Count} kusů dřeva, seq {worldSeq}.");
    }
    static byte[] Joined(int slot, string name, int character) => new NetWriter(Msg.PlayerJoined).U8((byte)slot).Str(name, NetProtocol.MaxNameLength).U8((byte)character).ToArray();

    PickupDto PickupDto(WoodPickup w, uint id)
    {
        var s = w.Snapshot();
        return new(id, s.Id, s.Type, s.Count, s.X, s.Y, s.Z, s.Age, s.Vx, s.Vy, s.Vz, s.Rx, s.Ry, s.Rz);
    }
    CreatureDto CreatureDto(Creature c, uint id) { var s = c.Snapshot(); return new(id, s.Id, s.Kind, s.X, s.Y, s.Z, c.Rotation.Y, s.Tamed,s.Following); }

    // =====================================================================================
    // World deltas (sequenced)
    // =====================================================================================
    /// <summary>Subscribed to BlockTerrain.OverlayChanged (HOOK). Fires for player edits, grass growth and leaf decay.</summary>
    void OnOverlayChanged(Vector3I cell, BlockType block)
    {
        worldSeq++;
        BroadcastWorld(WorldCodec.BlockChanged(worldSeq, cell.X, cell.Y, cell.Z, (int)block));
    }
    void BroadcastWorld(byte[] packet) { foreach (var p in Players) session.Send(p.PeerId, packet, NetProtocol.ChWorld); }
    void Broadcast(byte[] packet, int channel, bool reliable = true) { foreach (var p in Players) session.Send(p.PeerId, packet, channel, reliable); }

    /// <summary>Diff live pickups/creatures against what clients know. Cheap (hundreds of objects at most).</summary>
    void Reconcile()
    {
        var live = world.WoodPickups.Where(w => GodotObject.IsInstanceValid(w)).ToHashSet();
        foreach (var gone in pickupIds.Where(kv => !live.Contains(kv.Key)).ToArray())
        { pickupIds.Remove(gone.Key); pickups.Remove(gone.Value); pickupSent.Remove(gone.Value); worldSeq++; BroadcastWorld(WorldCodec.PickupRemove(worldSeq, gone.Value)); }
        foreach (var w in live)
            if (!pickupIds.ContainsKey(w)) { uint id = nextNetId++; pickupIds[w] = id; pickups[id] = w; worldSeq++; BroadcastWorld(WorldCodec.PickupSpawn(worldSeq, PickupDto(w, id))); }

        var eco = world.Ecology; if (eco == null || !GodotObject.IsInstanceValid(eco)) return;
        var liveC = eco.Creatures.Where(c => GodotObject.IsInstanceValid(c)).ToHashSet();
        foreach (var gone in creatureIds.Where(kv => !liveC.Contains(kv.Key)).ToArray())
        { creatureIds.Remove(gone.Key); creatures.Remove(gone.Value); worldSeq++; BroadcastWorld(WorldCodec.CreatureRemove(worldSeq, gone.Value)); }
        foreach (var c in liveC)
        {
            if (creatureIds.ContainsKey(c)) continue;
            try { Kinds.ToByte(c.Kind); } catch (ArgumentException) { continue; } // unknown kinds stay host-only
            uint id = nextNetId++; creatureIds[c] = id; creatures[id] = c; worldSeq++; BroadcastWorld(WorldCodec.CreatureSpawn(worldSeq, CreatureDto(c, id)));
        }
    }

    // =====================================================================================
    // Tick
    // =====================================================================================
    public void Tick(double delta)
    {
        double now = Now;
        foreach (var p in peers.Values.ToArray())
            if (p.State == NetPlayer.Phase.Handshake && now - p.ConnectedAt > NetProtocol.HelloTimeoutSeconds) Reject(p, RejectCode.Timeout, "Připojení vypršelo.");
        // Bounded snapshot streaming: ≤ 8 batches (≤ ~50 KB) per peer per frame on the reliable ordered world channel.
        foreach (var p in peers.Values.Where(p => p.State == NetPlayer.Phase.Syncing))
        {
            for (int i = 0; i < 8 && p.Outbox.Count > 0; i++) session.Send(p.PeerId, p.Outbox.Dequeue(), NetProtocol.ChWorld);
            if (p.Outbox.Count == 0) p.State = NetPlayer.Phase.Playing;
        }
        if (!GodotObject.IsInstanceValid(world)) return;
        reconcileTimer += delta; if (reconcileTimer >= .1) { reconcileTimer = 0; Reconcile(); }
        foreach (var p in peers.Values.Where(p => p.State == NetPlayer.Phase.Playing))
        {
            if (p.FinishPending && p.MiningCell is { } cell) TryCompleteMining(p, cell, deferred: true);
            if (p.Station is { } st && world.Terrain.GetBlock(st) is not (BlockType.CraftingTable or BlockType.Furnace or BlockType.Chest)) { p.Inv.Close(); p.Station = null; }
            CollectPickups(p);
            SendInventory(p);
        }
        world.Terrain.SetExtraStreamingCenters(Players.Select(p => ToGodot(p.Pos)).ToArray());
        motionTimer += delta; if (motionTimer >= 1.0 / 15) { motionTimer = 0; SendMotion(); }
        autosaveTimer += delta;
        if (autosaveTimer >= AutosaveSeconds && world.CharacterReady && world.UiMode != GameMode.Inventory) { autosaveTimer = 0; if (!world.SaveSnapshot(GameSession.SavePath)) world.NetNotify("Automatické uložení se nezdařilo."); }
    }

    void SendMotion()
    {
        var host = world.Body.Position;
        var w = new NetWriter(Msg.MotionBatch);
        var list = Players.Where(p => p.State == NetPlayer.Phase.Playing).ToList();
        w.U8((byte)(list.Count + 1));
        w.U8(0).F32(host.X).F32(host.Y).F32(host.Z).F32(world.Body.Rotation.Y).U8(world.NetAnimState);
        foreach (var p in list) w.U8(p.Slot).F32(p.Pos.X).F32(p.Pos.Y).F32(p.Pos.Z).F32(p.Heading).U8(p.Anim);
        // Kept below one ENet MTU (NetProtocol.MaxUnreliableBytes) so a lost fragment never drops a whole batch.
        // Pickups: only the ones that moved, round-robin, ≤ 20 per packet.
        var moving = new List<(uint, WoodPickup)>();
        var ids = pickups.Keys.ToArray();
        for (int k = 0; k < ids.Length && moving.Count < 20; k++)
        {
            uint id = ids[(pickupCursor + k) % ids.Length]; var pk = pickups[id];
            if (!GodotObject.IsInstanceValid(pk)) continue;
            if (pickupSent.TryGetValue(id, out var last) && last.DistanceSquaredTo(pk.Position) < .0001f) continue;
            moving.Add((id, pk)); pickupSent[id] = pk.Position;
        }
        if (ids.Length > 0) pickupCursor = (pickupCursor + 20) % ids.Length;
        w.U8((byte)moving.Count);
        foreach (var (id, pk) in moving) w.U32(id).F32(pk.Position.X).F32(pk.Position.Y).F32(pk.Position.Z).F32(pk.Rotation.X).F32(pk.Rotation.Y).F32(pk.Rotation.Z);
        var allC = creatures.Where(kv => GodotObject.IsInstanceValid(kv.Value)).ToList();
        var cs = allC.Count <= 24 ? allC : Enumerable.Range(0, 24).Select(k => allC[(creatureCursor + k) % allC.Count]).ToList();
        if (allC.Count > 0) creatureCursor = (creatureCursor + 24) % allC.Count;
        w.U8((byte)cs.Count);
        foreach (var (id, c) in cs) w.U32(id).F32(c.Position.X).F32(c.Position.Y).F32(c.Position.Z).F32(c.Rotation.Y).Bool(c.Tamed).Bool(c.Following);
        float tod = world.Ecology != null && GodotObject.IsInstanceValid(world.Ecology) ? world.Ecology.TimeOfDay : 0;
        w.F32(tod);
        var bytes = w.ToArray();
        System.Diagnostics.Debug.Assert(bytes.Length <= NetProtocol.MaxUnreliableBytes);
        foreach (var p in list) session.Send(p.PeerId, bytes, NetProtocol.ChMotion, reliable: false);
        foreach (var p in list) p.Avatar?.SetTarget(ToGodot(p.Pos), p.Heading, p.Anim);
    }

    // =====================================================================================
    // Movement
    // =====================================================================================
    void HandleMotion(NetPlayer p, byte[] packet)
    {
        var r = new NetReader(packet);
        uint seq = r.U32(); byte epoch = r.U8(); var to = new V3(r.F32(), r.F32(), r.F32());
        float heading = r.F32(-100, 100), yaw = r.F32(-1e4f, 1e4f), pitch = r.F32(-2, 2); byte anim = r.U8(); r.End();
        if (!p.Motion.Allow(Now)) return;                 // silently drop floods of unreliable packets
        if (epoch != p.Epoch || seq <= p.LastMotionSeq) return;   // stale (before a teleport) or reordered
        double dt = Now - p.LastMotionAt;
        if (!NetRules.PlausibleMove(p.Pos, to, (float)dt, out var why))
        {
            if (why is "nan" or "bounds") Strike(p, "motion " + why);
            Teleport(p, p.Pos, Msg.Correction); return;
        }
        p.LastMotionSeq = seq; p.LastMotionAt = Now; p.Pos = to; p.Heading = heading; p.Yaw = yaw; p.Pitch = pitch; p.Anim = anim > 3 ? (byte)0 : anim;
    }
    void Teleport(NetPlayer p, V3 pos, Msg kind)
    {
        p.Epoch++; p.Pos = pos; p.LastMotionAt = Now; p.MiningCell = null; p.FinishPending = false;
        session.Send(p.PeerId, new NetWriter(kind).U8(p.Epoch).F32(pos.X).F32(pos.Y).F32(pos.Z).ToArray(), NetProtocol.ChControl);
    }

    // =====================================================================================
    // Mining / building
    // =====================================================================================
    bool Solid(int x, int y, int z) => NetRules.InsideWorld(x, z) && y >= 0 && y <= NetProtocol.MaxBuildY + 1 && BlockCatalog.IsFullCube(world.Terrain.GetBlock(new Vector3I(x, y, z)));
    bool CanTouch(NetPlayer p, C3 cell, float reach = NetRules.Reach)
    {
        if(!NetRules.InReach(p.Eye,cell,reach))return false;
        // A centre-only ray incorrectly rejects a visible floor top behind another floor cell.
        // Check real face centres, slightly inset so the DDA reaches the target voxel.
        var c=cell.Center;
        foreach(var point in new[]{c,new V3(c.X,cell.Y+.999f,c.Z),new V3(c.X,cell.Y+.001f,c.Z),new V3(cell.X+.999f,c.Y,c.Z),new V3(cell.X+.001f,c.Y,c.Z),new V3(c.X,c.Y,cell.Z+.999f),new V3(c.X,c.Y,cell.Z+.001f)})
            if(NetRules.LineOfSight(p.Eye,cell,Solid,targetPoint:point))return true;
        return false;
    }

    void MineStart(NetPlayer p, C3 cell)
    {
        if (!NetRules.ValidEditCell(cell)) { Strike(p, "mine cell"); return; }
        if (p.MiningCell == cell && p.FinishPending) return;   // client restarted the same block while we wait to finish it
        p.MiningCell = cell; p.MiningStart = Now; p.FinishPending = false;
        var tool = p.Inv.SelectedStack; p.MiningTool = BlockCatalog.IsTool(tool.Type) ? tool.Type : 0;
    }
    void MineFinish(NetPlayer p, C3 cell)
    {
        if (p.MiningCell != cell) { MineReject(p, cell); return; }   // no matching start: never instant-mine
        if (!p.Edits.Allow(Now)) { MineReject(p, cell); return; }
        TryCompleteMining(p, cell, deferred: false);
    }
    void TryCompleteMining(NetPlayer p, C3 cell, bool deferred)
    {
        var at = world.Shapes.Anchor(new Vector3I(cell.X, cell.Y, cell.Z)); int type = (int)world.Terrain.GetBlock(at);
        if (type == 0) { p.MiningCell = null; p.FinishPending = false; if (!deferred) MineReject(p, cell); return; }   // somebody else was faster
        if (!CanTouch(p, cell)) { p.MiningCell = null; p.FinishPending = false; MineReject(p, cell); return; }
        double required = NetRules.MiningSeconds(type, p.MiningTool), elapsed = Now - p.MiningStart;
        if (elapsed + .05 < required)
        {
            if (required - elapsed <= NetRules.MiningEarlyToleranceSeconds) { p.FinishPending = true; return; }   // clock skew: finish on time
            p.MiningCell = null; p.FinishPending = false; MineReject(p, cell); Strike(p, "mining too fast"); return;
        }
        p.MiningCell = null; p.FinishPending = false;
        // --- same rules as BlockWorld.MineTarget (keep in sync; see INTEGRATION.md "shared rules") ---
        if (type is 3 or 14 or 17 or 18 && !BlockCatalog.IsPickaxe(p.MiningTool)) { Feedback(p, "Na kámen potřebuješ krumpáč z pracovního stolu."); MineReject(p, cell); return; }
        if (type == 17 && world.Furnaces.TryGetValue(at, out var furnace) && (furnace.Input.Count + furnace.Fuel.Count + furnace.Output.Count > 0 || furnace.Burn > 0)) { Feedback(p, "Nejdřív vyber zásoby z pece a nech ji vyhasnout."); MineReject(p, cell); return; }
        if (Players.Any(o => o != p && o.Station == at)) { Feedback(p, "Tento blok právě používá jiný hráč."); MineReject(p, cell); return; }
        int drop = BlockCatalog.DropFor(type), count = type == 15 ? 4 : 1;
        if (drop != 0 && !BlockCatalog.IsLog(type) && p.Inv.Capacity(drop) < count) { Feedback(p, "Batoh je plný."); MineReject(p, cell); return; }
        if (world.Terrain.EditCount >= NetProtocol.MaxEdits) { Feedback(p, "Svět dosáhl limitu úprav."); MineReject(p, cell); return; }
        if(BlockCatalog.IsShaped(type)){if(!world.Shapes.Remove(at)){MineReject(p,cell);return;}}
        else if (!world.Terrain.ApplyAuthoritativeBlock(at, BlockType.Air)) { MineReject(p, cell); return; }   // → OverlayChanged → broadcast
        if (BlockCatalog.IsLog(type)) world.SpawnWoodPickup(drop, new Vector3(cell.X + .5f, cell.Y + .5f, cell.Z + .5f));
        else if (drop != 0) { p.Inv.AddResource(drop, count); Feedback(p, $"+{count} {BlockCatalog.Get(drop).Name}"); }
    }
    void MineReject(NetPlayer p, C3 cell) => session.Send(p.PeerId, new NetWriter(Msg.MineRejected).Cell(cell.X, cell.Y, cell.Z).ToArray(), NetProtocol.ChControl);

    void Place(NetPlayer p, C3 target, C3 placement, int slot)
    {
        if (!p.Edits.Allow(Now)) return;
        if (slot != p.Inv.Selected || !NetRules.FaceAdjacent(target, placement) || !NetRules.ValidEditCell(placement)) { Strike(p, "place args"); return; }
        var stack = p.Inv.SelectedStack;
        if (stack.Count < 1 || !BlockCatalog.IsPlaceable(stack.Type)){if(world.IsQa)GD.Print("NET_QA_PLACE no-stack");return;}
        var t = new Vector3I(target.X, target.Y, target.Z); var at = new Vector3I(placement.X, placement.Y, placement.Z);
        if (world.Terrain.GetBlock(t) == BlockType.Air || world.Terrain.GetBlock(at) != BlockType.Air){if(world.IsQa)GD.Print("NET_QA_PLACE occupied");return;}
        if (!CanTouch(p, target)) { if(world.IsQa)GD.Print("NET_QA_PLACE reach/LOS");Feedback(p, "Tam nedosáhneš."); return; }
        if (!world.CellPhysicallyClear(at)||world.HostBodyIntersects(at) || Players.Any(o => NetRules.CellIntersectsPlayer(placement, o.Pos))){if(world.IsQa)GD.Print("NET_QA_PLACE body");return;}
        if (stack.Type == (int)BlockType.Torch && !BlockCatalog.IsFullCube(world.Terrain.GetBlock(at + Vector3I.Down))) { Feedback(p, "Pochodeň polož na pevný horní povrch."); return; }
        if (world.Terrain.EditCount >= NetProtocol.MaxEdits) { Feedback(p, "Svět dosáhl limitu úprav."); return; }
        if(BlockCatalog.IsShaped(stack.Type)){if(!world.Shapes.Place(at,stack.Type,world.Shapes.RotationFor(p.Yaw)))return;}
        else if (!world.Terrain.ApplyAuthoritativeBlock(at, (BlockType)stack.Type)) return;
        p.Inv.ConsumeSelected(stack.Type);   // consumed only after the voxel really changed
    }

    void UseBlock(NetPlayer p, C3 cell)
    {
        var at = new Vector3I(cell.X, cell.Y, cell.Z); var block = world.Terrain.GetBlock(at);
        if(block==BlockType.OakDoor&&CanTouch(p,cell)){var s=world.Shapes.At(at);if(s!=null)world.Shapes.SetOpen(at,!s.Open);return;}
        if(block==BlockType.Chest&&CanTouch(p,cell)){OpenChest(p,at);return;}
        if (block is not (BlockType.CraftingTable or BlockType.Furnace) || !CanTouch(p, cell)) { SendInventory(p, true); return; }
        CloseChest(p);p.Inv.Close();
        if (block == BlockType.CraftingTable) p.Inv.Open(3, null);
        else
        {
            if (!world.Furnaces.TryGetValue(at, out var f)) world.Furnaces[at] = f = new FurnaceSave { X = at.X, Y = at.Y, Z = at.Z };
            p.Inv.Open(2, new FurnaceStacks(f));
        }
        p.Station = at; SendInventory(p, true);
    }

    void CollectPickups(NetPlayer p)
    {
        foreach (var pk in world.WoodPickups.ToArray())
        {
            if (!GodotObject.IsInstanceValid(pk) || !pk.IsCollectible) continue;
            if (ToGodot(p.Pos).DistanceSquaredTo(pk.Position) > NetRules.PickupRadius * NetRules.PickupRadius) continue;
            if (p.Inv.Capacity(pk.Type) < pk.Count) continue;
            if (!world.NetRemovePickup(pk)) continue;           // atomic: removed from the world list exactly once
            p.Inv.AddResource(pk.Type, pk.Count); Feedback(p, $"+{pk.Count} {BlockCatalog.Get(pk.Type).Name}");
        }
    }

    // =====================================================================================
    // Creatures, health
    // =====================================================================================
    void Interact(NetPlayer p, uint id)
    {
        if (!creatures.TryGetValue(id, out var c) || !GodotObject.IsInstanceValid(c) || world.Ecology == null) return;
        var center = new C3(Mathf.FloorToInt(c.Position.X), Mathf.FloorToInt(c.Position.Y + .3f), Mathf.FloorToInt(c.Position.Z));
        if (NetRules.DistanceToCell(p.Eye, center) > NetRules.CreatureReach + 1) return;
        if (c.Kind is "carrot" or "berries")
        {
            if (p.Inv.AddResource(c.Kind == "carrot" ? (int)BlockType.Carrot : (int)BlockType.Berries, 3)) { Feedback(p, "Nasbíráno krmení pro zvířátka."); world.Ecology.Remove(c); }
            else Feedback(p, "Batoh je plný.");
            return;
        }
        if (c.Kind == "zombie") return;
        if (c.Tamed) { if(c.OwnerKey!=p.Key){Feedback(p,"Tenhle kamarád patří jinému hráči.");return;} c.Following = !c.Following; Feedback(p, c.Following ? "Pojď se mnou!" : "Zůstaň tady."); return; }
        int food = c.Kind == "dog" ? (int)BlockType.Bone : c.Kind == "rabbit" ? (int)BlockType.Carrot : (int)BlockType.Berries;
        if (!p.Inv.ConsumeSelected(food)) { Feedback(p, "Vyber: " + BlockCatalog.Get(food).Name); return; }
        c.Tamed = true; c.Following = true;c.OwnerKey=p.Key; Feedback(p, "Nový kamarád tě bude následovat!");
    }
    void Attack(NetPlayer p, uint id)
    {
        if (!creatures.TryGetValue(id, out var c) || !GodotObject.IsInstanceValid(c) || c.Kind != "zombie" || world.Ecology == null) return;
        if (Now < p.AttackUntil) return; p.AttackUntil = Now + .5;
        var center = new C3(Mathf.FloorToInt(c.Position.X), Mathf.FloorToInt(c.Position.Y + 1), Mathf.FloorToInt(c.Position.Z));
        if (NetRules.DistanceToCell(p.Eye, center) > NetRules.AttackReach + 1) return;
        int t = p.Inv.SelectedStack.Type; int damage = t == (int)BlockType.StoneSword ? 6 : t == (int)BlockType.WoodenSword ? 4 : 2;
        if (c.Health - damage <= 0) { world.Ecology.Remove(c); p.Inv.AddResource((int)BlockType.Bone, 1); }   // loot to the attacker, not the host
        else c.Hurt(damage);
    }

    /// <summary>Called by BlockWorld.DamagePlayer (HOOK from Ecology) for slots ≥ 1.</summary>
    public void Damage(int slot, int amount)
    {
        var p = Players.FirstOrDefault(o => o.Slot == slot && o.State == NetPlayer.Phase.Playing); if (p == null || Now < p.HurtUntil) return;
        p.HurtUntil = Now + 1; p.Health = Math.Max(0, p.Health - amount);
        if (p.Health == 0) { p.Health = 20; Teleport(p, SpawnFor(p.Slot), Msg.Respawn); Feedback(p, "Byl jsi poražen. Vrátil ses na start se svými zásobami."); }
        SendHealth(p);
    }
    public IEnumerable<(int Slot, Vector3 Position)> RemotePositions() => Players.Where(p => p.State == NetPlayer.Phase.Playing).Select(p => ((int)p.Slot, ToGodot(p.Pos)));

    // =====================================================================================
    // Outgoing per-player state
    // =====================================================================================
    void SendInventory(NetPlayer p, bool force = false)
    {
        if (!force && p.SentInvVersion == p.Inv.Version && p.SentInvOpen == p.Inv.IsOpen) return;
        p.SentInvVersion = p.Inv.Version; p.SentInvOpen = p.Inv.IsOpen;
        var w = new NetWriter(Msg.Inventory).U8((byte)p.Inv.Selected).U8((byte)p.Inv.CraftSize).U8((byte)Math.Min(255, p.Inv.RecipeChoice)).Bool(p.Inv.IsOpen);
        var st = p.Station ?? Vector3I.Zero; w.Bool(p.Station.HasValue).I8((sbyte)st.X).U8((byte)Math.Max(0, st.Y)).I8((sbyte)st.Z);
        foreach (var s in p.Inv.Slots) w.Stack(s.Type, s.Count);
        foreach (var s in p.Inv.Crafting) w.Stack(s.Type, s.Count);
        w.Stack(p.Inv.Carried.Type, p.Inv.Carried.Count);
        bool furnace = p.Inv.Furnace != null; w.Bool(furnace);
        if (furnace) { for (int i = 0; i < 3; i++) { var s = p.Inv.Furnace!.Get(i); w.Stack(s.Type, s.Count); } var f = ((FurnaceStacks)p.Inv.Furnace!).Data; w.F32(f.Burn).F32(f.Progress); }
        w.Bool(p.Inv.Chest!=null);if(p.Inv.Chest is {} chest)foreach(var s in chest.Slots)w.Stack(s.Type,s.Count);
        session.Send(p.PeerId, w.ToArray(), NetProtocol.ChControl);
    }
    void SendHealth(NetPlayer p) => session.Send(p.PeerId, new NetWriter(Msg.Health).U8((byte)p.Health).ToArray(), NetProtocol.ChControl);
    void Feedback(NetPlayer p, string text) => session.Send(p.PeerId, new NetWriter(Msg.Feedback).Str(text, 120).ToArray(), NetProtocol.ChControl);

    // =====================================================================================
    // Persistence
    // =====================================================================================
    RemotePlayerSave Export(NetPlayer p)
    {
        var (slots, crafting) = p.Inv.ExportForSave();
        return new RemotePlayerSave { Key = p.Key, Name = p.Name, Character = p.Character, X = p.Pos.X, Y = p.Pos.Y, Z = p.Pos.Z, Yaw = p.Yaw, Pitch = p.Pitch, Health = Math.Clamp(p.Health, 1, 20), SelectedSlot = p.Inv.Selected, Slots = slots, Crafting = crafting,Carried=p.Inv.Carried, LastSeenUtc = DateTime.UtcNow };
    }
    /// <summary>HOOK: BlockWorld.SaveSnapshot → SavedGame.RemotePlayers. Connected players are exported live; others from the last known record.</summary>
    public RemotePlayerSave[] ExportAll()
    {
        foreach (var p in Players) known[p.Key] = Export(p);
        return known.Values.OrderByDescending(r => r.LastSeenUtc).Take(64).ToArray();
    }

    static Vector3 ToGodot(V3 v) => new(v.X, v.Y, v.Z);
}
