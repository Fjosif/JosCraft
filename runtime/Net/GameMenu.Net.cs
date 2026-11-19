// JOSCRAFT multiplayer – menu pages: "Hrát spolu (LAN)" with Host / Join, join progress, auto-reconnect.
// NAVRH: runtime/Net/GameMenu.Net.cs
using Godot;
using System;
using System.Linq;

public partial class GameMenu
{
    Label? netStatus; ProgressBar? netProgress; double reconnectTimer; NetReconnectFile? reconnect; bool joining;

    /// <summary>HOOK: one extra button in ShowMain (before "Nastavení").</summary>
    public void AddMultiplayerButton(Container box)
    {
        if (GameSession.IsQa) return;
        GameUi.AddButton("Hrát spolu (LAN)", ShowMultiplayer, box, "MultiplayerButton");
    }
    /// <summary>
    /// HOOK in _Ready, BEFORE the existing "--resume" line:
    ///   if(NetMenuReady()) return;   // a client reconnect page took over; do not load the singleplayer save
    /// A host that was updating gets its LAN game re-opened, then the normal --resume ContinueGame runs.
    /// </summary>
    public bool NetMenuReady()
    {
        if (GameSession.IsQa){if(NetQa.Active){CallDeferred(MethodName.StartNetFromMenu);return true;}return false;}
        var session = NetSession.Instance;
        if (session != null && session.MenuMessage.Length > 0) { var m = session.MenuMessage; session.MenuMessage = ""; ShowMain(m); }
        reconnect = NetIdentity.ReadReconnect();
        if (reconnect != null) { ShowReconnect(); return true; }
        if (OS.GetCmdlineUserArgs().Contains("--resume") && NetIdentity.TakeHostResume() is int port and > 0) NetSession.Ensure(GetTree()).PrepareHost(port);
        return false;
    }
    void StartNetFromMenu()=>NetQa.StartFromMenu(this);

