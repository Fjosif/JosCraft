using Godot;
using System;
using System.Linq;
using System.Text.Json;

/// <summary>Measured original animation envelopes, shared by local and network avatars.</summary>
public static class HeroSizing
{
    static readonly JsonDocument data=JsonDocument.Parse(FileAccess.GetFileAsString("res://data/hero-gameplay-sizes.json"));
    static JsonElement Hero(int i)=>data.RootElement.GetProperty("heroes").EnumerateArray().First(h=>h.GetProperty("id").GetString()==Viewer.Ids[Math.Clamp(i,0,Viewer.Ids.Length-1)]);
    public static Vector3 Scale(int i)=>Vector3.One*Hero(i).GetProperty("scale").GetSingle();
    public static float Height(int i)=>Hero(i).GetProperty("height").GetSingle()*Scale(i).Y;
}
