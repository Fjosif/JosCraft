using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

// Targeted independent QA: actual game actions, imported skin, mesh materials
// and physics rays. Uses a separate process and only grass-qa artifacts.
public sealed class GrassQa
{
    readonly BlockWorld world;
    readonly SceneTree tree;
    readonly Node anchor;
    readonly List<string> failures=new();
    readonly List<object> checks=new();
    readonly string output=ProjectSettings.GlobalizePath("res://artifacts");
    public GrassQa(BlockWorld world)
    {
        this.world=world;tree=world.GetTree();anchor=new Node{Name="GrassQaAnchor",ProcessMode=Node.ProcessModeEnum.Always};tree.Root.AddChild(anchor);
    }
    void Check(bool ok,string message){if(!ok)failures.Add(message);}
    async Task Frame(int count=2){for(int i=0;i<count;i++)await anchor.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    async Task Physics(int count=2){for(int i=0;i<count;i++)await anchor.ToSignal(tree,SceneTree.SignalName.PhysicsFrame);}
    async Task Wait(double seconds)
    {
        ulong until=Time.GetTicksMsec()+(ulong)Math.Ceiling(seconds*1000);
        while(Time.GetTicksMsec()<until)await Frame(1);
    }
    async Task Ready()
    {
        for(int i=0;i<180;i++){await Frame(1);if(world.CharacterReady&&world.Terrain.PendingChunkCount==0){await Physics(3);return;}}
        throw new Exception("Grass QA world did not settle");
    }
    async Task Aim(Vector3 point)
    {
        world.SetFirstPerson(true);var d=point-world.Body.GlobalPosition-Vector3.Up*world.EyeHeight;
        world.CameraYaw=Mathf.Atan2(-d.X,-d.Z);world.FirstPersonPitch=Mathf.Atan2(-d.Y,new Vector2(d.X,d.Z).Length());await Frame(3);
    }
    async Task Capture(string name)
    {
        await Frame(3);await anchor.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var image=tree.Root.GetTexture().GetImage();Check(image!=null&&!image.IsEmpty(),"Empty grass render "+name);
        if(image!=null)Check(image.SavePng(Path.Combine(output,"grass-qa-"+name+".png"))==Error.Ok,"Failed screenshot "+name);
    }
    float? PhysicsTop(float x,float z)
    {
        var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x,40,z),new Vector3(x,-2,z),1));
        return hit.Count>0?hit["position"].AsVector3().Y:null;
    }
    // A vertical ray independently intersects actual ArrayMesh top triangles,
    // and records the actual bound texture; this does not call HeightAt.
    (float? Y,string Texture) RenderedTop(float x,float z)
    {
        float? top=null;string texture="";
        foreach(var mi in Viewer.Descendants(world.Terrain).OfType<MeshInstance3D>())for(int s=0;s<mi.Mesh.GetSurfaceCount();s++)
        {
            var arrays=mi.Mesh.SurfaceGetArrays(s);var vv=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var ii=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int p=0;p<ii.Length;p+=3)
            {
                if(normals[ii[p]].Y<.9)continue;var a=mi.GlobalTransform*vv[ii[p]];var b=mi.GlobalTransform*vv[ii[p+1]];var c=mi.GlobalTransform*vv[ii[p+2]];
                var ab=new Vector2(b.X-a.X,b.Z-a.Z);var ac=new Vector2(c.X-a.X,c.Z-a.Z);var ap=new Vector2(x-a.X,z-a.Z);float d=ab.Cross(ac);if(Math.Abs(d)<.00001)continue;
                float u=ap.Cross(ac)/d,v=ab.Cross(ap)/d;if(u<-.00001||v<-.00001||u+v>1.00001)continue;
                if(top==null||a.Y>top){top=a.Y;texture=(mi.Mesh.SurfaceGetMaterial(s) as StandardMaterial3D)?.AlbedoTexture?.ResourcePath??"";}
            }
        }
        return(top,texture);
    }
    void Surface(Vector3I cell,BlockType expected,string label)
    {
        var rendered=RenderedTop(cell.X+.5f,cell.Z+.5f);float? physics=PhysicsTop(cell.X+.5f,cell.Z+.5f);
        string expectedTexture=expected==BlockType.Grass?"grass-top-v1.png":expected==BlockType.Dirt?"dirt-v1.png":"stone-v1.png";
        Check(world.Terrain.GetBlock(cell)==expected,label+" actual voxel type");
        Check(rendered.Y!=null&&physics!=null&&Math.Abs(rendered.Y.Value-(cell.Y+1))<.002&&Math.Abs(rendered.Y.Value-physics.Value)<.002,label+" rendered triangle/physics top mismatch");
        Check(rendered.Texture.EndsWith(expectedTexture,StringComparison.OrdinalIgnoreCase),label+" actual rendered material not "+expectedTexture);
        checks.Add(new{label,cell=cell.ToString(),expected=expected.ToString(),renderedTop=rendered.Y,physicsTop=physics,texture=rendered.Texture});
    }
    void SkinGround(string label)
    {
        float min=float.PositiveInfinity;
        foreach(var mesh in Viewer.Descendants(world.Visual).OfType<MeshInstance3D>())
        {
            Mesh baked=mesh.Skin!=null?mesh.BakeMeshFromCurrentSkeletonPose():mesh.Mesh;
            for(int s=0;s<baked.GetSurfaceCount();s++)foreach(var v in baked.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())min=Math.Min(min,(mesh.GlobalTransform*v).Y);
        }
        float? physics=PhysicsTop(world.Body.Position.X,world.Body.Position.Z);var rendered=RenderedTop(world.Body.Position.X,world.Body.Position.Z);
        Check(physics!=null&&rendered.Y!=null&&Math.Abs(min-physics.Value)<.035&&Math.Abs(rendered.Y.Value-physics.Value)<.002&&world.Body.IsOnFloor(),label+" imported skinned feet/floor contact");
        checks.Add(new{label,skinMin=min,physicsTop=physics,renderedTop=rendered.Y});
    }
    int Dirt=>world.InventoryCount(BlockType.Dirt);
    int Grass=>world.InventoryCount(BlockType.Grass);
    async Task Fixture(Vector3I cell,BlockType type)
    {
        Check(world.Terrain.TrySetBlock(cell,type),"Could not create isolated growth fixture "+cell);await Physics(2);await Frame(2);
    }
    public async Task Run()
    {
        try{await Tests();}
        catch(Exception e){failures.Add(e.ToString());Write(new{pass=false,failures});GD.PrintErr(e);tree.Quit(1);}
    }
    void Write(object report)=>File.WriteAllText(Path.Combine(output,"grass-qa.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    async Task Tests()
    {
        await Ready();world.SimulateInput(Vector2.Zero);await Physics(35);
        Check(GameSession.IsQa&&GameSession.SavePath.Contains("artifacts"),"Grass QA must isolate save paths");
        Check(Dirt==0&&Grass==0,"Grass QA must start with empty survival inventory");
        Check(Math.Abs(BlockTerrain.GrassGrowthDelaySeconds-3)<.0001,"Configured grass growth delay is not3seconds");
        await Aim(new Vector3(-1.5f,3.95f,-1.5f));var mined=world.RayTarget();Check(mined?.Type==(int)BlockType.Grass,"Initial actual grass ray target");
        Check(world.MineTarget()&&Dirt==1&&Grass==0,"Mining grass must collect exactly1Dirt,notGrass");await Physics(2);
        int slot=Enumerable.Range(0,9).First(i=>world.GetSlot(i).Type==(int)BlockType.Dirt&&world.GetSlot(i).Count>0);world.SelectHotbar(slot);
        await Aim(new Vector3(3.5f,3.95f,.5f));var place=world.RayTarget();Check(place!=null,"Dirt placement ray missing");var cell=place!.Placement;
        Check(world.PlaceTarget()&&Dirt==0&&Grass==0,"Successful Dirt placement must consume exactly1unit");ulong placedAt=Time.GetTicksMsec();await Physics(2);Surface(cell,BlockType.Dirt,"placed Dirt actual surface");
        world.Body.Position=new Vector3(cell.X+.5f,cell.Y+1.08f,cell.Z+.5f);world.Body.Velocity=Vector3.Zero;await Physics(25);
        world.SetFirstPerson(false);world.CameraYaw=.9f;world.CameraPitch=.4f;world.CameraDistance=6;
        double remain=2-(Time.GetTicksMsec()-placedAt)/1000.0;if(remain>0)await Wait(remain);
        Surface(cell,BlockType.Dirt,"before3seconds");SkinGround("feet on Dirt before growth");await Capture("dirt-before-growth");
        remain=3.35-(Time.GetTicksMsec()-placedAt)/1000.0;if(remain>0)await Wait(remain);await Physics(2);Surface(cell,BlockType.Grass,"after3seconds");SkinGround("feet on Grass after growth");await Capture("grass-after-growth");
        await Aim(world.Body.Position+new Vector3(0,-.05f,-.45f));var regrown=world.RayTarget();Check(regrown?.Cell==cell&&regrown.Type==(int)BlockType.Grass,"Regrown grass actual mining target");
        Check(world.MineTarget()&&Dirt==1&&Grass==0,"Regrown Grass must drop1Dirt again without duplication");await Physics(35);SkinGround("feet after mining regrown block");
        // Covered column cannot accumulate growth; removing cover starts anew.
        var covered=new Vector3I(6,4,0);await Fixture(covered,BlockType.Dirt);await Fixture(covered+Vector3I.Up,BlockType.Stone);await Wait(3.35);
        Check(world.Terrain.GetBlock(covered)==BlockType.Dirt,"Covered/cave Dirt incorrectly grew");
        var side=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(5.5f,4.5f,.5f),new Vector3(6.5f,4.5f,.5f),1));Check(side.Count>0&&Math.Abs(side["position"].AsVector3().X-6)<.002,"Covered Dirt actual side collision missing");
        Check(world.Terrain.TrySetBlock(covered+Vector3I.Up,0),"Uncover fixture failed");await Wait(1);Surface(covered,BlockType.Dirt,"uncovered fresh timer");await Wait(2.4);await Physics(2);Surface(covered,BlockType.Grass,"uncovered growth completed");
        // Real pause freezes terrain process time, not merely the character.
        var paused=new Vector3I(8,4,0);await Fixture(paused,BlockType.Dirt);await Wait(.4);world.SetPaused(true);await Wait(3.35);Check(world.Terrain.GetBlock(paused)==BlockType.Dirt,"Pause allowed grass timer to mature");world.SetPaused(false);world.SimulateInput(Vector2.Zero);await Wait(1);Surface(paused,BlockType.Dirt,"resume timer not spent during pause");await Wait(2.1);await Physics(2);Surface(paused,BlockType.Grass,"resumed timer growth");
        // Chunk(0,0) is absent from the southwest radius2 neighbourhood.
        var unloaded=new Vector3I(10,4,0);await Fixture(unloaded,BlockType.Dirt);await Wait(.6);
        world.Body.Position=new Vector3(-40.5f,world.Terrain.HeightAt(-41,-41)+.1f,-40.5f);world.Body.Velocity=Vector3.Zero;world.Terrain.UpdateStreaming(world.Body.Position);await Ready();
        Check(!world.Terrain.IsLoadedAt(10.5f,.5f),"Growth test chunk did not unload");await Wait(3.35);Check(world.Terrain.GetBlock(unloaded)==BlockType.Dirt,"Unloaded Dirt matured without an active chunk");
        world.Respawn();await Ready();await Wait(1);Surface(unloaded,BlockType.Dirt,"reloaded fresh growth timer");await Wait(2.3);await Physics(2);Surface(unloaded,BlockType.Grass,"reloaded column regrew");
        // Save stores the material, not hidden elapsed simulation time.
        var saved=new Vector3I(12,4,0);await Fixture(saved,BlockType.Dirt);await Wait(.3);
        string save=Path.Combine(output,"grass-qa-data","growth-session.json");Check(world.SaveSnapshot(save),"Isolated grass save failed");await Wait(3.35);Check(world.Terrain.GetBlock(saved)==BlockType.Grass,"Pre-load live fixture did not mature");
        Check(world.LoadSnapshot(save),"Grass material save/load failed");await Ready();Surface(saved,BlockType.Dirt,"loaded saved Dirt material");Check(Dirt==1&&Grass==0,"Save/load growth duplicated inventory");
        await Wait(3.35);await Physics(2);Surface(saved,BlockType.Grass,"saved exposed Dirt regrows after fresh wait");SkinGround("final actual skinned spawn grounding");
        Check(Input.MouseMode!=Input.MouseModeEnum.Captured,"Grass QA captured native pointer");
        var result=new{pass=failures.Count==0,utc=DateTime.UtcNow,growthDelay=BlockTerrain.GrassGrowthDelaySeconds,inventory=new{grassMined=2,dirtPlaced=1,dirtRemaining=Dirt,grassRemaining=Grass,conserved=Dirt==1&&Grass==0},coveredDirt=true,pauseFrozen=true,unloadedDidNotGrow=true,saveRestoresMaterialAndFreshDelay=true,actualMeshMaterialAndPhysicsSamples=checks,failures};Write(result);GD.Print("JOSCRAFT_GRASS_QA "+(failures.Count==0?"PASS":"FAIL")+" "+string.Join("; ",failures));tree.Quit(failures.Count==0?0:1);
    }
}
