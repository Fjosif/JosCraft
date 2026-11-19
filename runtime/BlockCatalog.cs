using Godot;
using System;
using System.Collections.Generic;

/// <summary>JOSCRAFT-owned block identities; no external game assets.</summary>
public enum BlockType
{
    Air = 0, Grass = 1, Dirt = 2, Stone = 3,
    OakLog = 4, BirchLog = 5, SpruceLog = 6,
    OakLeaves = 7, BirchLeaves = 8, SpruceLeaves = 9,
    OakPlanks=10, BirchPlanks=11, SprucePlanks=12, CraftingTable=13,
    Cobblestone=14, Clay=15, Bricks=16, Furnace=17, CoalOre=18,
    Coal=19, ClayBall=20, Brick=21, Stick=22, WoodenPickaxe=23, StonePickaxe=24,
    WoodenAxe=25, StoneAxe=26, WoodenSword=27, StoneSword=28, Bone=29, Carrot=30, Berries=31, Torch=32, Charcoal=33, OakSlab=34, OakStairs=35, OakDoor=36, OakFence=37, Chest=38, DoorUpper=39
}

public static partial class BlockCatalog
{
    public const string GrassTopTexture = "res://assets/textures/grass-top-v1.png";
    public const string GrassSideTexture = "res://assets/textures/grass-side-v1.png";
    public const string DirtTexture = "res://assets/textures/dirt-v1.png";
    public const string StoneTexture = "res://assets/textures/stone-v1.png";
    public const int SurfaceCount = 23;
    public const string OakBarkTexture = "res://assets/textures/oak-bark-v1.png";
    public const string BirchBarkTexture = "res://assets/textures/birch-bark-v1.png";
    public const string SpruceBarkTexture = "res://assets/textures/spruce-bark-v1.png";
    public const string LogEndTexture = "res://assets/textures/log-end-v1.png";
    public const string OakLeavesTexture = "res://assets/textures/oak-leaves-v1.png";
    public const string BirchLeavesTexture = "res://assets/textures/birch-leaves-v1.png";
    public const string SpruceLeavesTexture = "res://assets/textures/spruce-leaves-v1.png";

    public sealed record Definition(BlockType Id, string Name, string IconPath,
        string TopTexture, string SideTexture, string BottomTexture);

    static readonly Definition air = new(BlockType.Air, "Vzduch", "", "", "", "");
    static readonly Definition grass = new(BlockType.Grass, "Tráva", GrassTopTexture,
        GrassTopTexture, GrassSideTexture, DirtTexture);
    static readonly Definition dirt = new(BlockType.Dirt, "Hlína", DirtTexture,
        DirtTexture, DirtTexture, DirtTexture);
    static readonly Definition stone = new(BlockType.Stone, "Kámen", StoneTexture,
        StoneTexture, StoneTexture, StoneTexture);
    static readonly Definition oakLog = new(BlockType.OakLog, "Dubové dřevo", OakBarkTexture, LogEndTexture, OakBarkTexture, LogEndTexture);
    static readonly Definition birchLog = new(BlockType.BirchLog, "Březové dřevo", BirchBarkTexture, LogEndTexture, BirchBarkTexture, LogEndTexture);
    static readonly Definition spruceLog = new(BlockType.SpruceLog, "Smrkové dřevo", SpruceBarkTexture, LogEndTexture, SpruceBarkTexture, LogEndTexture);
    static readonly Definition oakLeaves = new(BlockType.OakLeaves, "Dubové listí", OakLeavesTexture, OakLeavesTexture, OakLeavesTexture, OakLeavesTexture);
    static readonly Definition birchLeaves = new(BlockType.BirchLeaves, "Březové listí", BirchLeavesTexture, BirchLeavesTexture, BirchLeavesTexture, BirchLeavesTexture);
    static readonly Definition spruceLeaves = new(BlockType.SpruceLeaves, "Smrkové jehličí", SpruceLeavesTexture, SpruceLeavesTexture, SpruceLeavesTexture, SpruceLeavesTexture);

