// JOSCRAFT multiplayer – client side: handshake, snapshot download, applying deltas, sending intents only.
// The client NEVER sends inventory or edit DTOs; it sends "I want to …" and renders what the host decides.
// NAVRH: runtime/Net/NetClient.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record InventoryView(int Selected, int CraftSize, int RecipeChoice, bool Open, Vector3I? Station, ItemStack[] Slots, ItemStack[] Crafting, ItemStack Carried, ItemStack[]? Furnace, float Burn, float Progress, ItemStack[]? Chest=null);

public sealed class NetClient
{
    public enum Phase { Connecting, Handshake, Syncing, Entering, Playing, Closed }
    readonly NetSession session; public readonly string Address; public readonly int Port; readonly int character;
    readonly Action<string> status, failed;
    public Phase State { get; private set; } = Phase.Connecting;
    public byte Slot { get; private set; }
    public float Progress => assembler.Progress;
    readonly SnapshotAssembler assembler = new();
    readonly Queue<byte[]> pendingDeltas = new();
    readonly Dictionary<byte, (string Name, int Character)> roster = new();
    readonly Dictionary<byte, RemoteAvatar> avatars = new();
    readonly Dictionary<uint, WoodPickup> pickups = new();
    readonly Dictionary<uint, Creature> creatures = new();
    BlockWorld? world; InventoryView? lastInventory; int lastHealth = 20;
    double started, motionTimer; uint motionSeq; byte epoch; float serverTime = -1;
    bool openRequested;
    public bool Ready => world != null && State == Phase.Playing;

    public NetClient(NetSession session, string address, int port, int character, Action<string> status, Action<string> failed)
    {
        this.session = session; Address = address; Port = port; this.character = Math.Clamp(character, 0, Viewer.Ids.Length - 1);
        this.status = status; this.failed = failed; started = session.Now;
        status($"Hledám hostitele {address}:{port} …");
    }

    // ---------------- connection ----------------
    public void Tick(double delta)
    {
        var peer = session.Peer; if (peer == null || State == Phase.Closed) return;
        var st = peer.GetConnectionStatus();
        if (State == Phase.Connecting)
        {
            if (st == MultiplayerPeer.ConnectionStatus.Connected) SendHello();
            else if (st == MultiplayerPeer.ConnectionStatus.Disconnected || session.Now - started > 8)
                Fail($"Hostitele {Address}:{Port} se nepodařilo najít. Zkontroluj adresu, port a že hostitel má otevřenou hru pro LAN.");
            return;
        }
        if (st == MultiplayerPeer.ConnectionStatus.Disconnected) { Fail(State == Phase.Playing ? "Spojení s hostitelem bylo přerušeno. Tvůj postup je uložený u hostitele." : "Hostitel spojení ukončil."); return; }
        if (State == Phase.Handshake && session.Now - started > 15) { Fail("Hostitel neodpověděl."); return; }
        if (Ready && world!.CharacterReady)
        {
            motionTimer += delta;
            if (motionTimer >= 1.0 / 20) { motionTimer = 0; SendMotion(); }
            if (serverTime >= 0) world.NetSetTimeOfDay(serverTime);
        }
    }

    void SendHello()
    {
        State = Phase.Handshake; status("Ověřuji verzi hry …");
        session.PacketPeer(1)?.SetTimeout(4, 3000, 10000);
        var token = NetIdentity.TokenBytes(session.Identity);
        Send(new NetWriter(Msg.Hello).U16(NetProtocol.ProtocolVersion).Str(NetSession.GameVersion, 32).Str(session.LocalName, NetProtocol.MaxNameLength).Bytes(token).U8((byte)character));
    }

    void Fail(string message)
    {
        if (State == Phase.Closed) return;
        State = Phase.Closed;
        session.ClientFailed(message);
    }
    public void NotifyMenu(string message) => failed(message);

    void Send(NetWriter w, int channel = NetProtocol.ChControl, bool reliable = true) => session.Send(1, w.ToArray(), channel, reliable);

