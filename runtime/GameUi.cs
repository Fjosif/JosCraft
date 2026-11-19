using Godot;
using System;
using System.Linq;

public enum GameMode {Playing,Paused,Inventory,Settings}

public partial class GameUi : CanvasLayer
{
    readonly BlockWorld world;
    Control root=null!,hud=null!,modal=null!,touch=null!;
    HBoxContainer hotbar=null!;
    Label feedback=null!,carried=null!;
    ProgressBar mining=null!;
    PanelContainer modalPanel=null!;
    VBoxContainer modalContents=null!;
    Control inventoryCard=null!;
    SubViewport petViewport=null!;
    int previewCharacter=-1;
    int lastWidth;
    public GameUi(BlockWorld world){this.world=world;}
    public static Theme CreateTheme()
    {
        var theme=new Theme {DefaultFontSize=18};
        var panel=new StyleBoxFlat {BgColor=new Color("142637f5"),CornerRadiusTopLeft=12,CornerRadiusTopRight=12,CornerRadiusBottomLeft=12,CornerRadiusBottomRight=12,ContentMarginLeft=18,ContentMarginRight=18,ContentMarginTop=16,ContentMarginBottom=16};
        var button=new StyleBoxFlat {BgColor=new Color("304c60"),CornerRadiusTopLeft=7,CornerRadiusTopRight=7,CornerRadiusBottomLeft=7,CornerRadiusBottomRight=7,ContentMarginLeft=10,ContentMarginRight=10,ContentMarginTop=8,ContentMarginBottom=8};
        var hover=(StyleBoxFlat)button.Duplicate();hover.BgColor=new Color("496e7c");
        foreach(string type in new[]{"Button","OptionButton"}){theme.SetStylebox("normal",type,button);theme.SetStylebox("hover",type,hover);theme.SetStylebox("pressed",type,hover);theme.SetColor("font_color",type,Colors.White);theme.SetColor("font_hover_color",type,Colors.White);theme.SetColor("font_pressed_color",type,Colors.White);theme.SetColor("font_disabled_color",type,new Color("aebcc5"));}
        theme.SetStylebox("panel","PanelContainer",panel);theme.SetStylebox("panel","PopupMenu",panel);theme.SetStylebox("normal","LineEdit",button);theme.SetStylebox("focus","LineEdit",hover);
        foreach(string type in new[]{"Label","LineEdit","PopupMenu","CheckButton"})theme.SetColor("font_color",type,Colors.White);
        theme.SetColor("font_placeholder_color","LineEdit",new Color("c6d5e6"));theme.SetColor("caret_color","LineEdit",Colors.White);return theme;
    }
    public static Button AddButton(string text,Action action,Container parent,string name="")
    {
        var button=new Button {Text=text,CustomMinimumSize=new Vector2(0,46),FocusMode=Control.FocusModeEnum.None};if(name.Length>0)button.Name=name;button.Pressed+=action;parent.AddChild(button);return button;
    }
    public static void AddSettings(Container parent,Action back)
    {
        parent.AddChild(new Label {Text="Citlivost myši"});var sensitivity=new HSlider {MinValue=1,MaxValue=12,Step=.25,Value=GameSession.Settings.MouseSensitivity*1000,CustomMinimumSize=new Vector2(0,40)};sensitivity.ValueChanged+=v=>{GameSession.Settings.MouseSensitivity=(float)v/1000;GameSession.SaveSettings();};parent.AddChild(sensitivity);
        var fovLabel=new Label {Text=$"Zorné pole: {GameSession.Settings.Fov:0}°"};parent.AddChild(fovLabel);var fov=new HSlider {MinValue=55,MaxValue=100,Step=1,Value=GameSession.Settings.Fov,CustomMinimumSize=new Vector2(0,40)};fov.ValueChanged+=v=>{GameSession.Settings.Fov=(float)v;fovLabel.Text=$"Zorné pole: {v:0}°";GameSession.SaveSettings();};parent.AddChild(fov);
        parent.AddChild(new Label {Text="Dotykové ovládání"});var touch=new OptionButton {CustomMinimumSize=new Vector2(0,46)};foreach(var text in new[]{"Automaticky podle velikosti okna","Vždy zapnuté","Vypnuté"})touch.AddItem(text);touch.Select(GameSession.Settings.TouchMode);touch.ItemSelected+=i=>{GameSession.Settings.TouchMode=(int)i;GameSession.SaveSettings();};parent.AddChild(touch);
        parent.AddChild(new Label {Text="WASD pohyb · mezerník skok\nCtrl běh · Shift opatrná chůze u hrany\nLevé tlačítko držet: těžit · pravé: položit\nProstřední: vybrat vlastněný materiál\nE batoh · 1–9 / kolečko rychlá lišta\nF5 / V pohled · Esc pauza\nKlik do světa zachytí myš; pauza ji uvolní.",AutowrapMode=TextServer.AutowrapMode.WordSmart});AddButton("Zpět",back,parent,"SettingsBackButton");
    }
    public override void _Ready()
    {
        root=new Control {Theme=CreateTheme()};root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);root.MouseFilter=Control.MouseFilterEnum.Ignore;AddChild(root);
        hud=new Control {MouseFilter=Control.MouseFilterEnum.Ignore};hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);root.AddChild(hud);
        var crosshair=new Label {Text="+",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MouseFilter=Control.MouseFilterEnum.Ignore,OffsetLeft=-12,OffsetRight=12,OffsetTop=-14,OffsetBottom=14};crosshair.SetAnchor(Side.Left,.5f);crosshair.SetAnchor(Side.Right,.5f);crosshair.SetAnchor(Side.Top,.5f);crosshair.SetAnchor(Side.Bottom,.5f);crosshair.AddThemeFontSizeOverride("font_size",24);crosshair.AddThemeColorOverride("font_outline_color",new Color("182a31"));crosshair.AddThemeConstantOverride("outline_size",5);hud.AddChild(crosshair);
        hotbar=new HBoxContainer();hotbar.AddThemeConstantOverride("separation",3);hud.AddChild(hotbar);
        feedback=new Label {HorizontalAlignment=HorizontalAlignment.Center,AutowrapMode=TextServer.AutowrapMode.WordSmart,MouseFilter=Control.MouseFilterEnum.Ignore};hud.AddChild(feedback);
        mining=new ProgressBar {MinValue=0,MaxValue=1,ShowPercentage=false,MouseFilter=Control.MouseFilterEnum.Ignore};hud.AddChild(mining);
        touch=new Control {MouseFilter=Control.MouseFilterEnum.Ignore};touch.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);hud.AddChild(touch);BuildTouch();RefreshHotbar();
        modal=new Control();modal.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);root.AddChild(modal);modal.Visible=false;
    }
    void BuildTouch()
    {
        var move=new GridContainer {Columns=3,Position=new Vector2(12,0)};move.Name="TouchMove";touch.AddChild(move);
        void Hold(string text,Action<bool> action){var b=AddButton(text,()=>{},move);b.CustomMinimumSize=new Vector2(46,46);b.ButtonDown+=()=>action(true);b.ButtonUp+=()=>action(false);b.MouseExited+=()=>action(false);}
        Hold("←",b=>world.SetTouchAxis(true,b?-1:0));Hold("↑",b=>world.SetTouchAxis(false,b?-1:0));Hold("↓",b=>world.SetTouchAxis(false,b?1:0));Hold("→",b=>world.SetTouchAxis(true,b?1:0));AddButton("Skok",world.RequestJump,move);Hold("Běh",world.SetTouchRun);
        var actions=new VBoxContainer {Name="TouchActions"};touch.AddChild(actions);AddButton("Pauza",()=>world.SetPaused(true),actions);AddButton("Batoh",world.ToggleInventory,actions);AddButton("Pohled",()=>world.SetFirstPerson(!world.FirstPerson),actions);
        var mine=AddButton("Těžit",()=>{},actions);mine.ButtonDown+=()=>world.SetMiningHeld(true);mine.ButtonUp+=()=>world.SetMiningHeld(false);mine.MouseExited+=()=>world.SetMiningHeld(false);AddButton("Použít",()=>world.UseSelected(),actions);
        var sneak=AddButton("Opatrně",()=>{},actions);sneak.ButtonDown+=()=>world.SetTouchSneak(true);sneak.ButtonUp+=()=>world.SetTouchSneak(false);sneak.MouseExited+=()=>world.SetTouchSneak(false);
    }
    public void RefreshHotbar()
    {
        if(hotbar==null)return;foreach(Node n in hotbar.GetChildren()){hotbar.RemoveChild(n);n.QueueFree();}
        for(int i=0;i<9;i++){int index=i;var b=SlotButton(i,false);b.Pressed+=()=>world.SelectHotbar(index);hotbar.AddChild(b);}
    }
    Button SlotButton(int index,bool inventory)
    {
        var stack=world.GetSlot(index);var button=new Button {Name=inventory?"InventorySlot"+index:"HotbarSlot"+index,CustomMinimumSize=new Vector2(38,44),ExpandIcon=true,IconAlignment=HorizontalAlignment.Center,TooltipText=stack.Count>0?BlockCatalog.Get(stack.Type).Name+" · "+stack.Count:"Prázdné místo",FocusMode=Control.FocusModeEnum.None};
        if(stack.Count>0)
        {
            button.Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(stack.Type).IconPath);
            var count=new Label {Name="StackCount",Text=stack.Count.ToString(),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,MouseFilter=Control.MouseFilterEnum.Ignore};count.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);count.AddThemeFontSizeOverride("font_size",14);count.AddThemeColorOverride("font_color",Colors.White);count.AddThemeConstantOverride("outline_size",4);count.AddThemeColorOverride("font_outline_color",new Color("10212b"));button.AddChild(count);
        }
        if(index<9){var key=new Label {Name="SlotKey",Text=(index+1).ToString(),MouseFilter=Control.MouseFilterEnum.Ignore,Position=new Vector2(3,1)};key.AddThemeFontSizeOverride("font_size",11);button.AddChild(key);}
        button.AddThemeFontSizeOverride("font_size",14);button.AddThemeConstantOverride("icon_max_width",27);
        if(index==world.SelectedHotbar && !inventory){var selected=new StyleBoxFlat {BgColor=new Color("4d716b"),BorderColor=new Color("ffe4a0"),BorderWidthLeft=2,BorderWidthTop=2,BorderWidthRight=2,BorderWidthBottom=2,ContentMarginLeft=2,ContentMarginRight=2};button.AddThemeStyleboxOverride("normal",selected);}
        else {var style=new StyleBoxFlat {BgColor=new Color("19313fe8"),BorderColor=new Color("82969d"),BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,ContentMarginLeft=2,ContentMarginRight=2};button.AddThemeStyleboxOverride("normal",style);}
        return button;
    }
    VBoxContainer Modal(string title)
    {
        inventoryCard=null!;if(petViewport!=null){petViewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;petViewport.ProcessMode=Node.ProcessModeEnum.Disabled;}
        foreach(Node n in modal.GetChildren()){modal.RemoveChild(n);n.QueueFree();}
        var dim=new ColorRect {Color=new Color(0,0,0,.58f)};dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);modal.AddChild(dim);
        modalPanel=new PanelContainer();modal.AddChild(modalPanel);
        var scroll=new ScrollContainer {HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};modalPanel.AddChild(scroll);modalContents=new VBoxContainer {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};modalContents.AddThemeConstantOverride("separation",10);scroll.AddChild(modalContents);
        var label=new Label {Text=title,HorizontalAlignment=HorizontalAlignment.Center};label.AddThemeFontSizeOverride("font_size",28);modalContents.AddChild(label);modal.Visible=true;return modalContents;
    }
    public void ShowMode()
    {
        if(world.UiMode==GameMode.Playing){modal.Visible=false;if(petViewport!=null){petViewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;petViewport.ProcessMode=Node.ProcessModeEnum.Disabled;}return;}
        if(world.UiMode==GameMode.Inventory){ShowInventory();return;}
        if(world.UiMode==GameMode.Settings){var b=Modal("Nastavení");AddSettings(b,()=>world.SetPaused(true));return;}
        var box=Modal("Pauza");AddButton("Zpět do hry",()=>world.SetPaused(false),box,"ResumeButton");AddButton("Nastavení",()=>world.ShowSettings(),box,"PauseSettingsButton");AddButton("Bezpečný návrat na start",()=>{world.Respawn();world.SetPaused(false);},box,"SafeRespawnButton");AddButton(NetPauseUi.SaveAndMenuLabel,()=>world.ReturnToMenu(),box,"SaveAndMenuButton");NetPauseUi.AddInfo(box);
    }
    public void ShowInventory()
    {
        if(world.IsChestOpen){ShowChestInventory();return;}
        slotWidgets.Clear();
        foreach(Node n in modal.GetChildren()){modal.RemoveChild(n);n.QueueFree();}modalPanel=null!;
        var dim=new ColorRect {Color=new Color(0,0,0,.58f)};dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);modal.AddChild(dim);
        var lightTheme=(Theme)root.Theme.Duplicate();lightTheme.DefaultFontSize=16;lightTheme.SetColor("font_color","Label",new Color("242a2d"));lightTheme.SetColor("font_color","Button",new Color("242a2d"));lightTheme.SetColor("font_hover_color","Button",new Color("242a2d"));lightTheme.SetColor("font_pressed_color","Button",new Color("242a2d"));
        inventoryCard=new Control {Name="SurvivalInventoryCard",Size=new Vector2(344,464),Theme=lightTheme};modal.AddChild(inventoryCard);
        var paper=new Panel {Size=inventoryCard.Size,MouseFilter=Control.MouseFilterEnum.Ignore};paper.AddThemeStyleboxOverride("panel",new StyleBoxFlat {BgColor=new Color("c8ced0"),BorderColor=new Color("737e83"),BorderWidthTop=2,BorderWidthLeft=2,BorderWidthRight=2,BorderWidthBottom=2,CornerRadiusTopLeft=5,CornerRadiusTopRight=5,CornerRadiusBottomLeft=5,CornerRadiusBottomRight=5});inventoryCard.AddChild(paper);
        void Text(string value,Vector2 position,int size=16){var label=new Label {Text=value,Position=position,MouseFilter=Control.MouseFilterEnum.Ignore};label.AddThemeFontSizeOverride("font_size",size);inventoryCard.AddChild(label);}
        Text(world.IsFurnaceOpen?"Pec":world.CraftSize==3?"Pracovní stůl":"Inventář",new Vector2(12,8),22);
        EnsurePetPreview();
        var portrait=new TextureRect {Name="SelectedPetPreview",Position=new Vector2(48,47),Size=new Vector2(116,140),Texture=petViewport.GetTexture(),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore};inventoryCard.AddChild(portrait);
        Text(Viewer.Names[world.CurrentCharacter],new Vector2(48,189),12);
        for(int i=0;i<4;i++)inventoryCard.AddChild(new ArmorSlotGlyph(i) {Position=new Vector2(12,47+i*36),Size=new Vector2(32,32),TooltipText="Prázdné místo pro výbavu. Výbava přijde v další etapě."});
        Text(world.IsFurnaceOpen?"Vypalování":"Výroba",new Vector2(184,45),16);
        var craft=new GridContainer {Name=world.CraftSize==3?"LargeCraftingGrid":"SmallCraftingGrid",Columns=world.CraftSize,Position=new Vector2(184,77)};inventoryCard.AddChild(craft);
        if(!world.IsFurnaceOpen)for(int i=0;i<world.CraftSize*world.CraftSize;i++)
        {
            int index=i;var stack=world.GetCraftSlot(i);var b=new Button {Name="CraftSlot"+i,CustomMinimumSize=new Vector2(32,32),ExpandIcon=true,IconAlignment=HorizontalAlignment.Center,FocusMode=Control.FocusModeEnum.None};
            if(stack.Count>0){b.Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(stack.Type).IconPath);AddCount(b,stack.Count);}
            b.AddThemeConstantOverride("icon_max_width",26);LightSlot(b);craft.AddChild(b);WireSlot(b,1,index);
        }
        Text("→",new Vector2(world.CraftSize==3?294:256,105),19);
        if(world.IsFurnaceOpen)BuildFurnaceSlots(inventoryCard);
        else
        {
            var result=world.CraftOutput;var output=new Button {Name="CraftOutputSlot",Disabled=result.Count==0,Position=new Vector2(world.CraftSize==3?309:280,110),Size=new Vector2(27,32),ExpandIcon=true,TooltipText=result.Count>0?BlockCatalog.Get(result.Type).Name:"Slož suroviny podle receptu."};LightSlot(output);inventoryCard.AddChild(output);
            if(result.Count>0){output.Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(result.Type).IconPath);AddCount(output,result.Count);}output.Pressed+=()=>world.CraftOnce();
            Text(world.CraftSize==3?"3 × 3":"2 × 2",new Vector2(187,190),12);
            if(world.RecipeOptions.Length>1)
            {
                var options=new OptionButton{Position=new Vector2(184,154),Size=new Vector2(148,28),CustomMinimumSize=new Vector2(0,28)};
                options.AddThemeFontSizeOverride("font_size",11);
                foreach(var recipe in world.RecipeOptions)options.AddItem(recipe.Recipe.Output==22?"4 klacíky · JOSCRAFT":"4 prkna");
                options.Select(world.RecipeChoice%world.RecipeOptions.Length);options.ItemSelected+=i=>world.ChooseRecipe((int)i);LightSlot(options);inventoryCard.AddChild(options);
            }
        }
        var recipes=new Button{Icon=ResourceLoader.Load<Texture2D>("res://assets/icons/recipe-book.svg"),ExpandIcon=true,TooltipText="Receptář",Position=new Vector2(304,44),Size=new Vector2(28,28),FocusMode=Control.FocusModeEnum.None};LightSlot(recipes);recipes.Pressed+=ShowRecipes;inventoryCard.AddChild(recipes);
        Text("Batoh",new Vector2(12,208),14);
        var storage=new GridContainer {Name="StorageGrid",Columns=9,Position=new Vector2(12,230)};inventoryCard.AddChild(storage);
        var bar=new GridContainer {Name="InventoryHotbar",Columns=9,Position=new Vector2(12,348)};inventoryCard.AddChild(bar);
        foreach(var range in new[]{(Start:9,Count:27,Grid:storage),(Start:0,Count:9,Grid:bar)})
            for(int i=range.Start;i<range.Start+range.Count;i++){int slot=i;var b=SlotButton(i,true);b.CustomMinimumSize=new Vector2(32,32);LightSlot(b);b.GetNodeOrNull<Label>("SlotKey")?.AddThemeColorOverride("font_color",new Color("30383b"));range.Grid.AddChild(b);WireSlot(b,0,slot);}
        Text("Táhni: rozdělit · Pravé: půlka / jeden kus",new Vector2(12,393),12);
        var close=new Button {Name="CloseInventoryButton",Text="Zavřít (E)",Position=new Vector2(12,420),Size=new Vector2(320,32),FocusMode=Control.FocusModeEnum.None};close.AddThemeStyleboxOverride("normal",new StyleBoxFlat {BgColor=new Color("a4afb2"),BorderColor=new Color("657075"),BorderWidthBottom=2,ContentMarginLeft=6,ContentMarginRight=6});close.Pressed+=world.ToggleInventory;inventoryCard.AddChild(close);modal.Visible=true;
        if(bookOpen)BuildRecipePanel();BuildRecipeGhost();
    }
    static void AddCount(Button button,int amount)
    {
        var count=new Label {Name="StackCount",Text=amount.ToString(),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,MouseFilter=Control.MouseFilterEnum.Ignore};count.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);count.AddThemeFontSizeOverride("font_size",14);count.AddThemeColorOverride("font_color",Colors.White);count.AddThemeConstantOverride("outline_size",4);count.AddThemeColorOverride("font_outline_color",new Color("10212b"));button.AddChild(count);
    }
    static void LightSlot(Button button)
    {
        var normal=new StyleBoxFlat {BgColor=new Color("969fa3"),BorderColor=new Color("687479"),BorderWidthTop=2,BorderWidthLeft=2,BorderWidthRight=1,BorderWidthBottom=1,ContentMarginLeft=2,ContentMarginRight=2,ContentMarginTop=0,ContentMarginBottom=0};var hover=(StyleBoxFlat)normal.Duplicate();hover.BgColor=new Color("b0bbbd");
        button.AddThemeStyleboxOverride("normal",normal);button.AddThemeStyleboxOverride("disabled",normal);button.AddThemeStyleboxOverride("hover",hover);button.AddThemeStyleboxOverride("pressed",hover);
    }
    void EnsurePetPreview()
    {
        if(petViewport!=null&&previewCharacter!=world.CurrentCharacter){RemoveChild(petViewport);petViewport.QueueFree();petViewport=null!;}
        if(petViewport==null)
        {
            previewCharacter=world.CurrentCharacter;petViewport=new SubViewport {Name="InventoryPetViewport",Size=new Vector2I(240,300),OwnWorld3D=true};AddChild(petViewport);
            var scene=new Node3D();petViewport.AddChild(scene);
            scene.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("354146"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.35f,TonemapMode=Godot.Environment.ToneMapper.Linear}});
            scene.AddChild(new DirectionalLight3D {RotationDegrees=new Vector3(-25,145,0),LightEnergy=.55f});
            var pet=ResourceLoader.Load<PackedScene>($"res://assets/characters/{Viewer.Ids[previewCharacter]}.glb").Instantiate<Node3D>();pet.Name="SelectedPet";scene.AddChild(pet);
            foreach(var mesh in Viewer.Descendants(pet).OfType<MeshInstance3D>())for(int s=0;s<mesh.Mesh.GetSurfaceCount();s++)if(mesh.GetActiveMaterial(s) is StandardMaterial3D mat){mat.VertexColorUseAsAlbedo=true;mat.AlbedoColor=Colors.White;}
            var animation=Viewer.Descendants(pet).OfType<AnimationPlayer>().First();string idle=BlockWorld.ClipName(animation,"Idle");animation.GetAnimation(idle).LoopMode=Animation.LoopModeEnum.Linear;animation.Play(idle);
            var camera=new Camera3D {Position=new Vector3(0,1.55f,-5.4f),Fov=35,Current=true};scene.AddChild(camera);camera.LookAt(new Vector3(0,1.3f,0));
        }
        petViewport.ProcessMode=Node.ProcessModeEnum.Inherit;petViewport.RenderTargetUpdateMode=SubViewport.UpdateMode.WhenVisible;
    }
    public override void _Process(double delta)
    {
        if(root==null)return;var size=GetViewport().GetVisibleRect().Size;bool small=size.X<700;
        UpdateInventoryCursor();
        float slot=Math.Min(48,(size.X-26)/9);hotbar.Position=new Vector2((size.X-(slot*9-3))*.5f,size.Y-60);foreach(Button b in hotbar.GetChildren())b.CustomMinimumSize=new Vector2(slot-3,44);
        feedback.Position=new Vector2(12,size.Y-94);feedback.Size=new Vector2(size.X-24,28);feedback.Text=world.Feedback;
        if(world.Feedback.Length==0&&world.Ecology!=null)feedback.Text=$"♥ {world.Health}/20   ·   {world.Ecology.Clock}   {(world.Ecology.IsNight?"Noc":"Den")}";
        mining.Position=new Vector2(size.X*.5f-56,size.Y*.5f+24);mining.Size=new Vector2(112,8);mining.Value=world.MiningProgress;mining.Visible=world.MiningProgress>0;
        touch.Visible=world.UiMode==GameMode.Playing&&(GameSession.Settings.TouchMode==1||(GameSession.Settings.TouchMode==0&&small));touch.GetNode<Control>("TouchMove").Position=new Vector2(12,size.Y-208);touch.GetNode<Control>("TouchActions").Position=new Vector2(size.X-100,size.Y-372);
        if(modal.Visible && inventoryCard!=null){float scale=Math.Min(1.4f,Math.Min((size.X-24)/344,(size.Y-24)/464));inventoryCard.Scale=Vector2.One*scale;inventoryCard.Position=(size-new Vector2(344,464)*scale)*.5f;}
        LayoutRecipePanel(size);
        if(modal.Visible && inventoryCard==null&&modalPanel!=null){float width=Math.Min(570,size.X-24),desired=world.UiMode==GameMode.Inventory?680:world.UiMode==GameMode.Settings?660:430;float height=Math.Min(desired,size.Y-28);modalPanel.Position=(size-new Vector2(width,height))*.5f;modalPanel.Size=new Vector2(width,height);}
        if(lastWidth!=(int)size.X){lastWidth=(int)size.X;if(world.UiMode==GameMode.Inventory)ShowInventory();}
    }
}

