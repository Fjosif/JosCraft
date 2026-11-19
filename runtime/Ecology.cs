using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class CreatureSave
{
    public string Id{get;set;}=""; public string Kind{get;set;}="dog";
    public float X{get;set;} public float Y{get;set;} public float Z{get;set;}
    public bool Tamed{get;set;} public bool Following{get;set;}=true; public int Health{get;set;}=12;
    public string OwnerKey{get;set;}="";
}
public partial class Ecology : Node3D
{
    readonly BlockWorld world;
    readonly List<Creature> creatures=new();
    public IReadOnlyList<Creature> Creatures=>creatures;
    public float TimeOfDay{get;private set;}=.12f;
    public const float DaySeconds=1200;
    public bool IsNight=>TimeOfDay>.54f&&TimeOfDay<.96f;
    public string Clock=>$"{(int)((TimeOfDay*24+6)%24):00}:{(int)(TimeOfDay*1440)%60:00}";
    bool initialised; float spawnTimer,attackCooldown;
    readonly DirectionalLight3D sun; readonly Godot.Environment environment;
    public Ecology(BlockWorld world,DirectionalLight3D sun,Godot.Environment environment){this.world=world;this.sun=sun;this.environment=environment;Name="Ecology";}
    public void Restore(float time,CreatureSave[]? saved)
    {
        foreach(var c in creatures){RemoveChild(c);c.QueueFree();}creatures.Clear();TimeOfDay=Mathf.PosMod(time,1);initialised=saved!=null;
        if(saved!=null)foreach(var s in saved)Spawn(s);UpdateLight();
    }
    public CreatureSave[] Snapshot()=>creatures.Where(GodotObject.IsInstanceValid).Select(c=>c.Snapshot()).ToArray();
    public void SetTimeForQa(float time){TimeOfDay=Mathf.PosMod(time,1);UpdateLight();}
    public override void _PhysicsProcess(double dt)
    {
        if(RemoteMode){RemoteTick((float)dt);return;}
        if(!world.SimulationActive||!world.CharacterReady)return;float delta=(float)dt;
        if(!initialised&&world.Terrain.PendingChunkCount==0){initialised=true;SeedWildlife();}
        TimeOfDay=Mathf.PosMod(TimeOfDay+delta/DaySeconds,1);UpdateLight();attackCooldown=Math.Max(0,attackCooldown-delta);
        spawnTimer-=delta;if(IsNight&&spawnTimer<=0){spawnTimer=20;TrySpawnZombie();}
    }
    void UpdateLight()
    {
        float day=Math.Clamp(Mathf.Sin(TimeOfDay*Mathf.Tau)*2+.1f,0,1);
        sun.RotationDegrees=new Vector3(-Mathf.Sin(TimeOfDay*Mathf.Tau)*70,-35+TimeOfDay*360,0);sun.LightEnergy=.07f+.6f*day;
        environment.BackgroundColor=new Color("101c39").Lerp(new Color("b6d5e8"),day);environment.AmbientLightEnergy=.14f+.16f*day;environment.AmbientLightColor=new Color("798cbb").Lerp(Colors.White,day);
    }
    public bool FindGround(Vector2 xz,float height,out Vector3 position)
    {
        position=default;if(!world.Terrain.IsLoadedAt(xz.X,xz.Y))return false;
        var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new(xz.X,BlockTerrain.MaxBuildY+2,xz.Y),new(xz.X,-1,xz.Y),1));
        if(hit.Count==0||hit["normal"].AsVector3().Y<.9f)return false;
        var p=hit["position"].AsVector3();var cell=new Vector3I(Mathf.FloorToInt(p.X),Mathf.FloorToInt(p.Y-.01f),Mathf.FloorToInt(p.Z));var block=world.Terrain.GetBlock(cell);
        if(block!=BlockType.Grass&&block!=BlockType.Dirt&&block!=BlockType.Stone)return false;
        var query=new PhysicsShapeQueryParameters3D{Shape=new BoxShape3D{Size=new Vector3(.85f,height,.85f)},Transform=new Transform3D(Basis.Identity,p+Vector3.Up*(height/2+.025f)),CollisionMask=1};
        if(world.GetWorld3D().DirectSpaceState.IntersectShape(query).Count>0)return false;
        position=p+Vector3.Up*.03f;return true;
    }
    void SeedWildlife()
    {
        string[] kinds={"dog","rabbit","fox","carrot","berries","carrot","berries"};
        for(int i=0;i<kinds.Length;i++)for(int attempt=0;attempt<30;attempt++)
        {
            float a=(i*2.4f+attempt*.57f),r=6+i*.65f+attempt*.12f;var p=new Vector2(Mathf.Cos(a)*r,Mathf.Sin(a)*r);
            if(!FindGround(p,kinds[i] is "carrot" or "berries"?.6f:1.2f,out var ground))continue;
            Spawn(new(){Id="wild-"+i,Kind=kinds[i],X=ground.X,Y=ground.Y,Z=ground.Z});break;
        }
    }
    public Creature Spawn(CreatureSave data)
    {
        var c=new Creature(world,this,data);creatures.Add(c);AddChild(c);return c;
    }
    void TrySpawnZombie()
    {
        if(creatures.Count(c=>c.Kind=="zombie")>=3)return;
        for(int i=0;i<20;i++)
        {
            float a=GD.Randf()*Mathf.Tau,r=14+GD.Randf()*8;var p=world.RandomPlayerPosition()+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);
            if(FindGround(new(p.X,p.Z),2.4f,out var g)&&!world.FireSystem.IsLit(g)){Spawn(new(){Id=Guid.NewGuid().ToString("N"),Kind="zombie",X=g.X,Y=g.Y,Z=g.Z,Health=16});return;}
        }
    }
    public void Remove(Creature c,bool loot=false)
    {
        if(!creatures.Remove(c))return;if(loot)world.GiveResource((int)BlockType.Bone,1);c.CollisionLayer=0;c.Visible=false;c.SetPhysicsProcess(false);c.QueueFree();
    }
    Creature? Target(float distance)
    {
        var from=world.Camera.GlobalPosition;var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from,from-world.Camera.GlobalBasis.Z*distance,1|8));
        return hit.Count>0?hit["collider"].AsGodotObject() as Creature:null;
    }
    public bool TryAttack()
    {
        if(world.IsNetClient)return NetForwardInteract(true);
        var c=Target(3.4f);if(c==null||c.Kind!="zombie")return false;if(attackCooldown>0)return true;
        attackCooldown=.5f;int t=world.GetSlot(world.SelectedHotbar).Type;c.Hurt(t==28?6:t==27?4:2);return true;
    }
    public bool TryInteract()
    {
        if(world.IsNetClient)return NetForwardInteract(false);
        var c=Target(3.6f);if(c==null)return false;
        if(c.Kind is "carrot" or "berries"){if(world.GiveResource(c.Kind=="carrot"?30:31,3)){world.Notify("Nasbíráno krmení pro zvířátka.");Remove(c);}return true;}
        if(c.Kind=="zombie")return false;
        if(c.Tamed){if(c.OwnerKey.Length>0&&c.OwnerKey!="host"){world.Notify("Tenhle kamarád patří jinému hráči.");return true;}c.Following=!c.Following;world.Notify(c.Following?"Pojď se mnou!":"Zůstaň tady.");return true;}
        int food=c.Kind=="dog"?29:c.Kind=="rabbit"?30:31;
        if(!world.ConsumeSelected(food)){world.Notify("Vyber: "+BlockCatalog.Get(food).Name);return true;}
        c.Tamed=true;c.Following=true;c.OwnerKey="host";world.Notify("Nový kamarád tě bude následovat!");return true;
    }
}