    // ---------------- receive ----------------
    public void OnPacket(long from, int channel, byte[] packet)
    {
        if (from != 1 || State == Phase.Closed) return;
        var type = NetReader.TypeOf(packet);
        try
        {
            if (type == Msg.Reject && channel == NetProtocol.ChControl)
            {
                var r = new NetReader(packet); var code = (RejectCode)r.U8(); string text = r.Str(200); r.Str(32);
                Fail(text.Length > 0 ? text : "Hostitel připojení odmítl (" + code + ")."); return;
            }
            if (channel == NetProtocol.ChWorld) { OnWorld(type, packet); return; }
            if (channel == NetProtocol.ChMotion) { if (type == Msg.MotionBatch && Ready) OnMotion(packet); return; }
            OnControl(type, packet);
        }
        catch (NetFormatException e) { Fail("Hostitel poslal poškozená data (" + e.Message + ")."); }
    }

    void OnWorld(Msg type, byte[] packet)
    {
        if (type == Msg.Welcome)
        {
            var w = WorldCodec.ReadWelcome(packet);
            if (w.HostVersion != NetSession.GameVersion) { Fail($"Hostitel má verzi {w.HostVersion}, ty máš {NetSession.GameVersion}."); return; }
            Slot = w.Slot; State = Phase.Syncing; assembler.OnWelcome(w); epoch = 0; lastHealth = w.Health;
            status("Stahuji svět … 0 %"); return;
        }
        if (State == Phase.Syncing)
        {
            if (!assembler.OnWorldPacket(packet)) { Fail("Stažení světa selhalo: " + assembler.Error + "."); return; }
            status($"Stahuji svět … {(int)(assembler.Progress * 100)} %");
            if (assembler.Current == SnapshotAssembler.Stage.Complete) EnterWorld();
            return;
        }
        if (State is Phase.Entering or Phase.Playing)
        {
            if (!assembler.TryAcceptDelta(packet, out var ok) || ok == null) { Fail("Svět se rozešel s hostitelem: " + assembler.Error + "."); return; }
            if (world == null) pendingDeltas.Enqueue(ok); else ApplyDelta(ok);
        }
    }

    void EnterWorld()
    {
        var w = assembler.Welcome!;
        State = Phase.Entering; status("Vstupuji do světa …");
        // In-memory only. GameSession.SavePath is never written while NetSession.Mode == Client (see BlockWorld.Net).
        GameSession.Pending = new SavedGame
        {
            Seed = w.Seed, Character = w.Character, X = w.X, Y = w.Y, Z = w.Z, Yaw = w.Yaw, Pitch = w.Pitch, FirstPerson = true,
            Edits = assembler.Edits.Select(e => new BlockTerrain.BlockEdit(e.X, e.Y, e.Z, e.Type)).ToArray(),
            PlacedBlocks=assembler.Shapes.ToArray(),TreeExclusions = assembler.Trees.ToArray(), Pickups = Array.Empty<WoodPickupSave>(), // remote pickups are spawned in AttachWorld
        };
        foreach (var d in assembler.DrainBuffered()) pendingDeltas.Enqueue(d);
        if (assembler.Current == SnapshotAssembler.Stage.Failed) { Fail("Svět se rozešel s hostitelem: " + assembler.Error + "."); return; }
        GameSession.CaptureAfterStart = true; BlockWorld.SelectedCharacter = w.Character;
        session.GetTree().ChangeSceneToFile("res://world.tscn");
    }

    /// <summary>Called by NetSession.AttachWorld once the client BlockWorld finished ApplySave.</summary>
    public void AttachWorld(BlockWorld w)
    {
        world = w; State = Phase.Playing; NetIdentity.ClearReconnect();
        w.NetConfigureClient();
        foreach (var p in assembler.Pickups) SpawnPickup(p);
        foreach (var c in assembler.Creatures) SpawnCreature(c);
        assembler.Pickups.Clear(); assembler.Creatures.Clear(); assembler.Edits.Clear(); assembler.Edits.TrimExcess();
        while (pendingDeltas.Count > 0) ApplyDelta(pendingDeltas.Dequeue());
        foreach (var (slot, info) in roster) EnsureAvatar(slot, info.Name, info.Character);
        if (lastInventory != null) w.ApplyNetInventory(lastInventory, false);
        w.ApplyNetHealth(lastHealth);
        if (serverTime >= 0) w.NetSetTimeOfDay(serverTime, snap: true);
    }
    public void DetachWorld() { world = null; avatars.Clear(); pickups.Clear(); creatures.Clear(); }

