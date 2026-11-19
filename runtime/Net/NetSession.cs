// JOSCRAFT multiplayer – persistent session node (survives scene changes), ENet transport, identity.
// NAVRH: runtime/Net/NetSession.cs
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using RandomNumberGenerator=System.Security.Cryptography.RandomNumberGenerator;

public enum NetMode { Single, Host, Client }

/// <summary>Local LAN identity. The raw token never leaves this file except inside the Hello packet; it is never logged.</summary>
public sealed class NetIdentityFile
{
    public string Token { get; set; } = "";
    public string Name { get; set; } = "";
    public string LastAddress { get; set; } = "";
    public int LastPort { get; set; } = NetProtocol.DefaultPort;
}
public sealed class NetReconnectFile
{
    public string Address { get; set; } = "";
    public int Port { get; set; } = NetProtocol.DefaultPort;
    public DateTime ExpiresUtc { get; set; }
    public string Reason { get; set; } = "";
}

public static class NetIdentity
{
    /// <summary>"--net-profile=B" gives a second identity so two instances on one PC can both join (test only).</summary>
    public static string Profile => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--net-profile="))?["--net-profile=".Length..] is { Length: > 0 and <= 16 } p && p.All(char.IsAsciiLetterOrDigit) ? p : "";
    static string IdentityPath => ProjectSettings.GlobalizePath($"{(GameSession.IsQa?"res://artifacts/net-qa/profiles/":"user://")}net-identity{(Profile.Length > 0 ? "-" + Profile : "")}.json");
    static string ReconnectPath => ProjectSettings.GlobalizePath($"{(GameSession.IsQa?"res://artifacts/net-qa/profiles/":"user://")}net-reconnect{(Profile.Length > 0 ? "-" + Profile : "")}.json");

    public static NetIdentityFile Load()
    {
        NetIdentityFile? data = null;
        try { if (File.Exists(IdentityPath)) data = JsonSerializer.Deserialize<NetIdentityFile>(File.ReadAllText(IdentityPath)); } catch { data = null; }
        data ??= new();
        bool valid = false;
        try { valid = Convert.FromBase64String(data.Token).Length == NetProtocol.TokenBytes; } catch { valid = false; }
        if (!valid) { data.Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NetProtocol.TokenBytes)); Save(data); }
        data.Name = NetNames.Sanitize(data.Name.Length > 0 ? data.Name : "Hráč");
        data.LastPort = Math.Clamp(data.LastPort, 1024, 65535);
        return data;
    }
    public static void Save(NetIdentityFile data)
    {
        // user:// (AppData), deliberately NOT res://saves – a client must never touch the singleplayer save folder.
        try { GameSession.WriteAtomic(IdentityPath, data); } catch { }
    }
    public static byte[] TokenBytes(NetIdentityFile f) => Convert.FromBase64String(f.Token);
    /// <summary>Server-side identity key: SHA-256 of the token, lowercase hex. Stored in the host save, never the token.</summary>
    public static string KeyOf(ReadOnlySpan<byte> token) => Convert.ToHexString(SHA256.HashData(token)).ToLowerInvariant();

    public static void WriteReconnect(string address, int port, string reason, double minutes = 15)
    {
        try { GameSession.WriteAtomic(ReconnectPath, new NetReconnectFile { Address = address, Port = port, Reason = reason, ExpiresUtc = DateTime.UtcNow.AddMinutes(minutes) }); } catch { }
    }
    public static NetReconnectFile? ReadReconnect()
    {
        try
        {
            if (!File.Exists(ReconnectPath)) return null;
            var r = JsonSerializer.Deserialize<NetReconnectFile>(File.ReadAllText(ReconnectPath));
            if (r == null || r.ExpiresUtc < DateTime.UtcNow || r.Address.Length is 0 or > 253 || r.Port is < 1024 or > 65535) { ClearReconnect(); return null; }
            return r;
        }
        catch { return null; }
    }
    public static void ClearReconnect() { try { if (File.Exists(ReconnectPath)) File.Delete(ReconnectPath); } catch { } }

    // Host side of an update: remember the port so "--resume" re-opens the LAN game automatically.
    static string HostResumePath => ProjectSettings.GlobalizePath($"{(GameSession.IsQa?"res://artifacts/net-qa/profiles/":"user://")}net-host-resume{(Profile.Length > 0 ? "-" + Profile : "")}.json");
    public static void WriteHostResume(int port) { try { GameSession.WriteAtomic(HostResumePath, new NetReconnectFile { Address = "host", Port = port, Reason = "update", ExpiresUtc = DateTime.UtcNow.AddMinutes(15) }); } catch { } }
    public static int TakeHostResume()
    {
        try
        {
            if (!File.Exists(HostResumePath)) return 0;
            var r = JsonSerializer.Deserialize<NetReconnectFile>(File.ReadAllText(HostResumePath)); File.Delete(HostResumePath);
            return r != null && r.ExpiresUtc > DateTime.UtcNow && r.Port is >= 1024 and <= 65535 ? r.Port : 0;
        }
        catch { return 0; }
    }
}

/// <summary>
/// One instance lives under SceneTree.Root while a multiplayer session exists (menu → world → menu).
/// Owns the ENet peer and dispatches packets to <see cref="NetServer"/> or <see cref="NetClient"/>.
/// </summary>
public partial class NetSession : Node
{
    public static NetSession? Instance { get; private set; }
    public static NetMode Mode => Instance?.CurrentMode ?? NetMode.Single;
    public static bool IsHost => Mode == NetMode.Host;
    public static bool IsClient => Mode == NetMode.Client;

