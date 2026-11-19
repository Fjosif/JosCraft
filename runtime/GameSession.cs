using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed record ItemStack(int Type=0,int Count=0);
public sealed class GameSettings
{
    public float MouseSensitivity {get;set;}=.0035f;
    public float Fov {get;set;}=74;
    public int TouchMode {get;set;}
}
public sealed class SavedGame
{
    public int Version {get;set;}=1;
    public int Seed {get;set;}=20261113;
    public int Character {get;set;}
    public float X {get;set;}=.5f;
    public float Y {get;set;}=4.3f;
    public float Z {get;set;}=.5f;
    public float Yaw {get;set;}
    public float Pitch {get;set;}=.12f;
    public bool FirstPerson {get;set;}=true;
    public int SelectedSlot {get;set;}
    public ItemStack[] Slots {get;set;}=Enumerable.Range(0,36).Select(_=>new ItemStack()).ToArray();
    public BlockTerrain.BlockEdit[] Edits {get;set;}=Array.Empty<BlockTerrain.BlockEdit>();
    public int[]? TreeExclusions {get;set;}
    public WoodPickupSave[] Pickups {get;set;}=Array.Empty<WoodPickupSave>();
    public float TimeOfDay{get;set;}=.12f;
    public int Health{get;set;}=20;
    public CreatureSave[]? Creatures{get;set;}
    public FurnaceSave[] Furnaces{get;set;}=Array.Empty<FurnaceSave>();
    public RemotePlayerSave[]? RemotePlayers{get;set;}
    public PlacedBlockSave[] PlacedBlocks{get;set;}=Array.Empty<PlacedBlockSave>();
}
public static class GameSession
{
    public static bool IsQa;
    public static SavedGame? Pending;
    public static bool CaptureAfterStart;
    public static GameSettings Settings=new();
    public static string DataRoot=>ProjectSettings.GlobalizePath(OS.HasFeature("editor")?"res://saves":"user://saves");
    public static string SavePath=>IsQa?ProjectSettings.GlobalizePath("res://artifacts/game-qa-data/session.json"):Path.Combine(DataRoot,"slot-1.json");
    public static string SettingsPath=>Path.Combine(DataRoot,"settings.json");
    public static bool HasSave=>File.Exists(SavePath);
    public static bool WriteAtomic(string path,object data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp=path+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
            // A corrupt primary must never replace the last readable backup.
            if(File.Exists(path)&&(data is not SavedGame||Read(path)!=null))File.Copy(path,path+".bak",true);
            File.Move(temp,path,true);return true;
        }
        catch{return false;}
    }
    public static SavedGame? Read(string path)
    {
        try
        {
            if(!File.Exists(path)||new FileInfo(path).Length>32_000_000)return null;
            var data=JsonSerializer.Deserialize<SavedGame>(File.ReadAllText(path));
            if(data==null||data.Version!=1||data.Character<0||data.Character>=11||data.Slots==null||data.Slots.Length!=36||data.Edits==null||data.Edits.Length>250000)return null;
            if(!float.IsFinite(data.X)||!float.IsFinite(data.Y)||!float.IsFinite(data.Z)||!float.IsFinite(data.Yaw)||!float.IsFinite(data.Pitch))return null;
            if(data.Slots.Any(s=>s==null||!BlockCatalog.IsValid((BlockType)s.Type)||s.Count<0||s.Count>64||(s.Count==0)!=(s.Type==0)))return null;
            if(data.Edits.Any(e=>e.X<BlockTerrain.WorldMin||e.X>=BlockTerrain.WorldMaxExclusive||e.Z<BlockTerrain.WorldMin||e.Z>=BlockTerrain.WorldMaxExclusive||e.Y<1||e.Y>BlockTerrain.MaxBuildY||!BlockCatalog.IsBlock((BlockType)e.Type)))return null;
            if(!float.IsFinite(data.TimeOfDay)||data.TimeOfDay<0||data.TimeOfDay>=1||data.Health<1||data.Health>20)return null;
            if(data.Creatures!=null&&(data.Creatures.Length>100||data.Creatures.Any(c=>c==null||string.IsNullOrWhiteSpace(c.Id)||c.Id.Length>64||!new[]{"dog","rabbit","fox","zombie","carrot","berries"}.Contains(c.Kind)||!float.IsFinite(c.X)||!float.IsFinite(c.Y)||!float.IsFinite(c.Z)||c.Health<1||c.Health>100)||data.Creatures.Select(c=>c.Id).Distinct().Count()!=data.Creatures.Length))return null;
            if(data.Furnaces==null||data.Furnaces.Length>1000||data.Furnaces.Any(f=>f==null||f.Input==null||f.Fuel==null||f.Output==null||new[]{f.Input,f.Fuel,f.Output}.Any(s=>s.Count<0||s.Count>64||!BlockCatalog.IsValid((BlockType)s.Type)||(s.Count==0)!=(s.Type==0))||!float.IsFinite(f.Burn)||!float.IsFinite(f.Progress)||f.Burn<0||f.Burn>80||f.Progress<0||f.Progress>10)||data.Furnaces.Select(f=>(f.X,f.Y,f.Z)).Distinct().Count()!=data.Furnaces.Length)return null;
            if(data.TreeExclusions!=null&&(data.TreeExclusions.Length>10000||data.TreeExclusions.Distinct().Count()!=data.TreeExclusions.Length))return null;
            if(data.Pickups==null||data.Pickups.Length>10000||data.Pickups.Any(p=>!ValidPickup(p)))return null;
            if(data.Pickups.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=data.Pickups.Length)return null;
            if(data.RemotePlayers!=null&&(data.RemotePlayers.Length>64||data.RemotePlayers.Any(p=>!RemotePlayerSave.Valid(p))||data.RemotePlayers.Select(p=>p.Key).Distinct().Count()!=data.RemotePlayers.Length))return null;
            if(data.PlacedBlocks==null||data.PlacedBlocks.Length>10000||data.PlacedBlocks.Any(s=>!PlacedBlockSave.Valid(s))||data.PlacedBlocks.Select(s=>(s.X,s.Y,s.Z)).Distinct().Count()!=data.PlacedBlocks.Length)return null;
            return data;
        }
        catch{return null;}
    }
    static bool ValidPickup(WoodPickupSave? p)
    {
        if(p==null||string.IsNullOrWhiteSpace(p.Id)||p.Id.Length>64||p.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))return false;
        if(!BlockCatalog.IsCollectible(p.Type)||p.Count<1||p.Count>64||p.Age<0||!float.IsFinite(p.Age))return false;
        if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||p.X<BlockTerrain.WorldMin-1||p.X>BlockTerrain.WorldMaxExclusive+1||p.Z<BlockTerrain.WorldMin-1||p.Z>BlockTerrain.WorldMaxExclusive+1||p.Y< -16||p.Y>BlockTerrain.MaxBuildY+16)return false;
        return new[]{p.Vx,p.Vy,p.Vz}.All(v=>float.IsFinite(v)&&Math.Abs(v)<=100)&&new[]{p.Rx,p.Ry,p.Rz}.All(v=>float.IsFinite(v)&&Math.Abs(v)<=100000);
    }
    public static void LoadSettings()
    {
        if(IsQa)return;
        try{if(File.Exists(SettingsPath))Settings=JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(SettingsPath))??new();}catch{Settings=new();}
        Settings.MouseSensitivity=Math.Clamp(Settings.MouseSensitivity,.001f,.012f);Settings.Fov=Math.Clamp(Settings.Fov,55,100);Settings.TouchMode=Math.Clamp(Settings.TouchMode,0,2);
    }
    public static void SaveSettings(){if(!IsQa)WriteAtomic(SettingsPath,Settings);}
}