public partial class ArmorSlotGlyph : Control
{
    readonly int kind;
    public ArmorSlotGlyph(int kind){this.kind=kind;MouseFilter=MouseFilterEnum.Stop;}
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("aab1b3"));DrawRect(new Rect2(Vector2.One,Size-Vector2.One*2),new Color("7b8589"),false,1);
        Vector2[] points=kind switch {
            0=>new[]{new Vector2(8,22),new Vector2(8,13),new Vector2(12,8),new Vector2(20,8),new Vector2(24,13),new Vector2(24,22),new Vector2(19,22),new Vector2(19,17),new Vector2(13,17),new Vector2(13,22),new Vector2(8,22)},
            1=>new[]{new Vector2(7,12),new Vector2(12,8),new Vector2(15,12),new Vector2(17,12),new Vector2(20,8),new Vector2(25,12),new Vector2(22,18),new Vector2(20,17),new Vector2(20,25),new Vector2(12,25),new Vector2(12,17),new Vector2(10,18),new Vector2(7,12)},
            2=>new[]{new Vector2(10,8),new Vector2(22,8),new Vector2(23,25),new Vector2(18,25),new Vector2(16,16),new Vector2(14,25),new Vector2(9,25),new Vector2(10,8)},
            _=>new[]{new Vector2(10,9),new Vector2(15,9),new Vector2(15,22),new Vector2(22,22),new Vector2(22,26),new Vector2(9,26),new Vector2(10,9)} };
        DrawPolyline(points,new Color("707c81"),1.5f,true);
    }
}
