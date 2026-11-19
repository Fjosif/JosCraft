// JOSCRAFT multiplayer – BlockWorld partial: everything BlockWorld needs from/for the network.
// The ORIGINAL BlockWorld.cs / SurvivalWorld.cs only need the one-line hooks listed in INTEGRATION.md.
// NAVRH: runtime/Net/BlockWorld.Net.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BlockWorld
{
    public bool IsNetClient => NetSession.IsClient;
    public bool IsNetHost => NetSession.IsHost;
    NetClient? NetC => NetSession.IsClient ? NetSession.Instance?.Client : null;
    public byte NetAnimState => Body == null ? (byte)0 : !Body.IsOnFloor() ? (byte)3 : currentClip switch { "Walk" => (byte)1, "Run" => (byte)2, _ => (byte)0 };
    /// <summary>Filled from SavedGame.RemotePlayers in ApplySave (HOOK). Read by NetServer when hosting starts.</summary>
    public RemotePlayerSave[] NetLoadedRemotePlayers { get; private set; } = Array.Empty<RemotePlayerSave>();
    public void NetSetLoadedRemotePlayers(RemotePlayerSave[]? players) => NetLoadedRemotePlayers = players?.Where(RemotePlayerSave.Valid).ToArray() ?? Array.Empty<RemotePlayerSave>();
    /// <summary>HOOK target for SaveSnapshot: live export while hosting, otherwise keep what was loaded so offline play never drops friends' inventories.</summary>
    public RemotePlayerSave[] NetRemotePlayersForSave() => NetSession.Instance?.Server?.ExportAll() ?? NetLoadedRemotePlayers;

    public void NetNotify(string message) => ShowFeedback(message);

    // ------------------------------------------------------------------ client configuration
    public void NetConfigureClient()
    {
        Terrain.OverlaySimulation = false;          // grass/leaf changes come from the host as deltas
        if (Ecology != null && IsInstanceValid(Ecology)) Ecology.SetRemoteMode(true);
    }

    Vector3I? netAwaitCell; double netAwaitUntil; double netRespawnAt;
    static double NowSeconds => Time.GetTicksMsec() / 1000.0;

    /// <summary>HOOK in UpdateMining: returns true while we wait for the host's verdict on a finished block.</summary>
    public bool NetMiningAwaiting(Vector3I cell)
    {
        if (!IsNetClient || netAwaitCell != cell) return false;
        if (NowSeconds > netAwaitUntil) { netAwaitCell = null; return false; }
        MiningProgress = 1; return true;
    }
    /// <summary>HOOK in UpdateMining when the targeted cell changes.</summary>
    public void NetMiningStarted(Vector3I cell) { if (IsNetClient) NetC?.MineStart(cell); }
    /// <summary>HOOK at the top of MineTarget().</summary>
    bool NetClientMine()
    {
        var t = RayTarget(); if (t == null || t.Cell.Y <= 0) return false;
        NetC?.MineFinish(t.Cell); netAwaitCell = t.Cell; netAwaitUntil = NowSeconds + 1.5; return true;
    }
    public void NetMiningRejected() { netAwaitCell = null; miningTime = 0; MiningProgress = 0; miningCell = null; }
    /// <summary>HOOK in SetMiningHeld(false).</summary>
    void NetMiningStopped() { if (IsNetClient && miningCell != null) NetC?.MineCancel(); }

    /// <summary>HOOK at the top of PlaceTarget(). Local checks are only UX; the host re-validates everything.</summary>
    bool NetClientPlace()
    {
        var stack = inventory[SelectedHotbar]; if (stack.Count == 0) { ShowFeedback("Nejdřív vytěž materiál."); return false; }
        if (!BlockCatalog.IsPlaceable(stack.Type)) return false;
        var t = RayTarget(); if (t == null || Terrain.GetBlockId(t.Placement) != 0 || !CanPlaceCell(t.Placement)) return false;
        NetC?.Place(t.Cell, t.Placement, SelectedHotbar); return true;
    }
    /// <summary>HOOK at the top of SurvivalWorld.UseTarget().</summary>
    bool NetClientUse()
    {
        if (Ecology != null && IsInstanceValid(Ecology) && Ecology.TryInteract()) return true;   // Ecology hook forwards to the host
        var t = RayTarget();
        if (t?.Type is (int)BlockType.CraftingTable or (int)BlockType.Furnace or (int)BlockType.OakDoor or (int)BlockType.Chest) { NetC?.UseBlock(t.Cell); return true; }
        return false;
    }
    /// <summary>HOOK at the top of Respawn(): a client asks the host instead of teleporting itself.</summary>
    bool NetClientRespawn()
    {
        if (!IsNetClient || NetC?.Ready != true) return false;
        if (NowSeconds >= netRespawnAt) { netRespawnAt = NowSeconds + 2; NetC.RequestRespawn(); }
        return true;
    }

    // ------------------------------------------------------------------ client inventory mirror
    bool netHadStation;
    /// <summary>Server → client: the authoritative inventory replaces the local mirror completely.</summary>
    public void ApplyNetInventory(InventoryView v, bool openRequested)
    {
        for (int i = 0; i < inventory.Length && i < v.Slots.Length; i++) inventory[i] = v.Slots[i];
        for (int i = 0; i < crafting.Length && i < v.Crafting.Length; i++) crafting[i] = v.Crafting[i];
        CarriedStack = v.Carried; SelectedHotbar = v.Selected; CraftSize = v.CraftSize; OpenStation = v.Station; RecipeChoice = v.RecipeChoice;
        if (v.Station is { } st && v.Furnace != null)
            Furnaces[st] = new FurnaceSave { X = st.X, Y = st.Y, Z = st.Z, Input = v.Furnace[0], Fuel = v.Furnace[1], Output = v.Furnace[2], Burn = v.Burn, Progress = v.Progress };
        if(v.Station is {} chestCell&&v.Chest!=null&&Shapes.At(chestCell) is {} chest)chest.Slots=v.Chest;
        if (v.Open && openRequested && UiMode == GameMode.Playing) SetMode(GameMode.Inventory);
        else if (!v.Open && netHadStation && v.Station == null && UiMode == GameMode.Inventory) SetMode(GameMode.Playing);   // e.g. table was mined
        netHadStation = v.Station != null;
        GameUi?.RefreshHotbar(); if (UiMode == GameMode.Inventory) GameUi?.ShowInventory();
    }
    public void ApplyNetHealth(int health) => Health = Math.Clamp(health, 0, 20);
    public void NetTeleport(Vector3 position)
    {
        Body.Position = position; Body.Velocity = Vector3.Zero; NetMiningRejected();
        Terrain.UpdateStreaming(position); waitingForSpawn = true;
    }
    public void NetSetTimeOfDay(float time, bool snap = false) { if (Ecology != null && IsInstanceValid(Ecology)) Ecology.SetServerTime(time, snap); }

    /// <summary>Client UI intents. Each returns true when the action was forwarded (the HOOKs then return early).</summary>
    bool NetUiClick(int area, int index, bool right) { if (!IsNetClient) return false; NetC?.UiClick(area, index, right); return true; }
    bool NetUiDrag(IEnumerable<(int Area, int Slot)> targets, bool single) { if (!IsNetClient) return false; NetC?.UiDrag(targets, single); return true; }
    bool NetCraft() { if (!IsNetClient) return false; NetC?.Craft(); return true; }
    bool NetChooseRecipe(int choice) { if (!IsNetClient) return false; NetC?.ChooseRecipe(choice); return true; }
    bool NetMoveSlot(int from, int to) { if (!IsNetClient) return false; NetC?.MoveSlot(from, to); return true; }
    void NetSelectHotbar(int index) { if (IsNetClient) NetC?.SelectHotbar(index); }
    /// <summary>HOOK in SetMode: opening/closing the backpack on a client. Returns true when handled (skip local ReturnStaging).</summary>
    bool NetInventoryModeChange(GameMode from, GameMode to)
    {
        if (!IsNetClient) return false;
        if (from != GameMode.Inventory && to == GameMode.Inventory && OpenStation == null) NetC?.OpenInventory();
        if (from == GameMode.Inventory && to != GameMode.Inventory) { NetC?.CloseInventory(); OpenStation = null; CraftSize = 2; }
        return true;
    }

    // ------------------------------------------------------------------ host-side helpers used by NetServer / Ecology
    /// <summary>Removes a pickup from the world WITHOUT giving it to the host player. False if it was already gone.</summary>
    public bool NetRemovePickup(WoodPickup pickup)
    {
        if (!woodPickups.Remove(pickup)) return false;
        pickup.Freeze = true; pickup.Visible = false; pickup.SetPhysicsProcess(false); pickup.QueueFree(); return true;
    }
    /// <summary>Same footprint test as CanPlaceCell, without requiring the host to have the chunk loaded.</summary>
    public bool HostBodyIntersects(Vector3I cell)
    {
        var size = FootprintMax - FootprintMin; var local = new Aabb(new Vector3(FootprintMin.X, 0, FootprintMin.Y), new Vector3(size.X, CollisionHeight, size.Y));
        return (Body.GlobalTransform * local).Intersects(new Aabb(new Vector3(cell.X, cell.Y, cell.Z), Vector3.One));
    }
    public IEnumerable<(int Slot, Vector3 Position)> PlayerPositions()
    {
        yield return (0, Body.Position);
        if (NetSession.Instance?.Server is { } s) foreach (var p in s.RemotePositions()) yield return p;
    }
    /// <summary>HOOK for Ecology: zombies chase / hit the nearest player, not only the host.</summary>
    public Vector3 NearestPlayerPosition(Vector3 from, out int slot)
    {
        slot = 0; var best = Body.Position; float d = from.DistanceSquaredTo(best);
        foreach (var (s, p) in PlayerPositions()) { float dd = from.DistanceSquaredTo(p); if (dd < d) { d = dd; best = p; slot = s; } }
        return best;
    }
    public Vector3 RandomPlayerPosition() { var all = PlayerPositions().ToArray(); return all[(int)(GD.Randi() % (uint)all.Length)].Position; }
    public Vector3? CompanionOwnerPosition(string owner)
    {
        if(string.IsNullOrEmpty(owner)||owner=="host")return Body.Position;
        var p=NetSession.Instance?.Server?.Players.FirstOrDefault(p=>p.Key==owner);
        return p==null?null:new Vector3(p.Pos.X,p.Pos.Y,p.Pos.Z);
    }
    public void DamagePlayer(int slot, int amount) { if (slot == 0) Damage(amount); else NetSession.Instance?.Server?.Damage(slot, amount); }

    // ------------------------------------------------------------------ leaving
    /// <summary>Client path back to the menu: never writes GameSession.SavePath.</summary>
    public void NetReturnToMenuWithoutSave()
    {
        if (!IsInsideTree()) return;
        ReleaseMouse(); GetTree().ChangeSceneToFile("res://game.tscn");
    }
}