public partial class Creature : CharacterBody3D
{
    readonly BlockWorld world;readonly Ecology ecology; readonly string id;
    public string Kind{get;} public bool Tamed;public bool Following;public int Health;public string OwnerKey="";
    public float ModelHeight{get;private set;}
    public Node3D Model{get;}=new();
    float cooldown,wanderTime,stuck;
    public AnimationPlayer? Animator{get;private set;}
    string clip="";float actionTime;
    Vector3 wander,lastPosition;
    public Creature(BlockWorld world,Ecology ecology,CreatureSave s)
    {
        this.world=world;this.ecology=ecology;id=s.Id;Kind=s.Kind;Tamed=s.Tamed;Following=s.Following;Health=s.Health;OwnerKey=s.OwnerKey??"";
        Name="Creature_"+id;Position=new(s.X,s.Y,s.Z);CollisionLayer=8;CollisionMask=1;FloorSnapLength=.3f;FloorStopOnSlope=true;
        ModelHeight=Kind=="zombie"?2.4f:Kind=="rabbit"?1.1f:Kind is "carrot" or "berries"?.55f:1.2f;
        float depth=Kind=="fox"?1.7f:Kind=="dog"?1.45f:Kind=="rabbit"?1.1f:.85f;
        AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new Vector3(Kind=="zombie"?.8f:.65f,ModelHeight,depth)},Position=Vector3.Up*ModelHeight/2});AddChild(Model);BuildModel();
    }
    MeshInstance3D Box(Vector3 size,Vector3 center,string color,Node3D? parent=null)
    {
        var box=new MeshInstance3D{Mesh=new BoxMesh{Size=size},Position=center,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color(color),Roughness=1}};(parent??Model).AddChild(box);return box;
    }
    void BuildModel()
    {
        if(Kind is "carrot" or "berries")
        {
            Box(new(.6f,.32f,.6f),new(0,.16f,0),"527c3b");
            for(int i=0;i<3;i++)Box(new(.12f,.2f,.12f),new((i-1)*.16f,.4f,-.12f),Kind=="carrot"?"ed913b":"b43b60");
            return;
        }
        var scene=ResourceLoader.Load<PackedScene>("res://assets/creatures/"+Kind+".glb").Instantiate<Node3D>();Model.AddChild(scene);
        foreach(var m in Viewer.Descendants(scene).OfType<MeshInstance3D>())for(int i=0;i<m.Mesh.GetSurfaceCount();i++)
            if(m.GetActiveMaterial(i) is StandardMaterial3D material){material.VertexColorUseAsAlbedo=true;material.AlbedoColor=Colors.White;material.Roughness=1;}
        Animator=Viewer.Descendants(scene).OfType<AnimationPlayer>().First();
    }
    public CreatureSave Snapshot()=>new(){Id=id,Kind=Kind,X=Position.X,Y=Position.Y,Z=Position.Z,Tamed=Tamed,Following=Following,Health=Health,OwnerKey=OwnerKey};
    public void Hurt(int amount){Health-=amount;if(Health<=0)ecology.Remove(this,true);else{actionTime=.35f;Play("Hurt");}}
    public override void _Ready(){if(Animator!=null)Play("Idle");}
    void Play(string name)
    {
        if(Animator==null)return;string actual=BlockWorld.ClipName(Animator,name);if(!Animator.HasAnimation(actual))return;
        Animator.GetAnimation(actual).LoopMode=name is "Idle" or "Walk" or "Run" or "Sit"?Animation.LoopModeEnum.Linear:Animation.LoopModeEnum.None;
        if(clip!=name){Animator.Play(actual,.12);clip=name;}else if(!Animator.IsPlaying())Animator.Play();
    }
    public float RenderedFloor()
    {
        return PoseGeometry.Floor(Model);
    }
    public override void _PhysicsProcess(double dt)
    {
        if(IsProxy){NetFollow((float)dt);return;}
        if(!world.SimulationActive){Animator?.Pause();return;}
        bool loaded=world.Terrain.IsLoadedAt(Position.X,Position.Z);Visible=loaded;if(!loaded){Animator?.Pause();return;}
        float delta=(float)dt;cooldown=Math.Max(0,cooldown-delta);
        if(Kind is "carrot" or "berries")return;
        if(Kind=="zombie"&&!ecology.IsNight&&cooldown<=0)
        {
            var above=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(Position+Vector3.Up*2.2f,Position+Vector3.Up*40,1));
            if(above.Count==0){Hurt(3);cooldown=1;if(Health<=0)return;}
        }
        var target=world.NearestPlayerPosition(Position,out int targetSlot);if(Tamed&&Kind!="zombie")target=world.CompanionOwnerPosition(OwnerKey)??Position;var toPlayer=target-Position;toPlayer.Y=0;
        Vector3 dir=Vector3.Zero;float speed=Kind=="zombie"?1.6f:Kind=="rabbit"?2.8f:2.5f;
        if(Kind=="zombie")
        {
            if(toPlayer.Length()<24&&toPlayer.Length()>1.15f)dir=toPlayer.Normalized();
            if(toPlayer.Length()<1.3f&&Math.Abs(target.Y-Position.Y)<1.4f&&cooldown<=0)
            {
                var ray=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(Position+Vector3.Up,target+Vector3.Up,1));
                if(ray.Count==0){world.DamagePlayer(targetSlot,2);cooldown=1.2f;actionTime=.6f;Play("Attack");}
            }
        }
        else if(Tamed&&Following)
        {
            if(toPlayer.Length()>2.4f)dir=toPlayer.Normalized();
            if(toPlayer.Length()>16)for(int i=0;i<12;i++){float a=i*Mathf.Tau/12;var q=target+new Vector3(Mathf.Cos(a)*3,0,Mathf.Sin(a)*3);if(ecology.FindGround(new(q.X,q.Z),ModelHeight+.05f,out var p)){Position=p;Velocity=Vector3.Zero;break;}}
        }
        else if(!Tamed)
        {
            wanderTime-=delta;if(wanderTime<=0){wanderTime=2+GD.Randf()*3;float a=GD.Randf()*Mathf.Tau;wander=new(Mathf.Cos(a),0,Mathf.Sin(a));}dir=wanderTime>1.2f?wander:Vector3.Zero;speed*=.4f;
        }
        var v=Velocity;v.Y-=20*delta;if(IsOnFloor())v.Y=-.2f;
        v.X=dir.X*speed;v.Z=dir.Z*speed;
        if(dir.LengthSquared()>.1f)
        {
            Rotation=new(0,Mathf.Atan2(-dir.X,-dir.Z),0);
            var ahead=Position+dir*.55f;
            if(!world.Terrain.IsLoadedAt(ahead.X,ahead.Z)){v.X=v.Z=0;}
            else if(IsOnFloor())
            {
                var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(Position+Vector3.Up*.35f,Position+Vector3.Up*.35f+dir*.6f,1));
                if(hit.Count>0||Kind=="rabbit"&&cooldown<=0){v.Y=7;cooldown=.7f;}
            }
        }
        Velocity=v;MoveAndSlide();
        actionTime=Math.Max(0,actionTime-delta);
        if(Animator!=null)
        {
            if(actionTime<=0)Play(!IsOnFloor()?"Jump":Tamed&&!Following?"Sit":dir.LengthSquared()>.1f?speed>2?"Run":"Walk":"Idle");
            Model.Position=Vector3.Zero;
            if(IsOnFloor())Model.Position=Vector3.Up*(Position.Y-RenderedFloor());
        }
        if(Position.Y< -5&&ecology.FindGround(new(target.X+3,target.Z),ModelHeight,out var safe)){Position=safe;Velocity=Vector3.Zero;}
        if(Tamed&&Following&&dir.LengthSquared()>.1f){stuck=Position.DistanceSquaredTo(lastPosition)<.0001f?stuck+delta:0;if(stuck>2&&IsOnFloor()){Velocity+=Vector3.Up*7;stuck=0;}}lastPosition=Position;
    }
}
