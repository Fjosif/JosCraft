using Godot;
using System;
using System.Linq;

public partial class BlockWorld
{
    public ShapedBlocks Shapes {get;private set;}=null!;
    public bool IsChestOpen=>OpenStation is {} c&&Terrain.GetBlock(c)==BlockType.Chest;
    public bool RemotePlayerIntersects(Aabb bounds)=>NetSession.Instance?.Server?.RemotePositions().Any(p=>bounds.Intersects(new Aabb(p.Position-new Vector3(.45f,0,.45f),new Vector3(.9f,2.7f,.9f))))??false;
    public bool AnyPlayerIntersects(Aabb bounds)
    {
        var size=FootprintMax-FootprintMin;
        return bounds.Intersects(Body.GlobalTransform*new Aabb(new Vector3(FootprintMin.X,0,FootprintMin.Y),new Vector3(size.X,CollisionHeight,size.Y)))||RemotePlayerIntersects(bounds);
    }
    public bool CellPhysicallyClear(Vector3I cell)
    {
        using var box=new BoxShape3D{Size=Vector3.One*.98f};
        using var query=new PhysicsShapeQueryParameters3D{Shape=box,Transform=new Transform3D(Basis.Identity,(Vector3)cell+Vector3.One*.5f),CollisionMask=1,Margin=0};
        return GetWorld3D().DirectSpaceState.IntersectShape(query,1).Count==0;
    }
    public bool AnyPlayerInCell(Vector3I c)=>AnyPlayerIntersects(new Aabb((Vector3)c,Vector3.One));
    public bool ChestInUse(Vector3I cell)=>IsChestOpen&&OpenStation==cell||(NetSession.Instance?.Server?.Players.Any(p=>p.Station==cell&&p.Inv.IsOpen)??false);
    public bool OpenChest(Vector3I cell)
    {
        var s=Shapes.At(cell);if(!GameplayActive||s?.Type!=38)return false;
        if(ChestInUse(cell)){Notify("Truhlu právě používá jiný hráč.");return false;}
        if(!ReturnStaging()||!Shapes.SetOpen(cell,true))return false;
        OpenStation=cell;CraftSize=2;SetMode(GameMode.Inventory);return true;
    }
    void CloseLocalChest()
    {
        if(!IsNetClient&&IsChestOpen&&OpenStation is {} c)Shapes.CloseChestWhenFree(c);
    }
    public ItemStack GetChestStack(int slot)=>slot is >=0 and <27&&OpenStation is {} c&&Shapes.At(c) is {} s?s.Slots[slot]:new();
    void SetChestStack(int slot,ItemStack stack)
    {
        if(slot is >=0 and <27&&OpenStation is {} c&&Shapes.At(c) is {} s){s.Slots[slot]=stack;Shapes.Mark(s);}
    }
    bool UseShaped(Vector3I cell)
    {
        var s=Shapes.At(cell);if(s==null)return false;
        if(s.Type==36){Shapes.SetOpen(s.Cell,!s.Open);return true;}
        if(s.Type==38){OpenChest(s.Cell);return true;}return false;
    }
    // Half-metre steps use the real player shape at the raised position, then sweep down.
    // The controller cannot step up a full cube or through a ceiling.
    void TryStepUp(Vector3 horizontal)
    {
        if(!Body.IsOnFloor()||Body.Velocity.Y>0||horizontal.LengthSquared()<.00001f)return;
        var start=Body.GlobalTransform;if(!Body.TestMove(start,horizontal))return;
        var direction=horizontal.Normalized();var half=(FootprintMax-FootprintMin)*.5f;
        float reach=Math.Abs(direction.X)*half.X+Math.Abs(direction.Z)*half.Y;
        var leading=start.Origin+horizontal+direction*(reach+.015f);
        using var ray=PhysicsRayQueryParameters3D.Create(leading+Vector3.Up*.52f,leading-Vector3.Up*.05f,1);
        var support=GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if(support.Count==0||support["normal"].AsVector3().Y<.7f)return;
        float rise=support["position"].AsVector3().Y-start.Origin.Y;
        if(rise<=.0001f||rise>.51f)return;
        var raised=start;raised.Origin+=Vector3.Up*(rise+.005f);
        // Probe the full physical body above and across the step. This also rejects low ceilings.
        using var probe=new PhysicsShapeQueryParameters3D{Shape=footprintCollider.Shape,CollisionMask=1,Margin=.001f};
        probe.Transform=raised*footprintCollider.Transform;
        if(GetWorld3D().DirectSpaceState.IntersectShape(probe,1).Count>0)return;
        raised.Origin+=horizontal;probe.Transform=raised*footprintCollider.Transform;
        if(GetWorld3D().DirectSpaceState.IntersectShape(probe,1).Count>0)return;
        Body.GlobalPosition+=Vector3.Up*(rise+.005f);Body.Velocity=new Vector3(Body.Velocity.X,0,Body.Velocity.Z);
    }
    async void StartBuildingQa(){try{await new BuildingQa(this).Run();}catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}}
}