    public void ShowMultiplayer()
    {
        CurrentPage = "multiplayer"; joining = false;
        var session = NetSession.Ensure(GetTree()); var id = session.Identity;
        var box = Page("Hrát spolu");
        box.AddChild(new Label { Text = "Jeden počítač hostuje svět, ostatní se připojí ve stejné síti (Wi-Fi/LAN). Nejvýše 4 hráči. Každý má vlastní batoh; svět je společný a ukládá se jen u hostitele.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new Label { Text = "Tvoje jméno ve hře" });
        var name = new LineEdit { Name = "NetNameInput", Text = id.Name, MaxLength = NetProtocol.MaxNameLength, CustomMinimumSize = new Vector2(0, 46), PlaceholderText = "např. Tygřík" }; box.AddChild(name);
        box.AddChild(new Label { Text = "Tvoje postava" });
        var heroes = new OptionButton { Name = "NetCharacterPicker", CustomMinimumSize = new Vector2(0, 46) }; foreach (var n in Viewer.Names) heroes.AddItem(n); heroes.Select(Math.Clamp(BlockWorld.SelectedCharacter, 0, Viewer.Names.Length - 1)); box.AddChild(heroes);
        var error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };

        // ---- host ----
        var hostTitle = new Label { Text = "Hostovat na tomto počítači" }; hostTitle.AddThemeFontSizeOverride("font_size", 22); box.AddChild(hostTitle);
        var addresses = NetSession.LanAddresses();
        box.AddChild(new Label { Text = addresses.Length > 0 ? "Ostatní zadají adresu: " + string.Join(", ", addresses) : "Adresu počítače najdeš v nastavení sítě Windows.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new Label { Text = "Port (výchozí 24567)" });
        var hostPort = new LineEdit { Name = "NetHostPort", Text = NetProtocol.DefaultPort.ToString(), MaxLength = 5, CustomMinimumSize = new Vector2(0, 46) }; box.AddChild(hostPort);
        bool Prepare(out int port)
        {
            port = 0;
            if (!int.TryParse(hostPort.Text, out port) || port is < 1024 or > 65535) { error.Text = "Port musí být číslo 1024–65535."; return false; }
            session.SetLocalName(name.Text); BlockWorld.SelectedCharacter = heroes.Selected; session.PrepareHost(port); return true;
        }
        if (GameSession.HasSave)
            GameUi.AddButton("Hostovat uloženou hru", () => { if (Prepare(out _) && !ContinueGame()) session.EndSession(ShutdownReason.HostMenu, ""); }, box, "HostContinueButton");
        GameUi.AddButton("Hostovat nový svět", () => { if (Prepare(out _)) StartNew(unchecked((int)GD.Randi()), heroes.Selected); }, box, "HostNewButton");
        box.AddChild(new Label { Text = "Windows se může zeptat na povolení sítě pro Godot/JOSCRAFT – pro hraní v domácí síti zvol „Soukromé sítě“. Hra sama nic v bráně firewall nemění.", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        // ---- join ----
        var joinTitle = new Label { Text = "Připojit se ke hře" }; joinTitle.AddThemeFontSizeOverride("font_size", 22); box.AddChild(joinTitle);
        box.AddChild(new Label { Text = "Adresa hostitele" });
        var address = new LineEdit { Name = "NetAddressInput", Text = id.LastAddress, PlaceholderText = "192.168.1.20", MaxLength = 253, CustomMinimumSize = new Vector2(0, 46) }; box.AddChild(address);
        box.AddChild(new Label { Text = "Port" });
        var joinPort = new LineEdit { Name = "NetJoinPort", Text = id.LastPort.ToString(), MaxLength = 5, CustomMinimumSize = new Vector2(0, 46) }; box.AddChild(joinPort);
        GameUi.AddButton("Připojit", () =>
        {
            if (!int.TryParse(joinPort.Text, out int port) || port is < 1024 or > 65535) { error.Text = "Port musí být číslo 1024–65535."; return; }
            session.SetLocalName(name.Text); BlockWorld.SelectedCharacter = heroes.Selected;
            StartJoin(address.Text, port, heroes.Selected);
        }, box, "JoinButton");
        box.AddChild(error);
        GameUi.AddButton("Zpět", () => ShowMain(), box);
    }

    void StartJoin(string address, int port, int character)
    {
        var session = NetSession.Ensure(GetTree());
        CurrentPage = "joining"; joining = true;
        var box = Page("Připojování");
        netStatus = new Label { Text = "…", AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(netStatus);
        netProgress = new ProgressBar { MinValue = 0, MaxValue = 1, CustomMinimumSize = new Vector2(0, 18), ShowPercentage = false }; box.AddChild(netProgress);
        GameUi.AddButton("Zrušit", () => { joining = false; session.EndSession(ShutdownReason.HostMenu, ""); ShowMultiplayer(); }, box, "CancelJoinButton");
        session.BeginJoin(address, port, character,
            status: text => { if (netStatus != null && IsInstanceValid(netStatus)) netStatus.Text = text; },
            failed: text => { joining = false; if (IsInsideTree()) ShowMain(text); });
    }

    void ShowReconnect()
    {
        CurrentPage = "reconnect";
        var box = Page("Znovu připojit");
        netStatus = new Label { Text = $"Hostitel {reconnect!.Address}:{reconnect.Port} se aktualizoval. Připojím se znovu, jakmile budou obě hry ve stejné verzi.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(netStatus);
        GameUi.AddButton("Zkusit hned", () => { reconnectTimer = 0; TryReconnect(); }, box);
        GameUi.AddButton("Zrušit automatické připojení", () => { NetIdentity.ClearReconnect(); reconnect = null; ShowMain(); }, box);
        reconnectTimer = 2;
    }
    void TryReconnect()
    {
        if (reconnect == null) return;
        var r = reconnect; var session = NetSession.Ensure(GetTree());
        CurrentPage = "joining"; joining = true;
        var box = Page("Znovu připojit");
        netStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(netStatus);
        netProgress = new ProgressBar { MinValue = 0, MaxValue = 1, CustomMinimumSize = new Vector2(0, 18), ShowPercentage = false }; box.AddChild(netProgress);
        GameUi.AddButton("Zrušit", () => { joining = false; NetIdentity.ClearReconnect(); reconnect = null; session.EndSession(ShutdownReason.HostMenu, ""); ShowMain(); }, box);
        session.BeginJoin(r.Address, r.Port, BlockWorld.SelectedCharacter,
            status: text => { if (netStatus != null && IsInstanceValid(netStatus)) netStatus.Text = text; },
            failed: text =>
            {
                joining = false; if (!IsInsideTree()) return;
                // Version mismatch or host still down: keep waiting until the reconnect window expires.
                reconnect = NetIdentity.ReadReconnect();
                if (reconnect != null) { ShowReconnect(); if (netStatus != null) netStatus.Text = text + "\nZkusím to znovu za 10 s."; reconnectTimer = 10; }
                else ShowMain(text);
            });
    }

    /// <summary>HOOK: call from GameMenu._Process (after the existing layout code).</summary>
    public void NetMenuProcess(double delta)
    {
        if (joining && netProgress != null && IsInstanceValid(netProgress) && NetSession.Instance?.Client is { } c) netProgress.Value = c.Progress;
        if (CurrentPage == "reconnect" && reconnect != null) { reconnectTimer -= delta; if (reconnectTimer <= 0) TryReconnect(); }
    }
}