    void ApplyDelta(byte[] packet)
    {
        var r = new NetReader(packet); r.U32();
        switch (NetReader.TypeOf(packet))
        {
            case Msg.ShapeChanged:{var shape=ShapeCodec.Read(ref r);r.End();world!.Shapes.ApplyRemote(shape);break;}
            case Msg.BlockChanged:
                { var c = r.Cell(); int t = r.U8(); r.End(); if (c.Y < 1 || !BlockCatalog.IsBlock((BlockType)t)) throw new NetFormatException("block");
                  world!.Terrain.ApplyAuthoritativeBlock(new Vector3I(c.X, c.Y, c.Z), (BlockType)t); break; }
            case Msg.PickupSpawn: { var p = WorldCodec.ReadPickup(ref r); r.End(); SpawnPickup(p); break; }
            case Msg.PickupRemove: { uint id = r.U32(); r.End(); if (pickups.Remove(id, out var pk) && GodotObject.IsInstanceValid(pk)) world!.NetRemovePickup(pk); break; }
            case Msg.CreatureSpawn: { var c = WorldCodec.ReadCreature(ref r); r.End(); SpawnCreature(c); break; }
            case Msg.CreatureRemove: { uint id = r.U32(); r.End(); if (creatures.Remove(id, out var c) && GodotObject.IsInstanceValid(c)) world!.Ecology?.RemoveProxy(c); break; }
            default: throw new NetFormatException("delta type");
        }
    }
    void SpawnPickup(PickupDto p)
    {
        if (pickups.ContainsKey(p.NetId) || world!.WoodPickups.Any(x => x.Id == p.Id)) return;
        var pk = world.SpawnWoodPickup(p.Type, new Vector3(p.X, p.Y, p.Z), p.Id, p.Count, p.Age);
        pk.NetRemote = true; pk.Rotation = new Vector3(p.Rx, p.Ry, p.Rz); pk.NetTarget(new Vector3(p.X, p.Y, p.Z), pk.Rotation, snap: true);
        pickups[p.NetId] = pk;
    }
    void SpawnCreature(CreatureDto c)
    {
        if (creatures.ContainsKey(c.NetId) || world?.Ecology == null) return;
        creatures[c.NetId] = world.Ecology.SpawnProxy(new CreatureSave { Id = c.Id, Kind = c.Kind, X = c.X, Y = c.Y, Z = c.Z, Tamed = c.Tamed,Following=c.Following }, c.NetId, c.Yaw);
    }