    public static IReadOnlyList<Definition> Placeable { get; } = new[] { grass, dirt, stone, oakLog, birchLog, spruceLog };
    public static IReadOnlyList<Definition> All { get; } = new[] { air, grass, dirt, stone, oakLog, birchLog, spruceLog, oakLeaves, birchLeaves, spruceLeaves };
    public static bool IsValid(BlockType type) => type is >= BlockType.Air and <= BlockType.DoorUpper;
    public static bool IsBlock(BlockType type) => type is >= BlockType.Air and <= BlockType.CoalOre || type==BlockType.Torch || IsShaped(type);
    public static bool IsFullCube(BlockType type)=>IsBlock(type)&&type!=BlockType.Air&&type!=BlockType.Torch&&!IsShaped(type);
    public static bool IsShaped(BlockType t)=>t is >=BlockType.OakSlab and <=BlockType.DoorUpper;
    public static bool IsShaped(int t)=>IsShaped((BlockType)t);
    public static bool IsLog(BlockType type) => type is >= BlockType.OakLog and <= BlockType.SpruceLog;
    public static bool IsLeaves(BlockType type) => type is >= BlockType.OakLeaves and <= BlockType.SpruceLeaves;
    public static bool IsLog(int type) => IsLog((BlockType)type);
    public static bool IsLeaves(int type) => IsLeaves((BlockType)type);
    public static bool IsPlaceable(BlockType type) => type is >= BlockType.Grass and <= BlockType.SpruceLog || type is >= BlockType.OakPlanks and <= BlockType.CoalOre || type==BlockType.Torch || IsShaped(type)&&type!=BlockType.DoorUpper;
    public static bool IsPlaceable(int type) => IsPlaceable((BlockType)type);
    public static bool IsCollectible(int type) => IsValid((BlockType)type)&&type>0&&type!=(int)BlockType.DoorUpper&&!IsLeaves(type);
    public static bool IsValidDrop(int type) => IsCollectible(type);
    public static BlockType MiningDrop(BlockType type) => type switch {BlockType.Grass=>BlockType.Dirt,BlockType.Stone=>BlockType.Cobblestone,BlockType.Clay=>BlockType.ClayBall,BlockType.CoalOre=>BlockType.Coal,_=>IsLeaves(type)||type==BlockType.Air?BlockType.Air:type};
    public static int DropFor(int type) => (int)MiningDrop((BlockType)type);
    public static float MiningSeconds(int type) => MiningSeconds((BlockType)type);
    public static float MiningSeconds(BlockType type) => IsLog(type) ? 3f : IsLeaves(type) ? .3f : type == BlockType.Stone ? 1f : type == BlockType.Dirt ? .45f : .35f;
    public static Definition Get(int type) => Get((BlockType)type);
    public static Definition Get(BlockType type) => Extra.TryGetValue(type,out var extra)?extra:type switch
    {
        BlockType.Grass => grass, BlockType.Dirt => dirt, BlockType.Stone => stone,
        BlockType.OakLog => oakLog, BlockType.BirchLog => birchLog, BlockType.SpruceLog => spruceLog,
        BlockType.OakLeaves => oakLeaves, BlockType.BirchLeaves => birchLeaves, BlockType.SpruceLeaves => spruceLeaves, _ => air
    };

    /// <summary>Original four surfaces, three bark, shared end grain, three foliage.</summary>
    public static int SurfaceFor(BlockType block, Vector3 normal) => block>=BlockType.OakPlanks&&block<=BlockType.CoalOre ? ExtraSurface(block,normal) : block switch
    {
        BlockType.Grass => normal.Y > .5f ? 0 : normal.Y < -.5f ? 2 : 1,
        BlockType.Dirt => 2,
        BlockType.OakLog => Math.Abs(normal.Y) > .5f ? 7 : 4,
        BlockType.BirchLog => Math.Abs(normal.Y) > .5f ? 7 : 5,
        BlockType.SpruceLog => Math.Abs(normal.Y) > .5f ? 7 : 6,
        BlockType.OakLeaves => 8, BlockType.BirchLeaves => 9, BlockType.SpruceLeaves => 10,
        _ => 3
    };
    public static string SurfaceTexture(int surface) => surface>=11?ExtraTextures[surface-11]:surface switch
    {
        0 => GrassTopTexture, 1 => GrassSideTexture, 2 => DirtTexture, 3 => StoneTexture,
        4 => OakBarkTexture, 5 => BirchBarkTexture, 6 => SpruceBarkTexture, 7 => LogEndTexture,
        8 => OakLeavesTexture, 9 => BirchLeavesTexture, _ => SpruceLeavesTexture
    };
}
