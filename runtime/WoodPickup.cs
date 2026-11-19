using Godot;
using System;
using System.Collections.Generic;

public sealed class WoodPickupSave
{
    public string Id {get;set;}="";
    public int Type {get;set;}
    public int Count {get;set;}=1;
    public float X {get;set;}
    public float Y {get;set;}
    public float Z {get;set;}
    public float Age {get;set;}
    public float Vx {get;set;}
    public float Vy {get;set;}
    public float Vz {get;set;}
    public float Rx {get;set;}
    public float Ry {get;set;}
    public float Rz {get;set;}
}

/// <summary>A real, small, colliding log item. Remote chunks suspend it without deleting it.</summary>
public partial class WoodPickup : RigidBody3D
{
    public const float CollectionDelay=.25f;
    public const float CollectionRadius=1.5f;
    public const float CubeSize=.28f;
    static readonly Dictionary<int,ArrayMesh> sharedMeshes=new();
    readonly BlockWorld world;
    public int Type {get;}
    public int Count {get;}
    public string Id {get;}
    public float Age {get;private set;}
    public bool IsCollectible=>Age>=CollectionDelay;
    public RigidBody3D Body=>this;
    bool pauseSuspended,physicsSuspended;
    Vector3 storedVelocity,storedAngular;

    public WoodPickup(BlockWorld world,int type,Vector3 position,string id,int count=1,float age=0)
    {
        this.world=world;Type=type;Count=count;Id=id;Age=age;
        Name="WoodPickup_"+id;Position=position;CollisionLayer=4;CollisionMask=1;
        Mass=.08f;LinearDamp=.6f;AngularDamp=8;ContinuousCd=true;
        PhysicsMaterialOverride=new PhysicsMaterial {Friction=.9f,Bounce=.06f};
        AddChild(new CollisionShape3D {Name="WoodCollision",Shape=new BoxShape3D {Size=Vector3.One*CubeSize}});
        AddChild(new MeshInstance3D {Name="RenderedWood",Mesh=BuildCube(type)});
    }
    static ArrayMesh BuildCube(int type)
    {
        if(sharedMeshes.TryGetValue(type,out var cached))return cached;
        var mesh=new ArrayMesh();var definition=BlockCatalog.Get(type);float h=CubeSize*.5f;
        for(int surface=0;surface<2;surface++)
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var indices=new List<int>();
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
            {
                int start=vertices.Count;vertices.AddRange(new[]{a,b,c,d});for(int i=0;i<4;i++)normals.Add(normal);
                uvs.AddRange(new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)});
                foreach(int index in new[]{0,1,2,0,2,3})indices.Add(start+index);
            }
            if(surface==0)
            {
                Quad(new(h,-h,-h),new(h,-h,h),new(h,h,h),new(h,h,-h),Vector3.Right);
                Quad(new(-h,-h,h),new(-h,-h,-h),new(-h,h,-h),new(-h,h,h),Vector3.Left);
                Quad(new(h,-h,h),new(-h,-h,h),new(-h,h,h),new(h,h,h),Vector3.Back);
                Quad(new(-h,-h,-h),new(h,-h,-h),new(h,h,-h),new(-h,h,-h),Vector3.Forward);
            }
            else
            {
                Quad(new(-h,h,-h),new(h,h,-h),new(h,h,h),new(-h,h,h),Vector3.Up);
                Quad(new(-h,-h,h),new(h,-h,h),new(h,-h,-h),new(-h,-h,-h),Vector3.Down);
            }
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=vertices.ToArray();arrays[(int)Mesh.ArrayType.Normal]=normals.ToArray();arrays[(int)Mesh.ArrayType.TexUV]=uvs.ToArray();arrays[(int)Mesh.ArrayType.Index]=indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            mesh.SurfaceSetMaterial(surface,new StandardMaterial3D {AlbedoTexture=ResourceLoader.Load<Texture2D>(surface==0?definition.SideTexture:definition.TopTexture),AlbedoColor=Colors.White,Roughness=1,TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,TextureRepeat=false});
        }
        sharedMeshes[type]=mesh;return mesh;
    }
    public override void _Ready(){SetSuspended(!world.GameplayActive);}
    public void SetSuspended(bool paused){pauseSuspended=paused;UpdateSuspension();}
    void UpdateSuspension()
    {
        if(NetRemote){NetSuspension();return;}
        bool loaded=world.Terrain.IsLoadedAt(Position.X,Position.Z);Visible=loaded;
        bool suspend=pauseSuspended||!loaded;
        if(suspend==physicsSuspended)return;
        if(suspend){storedVelocity=LinearVelocity;storedAngular=AngularVelocity;Freeze=true;}
        else{Freeze=false;LinearVelocity=storedVelocity;AngularVelocity=storedAngular;Sleeping=false;}
        physicsSuspended=suspend;
    }
    public override void _PhysicsProcess(double delta)
    {
        if(NetRemote){NetFollow(delta);return;}
        UpdateSuspension();if(physicsSuspended||!world.SimulationActive)return;
        Age+=(float)delta;
        if(IsCollectible)world.CollectWoodPickup(this);
    }
    public WoodPickupSave Snapshot()
    {
        var velocity=physicsSuspended?storedVelocity:LinearVelocity;
        return new WoodPickupSave {Id=Id,Type=Type,Count=Count,X=Position.X,Y=Position.Y,Z=Position.Z,Age=Age,Vx=velocity.X,Vy=velocity.Y,Vz=velocity.Z,Rx=Rotation.X,Ry=Rotation.Y,Rz=Rotation.Z};
    }
    public void RestoreMotion(WoodPickupSave data)
    {
        Rotation=new Vector3(data.Rx,data.Ry,data.Rz);storedVelocity=new Vector3(data.Vx,data.Vy,data.Vz);
        if(!physicsSuspended)LinearVelocity=storedVelocity;
    }
}