    void OnControl(Msg type, byte[] packet)
    {
        var r = new NetReader(packet);
        switch (type)
        {
            case Msg.PlayerJoined:
                { byte slot = (byte)r.Range(r.U8(), 0, NetProtocol.MaxPlayers - 1); string name = NetNames.Sanitize(r.Str(NetProtocol.MaxNameLength)); int ch = r.Range(r.U8(), 0, Viewer.Ids.Length - 1); r.End();
                  if (slot == Slot) return; roster[slot] = (name, ch); if (world != null) EnsureAvatar(slot, name, ch);
                  world?.NetNotify($"{name} je ve hře."); break; }
            case Msg.PlayerLeft:
                { byte slot = r.U8(); r.End(); if (roster.Remove(slot, out var info)) world?.NetNotify($"{info.Name} odešel ze hry.");
                  if (avatars.Remove(slot, out var a) && GodotObject.IsInstanceValid(a)) a.QueueFree(); break; }
            case Msg.Inventory: { var inv = ReadInventory(ref r); r.End(); lastInventory = inv; if (world != null) { world.ApplyNetInventory(inv, openRequested); if (inv.Open) openRequested = false; } break; }
            case Msg.Health: { lastHealth = r.Range(r.U8(), 0, 20); r.End(); world?.ApplyNetHealth(lastHealth); break; }
            case Msg.Feedback: { string text = r.Str(120); r.End(); world?.NetNotify(text); break; }
            case Msg.Respawn:
            case Msg.Correction:
                { epoch = r.U8(); var p = new Vector3(r.F32(-60, 60), r.F32(-20, 60), r.F32(-60, 60)); r.End(); world?.NetTeleport(p); break; }
            case Msg.MineRejected: { r.Cell(); r.End(); world?.NetMiningRejected(); break; }
            case Msg.Shutdown:
                { var reason = (ShutdownReason)r.U8(); string text = r.Str(200); r.End();
                  if (reason == ShutdownReason.HostUpdate) NetIdentity.WriteReconnect(Address, Port, "host-update");
                  Fail(text.Length > 0 ? text : "Hostitel ukončil hru."); break; }
            default: throw new NetFormatException("control type " + (int)type);
        }
    }
    static InventoryView ReadInventory(ref NetReader r)
    {
        int selected = r.Range(r.U8(), 0, 8), craftSize = r.Range(r.U8(), 2, 3), recipe = r.U8(); bool open = r.Bool();
        bool hasStation = r.Bool(); var st = new Vector3I(r.I8(), r.U8(), r.I8());
        ItemStack Stack(ref NetReader rr) { int t = rr.U8(), c = rr.U8(); if (c > 64 || (c == 0) != (t == 0) || !BlockCatalog.IsValid((BlockType)t)) throw new NetFormatException("stack"); return new(t, c); }
        var slots = new ItemStack[36]; for (int i = 0; i < 36; i++) slots[i] = Stack(ref r);
        var craft = new ItemStack[9]; for (int i = 0; i < 9; i++) craft[i] = Stack(ref r);
        var carried = Stack(ref r);
        ItemStack[]? furnace = null; float burn = 0, progress = 0;
        if (r.Bool()) { furnace = new ItemStack[3]; for (int i = 0; i < 3; i++) furnace[i] = Stack(ref r); burn = r.F32(0, 1000); progress = r.F32(0, 1000); }
        ItemStack[]? chest=null;if(r.Bool()){chest=new ItemStack[27];for(int i=0;i<27;i++)chest[i]=Stack(ref r);}
        return new(selected, craftSize, recipe, open, hasStation ? st : null, slots, craft, carried, furnace, burn, progress,chest);
    }

    void OnMotion(byte[] packet)
    {
        var r = new NetReader(packet);
        int players = r.Range(r.U8(), 0, NetProtocol.MaxPlayers);
        for (int i = 0; i < players; i++)
        {
            byte slot = r.U8(); var pos = new Vector3(r.F32(-60, 60), r.F32(-20, 60), r.F32(-60, 60)); float heading = r.F32(-100, 100); byte anim = r.U8();
            if (slot != Slot && avatars.TryGetValue(slot, out var a) && GodotObject.IsInstanceValid(a)) a.SetTarget(pos, heading, anim);
        }
        int np = r.Range(r.U8(), 0, 20);
        for (int i = 0; i < np; i++)
        {
            uint id = r.U32(); var pos = new Vector3(r.F32(-60, 60), r.F32(-20, 60), r.F32(-60, 60)); var rot = new Vector3(r.F32(-1e5f, 1e5f), r.F32(-1e5f, 1e5f), r.F32(-1e5f, 1e5f));
            if (pickups.TryGetValue(id, out var pk) && GodotObject.IsInstanceValid(pk)) pk.NetTarget(pos, rot);
        }
        int nc = r.Range(r.U8(), 0, 24);
        for (int i = 0; i < nc; i++)
        {
            uint id = r.U32(); var pos = new Vector3(r.F32(-80, 80), r.F32(-20, 60), r.F32(-80, 80)); float yaw = r.F32(-100, 100);
            bool tamed=r.Bool(),following=r.Bool();
            if (creatures.TryGetValue(id, out var c) && GodotObject.IsInstanceValid(c)){c.Tamed=tamed;c.Following=following;c.NetTarget(pos, yaw);}
        }
        serverTime = r.F32(0, 1); r.End();
    }

