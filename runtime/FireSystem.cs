using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public partial class BlockWorld{public FireSystem FireSystem{get;private set;}=null!;}

public partial class FireSystem : Node3D
{
    readonly BlockWorld world;readonly Dictionary<Vector3I,FireVisual> torches=new(),furnaces=new();float refresh;
    public IReadOnlyDictionary<Vector3I,FireVisual> Torches=>torches;
    public IReadOnlyDictionary<Vector3I,FireVisual> FurnaceFires=>furnaces;
    public FireSystem(BlockWorld world){this.world=world;Name="FireSystem";}
    public override void _Process(double delta)
    {
        if(world.Terrain==null)return;refresh-=(float)delta;
        if(refresh<=0){refresh=.25f;Sync();}
        var lights=torches.Values.Concat(furnaces.Values).Where(f=>f.Visible&&f.Burning).OrderBy(f=>f.GlobalPosition.DistanceSquaredTo(world.Body.Position)).ToArray();
        for(int i=0;i<lights.Length;i++)lights[i].SetLight(i<6);
        foreach(var f in torches.Values.Concat(furnaces.Values))f.Step((float)delta,world.SimulationActive);
    }
    void Sync()
    {
        var cells=world.Terrain.GetChanges().Where(e=>e.Type==32).Select(e=>new Vector3I(e.X,e.Y,e.Z)).ToHashSet();
        foreach(var p in torches.ToArray())if(!cells.Contains(p.Key)){RemoveChild(p.Value);p.Value.QueueFree();torches.Remove(p.Key);}
        foreach(var cell in cells)
        {
            bool loaded=world.Terrain.IsLoadedAt(cell.X+.5f,cell.Z+.5f);
            if(!world.IsNetClient&&loaded&&!BlockCatalog.IsFullCube(world.Terrain.GetBlock(cell+Vector3I.Down)))
            {
                if(world.Terrain.TrySetBlock(cell,0))world.SpawnWoodPickup(32,new Vector3(cell.X+.5f,cell.Y+.4f,cell.Z+.5f));continue;
            }
            if(!torches.TryGetValue(cell,out var fire))
            {
                fire=new FireVisual(true){Position=new Vector3(cell.X+.5f,cell.Y,cell.Z+.5f)};torches[cell]=fire;AddChild(fire);
                var body=new StaticBody3D{CollisionLayer=16,CollisionMask=0};body.SetMeta("torch_cell",cell);body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new Vector3(.16f,.7f,.16f)},Position=Vector3.Up*.35f});fire.AddChild(body);
            }
            fire.Visible=loaded;fire.Burning=true;
        }
        foreach(var p in furnaces.ToArray())if(!world.Furnaces.ContainsKey(p.Key)||world.Terrain.GetBlock(p.Key)!=BlockType.Furnace){RemoveChild(p.Value);p.Value.QueueFree();furnaces.Remove(p.Key);}
        foreach(var p in world.Furnaces)
        {
            if(world.Terrain.GetBlock(p.Key)!=BlockType.Furnace)continue;
            if(!furnaces.TryGetValue(p.Key,out var fire)){fire=new FireVisual(false){Position=new Vector3(p.Key.X+.5f,p.Key.Y+.18f,p.Key.Z-.015f)};furnaces[p.Key]=fire;AddChild(fire);}
            fire.Visible=world.Terrain.IsLoadedAt(p.Key.X+.5f,p.Key.Z+.5f);fire.Burning=p.Value.Burn>0;
        }
    }
    public bool IsLit(Vector3 position,float distance=6)=>torches.Values.Concat(furnaces.Values).Any(f=>f.Visible&&f.Burning&&f.GlobalPosition.DistanceSquaredTo(position)<distance*distance);
}
public partial class FireVisual : Node3D
{
    public OmniLight3D Light{get;}=new(){LightColor=new Color("ffad53"),LightEnergy=1.7f,OmniRange=6,OmniAttenuation=1.2f,ShadowEnabled=true,ShadowBias=.04f};
    public bool Burning=true;
    readonly Node3D flames=new();readonly List<ShaderMaterial> materials=new();float phase;
    static readonly Shader shader=new(){Code=@"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never;
uniform float phase=0.0;
void vertex(){ VERTEX.x += sin(phase*7.0+UV.y*9.0)*0.045*UV.y; }
void fragment(){
 float y=1.0-UV.y; float center=0.5+sin(phase*8.0+y*10.0)*0.1*y;
 float width=(1.0-y)*0.42+0.015;
 float shape=(1.0-smoothstep(width*0.4,width,abs(UV.x-center)))*smoothstep(0.0,0.12,y)*(1.0-smoothstep(0.83,1.0,y));
 vec3 color=mix(vec3(1.0,0.75,0.2),vec3(1.0,0.16,0.015),y);
 ALBEDO=color;EMISSION=color*1.7;ALPHA=shape*0.85;
}"};
    public FireVisual(bool torch)
    {
        Name=torch?"TorchFire":"FurnaceFire";
        if(torch)
        {
            AddChild(new MeshInstance3D{Name="GroundedShaft",Mesh=new BoxMesh{Size=new Vector3(.105f,.58f,.105f)},Position=Vector3.Up*.29f,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("a9753c"),Roughness=1}});
            AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new Vector3(.14f,.1f,.14f)},Position=Vector3.Up*.56f,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("603b28"),Roughness=1}});
        }
        flames.Position=Vector3.Up*(torch?.59f:0);AddChild(flames);
        for(int i=0;i<3;i++)
        {
            var material=new ShaderMaterial{Shader=shader};materials.Add(material);flames.AddChild(new MeshInstance3D{Mesh=new QuadMesh{Size=new Vector2(torch?.3f:.42f,torch?.45f:.5f)},Position=Vector3.Up*(torch?.2f:.25f),Rotation=new Vector3(0,i*Mathf.Pi/3,0),MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        Light.Position=Vector3.Up*(torch?.82f:.3f);AddChild(Light);
    }
    public void SetLight(bool enabled){Light.Visible=enabled&&Burning;}
    public void Step(float delta,bool advance)
    {
        if(advance)phase+=delta;flames.Visible=Burning;if(!Burning)Light.Visible=false;
        Light.LightEnergy=1.5f+Mathf.Sin(phase*11)*.08f+Mathf.Sin(phase*17)*.05f;foreach(var m in materials)m.SetShaderParameter("phase",phase);
    }
}