    public NetMode CurrentMode { get; private set; } = NetMode.Single;
    public ENetMultiplayerPeer? Peer { get; private set; }
    public NetServer? Server { get; private set; }
    public NetClient? Client { get; private set; }
    public NetIdentityFile Identity { get; private set; } = new();
    public string LocalName => Identity.Name;
    /// <summary>Message the main menu shows once (host left, rejected, …).</summary>
    public string MenuMessage { get; set; } = "";
    public BlockWorld? World { get; private set; }
    int pendingHostPort;
    public double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Game build version; must match exactly between host and client. Codex: point this at the updater's version constant.</summary>
    public static string GameVersion
    {
        get
        {
            var qa = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--net-qa-version="));
            if (qa != null) return qa["--net-qa-version=".Length..];
            return AutoUpdater.CurrentVersion;   // same constant the updater compares; reconnect only succeeds once both sides match
        }
    }

    public static NetSession Ensure(SceneTree tree)
    {
        if (Instance != null && IsInstanceValid(Instance)) return Instance;
        var s = new NetSession { Name = "NetSession", ProcessMode = ProcessModeEnum.Always };
        tree.Root.CallDeferred(Node.MethodName.AddChild, s);
        Instance = s; s.Identity = NetIdentity.Load();
        // Keep the Godot-free protocol constants honest.
        if (NetProtocol.WorldMin != BlockTerrain.WorldMin || NetProtocol.WorldMaxExclusive != BlockTerrain.WorldMaxExclusive || NetProtocol.MaxBuildY != BlockTerrain.MaxBuildY)
            throw new InvalidOperationException("NetProtocol world bounds differ from BlockTerrain.");
        return s;
    }
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public void SetLocalName(string name) { Identity.Name = NetNames.Sanitize(name); NetIdentity.Save(Identity); }

    // ---------------- host ----------------
    /// <summary>Called from the menu before the world scene loads. The server opens in <see cref="AttachWorld"/>.</summary>
    public void PrepareHost(int port)
    {
        CloseTransport();
        pendingHostPort = Math.Clamp(port, 1024, 65535); CurrentMode = NetMode.Host;
    }

    Error StartServer(int port)
    {
        var peer = new ENetMultiplayerPeer();
        // maxChannels 0 = ENet maximum; we use transfer channels 0..2.
        var error = peer.CreateServer(port, NetProtocol.EnetMaxClients, 0, 0, 0);
        if (error != Error.Ok) return error;
        peer.RefuseNewConnections = false;
        peer.PeerConnected += id => Server?.OnPeerConnected(id);
        peer.PeerDisconnected += id => Server?.OnPeerDisconnected(id);
        Peer = peer; return Error.Ok;
    }

    // ---------------- client ----------------
    public bool BeginJoin(string address, int port, int character, Action<string> status, Action<string> failed)
    {
        CloseTransport();
        address = address.Trim();
        if (address.Length is 0 or > 253 || address.Any(c => char.IsWhiteSpace(c))) { failed("Zadej adresu hostitele, např. 192.168.1.20."); return false; }
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address, Math.Clamp(port, 1024, 65535), 0, 0, 0, 0);
        if (error != Error.Ok) { failed("Připojení nelze zahájit (" + error + ")."); return false; }
        Peer = peer; CurrentMode = NetMode.Client;
        Identity.LastAddress = address; Identity.LastPort = port; NetIdentity.Save(Identity);
        Client = new NetClient(this, address, port, character, status, failed);
        return true;
    }

    // ---------------- world lifecycle ----------------
    /// <summary>HOOK: BlockWorld._Ready, after ApplySave/Respawn.</summary>
    public void AttachWorld(BlockWorld world)
    {
        World = world;
        if (CurrentMode == NetMode.Host && Server == null && pendingHostPort > 0)
        {
            int port = pendingHostPort;
            var error = StartServer(port);
            if (error != Error.Ok)
            {
                CurrentMode = NetMode.Single; pendingHostPort = 0;
                world.NetNotify($"Port {port} nelze otevřít ({error}). Hra běží jen pro tebe.");
                return;
            }
            Server = new NetServer(this, world, port);
            world.NetNotify($"Hra je otevřená pro LAN na portu {port}.");
        }
        else if (CurrentMode == NetMode.Client) Client?.AttachWorld(world);
    }
    /// <summary>HOOK: BlockWorld._ExitTree.</summary>
    public void DetachWorld(BlockWorld world)
    {
        if (World != world) return;
        World = null;
        Client?.DetachWorld();
    }