    void EnsureAvatar(byte slot, string name, int ch)
    {
        if (avatars.TryGetValue(slot, out var old) && GodotObject.IsInstanceValid(old)) { if (old.Character == ch) return; old.QueueFree(); }
        avatars[slot] = RemoteAvatar.Create(world!, slot, name, ch);
    }

    // ---------------- intents (client → host) ----------------
    void SendMotion()
    {
        var w = world!; var p = w.Body.Position;
        Send(new NetWriter(Msg.Motion).U32(++motionSeq).U8(epoch).F32(p.X).F32(p.Y).F32(p.Z).F32(Mathf.Wrap(w.Body.Rotation.Y, -Mathf.Pi, Mathf.Pi))
            .F32(Mathf.Wrap(w.CameraYaw, -1000, 1000)).F32(w.FirstPersonPitch).U8(w.NetAnimState), NetProtocol.ChMotion, reliable: false);
    }
    static NetWriter CellMsg(Msg m, Vector3I c) => new NetWriter(m).Cell(c.X, c.Y, c.Z);
    public void MineStart(Vector3I cell) { if (Ready) Send(CellMsg(Msg.MineStart, cell)); }
    public void MineCancel() { if (Ready) Send(new NetWriter(Msg.MineCancel)); }
    public void MineFinish(Vector3I cell) { if (Ready) Send(CellMsg(Msg.MineFinish, cell)); }
    public void Place(Vector3I target, Vector3I placement, int slot) { if (Ready) Send(new NetWriter(Msg.Place).Cell(target.X, target.Y, target.Z).Cell(placement.X, placement.Y, placement.Z).U8((byte)slot)); }
    public void SelectHotbar(int slot) { if (Ready) Send(new NetWriter(Msg.SelectHotbar).U8((byte)slot)); }
    public void UiClick(int area, int index, bool right) { if (Ready) Send(new NetWriter(Msg.UiClick).U8((byte)area).U8((byte)index).Bool(right)); }
    public void UiDrag(IEnumerable<(int Area, int Slot)> targets, bool single)
    {
        var list = targets.Distinct().Take(72).ToList(); if (!Ready || list.Count == 0) return;
        var w = new NetWriter(Msg.UiDrag).Bool(single).U8((byte)list.Count); foreach (var t in list) w.U8((byte)t.Area).U8((byte)t.Slot); Send(w);
    }
    public void MoveSlot(int from, int to) { if (Ready) Send(new NetWriter(Msg.MoveSlot).U8((byte)from).U8((byte)to)); }
    public void Craft() { if (Ready) Send(new NetWriter(Msg.Craft)); }
    public void ChooseRecipe(int choice) { if (Ready) Send(new NetWriter(Msg.ChooseRecipe).U8((byte)Math.Clamp(choice, 0, 255))); }
    public void FillRecipe(int index){if(Ready&&index>=0&&index<global::Crafting.Recipes.Length)Send(new NetWriter(Msg.FillRecipe).U16((ushort)index));}
    public void OpenInventory() { if (Ready) Send(new NetWriter(Msg.OpenInventory)); }
    public void CloseInventory() { if (Ready) Send(new NetWriter(Msg.CloseInventory)); }
    public void UseBlock(Vector3I cell) { if (Ready) { openRequested = true; Send(CellMsg(Msg.UseBlock, cell)); } }
    public void Interact(Creature c) { if (Ready && c.NetId != 0) Send(new NetWriter(Msg.Interact).U32(c.NetId)); }
    public void Attack(Creature c) { if (Ready && c.NetId != 0) Send(new NetWriter(Msg.Attack).U32(c.NetId)); }
    double nextRespawnRequest;
    public void RequestRespawn() { if (Ready && session.Now >= nextRespawnRequest) { nextRespawnRequest = session.Now + 2; Send(new NetWriter(Msg.RequestRespawn)); } }
    public void SendLeave(ShutdownReason reason) { if (State != Phase.Closed && State != Phase.Connecting) Send(new NetWriter(Msg.Leave).U8((byte)reason)); State = Phase.Closed; }
}
