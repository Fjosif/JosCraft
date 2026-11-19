using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class Viewer : Node3D
{
    public static readonly string[] Ids = {"tiger","fox","dragon","unicorn","robot","owl","panda","cat","bunny","bear","octo"};
    public static readonly string[] Names = {"Tygřík","Blesk","Jiskra","Duha","Bit","Moudrá","Bambus","Kometa","Hopsa","Brumla","Inka"};
    public static readonly string[] Clips = {"Idle","Walk","Run","Jump","Attack","Hit","Sit","Ride"};
    readonly List<Node3D> models = new();
    readonly List<AnimationPlayer?> players = new();
    readonly List<Label3D> labels = new();
    Camera3D camera = null!;
    CharacterBody3D body = null!;
    Label status = null!;
    OptionButton picker = null!, clipPicker = null!;
    public MeshInstance3D FloorMesh = null!;
    public StaticBody3D FloorBody = null!;
    bool gallery = true, movement, qa, paused;
    int selected;
    float yaw, pitch = .29f, distance = 7.5f, touchX, touchZ;
    bool jumpRequested, fast, physicsAirborne;
    string currentClip = "Idle";
    public IReadOnlyList<Node3D> Models => models;
    public IReadOnlyList<AnimationPlayer?> Players => players;

    public override void _Ready()
    {
        GetWindow().Title="JOSCRAFT · Ateliér postav";
        if(OS.GetCmdlineUserArgs().Contains("--world-qa")){CallDeferred(MethodName.OpenWorld);return;}
        qa = OS.GetCmdlineUserArgs().Contains("--qa");
        if (qa) { DisplayServer.WindowSetPosition(new Vector2I(-16000,-16000)); Engine.MaxFps = 120; }
        var env = new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("162238"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("c7dbf8"), AmbientLightEnergy = .42f,
            TonemapMode = Godot.Environment.ToneMapper.Linear } };
        AddChild(env);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50,-35,0), LightEnergy = .85f, ShadowEnabled = true, DirectionalShadowMaxDistance = 45 });
        AddChild(new DirectionalLight3D { RotationDegrees=new Vector3(-25,145,0), LightEnergy=.55f, ShadowEnabled=false });
        FloorMesh = new MeshInstance3D { Name="RenderedFloor", Mesh = new BoxMesh { Size = new Vector3(38,.2f,30) }, Position = new Vector3(0,-.1f,0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("263645"), Roughness=1 } };
        AddChild(FloorMesh);
        FloorBody = new StaticBody3D { Name="FloorCollision", Position=FloorMesh.Position };
        FloorBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(38,.2f,30) } });
        AddChild(FloorBody);
        body = new CharacterBody3D { Name="MovementBody", FloorSnapLength=.3f };
        body.AddChild(new CollisionShape3D { Shape=new CapsuleShape3D { Radius=.34f, Height=1.6f }, Position=new Vector3(0,.8f,0) });
        AddChild(body);
        for (int i=0;i<Ids.Length;i++)
        {
            var scene = ResourceLoader.Load<PackedScene>($"res://assets/characters/{Ids[i]}.glb");
            if (scene == null) { GD.PrintErr($"Chybí model {Ids[i]}"); GetTree().Quit(1); return; }
            var model = scene.Instantiate<Node3D>(); model.Name=Ids[i]; AddChild(model); models.Add(model);
            foreach(var mesh in Descendants(model).OfType<MeshInstance3D>()) for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++) if(mesh.GetActiveMaterial(surface) is StandardMaterial3D mat) { if(qa) GD.Print($"MATERIAL {Ids[i]} vertex={mat.VertexColorUseAsAlbedo} albedo={mat.AlbedoColor} emission={mat.EmissionEnabled} energy={mat.EmissionEnergyMultiplier}"); mat.VertexColorUseAsAlbedo=true; mat.AlbedoColor=Colors.White; mat.Roughness=1; mat.Metallic=0; }
            var player = Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(); players.Add(player);
            var label = new Label3D { Text=Names[i], FontSize=52, PixelSize=.008f, Position=new Vector3(0,2.95f,0), Billboard=BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest=true, OutlineSize=10, Modulate=Colors.White };
            model.AddChild(label); labels.Add(label);
            Play(i,"Idle");
        }
        camera = new Camera3D { Fov=48, Near=.1f, Far=90, Current=true }; AddChild(camera);
        BuildUi(); ShowGallery();
        if (qa) CallDeferred(MethodName.StartQa);
    }
    public static IEnumerable<Node> Descendants(Node root) { foreach (Node child in root.GetChildren()) { yield return child; foreach (var d in Descendants(child)) yield return d; } }
    public string FindClip(int index,string clip) => players[index]?.GetAnimationList().FirstOrDefault(x => x==clip || x.EndsWith("/"+clip) || x.EndsWith("|"+clip)) ?? "";
    void Play(int index,string clip)
    {
        var p=players[index]; string name=FindClip(index,clip); if(p==null || name.Length==0)return;
        p.GetAnimation(name).LoopMode = clip is "Idle" or "Walk" or "Run" or "Ride" ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        if(p.CurrentAnimation!=name || !p.IsPlaying())p.Play(name,.12);
    }
    public void Choose(int index)
    {
        selected=Math.Clamp(index,0,10); gallery=false; movement=false; paused=false;
        if(models[selected].GetParent()!=this) models[selected].Reparent(this);
        body.Position=Vector3.Zero; body.Velocity=Vector3.Zero;
        for(int i=0;i<models.Count;i++) { models[i].Visible=i==selected; labels[i].Visible=false; models[i].Position=Vector3.Zero; models[i].Rotation=Vector3.Zero; }
        picker.Select(selected+1); currentClip="Idle"; clipPicker.Select(0); Play(selected,"Idle"); UpdateStatus();
    }
    public void ShowGallery()
    {
        gallery=true; movement=false; paused=false;
        for(int i=0;i<models.Count;i++) { if(models[i].GetParent()!=this)models[i].Reparent(this); models[i].Visible=true; models[i].Position=new Vector3((i%6-2.5f)*2.7f,0,(i/6)*4.4f); models[i].Rotation=Vector3.Zero; labels[i].Visible=true; Play(i,"Idle"); }
        picker?.Select(0); UpdateStatus();
    }
    public void Preview(string clip)
    {
        if(gallery)Choose(selected);
        movement=false; paused=false;
        if(models[selected].GetParent()!=this)models[selected].Reparent(this);
        models[selected].Position=Vector3.Zero; models[selected].Rotation=Vector3.Zero; body.Position=Vector3.Zero;
        currentClip=clip; clipPicker.Select(Array.IndexOf(Clips,clip)); Play(selected,clip); UpdateStatus();
    }
    static string ClipLabel(string c) => c switch { "Idle"=>"Klid (Idle)", "Walk"=>"Chůze (Walk)", "Run"=>"Běh (Run)", "Jump"=>"Skok · záměrně ve vzduchu", "Attack"=>"Útok · jen animace", "Hit"=>"Zásah · jen animace", "Sit"=>"Sezení (Sit)", "Ride"=>"Jízda · póza bez zvířete", _=>c };
    public void EnableMovement()
    {
        if(gallery)Choose(selected); movement=true; paused=false; body.Position=Vector3.Zero; body.Velocity=Vector3.Zero;
        models[selected].Reparent(body); models[selected].Position=Vector3.Zero; models[selected].Rotation=Vector3.Zero; UpdateStatus();
    }
    void UpdateStatus() { if(status!=null)status.Text=gallery?"11 vlastních postav • vyber si hrdinu":$"{Names[selected]}  •  {(movement?"Pohyb po podlaze":ClipLabel(currentClip))}"; }
    public override void _PhysicsProcess(double delta)
    {
        if(!movement || qa)return;
        var v=new Vector2((Input.IsPhysicalKeyPressed(Key.D)?1:0)-(Input.IsPhysicalKeyPressed(Key.A)?1:0)+touchX,(Input.IsPhysicalKeyPressed(Key.S)?1:0)-(Input.IsPhysicalKeyPressed(Key.W)?1:0)+touchZ).LimitLength();
        var direction=new Vector3(-v.X,0,-v.Y).Rotated(Vector3.Up,-yaw);
        bool run=fast||Input.IsPhysicalKeyPressed(Key.Shift);
        var velocity=body.Velocity; velocity.X=direction.X*(run?4:2); velocity.Z=direction.Z*(run?4:2); velocity.Y-=18*(float)delta;
        if(jumpRequested&&body.IsOnFloor())velocity.Y=6; jumpRequested=false;
        body.Velocity=velocity; body.MoveAndSlide();
        body.Position=new Vector3(Math.Clamp(body.Position.X,-17,17),body.Position.Y,Math.Clamp(body.Position.Z,-12,12));
        if(direction.LengthSquared()>.01)models[selected].Rotation=new Vector3(0,Mathf.Atan2(-direction.X,-direction.Z),0);
        if(!body.IsOnFloor()) {physicsAirborne=true; ApplyPhysicsJumpPose();}
        else {models[selected].Position=Vector3.Zero; string next=v.LengthSquared()<.01?"Idle":run?"Run":"Walk"; if(physicsAirborne) { players[selected]?.Play(FindClip(selected,next),0);players[selected]?.Advance(0);physicsAirborne=false; } else Play(selected,next); }
    }
    // Preview Jump contains its own vertical arc. Physics already supplies that arc;
    // preserve its folded pose while removing the actual skinned mesh floor offset.
    public void ApplyPhysicsJumpPose()
    {
        Play(selected,"Jump"); var p=players[selected]; if(p==null)return;
        p.Play(FindClip(selected,"Jump"),0);p.Seek(.42,true);p.Advance(0);p.Pause();
        var model=models[selected];float min=float.PositiveInfinity;
        var toLocal=model.GlobalTransform.AffineInverse();
        foreach(var mesh in Descendants(model).OfType<MeshInstance3D>())
        {
            var baked=mesh.Skin!=null?mesh.BakeMeshFromCurrentSkeletonPose():mesh.Mesh;
            for(int surface=0;surface<baked.GetSurfaceCount();surface++)
                foreach(var vertex in baked.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    min=Math.Min(min,(toLocal*(mesh.GlobalTransform*vertex)).Y);
        }
        if(float.IsFinite(min))model.Position=new Vector3(0,-min,0);
    }
    public override void _Process(double delta)
    {
        if(camera==null)return;
        if(gallery) { bool portrait=GetViewport().GetVisibleRect().Size.X/GetViewport().GetVisibleRect().Size.Y<.9f; camera.Projection=Camera3D.ProjectionType.Orthogonal; camera.Size=portrait?25:15; for(int i=0;i<models.Count;i++) models[i].Position=portrait?new Vector3((i%3-1)*2.7f,0,(i/3)*3.6f):new Vector3((i%6-2.5f)*2.7f,0,(i/6)*4.4f); camera.Position=portrait?new Vector3(0,18,-18):new Vector3(0,12,-16);camera.LookAt(portrait?new Vector3(0,-3.8f,6):new Vector3(0,-1.3f,2.2f));}
        else {camera.Projection=Camera3D.ProjectionType.Perspective; Vector3 target=(movement?body.Position:Vector3.Zero)+new Vector3(0,currentClip=="Jump"&&!movement?1.2f:.5f,0); camera.Position=target+new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),-Mathf.Cos(yaw)*Mathf.Cos(pitch))*distance; camera.LookAt(target);}
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if(e is InputEventMouseMotion m && (m.ButtonMask&MouseButtonMask.Right)!=0) { yaw-=m.Relative.X*.009f; pitch=Math.Clamp(pitch+m.Relative.Y*.006f,-.05f,.8f); }
        if(e is InputEventMouseButton b && b.Pressed) {if(b.ButtonIndex==MouseButton.WheelUp)distance=Math.Max(3.4f,distance-.4f);if(b.ButtonIndex==MouseButton.WheelDown)distance=Math.Min(12,distance+.4f);}
        if(e is InputEventScreenDrag drag) { yaw-=drag.Relative.X*.009f; pitch=Math.Clamp(pitch+drag.Relative.Y*.006f,-.05f,.8f); }
        if(e is InputEventKey k && k.Pressed && !k.Echo) {if(k.PhysicalKeycode==Key.Space)jumpRequested=true;if(k.PhysicalKeycode==Key.Escape)ShowGallery();if(k.PhysicalKeycode==Key.M)EnableMovement();}
    }
    Button Button(string text,Action action,Container parent)
    {
        var b=new Button {Text=text, CustomMinimumSize=new Vector2(64,46), FocusMode=Control.FocusModeEnum.None}; b.Pressed+=action;parent.AddChild(b);return b;
    }
    void Hold(string text,Action<bool> changed,Container parent) {var b=Button(text,()=>{},parent);b.ButtonDown+=()=>changed(true);b.ButtonUp+=()=>changed(false);b.MouseExited+=()=>changed(false);}
    void BuildUi()
    {
        var layer=new CanvasLayer();AddChild(layer);
        var ui=new Control();ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);ui.MouseFilter=Control.MouseFilterEnum.Ignore;layer.AddChild(ui);
        var theme=new Theme(); theme.DefaultFontSize=20;
        var normal=new StyleBoxFlat { BgColor=new Color("24344b"), BorderColor=new Color("7893ad"), BorderWidthBottom=1, CornerRadiusTopLeft=9,CornerRadiusTopRight=9,CornerRadiusBottomLeft=9,CornerRadiusBottomRight=9,ContentMarginLeft=12,ContentMarginRight=12,ContentMarginTop=8,ContentMarginBottom=8 };
        var hover=(StyleBoxFlat)normal.Duplicate();hover.BgColor=new Color("3d5872");
        theme.SetStylebox("normal","Button",normal);theme.SetStylebox("hover","Button",hover);theme.SetStylebox("pressed","Button",hover);
        theme.SetColor("font_color","Button",Colors.White); theme.SetColor("font_hover_color","Button",Colors.White);theme.SetColor("font_pressed_color","Button",Colors.White);theme.SetColor("font_color","Label",Colors.White);
        theme.SetStylebox("normal","OptionButton",normal);theme.SetStylebox("hover","OptionButton",hover);theme.SetStylebox("pressed","OptionButton",hover);theme.SetColor("font_color","OptionButton",Colors.White);theme.SetColor("font_hover_color","OptionButton",Colors.White);theme.SetColor("font_pressed_color","OptionButton",Colors.White);
        theme.SetColor("font_color","PopupMenu",Colors.White);theme.SetStylebox("panel","PopupMenu",normal);ui.Theme=theme;
        var top=new VBoxContainer {OffsetLeft=18,OffsetTop=12,OffsetRight=-18}; top.SetAnchor(Side.Right,1);ui.AddChild(top);
        top.AddChild(new Label {Text="JOSCRAFT  /  ATELIÉR POSTAV",MouseFilter=Control.MouseFilterEnum.Ignore});
        var row=new HBoxContainer();top.AddChild(row);
        picker=new OptionButton {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(180,48)};picker.AddItem("Celá sada · 11 postav");foreach(var n in Names)picker.AddItem(n);picker.ItemSelected+=i=>{if(i==0)ShowGallery();else Choose((int)i-1);};row.AddChild(picker);
        Button("−",()=>distance=Math.Min(12,distance+.6f),row);Button("+",()=>distance=Math.Max(3.4f,distance-.6f),row);
        status=new Label {AutowrapMode=TextServer.AutowrapMode.WordSmart, MouseFilter=Control.MouseFilterEnum.Ignore};top.AddChild(status);
        var bottom=new VBoxContainer {OffsetLeft=18,OffsetRight=-18,OffsetTop=-296,OffsetBottom=-14};bottom.SetAnchor(Side.Top,1);bottom.SetAnchor(Side.Bottom,1);bottom.SetAnchor(Side.Right,1);ui.AddChild(bottom);
        var actions=new HBoxContainer();bottom.AddChild(actions);
        clipPicker=new OptionButton {FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(110,46)}; foreach(var c in Clips)clipPicker.AddItem(ClipLabel(c));clipPicker.ItemSelected+=i=>Preview(Clips[(int)i]);actions.AddChild(clipPicker);
        Button("Ⅱ / ▶",()=> {if(gallery)return;paused=!paused;if(paused)players[selected]?.Pause();else players[selected]?.Play();},actions);
        Button("Pohyb",EnableMovement,actions);
        var cameraRow=new HBoxContainer();bottom.AddChild(cameraRow);Button("↶",()=>yaw-=.3f,cameraRow);Button("↷",()=>yaw+=.3f,cameraRow);Button("Sada",ShowGallery,cameraRow);
        var worldButton=Button("Blokový svět",OpenWorld,cameraRow);worldButton.CustomMinimumSize=new Vector2(0,46);worldButton.AddThemeFontSizeOverride("font_size",16);
        var moveRow=new GridContainer { Columns=3 };bottom.AddChild(moveRow);
        Hold("←",b=>touchX=b?-1:0,moveRow);Hold("↑",b=>touchZ=b?-1:0,moveRow);Hold("↓",b=>touchZ=b?1:0,moveRow);Hold("→",b=>touchX=b?1:0,moveRow);Button("Skok",()=>jumpRequested=true,moveRow);Hold("Běh",b=>fast=b,moveRow);
        bottom.AddChild(new Label {Text="WASD · Shift běh · mezerník skok · M pohyb\nPravé tlačítko / tažení: kamera · kolečko: zoom",AutowrapMode=TextServer.AutowrapMode.WordSmart});
    }
    async void StartQa() { try { await new ViewerQa(this).Run(); }catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);} }
    void OpenWorld(){BlockWorld.SelectedCharacter=selected;GetTree().ChangeSceneToFile("res://game.tscn");}
}