/// <summary>Pause-menu texts/info for GameUi.ShowMode (HOOK). Pause on the host only pauses the host's own input.</summary>
public static class NetPauseUi
{
    public static string SaveAndMenuLabel => NetSession.IsClient ? "Odpojit a hlavní menu" : NetSession.IsHost ? "Uložit, ukončit hru pro LAN a menu" : "Uložit a hlavní menu";
    public static void AddInfo(Container box)
    {
        string text = "";
        if (NetSession.Instance?.Server is { } s)
        {
            var ips = NetSession.LanAddresses();
            text = $"Hra pro LAN běží (port {s.Port}{(ips.Length > 0 ? ", adresa " + string.Join(", ", ips) : "")}). Hráči: {s.Players.Count() + 1}/{NetProtocol.MaxPlayers}. Pauza ostatní nezastaví.";
        }
        else if (NetSession.IsClient) text = "Jsi připojený k hostiteli. Svět i tvůj batoh se ukládají u hostitele, ne na tomto počítači.";
        if (text.Length > 0) box.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }
}

public partial class BlockWorld
{
    /// <summary>Target of the QA HOOK in _Ready: `else if (NetQa.Active) CallDeferred(MethodName.StartNetQa);`</summary>
    async void StartNetQa() { try { await NetQa.RunInWorld(this); } catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); } }
}
