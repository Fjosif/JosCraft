using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

// Targeted independent QA: actual game actions, imported skin, mesh materials
// and physics rays. Uses a separate process and only tree-qa artifacts.
public sealed class TreeQa
{
    readonly BlockWorld world;
    readonly SceneTree tree;
    readonly Node anchor;
    readonly List<string> failures=new();
    readonly List<object> checks=new();
    readonly string output=ProjectSettings.GlobalizePath("res://artifacts");
    public TreeQa(BlockWorld world)
    {
        this.world=world;tree=world.GetTree();anchor=new Node{Name="TreeQaAnchor",ProcessMode=Node.ProcessModeEnum.Always};tree.Root.AddChild(anchor);
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
        if(image!=null)Check(image.SavePng(Path.Combine(output,"tree-qa-"+name+".png"))==Error.Ok,"Failed screenshot "+name);
    }
    float? PhysicsTop(float x,float z,float start=40)
    {
        var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x,start,z),new Vector3(x,-2,z),1));
        return hit.Count>0?hit["position"].AsVector3().Y:null;
    }
    // A vertical ray independently intersects actual ArrayMesh top triangles,
    // and records the actual bound texture; this does not call HeightAt.
    (float? Y,string Texture) RenderedTop(float x,float z,float limit=40,bool soilOnly=false)
    {
        float? top=null;string texture="";
        foreach(var mi in Viewer.Descendants(world.Terrain).OfType<MeshInstance3D>())for(int s=0;s<mi.Mesh.GetSurfaceCount();s++)
        {
            string material=(mi.Mesh.SurfaceGetMaterial(s) as StandardMaterial3D)?.AlbedoTexture?.ResourcePath??"";
            if(soilOnly&&!new[]{"grass-top-v1.png","grass-side-v1.png","dirt-v1.png","stone-v1.png"}.Any(material.EndsWith))continue;
            var arrays=mi.Mesh.SurfaceGetArrays(s);var vv=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var ii=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int p=0;p<ii.Length;p+=3)
            {
                if(normals[ii[p]].Y<.9)continue;var a=mi.GlobalTransform*vv[ii[p]];var b=mi.GlobalTransform*vv[ii[p+1]];var c=mi.GlobalTransform*vv[ii[p+2]];if(a.Y>limit)continue;
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
    void Write(object report)=>File.WriteAllText(Path.Combine(output,"tree-qa.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    string PlanHash(TreeGenerator.Plan plan)
    {
        string canonical=string.Join("\n",plan.Trees.OrderBy(t=>t.Id).Select(t=>$"{t.Id}|{t.Species}|{t.Root}|{string.Join(';',t.Logs)}|{string.Join(';',t.Leaves)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
    async Task Move(Vector3 position)
    {
        world.SimulateInput(Vector2.Zero);world.Body.Position=position;world.Body.Velocity=Vector3.Zero;world.Terrain.UpdateStreaming(position);await Ready();await Physics(35);
    }
    async Task Photo(Vector3 center,string name,Vector3? offset=null)
    {
        var camera=new Camera3D{Name="IndependentTreePhoto",Position=center+(offset??new Vector3(10,8,12)),Current=true,Fov=50,Far=160};anchor.AddChild(camera);camera.LookAt(center);await Capture(name);camera.QueueFree();world.Camera.Current=true;await Frame(1);
    }
    bool SoilRoot(TreeGenerator.TreeRecord tree)
    {
        float min=float.PositiveInfinity;string bark=BlockCatalog.Get(tree.LogType).SideTexture;
        foreach(var mi in Viewer.Descendants(world.Terrain).OfType<MeshInstance3D>())for(int s=0;s<mi.Mesh.GetSurfaceCount();s++)
        {
            if((mi.Mesh.SurfaceGetMaterial(s) as StandardMaterial3D)?.AlbedoTexture?.ResourcePath!=bark)continue;
            foreach(var v in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                var p=mi.GlobalTransform*v;
                if(p.X>=tree.Root.X-.001&&p.X<=tree.Root.X+1.001&&p.Z>=tree.Root.Z-.001&&p.Z<=tree.Root.Z+1.001&&p.Y>=tree.Root.Y-.001&&p.Y<tree.Root.Y+.99)min=Math.Min(min,p.Y);
            }
        }
        bool realGround=false;object? proof=null;
        foreach(var delta in new[]{new Vector2(-.01f,.5f),new Vector2(1.01f,.5f),new Vector2(.5f,-.01f),new Vector2(.5f,1.01f)})
        {
            float x=tree.Root.X+delta.X,z=tree.Root.Z+delta.Y;var mesh=RenderedTop(x,z,tree.Root.Y+.01f,true);float? collision=PhysicsTop(x,z,tree.Root.Y+.05f);
            if(mesh.Y!=null&&collision!=null&&Math.Abs(mesh.Y.Value-tree.Root.Y)<.002&&Math.Abs(mesh.Y.Value-collision.Value)<.002){realGround=true;proof=new{x,z,meshTop=mesh.Y,collisionTop=collision};break;}
        }
        bool pass=Math.Abs(min-tree.Root.Y)<.002&&realGround&&world.Terrain.GetBlock(tree.Root+Vector3I.Down)!=BlockType.Air;
        Check(pass,$"Natural root grounding {tree.Id}/{tree.Species} barkBottom={min} realSoil={realGround}");checks.Add(new{label="initial natural root grounding",tree.Id,species=tree.Species.ToString(),root=tree.Root.ToString(),barkBottom=min,realSoil=proof,pass});return pass;
    }
    object MeshAudit(string label)
    {
        int count=0,invalid=0,wrongWinding=0,expected=0;var seenTextures=new HashSet<string>();
        foreach(var mi in Viewer.Descendants(world.Terrain).OfType<MeshInstance3D>())for(int s=0;s<mi.Mesh.GetSurfaceCount();s++)
        {
            var arrays=mi.Mesh.SurfaceGetArrays(s);var vv=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var nn=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var ii=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var mat=mi.Mesh.SurfaceGetMaterial(s) as StandardMaterial3D;Check(mat?.AlbedoTexture!=null,"Forest mesh material texture missing");if(mat?.AlbedoTexture!=null)seenTextures.Add(mat.AlbedoTexture.ResourcePath);
            Check(uv.Length==vv.Length&&uv.Any(v=>v.X>.9)&&uv.Any(v=>v.Y>.9),"Forest UV mapping missing");
            for(int i=0;i<ii.Length;i+=3)
            {
                var a=mi.GlobalTransform*vv[ii[i]];var b=mi.GlobalTransform*vv[ii[i+1]];var c=mi.GlobalTransform*vv[ii[i+2]];var n=nn[ii[i]];var center=(a+b+c)/3;count++;
                Vector3I Cell(Vector3 p)=>new(Mathf.FloorToInt(p.X),Mathf.FloorToInt(p.Y),Mathf.FloorToInt(p.Z));
                if(world.Terrain.GetBlock(Cell(center-n*.02f))==BlockType.Air||world.Terrain.GetBlock(Cell(center+n*.02f))!=BlockType.Air)invalid++;
                if((b-a).Cross(c-a).Dot(n)>=0)wrongWinding++;
                var owner=world.Terrain.GetBlock(Cell(center-n*.02f));string correct=BlockCatalog.SurfaceTexture(BlockCatalog.SurfaceFor(owner,n));Check(mat?.AlbedoTexture?.ResourcePath==correct,"Actual bark/end/leaf material assigned to wrong face");
            }
        }
        foreach(var chunk in world.Terrain.Chunks.Keys)for(int x=chunk.X*16;x<chunk.X*16+16;x++)for(int z=chunk.Y*16;z<chunk.Y*16+16;z++)for(int y=0;y<=BlockTerrain.MaxBuildY;y++)
        if(world.Terrain.GetBlock(new Vector3I(x,y,z))!=BlockType.Air)
            foreach(var d in new[]{Vector3I.Up,Vector3I.Down,Vector3I.Left,Vector3I.Right,Vector3I.Forward,Vector3I.Back})if(world.Terrain.GetBlock(new Vector3I(x,y,z)+d)==BlockType.Air)expected+=2;
        Check(invalid==0&&wrongWinding==0&&count==expected,$"{label}: culled mesh invalid{invalid}/winding{wrongWinding}/triangles{count}/{expected}");
        return new{label,triangles=count,expected,invalid,wrongWinding,actualTextures=seenTextures.Order().ToArray()};
    }
    async Task<bool> StandAndAimRoot(TreeGenerator.TreeRecord tree)
    {
        foreach(var d in new[]{new Vector2(0,3),new Vector2(3,0),new Vector2(0,-3),new Vector2(-3,0)})
        {
            int x=tree.Root.X+(int)d.X,z=tree.Root.Z+(int)d.Y;int h=world.Terrain.BaseHeightAt(x,z);if(h<1||Math.Abs(h-tree.Root.Y)>1)continue;
            await Move(new Vector3(x+.5f,h+.08f,z+.5f));await Aim(new Vector3(tree.Root.X+.5f,tree.Root.Y+.5f,tree.Root.Z+.5f));
            if(world.RayTarget()?.Cell==tree.Root)return true;
        }
        return false;
    }
    float PickupMin(WoodPickup pickup)
    {
        float min=float.PositiveInfinity;
        foreach(var mi in Viewer.Descendants(pickup).OfType<MeshInstance3D>())for(int s=0;s<mi.Mesh.GetSurfaceCount();s++)foreach(var v in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())min=Math.Min(min,(mi.GlobalTransform*v).Y);
        return min;
    }
    void PickupGround(WoodPickup pickup,string label)
    {
        float min=PickupMin(pickup);float? collision=PhysicsTop(pickup.Position.X,pickup.Position.Z,pickup.Position.Y+.2f);var mesh=RenderedTop(pickup.Position.X,pickup.Position.Z,pickup.Position.Y+.2f);
        Check(collision!=null&&mesh.Y!=null&&Math.Abs(min-collision.Value)<.035&&Math.Abs(collision.Value-mesh.Y.Value)<.002,label+" real pickup mesh/floor collision");checks.Add(new{label,pickup.Id,pickup.Type,pickup.Count,min,collisionTop=collision,renderedTop=mesh.Y});
    }
    Vector3 SafeChunkPosition(Vector2I chunk)
    {
        foreach(int dx in new[]{0,-3,3,-5,5})foreach(int dz in new[]{0,-3,3,-5,5})
        {
            int x=chunk.X*16+8+dx,z=chunk.Y*16+8+dz;int y=0;
            for(int a=-2;a<=2;a++)for(int b=-2;b<=2;b++)y=Math.Max(y,world.Terrain.BaseHeightAt(x+a,z+b));
            bool air=true;for(int a=-2;a<=2;a++)for(int b=-2;b<=2;b++)for(int h=y;h<=y+3;h++)if(world.Terrain.GetBlock(new Vector3I(x+a,h,z+b))!=BlockType.Air)air=false;
            if(air)return new Vector3(x+.5f,y+.08f,z+.5f);
        }
        throw new Exception("No clear forest observation position in chunk "+chunk);
    }
    async Task<object> UnloadedTrunkSupport(TreeGenerator.Plan plan)
    {
        foreach(var t in plan.Trees)foreach(var p in t.Leaves)
        {
            var trunk=BlockTerrain.ChunkAt(t.Root.X,t.Root.Z);var leaf=BlockTerrain.ChunkAt(p.X,p.Z);if(trunk==leaf)continue;
            Vector2I focus;
            if(leaf.X<trunk.X)focus=new(trunk.X-3,trunk.Y);else if(leaf.X>trunk.X)focus=new(trunk.X+3,trunk.Y);else if(leaf.Y<trunk.Y)focus=new(trunk.X,trunk.Y-3);else focus=new(trunk.X,trunk.Y+3);
            if(focus.X< -3||focus.X>2||focus.Y< -3||focus.Y>2)continue;
            await Move(SafeChunkPosition(focus));var observed=t.Leaves.Where(c=>world.Terrain.IsLoadedAt(c.X,c.Z)&&world.Terrain.GetBlock(c)==t.LeavesType).ToArray();
            if(world.Terrain.IsLoadedAt(t.Root.X,t.Root.Z)||observed.Length==0)continue;
            await Wait(8.4);bool supported=observed.All(c=>world.Terrain.GetBlock(c)==t.LeavesType);Check(supported,"Loaded crown decayed despite its actual log in an unloaded chunk");
            var audit=MeshAudit("loaded crown / unloaded supporting trunk seam");await Photo(new Vector3(p.X+.5f,p.Y+.5f,p.Z+.5f),"unloaded-trunk-supported-crown");
            world.Respawn();await Ready();await Physics(35);return new{treeId=t.Id,loadedLeaves=observed.Length,trunkChunk=trunk.ToString(),leafChunk=leaf.ToString(),focus=focus.ToString(),supported,audit};
        }
        throw new Exception("No cross-chunk crown/trunk support fixture found");
    }
    async Task Tests()
    {
        await Ready();world.SimulateInput(Vector2.Zero);await Physics(40);Check(GameSession.IsQa&&GameSession.SavePath.Contains("artifacts"),"Tree QA save isolation");
        int seed=world.Terrain.Seed;var plan=TreeGenerator.Generate(seed);string same=PlanHash(plan),repeat=PlanHash(TreeGenerator.Generate(seed)),different=PlanHash(TreeGenerator.Generate(seed+12345));
        Check(same==repeat&&same!=different&&plan.Trees.Select(t=>t.Species).Distinct().Count()==3,"Deterministic3species forest plan");
        Check(plan.Trees.All(t=>t.Logs.Concat(t.Leaves).All(p=>new Vector2(p.X+.5f,p.Z+.5f).Length()>=6.9f)),"Spawn clearing contains tree voxels");
        var textures=new List<object>();for(int s=4;s<BlockCatalog.SurfaceCount;s++){string path=BlockCatalog.SurfaceTexture(s);var image=ResourceLoader.Load<Texture2D>(path).GetImage();Check(image.GetWidth()==256&&image.GetHeight()==256&&image.HasMipmaps(),"Tree texture256+mips "+path);textures.Add(new{path,width=image.GetWidth(),height=image.GetHeight(),mipmaps=image.HasMipmaps()});}
        var initialMesh=MeshAudit("initial forest including chunk seams");int grounded=0;
        foreach(var t in world.Terrain.Trees.Where(t=>world.Terrain.IsLoadedAt(t.Root.X,t.Root.Z)))if(SoilRoot(t))grounded++;
        Check(grounded>=3,"Too few actual rendered root samples");await Photo(new Vector3(.5f,8,.5f),"forest-overview",new Vector3(28,22,30));
        foreach(var species in Enum.GetValues<TreeSpecies>()){var t=plan.Trees.Where(t=>t.Species==species).OrderBy(t=>new Vector2(t.Root.X,t.Root.Z).LengthSquared()).First();await Move(new Vector3(t.Root.X+4.5f,world.Terrain.BaseHeightAt(t.Root.X+4,t.Root.Z+4)+.08f,t.Root.Z+4.5f));await Photo(new Vector3(t.Root.X+.5f,t.Root.Y+3.5f,t.Root.Z+.5f),"species-"+species.ToString().ToLowerInvariant());}
        var unloadedSupport=await UnloadedTrunkSupport(plan);
        TreeGenerator.TreeRecord? harvested=null;foreach(var t in plan.Trees.Where(t=>t.Species==TreeSpecies.Oak).OrderBy(t=>new Vector2(t.Root.X,t.Root.Z).LengthSquared()))if(await StandAndAimRoot(t)){harvested=t;break;}
        Check(harvested!=null,"No reachable actual log base");var tree=harvested!;int type=(int)tree.LogType;int beforeWood=world.InventoryCount(type);
        Vector3 harvestStand=world.Body.Position;world.SetMiningHeld(true);await Wait(2);Check(world.Terrain.GetBlock(tree.Root)==tree.LogType&&world.WoodPickups.Count==0&&world.MiningProgress>0&&world.MiningProgress<1,"Log broke before3second hold");await Wait(1.25);world.SetMiningHeld(false);await Physics(2);
        Check(world.Terrain.GetBlock(tree.Root)==BlockType.Air&&tree.Logs.Skip(1).All(p=>world.Terrain.GetBlock(p)==tree.LogType),"Log mining removed more than selected voxel");
        Check(world.Terrain.Chunks.Values.Any(c=>Enumerable.Range(0,c.MeshInstance.Mesh.GetSurfaceCount()).Any(s=>(c.MeshInstance.Mesh.SurfaceGetMaterial(s) as StandardMaterial3D)?.AlbedoTexture?.ResourcePath==BlockCatalog.LogEndTexture)),"Cut trunk did not reveal actual end-grain material");
        Check(world.InventoryCount(type)==beforeWood&&world.WoodPickups.Count==1,"Wood was collected instantly instead of a physical item");var drop=world.WoodPickups.Single();string dropId=drop.Id;Check(drop.Type==type&&drop.Count==1,"Physical log drop wrong species/count");await Wait(1);PickupGround(drop,"mined log item on actual floor");
        world.SetPaused(true);Vector3 pausedPos=drop.Position;float pausedAge=drop.Age;await Wait(.7);Check(drop.Position.DistanceTo(pausedPos)<.0001&&Math.Abs(drop.Age-pausedAge)<.0001&&!world.CollectWoodPickup(drop),"Paused log item physics/age/collection advanced");world.SetPaused(false);
        string pendingSave=Path.Combine(output,"tree-qa-data","uncollected.json");Check(world.SaveSnapshot(pendingSave),"Save uncollected item failed");Check(world.LoadSnapshot(pendingSave),"Load uncollected item failed");await Ready();await Physics(20);
        Check(world.WoodPickups.Count==1&&world.WoodPickups[0].Id==dropId&&world.InventoryCount(type)==beforeWood&&world.Terrain.GetBlock(tree.Root)==BlockType.Air,"Uncollected item/harvest did not persist");drop=world.WoodPickups.Single();PickupGround(drop,"saved item floor restored");await Photo(new Vector3(tree.Root.X+.5f,tree.Root.Y+1.3f,tree.Root.Z+.5f),"trunk-cut-item",new Vector3(5,-.5f,5));
        var far=tree.Root.X<0||tree.Root.Z<0?new Vector2I(2,2):new Vector2I(-3,-3);await Move(SafeChunkPosition(far));float frozenAge=drop.Age;Vector3 frozenPosition=drop.Position;await Wait(.5);
        Check(!drop.Visible&&drop.Freeze&&Math.Abs(drop.Age-frozenAge)<.0001&&drop.Position.DistanceTo(frozenPosition)<.0001&&world.WoodPickups.Count==1,"Unloaded pickup did not suspend without deletion");await Move(harvestStand);await Wait(.3);
        await Move(new Vector3(drop.Position.X,drop.Position.Y-.14f+.08f,drop.Position.Z+1.0f));await Wait(.4);Check(world.WoodPickups.Count==0&&world.InventoryCount(type)==beforeWood+1,"Nearby collection did not transfer exactly1wood");
        var supportedBefore=tree.Leaves.Where(p=>world.Terrain.GetBlock(p)==tree.LeavesType).ToArray();await Wait(8.4);Check(supportedBefore.All(p=>world.Terrain.GetBlock(p)==tree.LeavesType),"Cutting base incorrectly killed leaves still supported by upper logs");
        Vector3I? handLeaf=null;foreach(var leaf in tree.Leaves.OrderBy(p=>new Vector3(p.X,p.Y,p.Z).DistanceSquaredTo(world.Body.Position)))
        {
            if(world.Terrain.GetBlock(leaf)!=tree.LeavesType)continue;await Aim(new Vector3(leaf.X+.5f,leaf.Y+.5f,leaf.Z+.5f));if(world.RayTarget()?.Cell==leaf){handLeaf=leaf;break;}
        }
        Check(handLeaf!=null,"No actual reachable leaf for bare-hand test");if(handLeaf!=null){int ownedBefore=world.InventoryCount(type);Check(world.MineTarget()&&world.Terrain.GetBlock(handLeaf.Value)==BlockType.Air&&world.WoodPickups.Count==0&&world.InventoryCount(type)==ownedBefore,"Bare-hand leaves incorrectly yielded loot");}
        // Pause unsupported natural leaves, then allow the scheduled decay.
        var natural=tree.Leaves.Where(p=>world.Terrain.GetBlock(p)==tree.LeavesType).ToArray();foreach(var p in tree.Logs)if(world.Terrain.GetBlock(p)!=BlockType.Air)world.Terrain.TrySetBlock(p,0);
        world.SetPaused(true);await Wait(8.3);Check(natural.All(p=>world.Terrain.GetBlock(p)==tree.LeavesType),"Paused unsupported foliage decayed");world.SetPaused(false);await Wait(8.8);
        Check(natural.All(p=>world.Terrain.GetBlock(p)==BlockType.Air)&&world.WoodPickups.Count==0,"Unsupported natural leaves did not decay without loot");await Photo(new Vector3(tree.Root.X+.5f,tree.Root.Y+2,tree.Root.Z+.5f),"decayed-canopy");
        string harvestSave=Path.Combine(output,"tree-qa-data","harvest-decay.json");Check(world.SaveSnapshot(harvestSave)&&world.LoadSnapshot(harvestSave),"Harvested tree save failed");await Ready();Check(tree.Logs.Concat(natural).All(p=>world.Terrain.GetBlock(p)==BlockType.Air)&&world.InventoryCount(type)==1,"Harvest/leaf Air or collected count lost on load");
        world.Respawn();await Ready();await Physics(40);int owned=Enumerable.Range(0,9).First(i=>world.GetSlot(i).Type==type);world.SelectHotbar(owned);await Aim(new Vector3(3.5f,3.95f,.5f));var placement=world.RayTarget()!.Placement;Check(world.PlaceTarget()&&world.Terrain.GetBlockId(placement)==type&&world.InventoryCount(type)==0,"Owned wood placement did not cost1same species");await Physics(2);await Photo(new Vector3(3.5f,5,.5f),"placed-owned-log",new Vector3(5,4,6));
        // Full bag cannot erase a physical drop. A validated QA snapshot frees
        // one slot without introducing a creative user-facing inventory path.
        string normalSave=Path.Combine(output,"tree-qa-data","normal.json");world.SaveSnapshot(normalSave);var full=GameSession.Read(normalSave)!;full.Slots=Enumerable.Range(0,36).Select(_=>new ItemStack(3,64)).ToArray();string fullFile=Path.Combine(output,"tree-qa-data","full.json");GameSession.WriteAtomic(fullFile,full);world.LoadSnapshot(fullFile);await Ready();await Physics(35);
        var fullTree=plan.Trees.First(t=>t.Species==TreeSpecies.Oak&&t.Id!=tree.Id);Check(await StandAndAimRoot(fullTree),"No full-bag log target");world.SetMiningHeld(true);await Wait(3.25);world.SetMiningHeld(false);await Wait(.5);
        Check(world.WoodPickups.Count==1&&world.Terrain.GetBlock(fullTree.Root)==BlockType.Air,"Full bag prevented log mining into physical item");var fullDrop=world.WoodPickups.Single();
        await Move(new Vector3(fullDrop.Position.X,fullDrop.Position.Y-.14f+.08f,fullDrop.Position.Z+1));await Wait(.3);Check(world.WoodPickups.Count==1&&!world.CollectWoodPickup(fullDrop)&&world.InventoryCount(type)==0,"Full bag lost/collected physical wood");PickupGround(fullDrop,"full bag item remains grounded");
        string fullDropFile=Path.Combine(output,"tree-qa-data","full-with-drop.json");world.SaveSnapshot(fullDropFile);var freed=GameSession.Read(fullDropFile)!;freed.Slots[0]=new ItemStack();string freedFile=Path.Combine(output,"tree-qa-data","freed-slot.json");GameSession.WriteAtomic(freedFile,freed);world.LoadSnapshot(freedFile);await Ready();await Wait(.5);Check(world.WoodPickups.Count==0&&world.InventoryCount(type)==1,"Freed bag slot did not collect retained wood exactly once");
        world.ToggleInventory();await Capture("inventory-wood");world.ToggleInventory();world.LoadSnapshot(normalSave);await Ready();await Physics(35);
        // Genuine old version1 JSON: omit both new forest/pickup fields.
        var oldEdited=plan.Trees.First(t=>t.Id!=tree.Id);var oldPlayer=plan.Trees.First(t=>t.Id!=tree.Id&&t.Id!=oldEdited.Id);string oldPath=Path.Combine(output,"tree-qa-data","legacy-v1.json");
        GameSession.WriteAtomic(oldPath,new{Version=1,Seed=seed,Character=0,X=oldPlayer.Root.X+.5f,Y=oldPlayer.Root.Y+.08f,Z=oldPlayer.Root.Z+.5f,Slots=Enumerable.Range(0,36).Select(_=>new ItemStack()).ToArray(),Edits=new[]{new BlockTerrain.BlockEdit(oldEdited.Root.X,oldEdited.Root.Y,oldEdited.Root.Z,3)}});
        Check(GameSession.Read(oldPath)!=null&&world.LoadSnapshot(oldPath),"Valid old version1 save rejected");await Ready();
        Check(world.Terrain.TreeExclusions.Contains(oldEdited.Id)&&world.Terrain.TreeExclusions.Contains(oldPlayer.Id)&&world.Terrain.GetBlock(oldEdited.Root)==BlockType.Stone&&!world.Terrain.Trees.Any(t=>t.Id==oldEdited.Id||t.Id==oldPlayer.Id),"Legacy forest migration overwrote edit/player or grew excluded tree");
        var mask=world.Terrain.TreeExclusions;string migrated=Path.Combine(output,"tree-qa-data","migrated.json");world.SaveSnapshot(migrated);Check(GameSession.Read(migrated)!.TreeExclusions!.SequenceEqual(mask)&&world.LoadSnapshot(migrated),"Legacy exclusion mask did not persist");await Ready();Check(world.Terrain.TreeExclusions.SequenceEqual(mask),"Reload recomputed frozen migration mask");
        world.SetSeed(seed+12345);await Ready();Check(world.Terrain.TreeExclusions.Length==0&&PlanHash(TreeGenerator.Generate(world.Terrain.Seed))!=same,"New forest seed reused legacy exclusions");world.SetSeed(seed);await Ready();world.Respawn();await Physics(35);
        // Existing grass behaviour remains a genuine player-action smoke test.
        await Aim(new Vector3(-1.5f,3.95f,-1.5f));Check(world.MineTarget()&&Dirt==1&&Grass==0,"Tree stage regressed grass→Dirt drop");world.SelectHotbar(Enumerable.Range(0,9).First(i=>world.GetSlot(i).Type==2));await Aim(new Vector3(3.5f,3.95f,.5f));var grassCell=world.RayTarget()!.Placement;Check(world.PlaceTarget()&&Dirt==0,"Tree stage Dirt placement consumption");await Wait(3.4);Surface(grassCell,BlockType.Grass,"grass smoke regrowth");SkinGround("actual imported skin on forest spawn floor");
        world.SetOverview(true);await Capture("forest-final");var times=new List<double>();var clock=System.Diagnostics.Stopwatch.StartNew();double previous=clock.Elapsed.TotalMilliseconds;for(int n=0;n<120;n++){await Frame(1);double now=clock.Elapsed.TotalMilliseconds;times.Add(now-previous);previous=now;}
        var performance=new{gpu=RenderingServer.GetVideoAdapterName(),renderer=RenderingServer.GetCurrentRenderingMethod(),msaa=world.GetViewport().Msaa3D.ToString(),chunks=world.Terrain.ActiveChunkCount,triangles=world.Terrain.TotalTriangles,drawCalls=Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),primitives=Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),medianMs=times.Order().ElementAt(60),p95Ms=times.Order().ElementAt(114),scope="Actual desktop1280x800,120FPS cap; no physical mobile performance claim"};
        world.SetOverview(false);this.tree.Root.Size=new Vector2I(390,844);await Capture("forest-mobile-390");
        var result=new{pass=failures.Count==0,utc=DateTime.UtcNow,determinism=new{same,repeat,different,treeCount=plan.Trees.Count,species=plan.Trees.Select(t=>t.Species.ToString()).Distinct().ToArray()},groundedRoots=grounded,textures,initialMesh,unloadedSupport,harvest=new{tree.Id,species=tree.Species.ToString(),dropType=type,dropCount=1,logDelay=3f,physicalCollectionOnly=true,fullBagMiningDropRetained=true},decay=new{naturalLeaves=natural.Length,paused=true,harvestAndAirSaved=true,bareHandLeafNoLoot=true},migration=new{legacyVersion1Accepted=true,frozenMask=mask},actualSamples=checks,performance,failures};Write(result);GD.Print("JOSCRAFT_TREE_QA "+(failures.Count==0?"PASS":"FAIL")+" "+string.Join("; ",failures));this.tree.Quit(failures.Count==0?0:1);
    }
}
