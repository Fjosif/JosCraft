using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

public sealed class PlacedBlockSave
{
    public int X {get;set;} public int Y {get;set;} public int Z {get;set;}
    public int Type {get;set;} public int Rotation {get;set;} public bool Open {get;set;}
    public ItemStack[] Slots {get;set;}=Enumerable.Range(0,27).Select(_=>new ItemStack()).ToArray();
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3I Cell=>new(X,Y,Z);
    public static bool Valid(PlacedBlockSave? s)=>s!=null&&s.Type is >=34 and <=38&&s.Rotation is >=0 and <4&&s.Y>=1&&s.Y<=BlockTerrain.MaxBuildY&&BlockTerrain.IsInsideWorld(s.X,s.Z)&&s.Slots!=null&&s.Slots.Length==27&&s.Slots.All(x=>x!=null&&x.Count is >=0 and <=64&&(x.Type==0)==(x.Count==0)&&(x.Count==0||BlockCatalog.IsCollectible(x.Type)));
}

/// <summary>One source of truth for shaped occupancy, visual pivots and physical shapes.</summary>
public partial class ShapedBlocks : Node3D
{
    public static readonly string[] Models={"oak-slab","oak-stairs","oak-door","oak-fence","wooden-chest"};
    readonly BlockWorld world;
    public readonly Dictionary<Vector3I,PlacedBlockSave> States=new();
    public readonly Dictionary<Vector3I,ShapedBlockVisual> Visuals=new();
    public event Action<PlacedBlockSave>? Changed;
    public int Revision {get;private set;}
    double scan;
    readonly HashSet<Vector3I> closingChests=new();
    public void CloseChestWhenFree(Vector3I cell){closingChests.Add(cell);SetOpen(cell,false);}
    public ShapedBlocks(BlockWorld world){this.world=world;Name="ShapedBlocks";}
    public override void _Ready(){world.Terrain.OverlayChanged+=OnBlock;}
    public override void _ExitTree(){world.Terrain.OverlayChanged-=OnBlock;}
    void OnBlock(Vector3I cell,BlockType type)
    {
        if(States.TryGetValue(cell,out var old)&&old.Type!=(int)type){States.Remove(cell);RemoveVisual(cell);Revision++;}
        if(type is >=BlockType.OakSlab and <=BlockType.Chest&&!States.ContainsKey(cell))States[cell]=New(cell,(int)type,0);
        RefreshNeighbours(cell);
    }
    public static PlacedBlockSave New(Vector3I cell,int type,int rotation)=>new(){X=cell.X,Y=cell.Y,Z=cell.Z,Type=type,Rotation=rotation};
    public Vector3I Anchor(Vector3I cell)=>world.Terrain.GetBlock(cell)==BlockType.DoorUpper?cell+Vector3I.Down:cell;
    public PlacedBlockSave? At(Vector3I cell)=>States.GetValueOrDefault(Anchor(cell));
    public static Transform3D TransformFor(Vector3I c,int rotation)=>new(new Basis(Vector3.Up,rotation*Mathf.Pi/2),new Vector3(c.X+.5f,c.Y,c.Z+.5f));
    public int RotationFor(float yaw)=>(int)Mathf.PosMod(Mathf.RoundToInt(yaw/(Mathf.Pi/2)),4);
    public bool Place(Vector3I cell,int type,int rotation)
    {
        if(type is <34 or >38||world.Terrain.GetBlock(cell)!=BlockType.Air||!CanPlace(cell,type,rotation))return false;
        if(!world.Terrain.ApplyAuthoritativeBlock(cell,(BlockType)type))return false;
        var s=New(cell,type,rotation);States[cell]=s;
        if(type==36)world.Terrain.ApplyAuthoritativeBlock(cell+Vector3I.Up,BlockType.DoorUpper);
        UpdateVisual(cell);RefreshNeighbours(cell);Mark(s);return true;
    }
    public bool CanPlace(Vector3I cell,int type,int rotation)
    {
        if(!BlockTerrain.IsInsideWorld(cell.X,cell.Z)||cell.Y<1||cell.Y>BlockTerrain.MaxBuildY)return false;
        if(type==36&&(cell.Y>=BlockTerrain.MaxBuildY||world.Terrain.GetBlock(cell+Vector3I.Up)!=BlockType.Air||!BlockCatalog.IsFullCube(world.Terrain.GetBlock(cell+Vector3I.Down))))return false;
        if(!world.CellPhysicallyClear(cell)||type==36&&(!world.CellPhysicallyClear(cell+Vector3I.Up)||world.AnyPlayerInCell(cell+Vector3I.Up)))return false;
        // Never create collision inside any player's actual footprint.
        foreach(var b in ShapedBlockVisual.ClosedBoxes(type))
            if(world.AnyPlayerIntersects(TransformFor(cell,rotation)*b))return false;
        return true;
    }
    public bool CanMine(Vector3I cell)
    {
        var s=At(cell);if(s==null)return true;
        if(s.Type==38&&(s.Slots.Any(x=>x.Count>0)||world.ChestInUse(s.Cell))){world.Notify("Nejdřív zavři truhlu a vyber z ní všechny věci.");return false;}
        return true;
    }
    public bool Remove(Vector3I cell)
    {
        cell=Anchor(cell);var s=At(cell);if(s==null||!CanMine(cell))return false;
        if(s.Type==36)world.Terrain.ApplyAuthoritativeBlock(cell+Vector3I.Up,BlockType.Air);
        world.Terrain.ApplyAuthoritativeBlock(cell,BlockType.Air);States.Remove(cell);RemoveVisual(cell);RefreshNeighbours(cell);Revision++;return true;
    }
    public bool SetOpen(Vector3I cell,bool open)
    {
        var s=At(cell);if(s==null||s.Type is not (36 or 38))return false;
        if(s.Open==open)return true;
        UpdateVisual(s.Cell);
        if(Visuals.TryGetValue(s.Cell,out var v)&&!v.SweepClear(open)){world.Notify("Tady není dost místa pro otevření nebo zavření.");return false;}
        s.Open=open;UpdateVisual(s.Cell);Mark(s);return true;
    }
    public void Mark(PlacedBlockSave s){Revision++;Changed?.Invoke(s);}
    public void ApplyRemote(PlacedBlockSave s){States[s.Cell]=s;UpdateVisual(s.Cell);Revision++;}
    public void Restore(IEnumerable<PlacedBlockSave>? saved)
    {
        foreach(var cell in Visuals.Keys.ToArray())RemoveVisual(cell);States.Clear();
        foreach(var e in world.Terrain.Changes.Where(e=>e.Value is >=BlockType.OakSlab and <=BlockType.Chest))States[e.Key]=New(e.Key,(int)e.Value,0);
        if(saved!=null)foreach(var s in saved)if(PlacedBlockSave.Valid(s)&&world.Terrain.GetBlockId(s.Cell)==s.Type)States[s.Cell]=s;
        foreach(var cell in States.Keys.ToArray())UpdateVisual(cell);Revision++;
    }
    void RemoveVisual(Vector3I c){if(Visuals.Remove(c,out var v)){RemoveChild(v);v.QueueFree();}}
    public void RefreshNeighbours(Vector3I cell)
    {
        foreach(var c in new[]{cell,cell+Vector3I.Left,cell+Vector3I.Right,cell+Vector3I.Forward,cell+Vector3I.Back})
            if(Visuals.TryGetValue(c,out var v))v.RefreshFence();
    }
    void UpdateVisual(Vector3I cell)
    {
        if(!States.TryGetValue(cell,out var s)||!world.Terrain.IsLoadedAt(cell.X,cell.Z))return;
        if(!Visuals.TryGetValue(cell,out var v))
        {
            v=new ShapedBlockVisual(world,s);AddChild(v);Visuals[cell]=v;v.Build();
        }
        v.ApplyState(s);
    }
    public override void _PhysicsProcess(double delta)
    {
        if(!world.SimulationActive)return;
        scan+=delta;if(scan<.1)return;scan=0;
        if(!world.IsNetClient)foreach(var c in closingChests.ToArray()){if(At(c) is not {Open:true}||!world.ChestInUse(c)&&SetOpen(c,false))closingChests.Remove(c);}
        foreach(var cell in Visuals.Keys.Where(c=>!world.Terrain.IsLoadedAt(c.X,c.Z)||!States.ContainsKey(c)).ToArray())RemoveVisual(cell);
        foreach(var s in States.Values.ToArray())
        {
            UpdateVisual(s.Cell);
            if(!world.IsNetClient&&s.Type==36&&!BlockCatalog.IsFullCube(world.Terrain.GetBlock(s.Cell+Vector3I.Down)))
            {Remove(s.Cell);world.SpawnWoodPickup(36,new Vector3(s.X+.5f,s.Y+.5f,s.Z+.5f));}
        }
    }
}

