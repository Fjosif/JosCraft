using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;

// Tests the actual imported scene, its skinning, rendered floor and physics floor.
public sealed class ViewerQa
{
    readonly Viewer viewer;
    readonly List<string> failures=new();
    readonly List<object> models=new();
    readonly string output=ProjectSettings.GlobalizePath("res://artifacts");
    public ViewerQa(Viewer viewer){this.viewer=viewer;}
    async Task Frame(int n=2){for(int i=0;i<n;i++)await viewer.ToSignal(viewer.GetTree(),SceneTree.SignalName.ProcessFrame);}
    async Task Capture(string name)
    {
        await Frame(3);await viewer.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var image=viewer.GetViewport().GetTexture().GetImage();
        if(image==null || image.IsEmpty()){failures.Add("Empty screenshot "+name);return;}
        var error=image.SavePng(Path.Combine(output,name+".png"));if(error!=Error.Ok)failures.Add("Screenshot save "+error);
    }
    List<Vector3> Vertices(Node3D root)
    {
        var vertices=new List<Vector3>();
        foreach(var mesh in Viewer.Descendants(root).OfType<MeshInstance3D>())
        {
            if(mesh.Mesh==null)continue;
            Mesh baked=mesh.Skin!=null?mesh.BakeMeshFromCurrentSkeletonPose():mesh.Mesh;
            for(int s=0;s<baked.GetSurfaceCount();s++)
            {
                var arrays=baked.SurfaceGetArrays(s);
                foreach(var v in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())vertices.Add(mesh.GlobalTransform*v);
            }
        }
        return vertices;
    }
    public async Task Run()
    {
        Directory.CreateDirectory(output);await Frame(12);
        var floorTop=viewer.FloorMesh.GlobalPosition.Y+viewer.FloorMesh.GetAabb().End.Y;
        var hit=viewer.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(12,4,0),new Vector3(12,-2,0)));
        float collisionY=hit.Count==0?999:hit["position"].AsVector3().Y;
        if(Math.Abs(collisionY-floorTop)>.002)failures.Add("Rendered floor differs from collider");
        viewer.ShowGallery();await Capture("gallery-desktop");
        var frameTimes=new List<double>(); var watch=System.Diagnostics.Stopwatch.StartNew();double last=watch.Elapsed.TotalMilliseconds;
        for(int n=0;n<120;n++){await Frame(1);double t=watch.Elapsed.TotalMilliseconds;frameTimes.Add(t-last);last=t;}
        var performance=new {platform=OS.GetName(),renderer=RenderingServer.GetCurrentRenderingMethod(),adapter=RenderingServer.GetVideoAdapterName(),resolution=viewer.GetViewport().GetVisibleRect().Size.ToString(),instances=11,frames=120,medianFrameMs=frameTimes.Order().ElementAt(60),p95FrameMs=frameTimes.Order().ElementAt(114),drawCalls=Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),primitives=Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),objects=Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),scope="Actual desktop offscreen window, whole gallery including UI; not a physical mobile benchmark"};
        for(int i=0;i<11;i++)
        {
            viewer.Choose(i); await Frame(2);var root=viewer.Models[i];var player=viewer.Players[i];
            var skeletons=Viewer.Descendants(root).OfType<Skeleton3D>().ToArray();
            var meshes=Viewer.Descendants(root).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null).ToArray();
            int bones=skeletons.Sum(s=>s.GetBoneCount());int surfaces=meshes.Sum(m=>m.Mesh.GetSurfaceCount());
            long triangles=0;foreach(var m in meshes)for(int s=0;s<m.Mesh.GetSurfaceCount();s++){var arrays=m.Mesh.SurfaceGetArrays(s);int[] idx=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();triangles+=(idx.Length>0?idx.Length:arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length)/3;}
            if(bones<4)failures.Add(Viewer.Ids[i]+": missing rig");if(meshes.Length==0)failures.Add(Viewer.Ids[i]+": no mesh");
            var clips=new List<object>();
            foreach(string clip in Viewer.Clips)
            {
                string name=viewer.FindClip(i,clip);if(player==null || name.Length==0){failures.Add(Viewer.Ids[i]+": missing "+clip);continue;}
                viewer.Preview(clip); var animation=player.GetAnimation(name);player.Play(name,0); player.Advance(0); player.Pause();
                var mins=new List<float>();var maxs=new List<float>();List<Vector3>? first=null;double variation=0;
                int keys=0;for(int t=0;t<animation.GetTrackCount();t++)keys+=animation.TrackGetKeyCount(t);
                for(int sample=0;sample<17;sample++)
                {
                    player.Seek(animation.Length*sample/17,true); await Frame(1);

                    var vertices=Vertices(root);if(vertices.Count==0)throw new Exception("Empty baked geometry");
                    mins.Add(vertices.Min(v=>v.Y));maxs.Add(vertices.Max(v=>v.Y));
                    if(first==null)first=vertices;else for(int v=0;v<vertices.Count;v++)variation=Math.Max(variation,vertices[v].DistanceTo(first[v]));
                }
                bool grounded=clip is "Idle" or "Walk" or "Run" or "Sit";
                if(grounded && (mins.Min()<floorTop-.025 || mins.Max()>floorTop+.035))failures.Add($"{Viewer.Ids[i]} {clip} ground contact range {mins.Min():F4} .. {mins.Max():F4}");
                if(keys<4 || variation<.002)failures.Add(Viewer.Ids[i]+": animation has no visual motion "+clip);
                clips.Add(new {clip,length=animation.Length,tracks=animation.GetTrackCount(),keys,minY=mins.Min(),maxContactY=mins.Max(),maxY=maxs.Max(),maxVertexMotion=variation,grounded,airborneIntentional=clip=="Jump",samples=17});
                if(clip is "Idle" or "Walk" or "Run" or "Jump" or "Sit" || i==0 || i==10) {player.Seek(animation.Length*(clip=="Sit"?.95:.29),true);await Capture(Viewer.Ids[i]+"-"+clip.ToLowerInvariant());}
            }
            models.Add(new {id=Viewer.Ids[i],meshes=meshes.Length,surfaces,bones,triangles,clips});
        }
        // Test the exact viewer CharacterBody against its actual floor with physics, independent of key input.
        viewer.Choose(0);viewer.Preview("Walk");viewer.EnableMovement();await Frame(20);
        var body=viewer.GetNode<CharacterBody3D>("MovementBody");body.Position=new Vector3(0,.5f,0);
        for(int n=0;n<90;n++) {await viewer.ToSignal(viewer.GetTree(),SceneTree.SignalName.PhysicsFrame);body.Velocity=new Vector3(1,body.Velocity.Y-18/60f,0);body.MoveAndSlide();}
        bool movementGrounded=body.IsOnFloor()&&Math.Abs(body.Position.Y)<.02&&body.Position.X>1;
        float visualContact=Vertices(viewer.Models[0]).Min(v=>v.Y);
        if(Math.Abs(visualContact)>.025)failures.Add("Attached walking visual is not grounded "+visualContact);
        if(!movementGrounded)failures.Add("CharacterBody movement/ground contact failed "+body.Position);
        await Capture("movement-walk");
        body.Velocity=new Vector3(0,6,0);body.MoveAndSlide();float peak=body.Position.Y;
        float airborneVisualOffset=999;
        for(int n=0;n<90;n++){await viewer.ToSignal(viewer.GetTree(),SceneTree.SignalName.PhysicsFrame);body.Velocity+=new Vector3(0,-18/60f,0);body.MoveAndSlide();peak=Math.Max(peak,body.Position.Y);viewer.ApplyPhysicsJumpPose();if(n==15){await Frame(2);viewer.ApplyPhysicsJumpPose();await Frame(2);airborneVisualOffset=Vertices(viewer.Models[0]).Min(v=>v.Y)-body.Position.Y;await Capture("movement-jump");}}
        if(Math.Abs(airborneVisualOffset)>.025)failures.Add("Physics jump retains preview arc "+airborneVisualOffset);
        bool jumpPass=peak>.6f&&body.IsOnFloor();if(!jumpPass)failures.Add("CharacterBody jump failed");
        viewer.GetWindow().Size=new Vector2I(480,900);viewer.GetWindow().ContentScaleSize=Vector2I.Zero;await Frame(4);
        viewer.Choose(8);await Frame(24);await Capture("mobile-480x900");
        viewer.ShowGallery();await Capture("gallery-mobile-480x900");
        var result=new {pass=failures.Count==0,utc=DateTime.UtcNow,engine=Engine.GetVersionInfo()["string"].AsString(),floor=new {renderedTop=floorTop,collisionTop=collisionY},movement=new {grounded=movementGrounded,visualContact,jump=jumpPass,jumpPeak=peak,airborneVisualOffset},performance,models,failures};
        File.WriteAllText(Path.Combine(output,"godot-qa.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("JOSCRAFT_QA "+(failures.Count==0?"PASS":"FAIL")+" "+string.Join("; ",failures));
        viewer.GetTree().Quit(failures.Count==0?0:1);
    }
}





