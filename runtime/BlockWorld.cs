using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class BlockWorld : Node3D
{
    public static int SelectedCharacter;
    public BlockTerrain Terrain { get; private set; } = null!;
    public CharacterBody3D Body { get; private set; } = null!;
    public Node3D Visual { get; private set; } = null!;
    public AnimationPlayer Player { get; private set; } = null!;
    public Camera3D Camera { get; private set; } = null!;
    public int CurrentCharacter => SelectedCharacter;
    public bool CharacterReady { get; private set; }
    public bool QaInputOverride { get; set; }
    public float CachedJumpOffset { get; private set; }
    public Vector2 VisualFootprintMin {get;private set;}
    public Vector2 VisualFootprintMax {get;private set;}
    public Vector2 FootprintMin { get; private set; }
    public Vector2 FootprintMax { get; private set; }
    public float BoundaryMargin { get; private set; }=.45f;
    public float CameraYaw { get => yaw; set => yaw=value; }
    public float CameraPitch { get => pitch; set => pitch=Math.Clamp(value,.12f,1.12f); }
    public float CameraDistance { get => distance; set => distance=Math.Clamp(value,4.8f,14f); }
    public bool CameraBlocked { get; private set; }
    public bool BoundaryBlocked { get; private set; }
    public bool IsQa { get; private set; }
    public int RespawnCount { get; private set; }
    public bool Overview { get; private set; }
    public bool FirstPerson { get; private set; }=true;
    public float FirstPersonPitch { get => firstPitch; set => firstPitch=Math.Clamp(value,-1.35f,1.35f); }
    public float EyeHeight => Math.Clamp(CharacterHeight*.87f,.42f,1.65f);
    public Vector3 CameraForward => -Camera.GlobalBasis.Z;
    public GameMode UiMode {get;private set;}=GameMode.Playing;
    public bool GameplayActive=>UiMode==GameMode.Playing;
    public bool SimulationActive=>UiMode is GameMode.Playing or GameMode.Inventory||NetSession.Mode!=NetMode.Single;
    public GameUi GameUi {get;private set;}=null!;
    public int SelectedHotbar {get;private set;}
    public ItemStack CarriedStack {get;private set;}=new();
    public float MiningProgress {get;private set;}
    public string Feedback {get;private set;}="";
    public float CharacterHeight {get;private set;}=2.6f;
    public float CollisionHeight=>Math.Max(.55f,CharacterHeight+.03f);
    readonly ItemStack[] inventory=Enumerable.Range(0,36).Select(_=>new ItemStack()).ToArray();
    readonly ItemStack[] crafting=Enumerable.Range(0,9).Select(_=>new ItemStack()).ToArray();
    readonly List<WoodPickup> woodPickups=new();
    public IReadOnlyList<WoodPickup> WoodPickups=>woodPickups;
    bool miningHeld,qaSneak,touchSneak;
    float miningTime,feedbackTime;
    Vector3I? miningCell;
    CollisionShape3D footprintCollider=null!;
    float yaw, pitch=.48f, distance=8f, touchX,touchZ;
    Vector2 qaInput;
    bool touchRun,qaRun,jumpRequested,airborne,waitingForSpawn;
    int selectionVersion;
    string currentClip="";
    float firstPitch=.12f;
    bool ownsMouseCapture;

    public override void _Ready()
    {
        GetWindow().Title="JOSCRAFT "+AutoUpdater.CurrentVersion+" · Blokový svět";
        GetTree().AutoAcceptQuit=false;GetWindow().CloseRequested+=CloseGame;
        IsQa=OS.GetCmdlineUserArgs().Any(x=>x is "--world-qa" or "--game-qa" or "--grass-qa" or "--tree-qa" or "--survival-qa" or "--building-qa"||x.StartsWith("--net-qa="));GameSession.IsQa=IsQa;GameSession.LoadSettings();
        if(IsQa){DisplayServer.WindowSetPosition(new Vector2I(-16000,-16000));Engine.MaxFps=120;}
        var worldEnvironment=new WorldEnvironment { Environment=new Godot.Environment {
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("b6d5e8"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.25f,
            TonemapMode=Godot.Environment.ToneMapper.Linear } };AddChild(worldEnvironment);
        var sun=new DirectionalLight3D {RotationDegrees=new Vector3(-50,-35,0),LightEnergy=.60f,ShadowEnabled=true,DirectionalShadowMaxDistance=75};AddChild(sun);
        AddChild(new DirectionalLight3D {RotationDegrees=new Vector3(-25,145,0),LightEnergy=.15f,ShadowEnabled=false});
        Terrain=new BlockTerrain {Name="BlockTerrain"};AddChild(Terrain);
        Body=new CharacterBody3D {Name="PlayerBody",CollisionLayer=2,CollisionMask=1,SafeMargin=.01f,FloorSnapLength=.22f,FloorStopOnSlope=true,FloorMaxAngle=Mathf.DegToRad(46)};
        footprintCollider=new CollisionShape3D {Name="AnimatedFootprint",Shape=new BoxShape3D {Size=new Vector3(.6f,1.65f,.6f)},Position=new Vector3(0,.825f,0)};
        Body.AddChild(footprintCollider);
        AddChild(Body);
        Camera=new Camera3D {Name="FollowCamera",Fov=58,Near=.1f,Far=150,Current=true};AddChild(Camera);
        GetWindow().FocusExited+=ResetHeldInput;
        BuildUi();Ecology=new Ecology(this,sun,worldEnvironment.Environment);AddChild(Ecology);FireSystem=new FireSystem(this);AddChild(FireSystem);Shapes=new ShapedBlocks(this);AddChild(Shapes);
        var pending=GameSession.Pending;GameSession.Pending=null;
        if(pending!=null)ApplySave(pending);else{SelectCharacter(SelectedCharacter);Respawn();}
        if(GameSession.CaptureAfterStart){GameSession.CaptureAfterStart=false;if(!IsQa)CaptureMouse();}
        NetSession.Instance?.AttachWorld(this);
        if(OS.GetCmdlineUserArgs().Contains("--building-qa"))CallDeferred(MethodName.StartBuildingQa);
        else if(OS.GetCmdlineUserArgs().Contains("--survival-qa"))CallDeferred(MethodName.StartSurvivalQa);
        else if(OS.GetCmdlineUserArgs().Contains("--tree-qa"))CallDeferred(MethodName.StartTreeQa);
        else if(OS.GetCmdlineUserArgs().Contains("--grass-qa"))CallDeferred(MethodName.StartGrassQa);
        else if(OS.GetCmdlineUserArgs().Contains("--game-qa"))CallDeferred(MethodName.StartGameQa);
        else if(NetQa.Active)CallDeferred(MethodName.StartNetQa);
        else if(IsQa)CallDeferred(MethodName.StartWorldQa);
    }
    public override void _ExitTree(){selectionVersion++;ReleaseMouse();NetSession.Instance?.DetachWorld(this);}
    public void CloseGame()
    {
        if(IsQa){NetSession.Instance?.EndSession(ShutdownReason.HostQuit,"");GetTree().Quit();return;}
        if(IsNetClient){NetSession.Instance!.EndSession(ShutdownReason.HostMenu,"");GetTree().Quit();return;}
        if(SaveSnapshot(GameSession.SavePath)){if(IsNetHost)NetSession.Instance!.EndSession(ShutdownReason.HostQuit,"Hostitel ukončil hru. Postup je uložený.");GetTree().Quit();}else{SetPaused(true);ShowFeedback("Uložení se nezdařilo. Hra zůstala otevřená.");}
    }
    void ResetHeldInput(){touchX=touchZ=0;touchRun=false;touchSneak=false;jumpRequested=false;SetMiningHeld(false);ReleaseMouse();if(!IsQa&&GameplayActive)SetPaused(true);}
    void ReleaseMouse()
    {
        if(ownsMouseCapture && Input.MouseMode==Input.MouseModeEnum.Captured)Input.MouseMode=Input.MouseModeEnum.Visible;
        ownsMouseCapture=false;
    }
    public void SetFirstPerson(bool firstPerson)
    {
        FirstPerson=firstPerson;
        if(Visual!=null && IsInstanceValid(Visual))ApplyPerspectiveToVisual();
    }
    void ApplyPerspectiveToVisual()
    {
        foreach(var mesh in Viewer.Descendants(Visual).OfType<MeshInstance3D>())
            mesh.CastShadow=FirstPerson?GeometryInstance3D.ShadowCastingSetting.ShadowsOnly:GeometryInstance3D.ShadowCastingSetting.On;
    }
    public void SelectCharacter(int index)
    {
        SelectedCharacter=Math.Clamp(index,0,Viewer.Ids.Length-1);CharacterReady=false;
        int version=++selectionVersion;
        if(Visual!=null && IsInstanceValid(Visual))Visual.QueueFree();
        var packed=ResourceLoader.Load<PackedScene>($"res://assets/characters/{Viewer.Ids[SelectedCharacter]}.glb");
        if(packed==null)throw new InvalidOperationException("Chybí model "+Viewer.Ids[SelectedCharacter]);
        Visual=packed.Instantiate<Node3D>();Visual.Name="CharacterVisual";Visual.Visible=false;Body.AddChild(Visual);
        foreach(var mesh in Viewer.Descendants(Visual).OfType<MeshInstance3D>())
            for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                if(mesh.GetActiveMaterial(surface) is StandardMaterial3D mat){mat.VertexColorUseAsAlbedo=true;mat.AlbedoColor=Colors.White;mat.Metallic=0;mat.Roughness=1;}
        Player=Viewer.Descendants(Visual).OfType<AnimationPlayer>().First();
        ApplyPerspectiveToVisual();
        currentClip="";airborne=false;
        _=CacheJumpPose(version,Visual,Player);
    }
    async Task CacheJumpPose(int version,Node3D visual,AnimationPlayer player)
    {
        try
        {
            string jump=ClipName(player,"Jump");player.Play(jump,0);player.Seek(.42,true);player.Advance(0);player.Pause();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(version!=selectionVersion||!IsInsideTree())return;
            Vector3[] SkinVertices()
            {
                var result=new System.Collections.Generic.List<Vector3>();var toLocal=visual.GlobalTransform.AffineInverse();
                foreach(var mesh in Viewer.Descendants(visual).OfType<MeshInstance3D>())
                {
                    var baked=mesh.Skin!=null?mesh.BakeMeshFromCurrentSkeletonPose():mesh.Mesh;
                    for(int surface=0;surface<baked.GetSurfaceCount();surface++)
                        foreach(var vertex in baked.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())result.Add(toLocal*(mesh.GlobalTransform*vertex));
                }
                return result.ToArray();
            }
            var jumpVertices=SkinVertices();
            if(jumpVertices.Length==0)throw new InvalidOperationException("Prázdný skin modelu");
            CachedJumpOffset=jumpVertices.Min(v=>v.Y);
            Vector2 low=new(float.PositiveInfinity,float.PositiveInfinity),high=new(float.NegativeInfinity,float.NegativeInfinity);float maxHeight=0;
            void ExtendFootprint(Vector3[] vertices,float floorOffset=0)
            {
                maxHeight=Math.Max(maxHeight,vertices.Max(v=>v.Y-floorOffset));
                // Survival permits tall placed walls and mined ceilings. Keep
                // the complete animated envelope, including wings and horns.
                foreach(var vertex in vertices)
                {low=new Vector2(Math.Min(low.X,vertex.X),Math.Min(low.Y,vertex.Z));high=new Vector2(Math.Max(high.X,vertex.X),Math.Max(high.Y,vertex.Z));}
            }
            ExtendFootprint(jumpVertices,CachedJumpOffset);
            foreach(string clip in new[]{"Idle","Walk","Run"})
            {
                string name=ClipName(player,clip);var animation=player.GetAnimation(name);animation.LoopMode=Animation.LoopModeEnum.Linear;
                player.Play(name,0);player.Advance(0);player.Pause();
                for(int sample=0;sample<17;sample++)
                {
                    player.Seek(animation.Length*sample/17,true);player.Advance(0);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    if(version!=selectionVersion||!IsInsideTree())return;
                    ExtendFootprint(SkinVertices());
                }
            }
            var gameScale=HeroSizing.Scale(SelectedCharacter);
            visual.Scale=gameScale;var horizontalScale=new Vector2(gameScale.X,gameScale.Z);low*=horizontalScale;high*=horizontalScale;maxHeight*=gameScale.Y;CachedJumpOffset*=gameScale.Y;
            VisualFootprintMin=low;VisualFootprintMax=high;
            // Gameplay body stays centered on the feet. Wings, ears and tails are visual extensions.
            FootprintMin=new Vector2(-.3f,-.3f);FootprintMax=new Vector2(.3f,.3f);
            CharacterHeight=maxHeight;
            var size=FootprintMax-FootprintMin;var center=(FootprintMax+FootprintMin)*.5f;
            footprintCollider.Shape=new BoxShape3D {Size=new Vector3(size.X,CollisionHeight,size.Y)};
            footprintCollider.Position=new Vector3(center.X,CollisionHeight*.5f,center.Y);
            BoundaryMargin=new[]{new Vector2(low.X,low.Y).Length(),new Vector2(low.X,high.Y).Length(),new Vector2(high.X,low.Y).Length(),new Vector2(high.X,high.Y).Length()}.Max()+.08f;
            player.Play(ClipName(player,"Idle"),0);player.Advance(0);currentClip="Idle";
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(version!=selectionVersion||!IsInsideTree())return;
            CharacterReady=true;visual.Visible=true;if(!GameplayActive)player.Pause();
        }
        catch(Exception error){GD.PrintErr(error);if(IsQa)GetTree().Quit(1);}
    }
    public static string ClipName(AnimationPlayer player,string clip) => player.GetAnimationList().First(x=>x==clip||x.EndsWith("/"+clip)||x.EndsWith("|"+clip));
    void PlayLocomotion(string clip,bool immediate=false)
    {
        if(currentClip==clip && Player.IsPlaying())return;
        string name=ClipName(Player,clip);Player.GetAnimation(name).LoopMode=Animation.LoopModeEnum.Linear;
        Player.Play(name,immediate?0:.10);if(immediate)Player.Advance(0);currentClip=clip;
    }
    void JumpPose()
    {
        if(currentClip!="Jump") {Player.Play(ClipName(Player,"Jump"),0);Player.Seek(.42,true);Player.Advance(0);Player.Pause();currentClip="Jump";}
        Visual.Position=new Vector3(0,-CachedJumpOffset,0);
    }
    public void SetSeed(int seed)
    {
        ClearWoodPickups();Terrain.Regenerate(seed);Shapes.Restore(null);Furnaces.Clear();Respawn();
    }
    public void Respawn()
    {
        if(NetClientRespawn())return;
        Body.Position=new Vector3(.5f,Terrain.HeightAt(0,0)+.30f,.5f);Body.Velocity=Vector3.Zero;
        waitingForSpawn=true;jumpRequested=false;airborne=false;RespawnCount++;
        if(Visual!=null && IsInstanceValid(Visual))Visual.Position=Vector3.Zero;
        Terrain.UpdateStreaming(Body.Position);
    }
    public void SetOverview(bool overview){Overview=overview;}
    // QA drives the exact same controller path as keys and touch buttons.
    public void SimulateInput(Vector2 input,bool run=false,bool jump=false)
    {
        QaInputOverride=true;qaInput=input.LimitLength();qaRun=run;if(jump)jumpRequested=true;
    }
    public void SetSneaking(bool sneak){qaSneak=sneak;}
    public void SetQaSneak(bool sneak){qaSneak=sneak;}
    public void SetTouchAxis(bool horizontal,float value){if(horizontal)touchX=value;else touchZ=value;}
    public void SetTouchRun(bool run){touchRun=run;}
    public void SetTouchSneak(bool sneak){touchSneak=sneak;}
    public void RequestJump(){if(GameplayActive)jumpRequested=true;}
    public override void _PhysicsProcess(double delta)
    {
        if(Terrain==null||Body==null)return;
        if(!SimulationActive)return;
        SurvivalTick((float)delta);
        if(!GameplayActive)return;
        Terrain.UpdateStreaming(Body.Position);
        if(!CharacterReady)return;
        if(waitingForSpawn)
        {
            if(!Terrain.IsLoadedAt(Body.Position.X,Body.Position.Z))return;
            waitingForSpawn=false;
        }
        if(Body.Position.Y < -8 || !float.IsFinite(Body.Position.Y)){Respawn();return;}
        bool typing=GetViewport().GuiGetFocusOwner() is LineEdit;
        Vector2 input=QaInputOverride?qaInput:typing?Vector2.Zero:new Vector2((Input.IsPhysicalKeyPressed(Key.D)?1:0)-(Input.IsPhysicalKeyPressed(Key.A)?1:0)+touchX,(Input.IsPhysicalKeyPressed(Key.S)?1:0)-(Input.IsPhysicalKeyPressed(Key.W)?1:0)+touchZ).LimitLength();
        bool sneak=QaInputOverride?qaSneak:touchSneak||Input.IsPhysicalKeyPressed(Key.Shift);
        bool run=!sneak&&(QaInputOverride?qaRun:!typing&&(touchRun||Input.IsPhysicalKeyPressed(Key.Ctrl)));
        var direction=new Vector3(input.X,0,input.Y).Rotated(Vector3.Up,yaw);
        Vector3 velocity=Body.Velocity;float speed=sneak?1.05f:run?4.5f:2.6f;velocity.X=direction.X*speed;velocity.Z=direction.Z*speed;velocity.Y-=18*(float)delta;
        if(jumpRequested&&Body.IsOnFloor())velocity.Y=7;jumpRequested=false;
        BoundaryBlocked=false;
        // Every corner of the real rotated footprint must remain on generated
        // chunks, including diagonal motion and the long tails behind a hero.
        float desiredHeading=FirstPerson?yaw:direction.LengthSquared()>.001f?Mathf.Atan2(-direction.X,-direction.Z):Body.Rotation.Y;
        float heading=LegalFootprint(Body.Position,desiredHeading,false)&&LegalTurn(desiredHeading)?desiredHeading:Body.Rotation.Y;
        var horizontal=new Vector3(velocity.X,0,velocity.Z)*(float)delta;
        bool ledgeGuard=sneak&&Body.IsOnFloor();
        if(!LegalFootprint(Body.Position+horizontal,heading,ledgeGuard))
        {
            BoundaryBlocked=true;
            if(!LegalFootprint(Body.Position+new Vector3(horizontal.X,0,0),heading,ledgeGuard))velocity.X=0;
            if(!LegalFootprint(Body.Position+new Vector3(velocity.X*(float)delta,0,horizontal.Z),heading,ledgeGuard))velocity.Z=0;
        }
        Body.Rotation=new Vector3(0,heading,0);
        Body.Velocity=velocity;TryStepUp(new Vector3(velocity.X,0,velocity.Z)*(float)delta);Body.MoveAndSlide();
        if(!Body.IsOnFloor()){airborne=true;JumpPose();}
        else {Visual.Position=Vector3.Zero;PlayLocomotion(input.LengthSquared()<.001f?"Idle":run?"Run":"Walk",airborne);airborne=false;}
        UpdateMining((float)delta);
    }
    public override void _Process(double delta)
    {
        if(Body==null||Terrain==null||Camera==null)return;
        if(feedbackTime>0){feedbackTime-=(float)delta;if(feedbackTime<=0)Feedback="";}
        if(!GameplayActive)return;
        if(Overview){Camera.Position=Body.Position+new Vector3(22,24,26);Camera.LookAt(Body.Position+Vector3.Up);return;}
        if(FirstPerson)
        {
            Camera.Fov=GameSession.Settings.Fov;CameraBlocked=false;
            Camera.Position=Body.Position+new Vector3(0,EyeHeight,0);
            var forward=new Vector3(-Mathf.Sin(yaw)*Mathf.Cos(firstPitch),-Mathf.Sin(firstPitch),-Mathf.Cos(yaw)*Mathf.Cos(firstPitch));
            Camera.LookAt(Camera.Position+forward);return;
        }
        Camera.Fov=GameSession.Settings.Fov*.784f;
        var target=Body.Position+new Vector3(0,1.25f,0);
        var offset=new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),Mathf.Cos(yaw)*Mathf.Cos(pitch))*distance;
        var desired=target+offset;
        var query=PhysicsRayQueryParameters3D.Create(target,desired,1);
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);CameraBlocked=hit.Count>0;
        if(CameraBlocked){var point=hit["position"].AsVector3();desired=point+(target-point).Normalized()*.30f;}
        Camera.Position=desired;if(desired.DistanceSquaredTo(target)>.01f)Camera.LookAt(target);
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if(e is InputEventKey k && k.Pressed&&!k.Echo)
        {
            if(k.PhysicalKeycode==Key.Escape){if(UiMode==GameMode.Playing)SetPaused(true);else if(UiMode==GameMode.Settings)SetPaused(true);else SetPaused(false);return;}
            if(k.PhysicalKeycode==Key.E){if(UiMode is GameMode.Playing or GameMode.Inventory)ToggleInventory();return;}
            if(!GameplayActive)return;
            if(k.PhysicalKeycode==Key.Space)RequestJump();
            if(k.PhysicalKeycode is Key.V or Key.F5)SetFirstPerson(!FirstPerson);
            if(k.PhysicalKeycode>=Key.Key1&&k.PhysicalKeycode<=Key.Key9)SelectHotbar((int)k.PhysicalKeycode-(int)Key.Key1);
        }
        if(!GameplayActive)return;
        if(e is InputEventMouseMotion m && (ownsMouseCapture || (m.ButtonMask&MouseButtonMask.Right)!=0))Look(m.Relative);
        if(e is InputEventScreenDrag t)Look(t.Relative);
        if(e is InputEventMouseButton b)
        {
            if(b.ButtonIndex==MouseButton.Left&&!b.Pressed){SetMiningHeld(false);return;}
            if(!b.Pressed)return;
            if(b.ButtonIndex==MouseButton.WheelUp){SelectHotbar((SelectedHotbar+8)%9);return;}
            if(b.ButtonIndex==MouseButton.WheelDown){SelectHotbar((SelectedHotbar+1)%9);return;}
            // The click that enters mouse capture is never also a mining click.
            if(!ownsMouseCapture&&!IsQa&&b.Device>=0&&b.ButtonIndex==MouseButton.Left){CaptureMouse();return;}
            if(!ownsMouseCapture&&!IsQa)return;
            if(b.ButtonIndex==MouseButton.Left){if(!Ecology.TryAttack())SetMiningHeld(true);}
            if(b.ButtonIndex==MouseButton.Right){if(!UseTarget())PlaceTarget();}
            if(b.ButtonIndex==MouseButton.Middle)PickTarget();
        }
    }
    void Look(Vector2 relative){yaw-=relative.X*GameSession.Settings.MouseSensitivity;if(FirstPerson)FirstPersonPitch+=relative.Y*GameSession.Settings.MouseSensitivity;else CameraPitch+=relative.Y*GameSession.Settings.MouseSensitivity;}
    public void CaptureMouse(){if(IsQa||!GameplayActive)return;ownsMouseCapture=true;Input.MouseMode=Input.MouseModeEnum.Captured;}
    void BuildUi(){GameUi=new GameUi(this);AddChild(GameUi);}
    public void SetPaused(bool paused){SetMode(paused?GameMode.Paused:GameMode.Playing);}
    public void ToggleInventory(){if(UiMode!=GameMode.Inventory){OpenStation=null;CraftSize=2;}SetMode(UiMode==GameMode.Inventory?GameMode.Playing:GameMode.Inventory);}
    public void ShowSettings(){SetMode(GameMode.Settings);}
    void SetMode(GameMode mode)
    {
        bool netHandled=NetInventoryModeChange(UiMode,mode);
        if(!netHandled&&UiMode==GameMode.Inventory&&mode!=GameMode.Inventory&&!ReturnStaging()){ShowFeedback("Pro zásoby není místo. Nejdřív uvolni políčko.");return;}
        if(UiMode==GameMode.Inventory&&mode!=GameMode.Inventory)CloseLocalChest();
        UiMode=mode;touchX=touchZ=0;touchRun=false;touchSneak=false;jumpRequested=false;SetMiningHeld(false);
        if(mode!=GameMode.Inventory){OpenStation=null;CraftSize=2;}
        Terrain.SetProcess(SimulationActive);if(!GameplayActive){ReleaseMouse();Player?.Pause();}
        foreach(var pickup in woodPickups)pickup.SetSuspended(!SimulationActive);
        GameUi?.ShowMode();GameUi?.RefreshHotbar();
        if(GameplayActive&&!IsQa)CaptureMouse();
    }
    public bool ReturnToMenu(bool save=true)
    {
        if(IsNetClient){NetSession.Instance!.EndSession(ShutdownReason.HostMenu,"");NetReturnToMenuWithoutSave();return true;}
        if(save&&!SaveSnapshot(GameSession.SavePath)){ShowFeedback("Uložení se nepodařilo. Hra zůstala otevřená.");return false;}
        if(IsNetHost)NetSession.Instance!.EndSession(ShutdownReason.HostMenu,"Hostitel ukončil hru. Tvůj postup je uložený.");
        ReleaseMouse();GetTree().ChangeSceneToFile("res://game.tscn");return true;
    }
    public ItemStack GetSlot(int index)=>index>=0&&index<36?inventory[index]:new();
    public int InventoryCount(int type)=>inventory.Where(s=>s.Type==type).Sum(s=>s.Count)+crafting.Where(s=>s.Type==type).Sum(s=>s.Count)+(CarriedStack.Type==type?CarriedStack.Count:0);
    public int InventoryCount(BlockType type)=>InventoryCount((int)type);
    public void SelectHotbar(int index){SelectedHotbar=Math.Clamp(index,0,8);NetSelectHotbar(SelectedHotbar);GameUi?.RefreshHotbar();}
    public bool TransferSlot(int from,int to)
    {
        if(NetMoveSlot(from,to))return true;
        if(from<0||from>=36||to<0||to>=36||from==to)return false;
        var a=inventory[from];var b=inventory[to];if(a.Count==0)return false;
        if(a.Type==b.Type&&b.Count<64){int move=Math.Min(a.Count,64-b.Count);inventory[to]=new(a.Type,b.Count+move);inventory[from]=a.Count==move?new():new(a.Type,a.Count-move);}
        else{inventory[to]=a;inventory[from]=b;}
        GameUi?.RefreshHotbar();if(UiMode==GameMode.Inventory)GameUi?.ShowInventory();return true;
    }
    public void ClickInventorySlot(int index)
    {
        if(UiMode!=GameMode.Inventory||index<0||index>=36)return;
        if(NetUiClick(0,index,false))return;
        ClickStack(inventory,index);
        GameUi.RefreshHotbar();GameUi.ShowInventory();
    }
    public ItemStack GetCraftSlot(int index)=>index>=0&&index<CraftSize*CraftSize?crafting[index]:new();
    public void ClickCraftSlot(int index){if(UiMode!=GameMode.Inventory||index<0||index>=CraftSize*CraftSize)return;UiSlotClick(1,index);}
    void ClickStack(ItemStack[] slots,int index)
    {
        var there=slots[index];
        if(CarriedStack.Count==0){CarriedStack=there;slots[index]=new();}
        else if(there.Type==CarriedStack.Type)
        {
            if(there.Count>=64)return;
            int move=Math.Min(CarriedStack.Count,64-there.Count);slots[index]=new(there.Type,there.Count+move);CarriedStack=move==CarriedStack.Count?new():new(CarriedStack.Type,CarriedStack.Count-move);
        }
        else{slots[index]=CarriedStack;CarriedStack=there;}
    }
    int Capacity(int type)=>inventory.Sum(s=>s.Count==0?64:s.Type==type?64-s.Count:0);
    bool AddResource(int type,int count)
    {
        if(count<0||Capacity(type)<count)return false;
        for(int pass=0;pass<2;pass++)for(int i=0;i<36&&count>0;i++)
        {
            var s=inventory[i];if(pass==0?s.Type!=type||s.Count>=64:s.Count!=0)continue;
            int add=Math.Min(count,64-s.Count);inventory[i]=new(type,s.Count+add);count-=add;
        }
        GameUi?.RefreshHotbar();return count==0;
    }
    bool ReturnStaging()
    {
        for(int i=0;i<9;i++)if(crafting[i].Count>0&&AddResource(crafting[i].Type,crafting[i].Count))crafting[i]=new();
        if(CarriedStack.Count>0&&AddResource(CarriedStack.Type,CarriedStack.Count))CarriedStack=new();
        return CarriedStack.Count==0&&crafting.All(s=>s.Count==0);
    }
    public sealed record BlockTarget(Vector3I Cell,Vector3I Placement,Vector3 Position,Vector3 Normal,int Type,float Distance);
    static Vector3I CellAt(Vector3 point)=>new(Mathf.FloorToInt(point.X),Mathf.FloorToInt(point.Y),Mathf.FloorToInt(point.Z));
    public BlockTarget? RayTarget()
    {
        var origin=Camera.GlobalPosition;var forward=-Camera.GlobalBasis.Z;var eyes=Body.GlobalPosition+Vector3.Up*EyeHeight;
        float length=FirstPerson?5:origin.DistanceTo(eyes)+5;
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin,origin+forward*length,1|16));
        if(hit.Count==0)return null;var point=hit["position"].AsVector3();var normal=hit["normal"].AsVector3();float reach=eyes.DistanceTo(point);
        if(reach>5.001f)return null;
        var cell=CellAt(point-normal*.015f);
        var targetCollider=hit["collider"].AsGodotObject();if(targetCollider.HasMeta("shape_cell"))cell=targetCollider.GetMeta("shape_cell").AsVector3I();if(targetCollider.HasMeta("torch_cell"))cell=targetCollider.GetMeta("torch_cell").AsVector3I();
        // At a grid edge, hit coordinates alone are ambiguous in the two
        // tangential axes. Resolve the actual physics triangle's block owner.
        int face=hit.ContainsKey("face_index")?hit["face_index"].AsInt32():-1;
        if(face>=0)
        {
            var collider=hit["collider"].AsGodotObject();
            var chunk=Terrain.Chunks.Values.FirstOrDefault(c=>c.Body==collider);
            int index=face*3;
            if(chunk!=null&&index+2<chunk.Indices.Length)
            {
                var center=(chunk.Vertices[chunk.Indices[index]]+chunk.Vertices[chunk.Indices[index+1]]+chunk.Vertices[chunk.Indices[index+2]])/3;
                cell=CellAt(chunk.Root.GlobalTransform*center-normal*.015f);
            }
        }
        var adjacent=new Vector3I(Mathf.RoundToInt(normal.X),Mathf.RoundToInt(normal.Y),Mathf.RoundToInt(normal.Z));
        var placement=cell+adjacent;int type=Terrain.GetBlockId(cell);
        return type==0?null:new(cell,placement,point,normal,type,reach);
    }
    public void SetMiningHeld(bool held){if(!held)NetMiningStopped();miningHeld=held;if(!held){miningTime=0;MiningProgress=0;miningCell=null;}}
    void UpdateMining(float delta)
    {
        if(!miningHeld||!GameplayActive){MiningProgress=0;return;}
        var target=RayTarget();if(target==null||target.Cell.Y<=0){miningTime=0;MiningProgress=0;miningCell=null;return;}
        if(NetMiningAwaiting(target.Cell))return;
        if(miningCell!=target.Cell){miningCell=target.Cell;miningTime=0;NetMiningStarted(target.Cell);}
        miningTime+=delta;float duration=BlockCatalog.MiningDuration(target.Type,GetSlot(SelectedHotbar).Type);MiningProgress=Math.Clamp(miningTime/duration,0,1);
        if(miningTime>=duration){MineTarget();miningTime=0;MiningProgress=0;miningCell=null;}
    }
    public bool MineTarget()
    {
        if(!GameplayActive)return false;if(IsNetClient)return NetClientMine();var t=RayTarget();if(t==null||t.Cell.Y<=0)return false;
        if(t.Type is 3 or 14 or 17 or 18 && !BlockCatalog.IsPickaxe(GetSlot(SelectedHotbar).Type)){ShowFeedback("Na kámen potřebuješ krumpáč z pracovního stolu.");return false;}
        if(t.Type==17&&Furnaces.TryGetValue(t.Cell,out var f)&&(f.Input.Count+f.Fuel.Count+f.Output.Count>0||f.Burn>0)){ShowFeedback("Nejdřív vyber zásoby z pece a nech ji vyhasnout.");return false;}
        int drop=BlockCatalog.DropFor(t.Type);
        int count=t.Type==15?4:1;
        if(drop!=0&&!BlockCatalog.IsLog(t.Type)&&Capacity(drop)<count){ShowFeedback("Batoh je plný.");return false;}
        if(BlockCatalog.IsShaped(t.Type)){if(!Shapes.Remove(t.Cell))return false;}
        else if(!Terrain.TrySetBlock(t.Cell,0))return false;
        if(BlockCatalog.IsLog(t.Type))SpawnWoodPickup(drop,new Vector3(t.Cell.X+.5f,t.Cell.Y+.5f,t.Cell.Z+.5f));
        else if(drop!=0){AddResource(drop,count);ShowFeedback($"+{count} {BlockCatalog.Get(drop).Name}");}
        return true;
    }
    public bool PlaceTarget()
    {
        if(!GameplayActive)return false;if(IsNetClient)return NetClientPlace();var stack=inventory[SelectedHotbar];if(stack.Count==0){ShowFeedback("Nejdřív vytěž materiál.");return false;}
        if(!BlockCatalog.IsPlaceable((BlockType)stack.Type))return false;
        var t=RayTarget();if(t==null||Terrain.GetBlockId(t.Placement)!=0)return false;
        if(!CanPlaceCell(t.Placement))return false;
        if(stack.Type==32&&!BlockCatalog.IsFullCube(Terrain.GetBlock(t.Placement+Vector3I.Down))){ShowFeedback("Pochodeň polož na pevný horní povrch.");return false;}
        if(BlockCatalog.IsShaped(stack.Type)){if(!Shapes.Place(t.Placement,stack.Type,Shapes.RotationFor(yaw)))return false;}
        else if(!Terrain.TrySetBlock(t.Placement,stack.Type))return false;
        inventory[SelectedHotbar]=stack.Count==1?new():new(stack.Type,stack.Count-1);GameUi.RefreshHotbar();return true;
    }
    public bool CanPlaceCell(Vector3I cell)
    {
        if(cell.Y<=0||cell.Y>BlockTerrain.MaxBuildY||!Terrain.IsLoadedAt(cell.X+.5f,cell.Z+.5f))return false;
        // Use the physical gameplay body for placement; decorative parts do not block doorways.
        var size=FootprintMax-FootprintMin;var local=new Aabb(new Vector3(FootprintMin.X,0,FootprintMin.Y),new Vector3(size.X,CollisionHeight,size.Y));
        var bodyBounds=Body.GlobalTransform*local;
        return !bodyBounds.Intersects(new Aabb(new Vector3(cell.X,cell.Y,cell.Z),Vector3.One))&&CellPhysicallyClear(cell);
    }
    public bool PickTarget()
    {
        if(!GameplayActive)return false;var t=RayTarget();if(t==null)return false;
        int index=Array.FindIndex(inventory,s=>s.Type==t.Type&&s.Count>0);if(index<0)return false;
        if(index<9)SelectHotbar(index);else TransferSlot(index,SelectedHotbar);return true;
    }
    void ShowFeedback(string message){Feedback=message;feedbackTime=2.5f;}
    public WoodPickup SpawnWoodPickup(int type,Vector3 position,string? id=null,int count=1,float age=0)
    {
        if(!BlockCatalog.IsCollectible(type)||count<1||count>64)throw new ArgumentException("Pickup requires a valid collectible stack.");
        id??=Guid.NewGuid().ToString("N");
        if(woodPickups.Any(p=>p.Id==id))throw new ArgumentException("Duplicate pickup identity.");
        var pickup=new WoodPickup(this,type,position,id,count,age);woodPickups.Add(pickup);AddChild(pickup);return pickup;
    }
    public bool CollectWoodPickup(WoodPickup pickup)
    {
        if(IsNetClient||pickup.NetRemote)return false;
        if(!GameplayActive||!woodPickups.Contains(pickup)||!pickup.IsCollectible||!pickup.Visible)return false;
        if(Body.Position.DistanceSquaredTo(pickup.Position)>WoodPickup.CollectionRadius*WoodPickup.CollectionRadius)return false;
        if(Capacity(pickup.Type)<pickup.Count)return false;
        if(!AddResource(pickup.Type,pickup.Count))return false;
        woodPickups.Remove(pickup);pickup.Freeze=true;pickup.Visible=false;pickup.SetPhysicsProcess(false);pickup.QueueFree();
        ShowFeedback($"+{pickup.Count} {BlockCatalog.Get(pickup.Type).Name}");return true;
    }
    void ClearWoodPickups()
    {
        foreach(var pickup in woodPickups){pickup.Freeze=true;pickup.Visible=false;pickup.SetPhysicsProcess(false);pickup.QueueFree();}
        woodPickups.Clear();
    }
    bool LegalFootprint(Vector3 position,float heading,bool ledgeGuard)
    {
        int supports=0;
        foreach(float x in new[]{FootprintMin.X,FootprintMax.X})foreach(float z in new[]{FootprintMin.Y,FootprintMax.Y})
        {
            var p=position+new Vector3(x,0,z).Rotated(Vector3.Up,heading);
            if(!Terrain.IsLoadedAt(p.X,p.Z))return false;
            if(ledgeGuard)
            {
                var hit=GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(p+Vector3.Up*.18f,p-Vector3.Up*.20f,1));
                if(hit.Count>0)supports++;
            }
        }
        if(ledgeGuard&&supports==0)
        {
            var center=position+new Vector3((FootprintMin.X+FootprintMax.X)*.5f,0,(FootprintMin.Y+FootprintMax.Y)*.5f).Rotated(Vector3.Up,heading);
            return GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(center+Vector3.Up*.18f,center-Vector3.Up*.20f,1)).Count>0;
        }
        return true;
    }
    bool LegalTurn(float heading)
    {
        var half=(FootprintMax-FootprintMin)*.5f;var localCenter=(FootprintMax+FootprintMin)*.5f;
        var center3=Body.Position+new Vector3(localCenter.X,0,localCenter.Y).Rotated(Vector3.Up,heading);var center=new Vector2(center3.X,center3.Z);
        var ax=new Vector2(Mathf.Cos(heading),-Mathf.Sin(heading));var az=new Vector2(Mathf.Sin(heading),Mathf.Cos(heading));
        var radius=new Vector2(Math.Abs(ax.X)*half.X+Math.Abs(az.X)*half.Y,Math.Abs(ax.Y)*half.X+Math.Abs(az.Y)*half.Y);
        for(int x=Mathf.FloorToInt(center.X-radius.X);x<=Mathf.FloorToInt(center.X+radius.X);x++)
        for(int z=Mathf.FloorToInt(center.Y-radius.Y);z<=Mathf.FloorToInt(center.Y+radius.Y);z++)
        for(int y=Mathf.FloorToInt(Body.Position.Y+.025f);y<Mathf.CeilToInt(Body.Position.Y+CollisionHeight);y++)
        {
            if(y+1<=Body.Position.Y+.025f||!BlockCatalog.IsFullCube(Terrain.GetBlock(new Vector3I(x,y,z))))continue;
            var difference=new Vector2(x+.5f,z+.5f)-center;bool overlap=true;
            foreach(var axis in new[]{Vector2.Right,Vector2.Down,ax,az})
            {float a=half.X*Math.Abs(ax.Dot(axis))+half.Y*Math.Abs(az.Dot(axis));float b=.5f*(Math.Abs(axis.X)+Math.Abs(axis.Y));if(Math.Abs(difference.Dot(axis))>=a+b-.004f){overlap=false;break;}}
            if(overlap)return false;
        }
        return true;
    }
    public bool SaveSnapshot(string path)
    {
        if(IsNetClient)return true;
        if(!ReturnStaging())return false;if(UiMode==GameMode.Inventory)GameUi.ShowInventory();var position=Body.Position;
        var data=new SavedGame {Seed=Terrain.Seed,Character=SelectedCharacter,X=position.X,Y=position.Y,Z=position.Z,Yaw=yaw,Pitch=firstPitch,FirstPerson=FirstPerson,SelectedSlot=SelectedHotbar,Slots=inventory.ToArray(),Edits=Terrain.GetChanges(),TreeExclusions=Terrain.TreeExclusions,Pickups=woodPickups.Select(p=>p.Snapshot()).ToArray()};
        data.TimeOfDay=Ecology.TimeOfDay;data.Creatures=Ecology.Snapshot();data.Furnaces=Furnaces.Values.ToArray();data.Health=Health;data.RemotePlayers=NetRemotePlayersForSave();data.PlacedBlocks=Shapes.States.Values.ToArray();
        return GameSession.WriteAtomic(path,data);
    }
    public bool LoadSnapshot(string path)
    {
        var data=GameSession.Read(path);if(data==null)return false;ApplySave(data);return true;
    }
    void ApplySave(SavedGame data)
    {
        var treeExclusions=data.TreeExclusions??TreeGenerator.LegacyExclusions(data.Seed,data.Edits,new Vector3(data.X,data.Y,data.Z));
        ClearWoodPickups();Terrain.Regenerate(data.Seed,treeExclusions);using(Terrain.SuppressOverlayEvents())Terrain.RestoreChanges(data.Edits);SelectCharacter(data.Character);NetSetLoadedRemotePlayers(data.RemotePlayers);
        for(int i=0;i<36;i++)inventory[i]=data.Slots[i];for(int i=0;i<9;i++)crafting[i]=new();CarriedStack=new();SelectedHotbar=Math.Clamp(data.SelectedSlot,0,8);yaw=data.Yaw;FirstPersonPitch=data.Pitch;SetFirstPerson(data.FirstPerson);
        Shapes.Restore(data.PlacedBlocks);Health=data.Health;Furnaces.Clear();foreach(var f in data.Furnaces)Furnaces[new(f.X,f.Y,f.Z)]=f;Ecology.Restore(data.TimeOfDay,data.Creatures);
        Body.Position=new Vector3(data.X,data.Y,data.Z);Body.Velocity=Vector3.Zero;
        if(data.X<BlockTerrain.WorldMin||data.X>=BlockTerrain.WorldMaxExclusive||data.Z<BlockTerrain.WorldMin||data.Z>=BlockTerrain.WorldMaxExclusive||data.Y<0||data.Y>BlockTerrain.MaxBuildY+8)Respawn();
        else {Terrain.UpdateStreaming(Body.Position);waitingForSpawn=true;}
        foreach(var item in data.Pickups){var pickup=SpawnWoodPickup(item.Type,new Vector3(item.X,item.Y,item.Z),item.Id,item.Count,item.Age);pickup.RestoreMotion(item);}
        GameUi.RefreshHotbar();
    }
    async void StartWorldQa(){try{await new WorldQa(this).Run();}catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}}
    async void StartGameQa(){try{await new GameQa(this).Run();}catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}}
    async void StartGrassQa(){try{await new GrassQa(this).Run();}catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}}
    async void StartTreeQa(){try{await new TreeQa(this).Run();}catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}}
}

