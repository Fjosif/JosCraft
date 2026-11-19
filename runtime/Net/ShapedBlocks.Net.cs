using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public static class ShapeCodec
{
    public static void Write(NetWriter w,PlacedBlockSave s)=>w.Cell(s.X,s.Y,s.Z).U8((byte)s.Type).U8((byte)s.Rotation).Bool(s.Open);
    public static PlacedBlockSave Read(ref NetReader r)
    {
        var c=r.Cell();var s=new PlacedBlockSave{X=c.X,Y=c.Y,Z=c.Z,Type=r.U8(),Rotation=r.U8(),Open=r.Bool()};
        if(!PlacedBlockSave.Valid(s))throw new NetFormatException("shaped block");return s;
    }
    public static IEnumerable<byte[]> Batches(PlacedBlockSave[] states)
    {
        for(int i=0;i<states.Length;i+=200){int n=Math.Min(200,states.Length-i);var w=new NetWriter(Msg.SnapShapes).U16((ushort)n);for(int j=0;j<n;j++)Write(w,states[i+j]);yield return w.ToArray();}
    }
}

public sealed partial class NetServer
{
    void OnShapeChanged(PlacedBlockSave s)
    {
        var w=new NetWriter(Msg.ShapeChanged).U32(++worldSeq);ShapeCodec.Write(w,s);BroadcastWorld(w.ToArray());
    }
    void CloseChest(NetPlayer p)
    {
        if(p.Inv.Chest is {} chest)world.Shapes.CloseChestWhenFree(chest.Cell);
    }
    void OpenChest(NetPlayer p,Vector3I cell)
    {
        if(world.Shapes.At(cell) is not {} s)return;
        if(world.IsChestOpen&&world.OpenStation==cell||Players.Any(o=>o!=p&&o.Station==cell&&o.Inv.IsOpen)){Feedback(p,"Truhlu právě používá jiný hráč.");return;}
        if(p.Inv.IsOpen&&p.Inv.Chest?.Cell==cell){SendInventory(p,true);return;}
        if(!world.Shapes.SetOpen(cell,true)){Feedback(p,"Nad truhlou nebo za ní není dost místa.");return;}
        CloseChest(p);p.Inv.Close();p.Inv.Open(2,null,s);p.Station=cell;SendInventory(p,true);
    }
}