    public override void _Process(double delta)
    {
        if (Peer == null) { Client?.Tick(delta); return; }
        Peer.Poll();
        while (Peer != null && Peer.GetAvailablePacketCount() > 0)
        {
            int from = Peer.GetPacketPeer();
            int channel = Peer.GetPacketChannel();
            var packet = Peer.GetPacket();
            if (packet == null || packet.Length == 0) continue;
            if (CurrentMode == NetMode.Host) Server?.OnPacket(from, channel, packet);
            else if (CurrentMode == NetMode.Client) Client?.OnPacket(from, channel, packet);
        }
        if (CurrentMode == NetMode.Host) Server?.Tick(delta);
        else if (CurrentMode == NetMode.Client) Client?.Tick(delta);
    }

    public void Send(long peerId, byte[] packet, int channel, bool reliable = true)
    {
        if (Peer == null || packet.Length > (reliable ? NetProtocol.MaxPacketBytes : NetProtocol.MaxUnreliableBytes)) return;
        Peer.TransferChannel = channel;
        Peer.TransferMode = reliable ? MultiplayerPeer.TransferModeEnum.Reliable : MultiplayerPeer.TransferModeEnum.Unreliable;
        Peer.SetTargetPeer((int)peerId);
        Peer.PutPacket(packet);
    }

    /// <summary>Graceful disconnect: queued packets go out first (ENet disconnect_later).</summary>
    public void DisconnectPeerLater(long peerId)
    {
        try { Peer?.GetPeer((int)peerId)?.PeerDisconnectLater(); } catch { Peer?.DisconnectPeer((int)peerId); }
    }

    public ENetPacketPeer? PacketPeer(long id) { try { return Peer?.GetPeer((int)id); } catch { return null; } }

    void CloseTransport()
    {
        try { Peer?.Host?.Flush(); } catch { }
        Server?.Dispose();
        Peer?.Close(); Peer = null; Server = null; Client = null; pendingHostPort = 0;
    }

    /// <summary>
    /// HOOK: host leaving (menu / window close / update) AFTER a successful save, or client leaving.
    /// Sends a readable reason, flushes and closes. Mode returns to Single.
    /// </summary>
    public void EndSession(ShutdownReason reason, string text)
    {
        if (CurrentMode == NetMode.Host) Server?.BroadcastShutdown(reason, text);
        else if (CurrentMode == NetMode.Client) Client?.SendLeave(reason);
        CloseTransport(); CurrentMode = NetMode.Single; World = null;
    }

    /// <summary>
    /// HOOK for the future updater (AutoUpdater does not exist yet; it must call this BEFORE replacing files).
    /// Host: saves via callback, tells clients to reconnect after their own update, closes.
    /// Client: leaves and remembers the host for an automatic reconnect that only succeeds once versions match.
    /// Returns false if the host save failed (then the update must not proceed).
    /// </summary>
    public bool PrepareForUpdate(Func<bool> saveWorld)
    {
        if (CurrentMode == NetMode.Host)
        {
            if (!saveWorld()) return false;
            if (Server != null) NetIdentity.WriteHostResume(Server.Port);
            EndSession(ShutdownReason.HostUpdate, "Hostitel aktualizuje hru. Po aktualizaci se znovu připojíš automaticky.");
        }
        else if (CurrentMode == NetMode.Client && Client != null)
        {
            NetIdentity.WriteReconnect(Client.Address, Client.Port, "update");
            EndSession(ShutdownReason.HostUpdate, "update");
        }
        return true;
    }

    /// <summary>Client lost the host or was rejected: drop back to the menu with a message, never saving locally.</summary>
    public void ClientFailed(string message, bool reconnect = false)
    {
        if (reconnect && Client != null) NetIdentity.WriteReconnect(Client.Address, Client.Port, "host-update");
        MenuMessage = message;
        var world = World; var client = Client;
        CloseTransport(); CurrentMode = NetMode.Single; World = null;
        if (world != null && IsInstanceValid(world)) world.NetReturnToMenuWithoutSave();
        else client?.NotifyMenu(message);
    }

    /// <summary>Local LAN IPv4 addresses to show to the host.</summary>
    public static string[] LanAddresses() => IP.GetLocalAddresses()
        .Where(a => a.Contains('.') && !a.StartsWith("127.") && !a.StartsWith("169.254."))
        .Distinct().OrderBy(a => a.StartsWith("192.168.") ? 0 : 1).Take(4).ToArray();
}
