using Godot;
using System;
using System.Linq;

public static class PoseGeometry
{
    public static float Floor(Node3D root)
    {
        float floor=float.PositiveInfinity;
        foreach(var m in Viewer.Descendants(root).OfType<MeshInstance3D>())
        {
            bool baked=m.Skin!=null;var mesh=baked?m.BakeMeshFromCurrentSkeletonPose():m.Mesh;
            try{for(int s=0;s<mesh.GetSurfaceCount();s++){using var arrays=mesh.SurfaceGetArrays(s);foreach(var v in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())floor=Math.Min(floor,(m.GlobalTransform*v).Y);}}
            finally{if(baked)mesh.Dispose();}
        }
        return floor;
    }
}