public partial class ShapedBlockVisual : Node3D
{
    readonly BlockWorld world;PlacedBlockSave state;
    public Node3D Model {get;private set;}=null!;
    public AnimationPlayer? Animation {get;private set;}
    readonly List<CollisionObject3D> bodies=new();
    readonly Dictionary<string,Node3D> branches=new();
    bool? lastOpen;
    static JsonDocument? collisionDoc;
    static JsonElement Boxes(int type)=> (collisionDoc??=JsonDocument.Parse(FileAccess.GetFileAsString("res://data/block-collisions.json"))).RootElement.GetProperty("blocks").GetProperty(type switch{34=>"minecraft:oak_slab",35=>"minecraft:oak_stairs",36=>"minecraft:oak_door",37=>"minecraft:oak_fence",_=>"minecraft:chest"});
    static Vector3 Vec(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    static Aabb Box(JsonElement e)=>new(Vec(e.GetProperty("center"))-Vec(e.GetProperty("size"))*.5f,Vec(e.GetProperty("size")));
    public static Aabb[] ClosedBoxes(int type)
    {
        var b=Boxes(type);return (type==37?new[]{"post"}:new[]{type is 36 or 38?"closed":"default"}).SelectMany(key=>b.GetProperty(key).EnumerateArray().Select(Box)).ToArray();
    }
    public ShapedBlockVisual(BlockWorld w,PlacedBlockSave s){world=w;state=s;Name="Placed_"+s.Type+"_"+s.X+"_"+s.Y+"_"+s.Z;Transform=ShapedBlocks.TransformFor(s.Cell,s.Rotation);}
    public void Build()
    {
        Model=ResourceLoader.Load<PackedScene>("res://assets/blocks/"+ShapedBlocks.Models[state.Type-34]+".glb").Instantiate<Node3D>();AddChild(Model);
        Animation=Viewer.Descendants(Model).OfType<AnimationPlayer>().FirstOrDefault();
        if(Animation!=null){Animation.SpeedScale=2;Animation.CallbackModeProcess=AnimationMixer.AnimationCallbackModeProcess.Physics;foreach(string clip in Animation.GetAnimationList())Animation.GetAnimation(clip).LoopMode=Godot.Animation.LoopModeEnum.None;}
        var all=Viewer.Descendants(Model).OfType<Node3D>().Prepend(Model).ToArray();
        var boxes=Boxes(state.Type);
        if(state.Type==37)
        {
            foreach(string part in new[]{"post","North","South","East","West"})
            {
                var node=all.First(n=>n.Name.ToString()==(part=="post"?"Post":part));branches[part]=node;
                foreach(var b in boxes.GetProperty(part).EnumerateArray())AddBox(node,b);
            }
            RefreshFence();
        }
        else foreach(var b in boxes.GetProperty(state.Type is 36 or 38?"closed":"default").EnumerateArray())
        {
            var parent=state.Type==36?all.First(n=>n.Name=="DoorHinge"):state.Type==38&&b.TryGetProperty("name",out var name)&&name.GetString()!.StartsWith("ChestLid")?all.First(n=>n.Name=="LidHinge"):this;
            AddBox(parent,b);
        }
    }
    void AddBox(Node3D parent,JsonElement b)
    {
        var box=Box(b);var body=new AnimatableBody3D{CollisionLayer=1,CollisionMask=0,SyncToPhysics=false};parent.AddChild(body);
        body.SetMeta("shape_cell",state.Cell);body.Transform=parent.GlobalTransform.AffineInverse()*GlobalTransform*new Transform3D(Basis.Identity,box.GetCenter());
        body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=box.Size}});bodies.Add(body);
    }
    public void ApplyState(PlacedBlockSave? value=null)
    {
        if(value!=null)state=value;
        Transform=ShapedBlocks.TransformFor(state.Cell,state.Rotation);
        if(lastOpen==state.Open)return;
        if(Animation!=null)
        {
            string clip=BlockWorld.ClipName(Animation,state.Open?"Open":"Close");Animation.Play(clip);
            if(lastOpen==null)Animation.Seek(Animation.GetAnimation(clip).Length,true);
        }
        lastOpen=state.Open;
    }
    public void RefreshFence()
    {
        if(state.Type!=37)return;
        foreach(var (name,local) in new[]{("North",Vector3I.Forward),("South",Vector3I.Back),("East",Vector3I.Right),("West",Vector3I.Left)})
        {
            var d=(Vector3)local;d=d.Rotated(Vector3.Up,state.Rotation*Mathf.Pi/2);var cell=state.Cell+new Vector3I(Mathf.RoundToInt(d.X),0,Mathf.RoundToInt(d.Z));
            var type=world.Terrain.GetBlock(cell);bool active=type==BlockType.OakFence||BlockCatalog.IsFullCube(type);
            branches[name].Visible=active;
            foreach(var c in Viewer.Descendants(branches[name]).OfType<CollisionShape3D>())c.Disabled=!active;
        }
    }
    // Test the actual moving collision boxes at 19 poses, with the same hinge as the rendered model.
    // Small hinge bevel overlap with a supporting wall is tolerated (3 cm), never an entire voxel.
    public bool SweepClear(bool open)
    {
        if(Animation==null)return true;
        if(Animation.IsPlaying())return false;
        string clip=BlockWorld.ClipName(Animation,open?"Open":"Close");
        string old=BlockWorld.ClipName(Animation,state.Open?"Open":"Close");
        var exclude=new Godot.Collections.Array<Rid>();foreach(var b in bodies)exclude.Add(b.GetRid());
        bool clear=true;
        Animation.Play(clip);
        for(int step=0;step<=18&&clear;step++)
        {
            Animation.Seek(Animation.GetAnimation(clip).Length*step/18.0,true);
            foreach(var b in bodies)
            foreach(var c in b.GetChildren().OfType<CollisionShape3D>())
            {
                if(c.Disabled||c.Shape is not BoxShape3D box)continue;
                using var shape=new BoxShape3D{Size=(box.Size-Vector3.One*.06f).Clamp(Vector3.One*.02f,Vector3.One*5)};
                using var query=new PhysicsShapeQueryParameters3D{Shape=shape,Transform=c.GlobalTransform,CollisionMask=1|2,Exclude=exclude,Margin=0};
                var hits=GetWorld3D().DirectSpaceState.IntersectShape(query,1);
                if(hits.Count>0){clear=false;break;}
                // Remote avatars have no physical collider on the host.
                if(world.RemotePlayerIntersects(c.GlobalTransform*new Aabb(-box.Size*.5f,box.Size))){clear=false;break;}
            }
        }
        Animation.Play(old);Animation.Seek(Animation.GetAnimation(old).Length,true);Animation.Pause();
        return clear;
    }
}
