using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

// Independent checks inspect actual imported skin, rendered mesh triangles,
// physics raycasts and the normal runtime input/CharacterBody movement path.
public sealed class GameQa
{
    BlockWorld world;
    readonly SceneTree tree;
    readonly Node anchor;
    static bool running;
    readonly List<string> failures = new();
    readonly List<object> samples = new(), actors = new();
    readonly List<object> wallActors = new();
    int maxObservedBuildsPerFrame,maxObservedChunks;
    readonly string output = ProjectSettings.GlobalizePath("res://artifacts");
    public GameQa(BlockWorld world)
    {
        this.world=world;tree=world.GetTree();
        anchor=tree.Root.GetNodeOrNull<Node>("GameQaAnchor")??new Node{Name="GameQaAnchor",ProcessMode=Node.ProcessModeEnum.Always};
        if(!anchor.IsInsideTree())tree.Root.AddChild(anchor);
    }
    void Check(bool test,string reason) { if(!test) failures.Add(reason); }
    async Task Frame(int count=2) {for(int n=0;n<count;n++) await anchor.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    async Task Physics(int count=1) {for(int n=0;n<count;n++) await anchor.ToSignal(tree,SceneTree.SignalName.PhysicsFrame);}
    async Task Settled()
    {
        for(int n=0;n<150;n++)
        {
            int generated=world.Terrain.TotalGeneratedChunks;await Frame(1);
            maxObservedBuildsPerFrame=Math.Max(maxObservedBuildsPerFrame,world.Terrain.TotalGeneratedChunks-generated);
            maxObservedChunks=Math.Max(maxObservedChunks,world.Terrain.ActiveChunkCount);
            Check(world.Terrain.ActiveChunkCount<=25,"Observed active chunk budget exceeded");
            if(world.Terrain.PendingChunkCount==0 && world.CharacterReady){await Physics(3);return;}
        }
        throw new Exception("World streaming/character loading did not settle");
    }
    async Task Capture(string name)
    {
        await Frame(3); await anchor.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var img=tree.Root.GetViewport().GetTexture().GetImage();
        Check(img!=null&&!img.IsEmpty(),"Empty render "+name);
        if(img!=null)Check(img.SavePng(Path.Combine(output,"game-"+name+".png"))==Error.Ok,"Failed screenshot "+name);
    }
    record Triangle(Vector3 A,Vector3 B,Vector3 C,Vector3 Normal);
    List<Triangle> Geometry()
    {
        var triangles=new List<Triangle>();
        foreach(var mi in Viewer.Descendants(world.Terrain).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null))
        for(int s=0;s<mi.Mesh.GetSurfaceCount();s++)
        {
            var arrays=mi.Mesh.SurfaceGetArrays(s);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            int count=indices.Length>0?indices.Length:vertices.Length;
            for(int p=0;p<count;p+=3)
            {
                int a=indices.Length>0?indices[p]:p,b=indices.Length>0?indices[p+1]:p+1,c=indices.Length>0?indices[p+2]:p+2;
                var normal=normals.Length>0?(mi.GlobalBasis*normals[a]).Normalized():-(vertices[b]-vertices[a]).Cross(vertices[c]-vertices[a]).Normalized();
                triangles.Add(new(mi.GlobalTransform*vertices[a],mi.GlobalTransform*vertices[b],mi.GlobalTransform*vertices[c],normal));
            }
        }
        return triangles;
    }
    string Fingerprint(List<Triangle> triangles)
    {
        string V(Vector3 v)=>$"{v.X:F3},{v.Y:F3},{v.Z:F3}";
        var canonical=triangles.Select(t=>string.Join(";",new[]{V(t.A),V(t.B),V(t.C)}.Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",canonical)))).ToLowerInvariant();
    }
    bool Solid(Vector3 p)
    {
        int x=Mathf.FloorToInt(p.X),y=Mathf.FloorToInt(p.Y),z=Mathf.FloorToInt(p.Z);
        return world.Terrain.GetBlock(new Vector3I(x,y,z))!=BlockType.Air;
    }
    float? RenderedTop(List<Triangle> triangles,float x,float z)
    {
        float? top=null;
        foreach(var t in triangles)
        {
            if(t.Normal.Y<.9f)continue;
            var a=new Vector2(t.A.X,t.A.Z);var b=new Vector2(t.B.X,t.B.Z);var c=new Vector2(t.C.X,t.C.Z);var p=new Vector2(x,z);
            float d=(b-a).Cross(c-a);if(Math.Abs(d)<.00001)continue;
            float u=(p-a).Cross(c-a)/d,v=(b-a).Cross(p-a)/d;
            if(u>=-.0001&&v>=-.0001&&u+v<=1.0001)top=top==null?t.A.Y:Math.Max(top.Value,t.A.Y);
        }
        return top;
    }
    float? CollisionTop(float x,float z)
    {
        var query=PhysicsRayQueryParameters3D.Create(new Vector3(x,64,z),new Vector3(x,-4,z));
        query.Exclude=new Godot.Collections.Array<Rid>{world.Body.GetRid()};
        var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count>0?hit["position"].AsVector3().Y:null;
    }
    void TestRay(List<Triangle> triangles,float x,float z,string label)
    {
        float? rendered=RenderedTop(triangles,x,z),collision=CollisionTop(x,z);
        Check(rendered!=null&&collision!=null&&Math.Abs(rendered.Value-collision.Value)<.002f,$"Rendered triangle/collider mismatch {label}({x},{z}): {rendered}/{collision}");
        samples.Add(new {label,x,z,renderedTop=rendered,collisionTop=collision});
    }
    List<Vector3> Skin()
    {
        var vertices=new List<Vector3>();
        foreach(var mesh in Viewer.Descendants(world.Visual).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null))
        {
            Mesh baked=mesh.Skin!=null?mesh.BakeMeshFromCurrentSkeletonPose():mesh.Mesh;
            for(int s=0;s<baked.GetSurfaceCount();s++)foreach(var v in baked.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())vertices.Add(mesh.GlobalTransform*v);
        }
        return vertices;
    }
    async Task Place(Vector3 position)
    {
        world.SimulateInput(Vector2.Zero,false,false);world.Body.GlobalPosition=position;world.Body.Velocity=Vector3.Zero;
        world.Terrain.UpdateStreaming(position);await Settled();await Physics(30);
    }
    int EmbeddedSkinVertices(List<Triangle> terrain)
    {
        int inside=0;
        foreach(var v in Skin())
        {
            if(v.Y>=world.Terrain.HeightAt(v.X,v.Z)-.025f)continue;
            float? mesh=RenderedTop(terrain,v.X,v.Z),collision=CollisionTop(v.X,v.Z);
            if(mesh!=null&&collision!=null&&v.Y<Math.Min(mesh.Value,collision.Value)-.025f)inside++;
        }
        return inside;
    }
    public async Task Run()
    {
        if(running)return;running=true;
        try { await RunChecks(); }
        catch(Exception e) { failures.Add(e.ToString()); File.WriteAllText(Path.Combine(output,"game-qa.json"),JsonSerializer.Serialize(new{pass=false,failures},new JsonSerializerOptions{WriteIndented=true})); GD.PrintErr(e);tree.Quit(1); }
    }
    void PressKey(Key key) {world._UnhandledInput(new InputEventKey{PhysicalKeycode=key,Keycode=key,Pressed=true});}
    int Total()=>Enumerable.Range(1,3).Sum(i=>world.InventoryCount(i));
    string InventoryFingerprint()=>string.Join(";",Enumerable.Range(0,36).Select(i=>$"{world.GetSlot(i).Type}:{world.GetSlot(i).Count}"))+"|"+world.CarriedStack;
    async Task Aim(Vector3 point)
    {
        world.SetFirstPerson(true);Vector3 d=point-(world.Body.GlobalPosition+Vector3.Up*world.EyeHeight);
        world.CameraYaw=Mathf.Atan2(-d.X,-d.Z);world.FirstPersonPitch=Mathf.Atan2(-d.Y,new Vector2(d.X,d.Z).Length());await Frame(4);
    }
    async Task WaitWorld()
    {
        for(int n=0;n<180;n++){await Frame(1);if(tree.CurrentScene is BlockWorld next){world=next;if(world.CharacterReady){await Settled();await Physics(40);return;}}}
        throw new Exception("Menu did not create ready world");
    }
    Button FindButton(Node root,string text)=>Viewer.Descendants(root).OfType<Button>().First(b=>b.IsVisibleInTree()&&b.Text==text);
    Button NamedButton(string name)=>Viewer.Descendants(world.GameUi).OfType<Button>().First(b=>b.Name==name&&b.IsVisibleInTree());
    void CheckInventoryLayout()
    {
        var card=Viewer.Descendants(world.GameUi).OfType<Control>().Single(c=>c.Name=="SurvivalInventoryCard");
        var storage=Viewer.Descendants(card).OfType<GridContainer>().Single(c=>c.Name=="StorageGrid");var bar=Viewer.Descendants(card).OfType<GridContainer>().Single(c=>c.Name=="InventoryHotbar");var craft=Viewer.Descendants(card).OfType<GridContainer>().Single(c=>c.Name=="SmallCraftingGrid");
        Check(storage.Columns==9&&storage.GetChildCount()==27&&bar.Columns==9&&bar.GetChildCount()==9&&bar.GlobalPosition.Y>storage.GlobalPosition.Y,"Inventory must be storage3x9 with9hotbar below");
        Check(craft.Columns==2&&craft.GetChildCount()==4&&NamedButton("CraftOutputSlot").Disabled,"2x2 staging/output recipe semantics");
        var preview=Viewer.Descendants(world.GameUi).OfType<SubViewport>().Single(v=>v.Name=="InventoryPetViewport");
        var previewMeshes=Viewer.Descendants(preview).OfType<MeshInstance3D>().ToArray();
        Check(preview.OwnWorld3D&&previewMeshes.Length>0&&Viewer.Descendants(preview).OfType<Skeleton3D>().Any(s=>s.GetBoneCount()>=10),"Inventory portrait is not an actual rigged GLB in a viewport");
        Check(previewMeshes.Any(m=>m.Name.ToString().Contains(Viewer.Ids[world.CurrentCharacter],StringComparison.OrdinalIgnoreCase)),"Inventory preview does not match selected hero");
        var screen=new Rect2(Vector2.Zero,tree.Root.Size);Check(screen.Encloses(card.GetGlobalRect()),"Inventory card extends outside viewport");
        foreach(var slot in Viewer.Descendants(card).OfType<Button>().Where(b=>b.Name.ToString().StartsWith("InventorySlot")))Check(screen.Encloses(slot.GetGlobalRect()),"Inventory slot clipped by screen "+slot.Name);
        var text=card.GetThemeColor("font_color","Label");Check(text.R<.3f&&text.G<.3f&&text.B<.3f,"Light inventory must use explicit dark text");
    }
    void CheckGeometry(string label)
    {
        var geometry=Geometry();int invalid=0,winding=0,expected=0;
        foreach(var t in geometry)
        {
            var c=(t.A+t.B+t.C)/3;if(!Solid(c-t.Normal*.02f)||Solid(c+t.Normal*.02f))invalid++;
            if((t.B-t.A).Cross(t.C-t.A).Dot(t.Normal)>=0)winding++;
        }
        foreach(var chunk in world.Terrain.Chunks.Keys)
        for(int x=chunk.X*16;x<chunk.X*16+16;x++)for(int z=chunk.Y*16;z<chunk.Y*16+16;z++)
        for(int y=0;y<world.Terrain.HeightAt(x,z);y++)
        if(world.Terrain.GetBlock(new Vector3I(x,y,z))!=BlockType.Air)
            foreach(var delta in new[]{Vector3I.Up,Vector3I.Down,Vector3I.Left,Vector3I.Right,Vector3I.Forward,Vector3I.Back})
                if(world.Terrain.GetBlock(new Vector3I(x,y,z)+delta)==BlockType.Air)expected+=2;
        Check(invalid==0&&winding==0&&geometry.Count==expected,$"{label}: actual6face geometry invalid={invalid},winding={winding},count={geometry.Count}/{expected}");
        samples.Add(new{label,triangles=geometry.Count,expected,invalid,winding});
    }
    object CheckTextures()
    {
        var found=new Dictionary<string,object>();int wrongUvs=0;
        foreach(var mesh in Viewer.Descendants(world.Terrain).OfType<MeshInstance3D>())for(int s=0;s<mesh.Mesh.GetSurfaceCount();s++)
        {
            var arrays=mesh.Mesh.SurfaceGetArrays(s);var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var verts=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Check(uv.Length==verts.Length&&uv.Any(p=>p.X>.9)&&uv.Any(p=>p.Y>.9),"Terrain UVs missing/nonvarying");
            if(mesh.Mesh.SurfaceGetMaterial(s) is not StandardMaterial3D mat||mat.AlbedoTexture==null){Check(false,"Terrain texture missing");continue;}
            string path=mat.AlbedoTexture.ResourcePath;
            if(!found.ContainsKey(path))
            {
                var image=mat.AlbedoTexture.GetImage();Check(image.GetWidth()<=256&&image.GetHeight()<=256&&image.HasMipmaps(),"Texture GPU256/mipmap budget "+path);
                Check(mat.TextureFilter==BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps&&!mat.TextureRepeat,"Texture filtering/wrap "+path);
                found[path]=new{path,width=image.GetWidth(),height=image.GetHeight(),mipmaps=image.HasMipmaps()};
            }
            if(path.Contains("grass-side"))for(int p=0;p<verts.Length;p+=4)
            {
                float top=verts.Skip(p).Take(4).Max(v=>v.Y),bottom=verts.Skip(p).Take(4).Min(v=>v.Y);
                for(int i=p;i<p+4;i++)if((Math.Abs(verts[i].Y-top)<.001&&uv[i].Y>.001)||(Math.Abs(verts[i].Y-bottom)<.001&&uv[i].Y<.999))wrongUvs++;
            }
        }
        var allowed=Enumerable.Range(0,BlockCatalog.SurfaceCount).Select(BlockCatalog.SurfaceTexture).ToHashSet(); Check(found.Count>=4&&found.Keys.All(allowed.Contains)&&Enumerable.Range(0,4).Select(BlockCatalog.SurfaceTexture).All(found.ContainsKey)&&wrongUvs==0,$"Four mapped textures/grass-side orientation: {found.Count}/{wrongUvs}");return new{materials=found.Values,wrongUvs};
    }
    void CheckRayOwner(string label)
    {
        var origin=world.Camera.Position;var eyes=world.Body.Position+Vector3.Up*world.EyeHeight;
        float reach=world.FirstPerson?5:origin.DistanceTo(eyes)+5;
        var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin,origin-world.Camera.GlobalBasis.Z*reach,1));
        var target=world.RayTarget();if(hit.Count==0){Check(target==null,label+" missing actual ray hit");return;}
        var point=hit["position"].AsVector3();if(eyes.DistanceTo(point)>5.001){Check(target==null,label+" exceeded player5m reach");return;}
        int face=hit["face_index"].AsInt32();var collider=hit["collider"].AsGodotObject();var chunk=world.Terrain.Chunks.Values.FirstOrDefault(c=>c.Body.GetInstanceId()==collider.GetInstanceId());
        Check(face>=0&&chunk!=null,label+" has no actual triangle owner");if(face<0||chunk==null)return;
        Vector3 centroid=Vector3.Zero;for(int n=0;n<3;n++)centroid+=chunk.Root.GlobalTransform*chunk.Vertices[chunk.Indices[face*3+n]];centroid/=3;
        var normal=hit["normal"].AsVector3();var ownerPoint=centroid-normal*.015f;var owner=new Vector3I(Mathf.FloorToInt(ownerPoint.X),Mathf.FloorToInt(ownerPoint.Y),Mathf.FloorToInt(ownerPoint.Z));
        var adjacent=owner+new Vector3I(Mathf.RoundToInt(normal.X),Mathf.RoundToInt(normal.Y),Mathf.RoundToInt(normal.Z));
        Check(target!=null&&target.Cell==owner&&target.Placement==adjacent,label+" grazing ray assigned wrong voxel owner");
        samples.Add(new{label,face,point=point.ToString(),normal=normal.ToString(),expectedCell=owner.ToString(),actualCell=target?.Cell.ToString(),expectedPlacement=adjacent.ToString(),actualPlacement=target?.Placement.ToString()});
    }
    async Task<object> TestBoundaries()
    {
        var results=new List<object>();world.SetFirstPerson(true);
        foreach(int hero in new[]{1,10})
        {
            world.SelectCharacter(hero);await Settled();
            foreach(int edge in new[]{0,1,2,3})
            {
                float initialYaw=edge<2?0:Mathf.Pi/2;world.CameraYaw=initialYaw;world.Body.Rotation=new Vector3(0,initialYaw,0);
                var corners=(from x in new[]{world.FootprintMin.X,world.FootprintMax.X} from z in new[]{world.FootprintMin.Y,world.FootprintMax.Y} select new Vector3(x,0,z).Rotated(Vector3.Up,initialYaw)).ToArray();
                float minX=corners.Min(c=>c.X),maxX=corners.Max(c=>c.X),minZ=corners.Min(c=>c.Z),maxZ=corners.Max(c=>c.Z);
                Vector3? start=null;
                for(int tangent=-38;tangent<=38&&start==null;tangent++)
                {
                    var p=edge switch {0=>new Vector3(48-maxX-.08f,0,tangent+.5f),1=>new Vector3(-48-minX+.08f,0,tangent+.5f),2=>new Vector3(tangent+.5f,0,48-maxZ-.08f),_=>new Vector3(tangent+.5f,0,-48-minZ+.08f)};
                    var away=edge switch {0=>Vector3.Left,1=>Vector3.Right,2=>Vector3.Forward,_=>Vector3.Back};
                    var tangentDirection=edge<2?Vector3.Back:Vector3.Right;int low=99,high=-99;
                    float lowX=Math.Min(p.X,Math.Min(p.X+away.X*2,p.X+tangentDirection.X*1.5f));float highX=Math.Max(p.X,Math.Max(p.X+away.X*2,p.X+tangentDirection.X*1.5f));
                    float lowZ=Math.Min(p.Z,Math.Min(p.Z+away.Z*2,p.Z+tangentDirection.Z*1.5f));float highZ=Math.Max(p.Z,Math.Max(p.Z+away.Z*2,p.Z+tangentDirection.Z*1.5f));
                    for(int x=Mathf.FloorToInt(lowX+minX);x<=Mathf.FloorToInt(highX+maxX);x++)
                    for(int z=Mathf.FloorToInt(lowZ+minZ);z<=Mathf.FloorToInt(highZ+maxZ);z++)
                    {int h=world.Terrain.HeightAt(x,z);low=Math.Min(low,h);high=Math.Max(high,h);}
                    if(low==high&&low>0)start=p+Vector3.Up*(low+.08f);
                }
                bool controlledPlatform=start==null;
                if(controlledPlatform)
                {
                    var prep=edge switch {0=>new Vector3(44,16.08f,.5f),1=>new Vector3(-44,16.08f,.5f),2=>new Vector3(.5f,16.08f,44),_=>new Vector3(.5f,16.08f,-44)};
                    world.Body.Position=prep;world.Body.Velocity=Vector3.Zero;world.Terrain.UpdateStreaming(prep);await Settled();
                    for(int inward=40;inward<48;inward++)for(int along=-5;along<=5;along++)
                    {
                        var cell=edge switch {0=>new Vector3I(inward,15,along),1=>new Vector3I(-inward-1,15,along),2=>new Vector3I(along,15,inward),_=>new Vector3I(along,15,-inward-1)};
                        world.Terrain.TrySetBlock(cell,BlockType.Stone);
                    }
                    start=edge switch {0=>new Vector3(48-maxX-.08f,16.08f,.5f),1=>new Vector3(-48-minX+.08f,16.08f,.5f),2=>new Vector3(.5f,16.08f,48-maxZ-.08f),_=>new Vector3(.5f,16.08f,-48-minZ+.08f)};
                }
                Check(start!=null,$"No boundary fixture {hero}/{edge}");if(start==null)continue;
                await Place(start.Value);var actualEdge=Geometry();foreach(var c in corners)TestRay(actualEdge,world.Body.Position.X+c.X,world.Body.Position.Z+c.Z,"actual boundary footprint");Vector3 before=world.Body.Position;
                var awayDir=edge switch {0=>Vector3.Left,1=>Vector3.Right,2=>Vector3.Forward,_=>Vector3.Back};
                world.CameraYaw=Mathf.Atan2(-awayDir.X,-awayDir.Z);await Frame(3);
                world.SimulateInput(new Vector2(0,-1),false,false);await Physics(25);world.SimulateInput(Vector2.Zero,false,false);await Physics(3);
                float escaped=(world.Body.Position-before).Dot(awayDir);Check(escaped>.6,$"Boundary turn/backaway trapped {Viewer.Ids[hero]} edge{edge}: {escaped}");
                // Return to the valid edge and attempt diagonal motion into+along it.
                world.CameraYaw=initialYaw;world.Body.Rotation=new Vector3(0,initialYaw,0);await Place(start.Value);before=world.Body.Position;
                var tangentDir=edge<2?Vector3.Back:Vector3.Right;var diagonal=(-awayDir+tangentDir).Normalized();
                world.CameraYaw=Mathf.Atan2(-diagonal.X,-diagonal.Z);world.SimulateInput(new Vector2(0,-1));await Physics(30);world.SimulateInput(Vector2.Zero);await Physics(3);
                float slid=(world.Body.Position-before).Dot(tangentDir);Check(slid>.35,$"Boundary diagonal slide trapped {Viewer.Ids[hero]} edge{edge}: {slid}");
                results.Add(new{id=Viewer.Ids[hero],edge,escaped,slid,controlledPlatform});
            }
            // A flat, genuine mesh/collider fixture at the finite corner
            // isolates turn/backaway from unrelated terrace collisions.
            int cornerHeight=Enumerable.Range(42,6).Max(x=>Enumerable.Range(42,6).Max(z=>world.Terrain.HeightAt(x,z)));
            for(int x=42;x<48;x++)for(int z=42;z<48;z++)world.Terrain.TrySetBlock(new Vector3I(x,cornerHeight,z),BlockType.Stone);
            world.CameraYaw=0;world.Body.Rotation=Vector3.Zero;
            await Place(new Vector3(48-world.FootprintMax.X-.08f,cornerHeight+1.08f,48-world.FootprintMax.Y-.08f));Vector3 cornerBefore=world.Body.Position;
            TestRay(Geometry(),cornerBefore.X,cornerBefore.Z,"actual finite-corner fixture");world.CameraYaw=Mathf.Pi/4;await Frame(3);world.SimulateInput(new Vector2(0,-1));await Physics(45);world.SimulateInput(Vector2.Zero);await Physics(3);
            float cornerEscape=(world.Body.Position-cornerBefore).Dot(new Vector3(-1,0,-1).Normalized());Check(cornerEscape>1,$"Corner turn/backaway trapped {Viewer.Ids[hero]} {cornerEscape}");results.Add(new{id=Viewer.Ids[hero],edge=4,escaped=cornerEscape,slid=0f});await Capture("corner-escape-"+Viewer.Ids[hero]);
        }
        world.SelectCharacter(0);await Settled();world.Respawn();await Physics(40);world.CameraYaw=0;return results;
    }
    async Task RunChecks()
    {
        Directory.CreateDirectory(output);await Settled();await Physics(40);
        Check(GameSession.IsQa&&GameSession.SavePath.StartsWith(output,StringComparison.OrdinalIgnoreCase),"QA save isolation must precede any save/load");
        // Actual main menu -> world options -> start flow, not a duplicate test UI.
        world.ReturnToMenu(false);await Frame(6);var menu=tree.CurrentScene as GameMenu??throw new Exception("Main menu scene not loaded");
        await Capture("main-menu-desktop");FindButton(menu,"Nový svět").EmitSignal(BaseButton.SignalName.Pressed);await Frame(3);
        Check(menu.CurrentPage=="new","New-world UI not opened");
        Viewer.Descendants(menu).OfType<LineEdit>().Single().Text="20261113";Viewer.Descendants(menu).OfType<OptionButton>().Single().Select(0);
        await Capture("new-world-options");FindButton(menu,"Vstoupit do světa").EmitSignal(BaseButton.SignalName.Pressed);await WaitWorld();
        Check(world.FirstPerson&&Total()==0&&Enumerable.Range(0,36).All(i=>world.GetSlot(i).Count==0),"Survival new game must start first person with36empty slots");
        Check(!Viewer.Descendants(world).OfType<LineEdit>().Any(c=>c.IsVisibleInTree())&&!Viewer.Descendants(world).OfType<OptionButton>().Any(c=>c.IsVisibleInTree()),"Debug seed/picker remained in gameplay HUD");
        Check(Input.MouseMode!=Input.MouseModeEnum.Captured,"QA captured real pointer");
        await Capture("gameplay-desktop");var textures=CheckTextures();CheckGeometry("initial textured terrain");
        var geometry=Geometry();foreach(int seam in new[]{-16,0,16,32})foreach(float side in new[]{-.01f,.01f})if(world.Terrain.IsLoadedAt(seam+side,.5f))TestRay(geometry,seam+side,.5f,"actual textured seam");
        PressKey(Key.F5);Check(!world.FirstPerson,"F5 did not toggle third person");PressKey(Key.F5);Check(world.FirstPerson,"F5 did not restore first person");
        PressKey(Key.Key9);Check(world.SelectedHotbar==8,"Key9 hotbar selection");world._UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.WheelUp,Pressed=true});Check(world.SelectedHotbar!=8,"Mousewheel hotbar selection");
        world.SelectHotbar(0);world.SimulateInput(Vector2.Zero);await Aim(new Vector3(2.5f,3.95f,-1.5f));
        var target=world.RayTarget();Check(target!=null&&target.Distance<=5,"Initial actual block target missing");
        CheckRayOwner("grazing diagonal surface ray");
        var firstCell=target!.Cell;int mined=0,placed=0;
        world.SetMiningHeld(true);await Physics(9);Check(world.MiningProgress>0&&world.MiningProgress<1&&Total()==0,"Survival mining hold timer");
        PressKey(Key.Escape);Vector3 pausePosition=world.Body.Position;double pauseAnimation=world.Player.CurrentAnimationPosition;int pauseEdits=world.Terrain.EditCount;
        world.SimulateInput(new Vector2(1,0),true,true);await Physics(30);await Frame(10);
        Check(!world.GameplayActive&&world.Body.Position.DistanceTo(pausePosition)<.0001&&Math.Abs(world.Player.CurrentAnimationPosition-pauseAnimation)<.0001,"Pause did not freeze physics/animation");
        Check(!world.MineTarget()&&!world.PlaceTarget()&&world.Terrain.EditCount==pauseEdits&&Total()==0,"Pause allowed world/inventory edits");await Capture("pause-desktop");
        PressKey(Key.Escape);world.SimulateInput(Vector2.Zero);world.SetMiningHeld(false);
        Check(world.MineTarget(),"Actual ray survival mining failed");mined++;await Physics(2);await Frame(2);Check(Total()==1&&world.Terrain.GetBlock(firstCell)==BlockType.Air,"Mining did not conserve one collected block");
        await Aim(new Vector3(-1.5f,3.95f,-1.5f));Check(world.RayTarget()?.Type==1&&world.MineTarget(),"Second reachable grass block not mineable");mined++;await Physics(2);await Frame(2);
        Check(world.InventoryCount(BlockType.Dirt)==2&&world.InventoryCount(BlockType.Grass)==0,"Grass must drop collected dirt");
        int ownedSlot=Enumerable.Range(0,36).First(i=>world.GetSlot(i).Type==(int)BlockType.Dirt);world.SelectHotbar(7);int beforeEmpty=Total();Check(!world.PlaceTarget()&&Total()==beforeEmpty,"Empty hotbar created block or consumed inventory");
        Check(world.TransferSlot(ownedSlot,9)&&world.TransferSlot(9,8),"Actual storage/hotbar assignment failed");world.SelectHotbar(8);
        await Aim(new Vector3(3.5f,3.95f,.5f));var placeTarget=world.RayTarget();
        GD.Print("GAME_PLACEMENT_TARGET "+JsonSerializer.Serialize(new{cell=placeTarget?.Cell.ToString(),placement=placeTarget?.Placement.ToString(),position=placeTarget?.Position.ToString(),normal=placeTarget?.Normal.ToString(),distance=placeTarget?.Distance,canPlace=placeTarget!=null&&world.CanPlaceCell(placeTarget.Placement),existing=placeTarget!=null?world.Terrain.GetBlockId(placeTarget.Placement):-1}));
        Check(world.PlaceTarget(),"Owned material placement failed");placed++;await Physics(2);await Frame(2);Check(Total()==mined-placed,"Successful placement did not consume exactly1unit");
        if(placeTarget!=null)await Aim(new Vector3(placeTarget.Placement.X+.5f,placeTarget.Placement.Y+.5f,placeTarget.Placement.Z+.5f));
        Check(world.MineTarget(),"Re-mining placed owned material failed");mined++;await Physics(2);await Frame(2);
        world.SetFirstPerson(false);world.CameraYaw=0;world.CameraPitch=.48f;world.CameraDistance=8;await Frame(4);
        var thirdTarget=world.RayTarget();Check(thirdTarget!=null&&thirdTarget.Distance<=5&&world.MineTarget(),"Third-person8m camera could not mine within5m player reach");mined++;await Physics(2);await Frame(2);
        world.SetFirstPerson(true);await Aim(world.Body.Position+new Vector3(0,-.05f,0));ownedSlot=Enumerable.Range(0,9).First(i=>world.GetSlot(i).Type==(int)BlockType.Dirt);world.SelectHotbar(ownedSlot);
        int selfBefore=Total();Check(!world.PlaceTarget()&&Total()==selfBefore,"Self-placement was not rejected without consumption");
        Check(!world.CanPlaceCell(new Vector3I(Mathf.FloorToInt(world.Body.Position.X),Mathf.FloorToInt(world.Body.Position.Y+1),Mathf.FloorToInt(world.Body.Position.Z))),"Player head/body self placement allowed");
        await Aim(world.Body.Position+new Vector3(0,-.05f,-10));int farBefore=Total();Check(world.RayTarget()==null&&!world.MineTarget()&&Total()==farBefore,"Out-of-reach mining collected resources");
        PressKey(Key.E);Check(world.UiMode==GameMode.Inventory,"E did not open inventory");await Frame(3);CheckInventoryLayout();NamedButton("InventorySlot"+ownedSlot).EmitSignal(BaseButton.SignalName.Pressed);Check(world.CarriedStack.Count>0&&Total()==mined-placed,"Carried stack lost/duplicated resources");await Capture("inventory-desktop");
        Check(!world.MineTarget()&&!world.PlaceTarget(),"Inventory allowed world edits");PressKey(Key.Escape);Check(world.GameplayActive&&world.CarriedStack.Count==0&&Total()==mined-placed,"Closing inventory lost carried stack");
        PressKey(Key.E);ownedSlot=Enumerable.Range(0,36).First(i=>world.GetSlot(i).Count>0);NamedButton("InventorySlot"+ownedSlot).EmitSignal(BaseButton.SignalName.Pressed);NamedButton("CraftSlot0").EmitSignal(BaseButton.SignalName.Pressed);
        Check(world.GetCraftSlot(0).Count==mined-placed&&world.CarriedStack.Count==0&&Total()==mined-placed,"2x2 staging lost/duplicated actual owned resources");await Capture("craft-staging-desktop");
        PressKey(Key.E);Check(Enumerable.Range(0,4).All(i=>world.GetCraftSlot(i).Count==0)&&Total()==mined-placed,"Closing inventory lost crafting staging");
        PressKey(Key.E);ownedSlot=Enumerable.Range(0,36).First(i=>world.GetSlot(i).Count>0);NamedButton("InventorySlot"+ownedSlot).EmitSignal(BaseButton.SignalName.Pressed);NamedButton("CraftSlot1").EmitSignal(BaseButton.SignalName.Pressed);
        string save=Path.Combine(output,"game-qa-data","verified-session.json");Check(world.SaveSnapshot(save)&&world.CarriedStack.Count==0&&Enumerable.Range(0,4).All(i=>world.GetCraftSlot(i).Count==0)&&Total()==mined-placed,"Save with crafting staging lost inventory");PressKey(Key.E);
        PressKey(Key.E);ownedSlot=Enumerable.Range(0,36).First(i=>world.GetSlot(i).Count>0);NamedButton("InventorySlot"+ownedSlot).EmitSignal(BaseButton.SignalName.Pressed);NamedButton("CraftSlot2").EmitSignal(BaseButton.SignalName.Pressed);
        Check(world.ReturnToMenu(true),"Save/menu with crafting staging failed");await Frame(6);menu=(GameMenu)tree.CurrentScene;FindButton(menu,"Pokračovat").EmitSignal(BaseButton.SignalName.Pressed);await WaitWorld();
        Check(Total()==mined-placed&&world.CarriedStack.Count==0&&Enumerable.Range(0,4).All(i=>world.GetCraftSlot(i).Count==0),"Actual continue flow lost/duplicated crafting staging");
        string inv=InventoryFingerprint(),savedGeometry=Fingerprint(Geometry());int savedResources=Total();
        Check(world.SaveSnapshot(save)&&File.Exists(save+".bak")&&!File.Exists(save+".tmp")&&GameSession.Read(save)!=null,"Atomic JSON save/backup invalid");
        string validBackup=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(save+".bak")));File.WriteAllText(save,"{corrupt-primary");
        Check(world.SaveSnapshot(save)&&GameSession.Read(save)!=null&&GameSession.Read(save+".bak")!=null&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(save+".bak")))==validBackup,"Corrupt primary overwrote last valid atomic backup");
        var mutation=new Vector3I(5,3,5);world.Terrain.TrySetBlock(mutation,0);world.TransferSlot(ownedSlot,35);
        Check(world.LoadSnapshot(save),"Saved survival session failed to load");await Settled();await Physics(40);
        Check(InventoryFingerprint()==inv&&Total()==savedResources&&Fingerprint(Geometry())==savedGeometry,"Save/load did not preserve resources and actual edited geometry");
        string corrupt=Path.Combine(output,"game-qa-data","corrupt.json");File.WriteAllText(corrupt,"{");Check(!world.LoadSnapshot(corrupt)&&InventoryFingerprint()==inv,"Corrupt save changed current inventory");
        var invalid=GameSession.Read(save)!;invalid.Slots[0]=new ItemStack(1,65);string invalidPath=Path.Combine(output,"game-qa-data","invalid-stack.json");GameSession.WriteAtomic(invalidPath,invalid);Check(!world.LoadSnapshot(invalidPath),"Invalid stack>64 accepted");
        // A bounded serialized QA fixture checks stack64 layout/capacity; it is
        // never exposed as a creative catalogue or written into the live slot.
        var full=GameSession.Read(save)!;full.Slots=Enumerable.Range(0,36).Select(i=>new ItemStack(i%3+1,64)).ToArray();string fullPath=Path.Combine(output,"game-qa-data","full-capacity.json");GameSession.WriteAtomic(fullPath,full);Check(world.LoadSnapshot(fullPath),"Valid full-capacity inventory did not load");await Settled();await Physics(40);
        await Aim(new Vector3(3.5f,3.95f,-2.5f));int fullBefore=Total();var fullTarget=world.RayTarget();Check(fullTarget!=null&&!world.MineTarget()&&Total()==fullBefore&&world.Terrain.GetBlockId(fullTarget.Cell)==fullTarget.Type,"Full inventory mined/destroyed uncollectable block");
        tree.Root.Size=new Vector2I(390,844);await Capture("full-hotbar-mobile-390");PressKey(Key.E);await Frame(3);CheckInventoryLayout();await Capture("full-inventory-mobile-390");PressKey(Key.E);tree.Root.Size=new Vector2I(1280,800);
        Check(world.LoadSnapshot(save),"Restore real survival session after isolated capacity fixture");await Settled();await Physics(40);
        // Independent editable-voxel fixtures: inner cavity, chunk border and corner.
        foreach(var pos in new[]{new Vector3I(6,2,6),new Vector3I(15,3,6),new Vector3I(15,3,15)})
        {
            var original=world.Terrain.GetBlock(pos);string before=Fingerprint(Geometry());Check(world.Terrain.TrySetBlock(pos,0),"Voxel fixture mining failed");
            int expectedChunks=pos.X==15&&pos.Z==15?3:pos.X==15?2:1;Check(world.Terrain.LastEditRebuiltChunks==expectedChunks,"Adjacent chunk edit rebuild count");await Physics(2);await Frame(2);CheckGeometry("edited cavity/border");
            if(pos.Y==2)
            {
                var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(6.5f,2.5f,6.5f),new Vector3(6.5f,3.5f,6.5f),1));
                Check(hit.Count>0&&hit["normal"].AsVector3().Y<-.9&&Math.Abs(hit["position"].AsVector3().Y-3)<.002,"Actual cavity roof underside collision missing");
            }
            Check(world.Terrain.TrySetBlock(pos,original),"Fixture restore failed");await Physics(2);await Frame(2);Check(Fingerprint(Geometry())==before,"Restoring voxel did not restore identical actual mesh");
        }
        Check(!world.Terrain.TrySetBlock(new Vector3I(0,0,0),0)&&world.Terrain.GetBlock(new Vector3I(0,0,0))==BlockType.Stone,"Protected bedrock allowed hole below world");
        // A two-metre tunnel clearance must physically block Hopsa's tall head.
        world.LoadSnapshot(save);await Settled();world.SelectCharacter(8);await Settled();world.SetFirstPerson(false);world.CameraYaw=0;
        for(int x=4;x<=6;x++)for(int z=4;z<=6;z++)world.Terrain.TrySetBlock(new Vector3I(x,6,z),BlockType.Stone);
        await Place(new Vector3(2.2f,4.08f,5.5f));world.SimulateInput(new Vector2(1,0));await Physics(70);world.SimulateInput(Vector2.Zero);await Physics(3);await Frame(2);
        int headEmbedded=EmbeddedSkinVertices(Geometry());var actualBox=(BoxShape3D)world.Body.GetNode<CollisionShape3D>("AnimatedFootprint").Shape;
        bool ceilingProtected=world.Body.Position.X<4&&headEmbedded==0&&actualBox.Size.Y>=world.CharacterHeight+.029f;
        Check(ceilingProtected,$"Tall bunny head clips2m tunnel roof: x{world.Body.Position.X} inside{headEmbedded} box{actualBox.Size.Y}/head{world.CharacterHeight}");await Capture("bunny-low-ceiling");
        world.LoadSnapshot(save);await Settled();await Physics(40);var editsBefore=world.Terrain.GetChanges();
        await Place(new Vector3(40.5f,world.Terrain.HeightAt(40,40)+.1f,40.5f));Check(world.Terrain.ActiveChunkCount<=9,"Streaming unload budget");world.Respawn();await Settled();await Physics(40);
        Check(editsBefore.All(e=>{var now=world.Terrain.GetBlock(new Vector3I(e.X,e.Y,e.Z));return now==(BlockType)e.Type||(e.Type==(int)BlockType.Dirt&&now==BlockType.Grass);})&&Fingerprint(Geometry())==savedGeometry,"Edited blocks lost on chunk unload/reload");
        // Reconfirm all11 imported skins against the actual textured block floor.
        world.SetFirstPerson(false);
        for(int i=0;i<11;i++){world.SelectCharacter(i);await Settled();world.Respawn();await Physics(35);await Frame(2);var skin=Skin();float floor=CollisionTop(.5f,.5f)??-999;float contact=skin.Min(v=>v.Y)-floor;Check(world.Body.IsOnFloor()&&contact>=-.025&&contact<=.035,"Textured world grounding "+Viewer.Ids[i]);actors.Add(new{id=Viewer.Ids[i],contact});}
        var boundaries=await TestBoundaries();world.LoadSnapshot(save);await Settled();await Physics(40);
        // Ctrl sprint and Shift sneak use the same controller with explicit QA inputs.
        var distances=new List<float>();foreach(int mode in new[]{0,1,2})
        {
            world.Respawn();await Physics(40);world.CameraYaw=0;world.QaInputOverride=false;
            void Physical(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=key,Keycode=key,Pressed=pressed});
            Vector3 start=world.Body.Position;Physical(Key.W,true);if(mode==1)Physical(Key.Ctrl,true);if(mode==2)Physical(Key.Shift,true);await Physics(30);
            Physical(Key.W,false);Physical(Key.Ctrl,false);Physical(Key.Shift,false);world.SimulateInput(Vector2.Zero);distances.Add(world.Body.Position.DistanceTo(start));
        }
        Check(distances[1]>distances[0]*1.5&&distances[2]<distances[0]*.6,"Ctrl sprint/Shift sneak speed semantics");
        // Internal block ledge: Shift must stop falling, while walking can leave it.
        world.LoadSnapshot(save);await Settled();world.SetFirstPerson(false);world.CameraYaw=0;
        for(int x=4;x<=9;x++)for(int z=2;z<=7;z++)world.Terrain.TrySetBlock(new Vector3I(x,5,z),BlockType.Stone);
        await Place(new Vector3(8.25f,6.08f,4.5f));world.SetSneaking(true);world.SimulateInput(new Vector2(1,0));await Physics(120);world.SimulateInput(Vector2.Zero);world.SetSneaking(false);await Physics(3);
        bool sneakLedge=world.Body.IsOnFloor()&&Math.Abs(world.Body.Position.Y-6)<.02;Check(sneakLedge,"Shift sneak walked off real internal block ledge");await Capture("sneak-ledge");
        await Place(new Vector3(8.25f,6.08f,4.5f));world.SimulateInput(new Vector2(1,0));await Physics(120);world.SimulateInput(Vector2.Zero);await Physics(30);
        float? lowerFloor=CollisionTop(world.Body.Position.X,world.Body.Position.Z),lowerRender=RenderedTop(Geometry(),world.Body.Position.X,world.Body.Position.Z);
        bool walkFalls=world.Body.Position.X>10.5f&&world.Body.Position.Y<5.9f&&world.Body.IsOnFloor()&&lowerFloor!=null&&lowerRender!=null&&Math.Abs(lowerFloor.Value-lowerRender.Value)<.002f&&Math.Abs(world.Body.Position.Y-lowerFloor.Value)<.025f;
        Check(walkFalls,$"Control walk did not leave ledge onto actual lower mesh/collision: body{world.Body.Position} floor{lowerFloor}/{lowerRender}");await Capture("walk-off-ledge");
        world.LoadSnapshot(save);await Settled();await Physics(40);
        world.Respawn();await Physics(40);world.SetFirstPerson(true);world.CameraYaw=0;await Capture("gameplay-textures-close");
        var times=new List<double>();var timer=System.Diagnostics.Stopwatch.StartNew();double previous=timer.Elapsed.TotalMilliseconds;
        for(int n=0;n<120;n++){await Frame(1);double now=timer.Elapsed.TotalMilliseconds;times.Add(now-previous);previous=now;}
        var performance=new{platform=OS.GetName(),gpu=RenderingServer.GetVideoAdapterName(),renderer=RenderingServer.GetCurrentRenderingMethod(),msaa=world.GetViewport().Msaa3D.ToString(),resolution=world.GetViewport().GetVisibleRect().Size.ToString(),chunks=world.Terrain.ActiveChunkCount,triangles=world.Terrain.TotalTriangles,drawCalls=Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),primitives=Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),medianMs=times.Order().ElementAt(60),p95Ms=times.Order().ElementAt(114),scope="Actual Windows offscreen render,120FPS cap; mobile viewport is not physical mobile hardware"};
        tree.Root.Size=new Vector2I(390,844);await Capture("gameplay-mobile-390");PressKey(Key.E);await Capture("inventory-mobile-390");PressKey(Key.E);PressKey(Key.Escape);await Capture("pause-mobile-390");PressKey(Key.Escape);
        world.ReturnToMenu(false);await Frame(6);menu=(GameMenu)tree.CurrentScene;await Capture("main-menu-mobile-390");menu.ShowNewWorld();await Frame(3);await Capture("new-world-mobile-390");
        var result=new{pass=failures.Count==0,utc=DateTime.UtcNow,controlsReference="https://www.minecraft.net/en-us/article/minecraft-controls",survival=new{mined,placed,resources=savedResources,conserved=savedResources==mined-placed,savePath=save},textures,geometryChecks=samples,groundedCharacters=actors,ceiling=new{ceilingProtected,headEmbedded},boundaries,speedTests=new{walk=distances[0],sprint=distances[1],sneak=distances[2],sneakLedge,walkFalls},performance,failures};
        File.WriteAllText(Path.Combine(output,"game-qa.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));GD.Print("JOSCRAFT_GAME_QA "+(failures.Count==0?"PASS":"FAIL")+" "+string.Join("; ",failures));tree.Quit(failures.Count==0?0:1);
    }
}


