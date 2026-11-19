using Godot;
using System;
using System.Linq;

public partial class GameMenu : Control
{
    PanelContainer panel=null!;
    VBoxContainer contents=null!;
    public string CurrentPage {get;private set;}="main";
    public override void _Ready()
    {
        GetWindow().Title="JOSCRAFT "+AutoUpdater.CurrentVersion;
        GetTree().AutoAcceptQuit=true;
        GameSession.IsQa=OS.GetCmdlineUserArgs().Any(x=>x is "--game-qa" or "--world-qa" or "--grass-qa" or "--tree-qa" or "--survival-qa" or "--update-qa" or "--building-qa"||x.StartsWith("--net-qa="));
        if(OS.GetCmdlineUserArgs().Contains("--building-qa")){CallDeferred(MethodName.EnterBuildingQa);return;}
        GameSession.LoadSettings();Theme=GameUi.CreateTheme();
        var background=new ColorRect {Color=new Color("142637")};background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(background);
        // Quiet, original block silhouettes keep the menu independent of a live world.
        for(int i=0;i<14;i++){var b=new ColorRect {Color=i%2==0?new Color("1c3840"):new Color("254b43"),MouseFilter=MouseFilterEnum.Ignore,Position=new Vector2(i*140-120,400+(i%4)*32),Size=new Vector2(145,900)};AddChild(b);}
        panel=new PanelContainer();AddChild(panel);ShowMain();
        if(NetMenuReady())return;
        if(OS.GetCmdlineUserArgs().Contains("--resume")&&GameSession.HasSave)CallDeferred(MethodName.ContinueGame);
    }
    void EnterBuildingQa(){GameSession.Pending=null;GetTree().ChangeSceneToFile("res://world.tscn");}
    public override void _Process(double delta)
    {
        if(panel==null)return;float w=Math.Min(580,Size.X-32),h=Math.Min(CurrentPage=="main"?600:700,Size.Y-32);
        panel.Position=(Size-new Vector2(w,h))*.5f;panel.Size=new Vector2(w,h);
        NetMenuProcess(delta);
    }
    VBoxContainer Page(string title)
    {
        foreach(Node n in panel.GetChildren()){panel.RemoveChild(n);n.QueueFree();}
        var scroll=new ScrollContainer {HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};panel.AddChild(scroll);
        contents=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};contents.AddThemeConstantOverride("separation",12);scroll.AddChild(contents);
        var label=new Label {Text=title,HorizontalAlignment=HorizontalAlignment.Center};label.AddThemeFontSizeOverride("font_size",32);contents.AddChild(label);return contents;
    }
    public void ShowMain(string message="")
    {
        CurrentPage="main";var box=Page("JOSCRAFT");box.AddChild(new Label {Text="PŘEŽITÍ · SBÍREJ A STAV · "+AutoUpdater.CurrentVersion,HorizontalAlignment=HorizontalAlignment.Center});
        GameUi.AddButton(GameSession.HasSave?"Pokračovat":"Hrát",()=>{if(GameSession.HasSave)ContinueGame();else ShowNewWorld();},box);
        GameUi.AddButton("Nový svět",ShowNewWorld,box,"NewWorldButton");AddMultiplayerButton(box);GameUi.AddButton("Nastavení",ShowSettings,box,"MainSettingsButton");
        GameUi.AddButton("Ateliér postav",()=>GetTree().ChangeSceneToFile("res://main.tscn"),box);GameUi.AddButton("Konec",()=>GetTree().Quit(),box);
        if(message.Length>0)box.AddChild(new Label {Text=message,AutowrapMode=TextServer.AutowrapMode.WordSmart});
    }
    public void ShowNewWorld()
    {
        CurrentPage="new";var box=Page("Nový svět");box.AddChild(new Label {Text="Prázdný batoh. Suroviny získáš těžením.\nStejný seed vytváří stejnou krajinu.",AutowrapMode=TextServer.AutowrapMode.WordSmart});
        box.AddChild(new Label {Text="Číslo světa (seed)"});var seed=new LineEdit {Name="SeedInput",Text=unchecked((int)GD.Randi()).ToString(),CustomMinimumSize=new Vector2(0,46),MaxLength=11};box.AddChild(seed);
        box.AddChild(new Label {Text="Tvoje postava"});var heroes=new OptionButton {Name="CharacterPicker",CustomMinimumSize=new Vector2(0,46)};foreach(var n in Viewer.Names)heroes.AddItem(n);heroes.Select(Math.Clamp(BlockWorld.SelectedCharacter,0,10));box.AddChild(heroes);
        if(GameSession.HasSave)box.AddChild(new Label {Text="Nový svět nahradí uloženou hru až při uložení. Předchozí uložení zůstane také jako místní .bak.",AutowrapMode=TextServer.AutowrapMode.WordSmart});
        var error=new Label {AutowrapMode=TextServer.AutowrapMode.WordSmart};box.AddChild(error);
        GameUi.AddButton("Vstoupit do světa",()=>{if(int.TryParse(seed.Text,out int value))StartNew(value,heroes.Selected);else error.Text="Zadej celé číslo od −2147483648 do 2147483647.";},box,"StartNewButton");GameUi.AddButton("Zpět",()=>ShowMain(),box);
    }
    public void StartNew(int seed,int character)
    {
        GameSession.Pending=new SavedGame {Seed=seed,Character=Math.Clamp(character,0,10),TreeExclusions=Array.Empty<int>()};
        GameSession.CaptureAfterStart=true;BlockWorld.SelectedCharacter=Math.Clamp(character,0,10);GetTree().ChangeSceneToFile("res://world.tscn");
    }
    public bool ContinueGame()
    {
        var data=GameSession.Read(GameSession.SavePath);if(data==null){ShowMain("Uloženou hru nelze přečíst. Zůstala zachovaná; můžeš založit nový svět.");return false;}
        GameSession.Pending=data;GameSession.CaptureAfterStart=true;BlockWorld.SelectedCharacter=data.Character;GetTree().ChangeSceneToFile("res://world.tscn");return true;
    }
    void ShowSettings(){CurrentPage="settings";var box=Page("Nastavení");GameUi.AddSettings(box,()=>ShowMain());}
}
