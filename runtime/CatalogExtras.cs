using Godot;
using System.Collections.Generic;

public static partial class BlockCatalog
{
    public static readonly string[] ExtraTextures={"res://assets/textures/oak-planks-v1.png","res://assets/textures/birch-planks-v1.png","res://assets/textures/spruce-planks-v1.png","res://assets/textures/bricks-v1.png","res://assets/textures/clay-v1.png","res://assets/textures/cobblestone-v1.png","res://assets/textures/table-top-v1.png","res://assets/textures/table-side-v1.png","res://assets/textures/coal-ore-v1.png","res://assets/textures/furnace-front-v1.png","res://assets/textures/furnace-top-v1.png","res://assets/textures/furnace-side-v1.png"};
    static Definition D(BlockType t,string n,int texture)=>new(t,n,ExtraTextures[texture],ExtraTextures[texture],ExtraTextures[texture],ExtraTextures[texture]);
    static Definition I(BlockType t,string n,string icon)=>new(t,n,"res://assets/icons/"+icon+".svg","","","");
    static readonly Dictionary<BlockType,Definition> Extra=new()
    {
        [BlockType.OakPlanks]=D(BlockType.OakPlanks,"Dubová prkna",0),[BlockType.BirchPlanks]=D(BlockType.BirchPlanks,"Březová prkna",1),[BlockType.SprucePlanks]=D(BlockType.SprucePlanks,"Smrková prkna",2),
        [BlockType.CraftingTable]=new(BlockType.CraftingTable,"Pracovní stůl",ExtraTextures[6],ExtraTextures[6],ExtraTextures[7],ExtraTextures[0]),
        [BlockType.Cobblestone]=D(BlockType.Cobblestone,"Dlažební kámen",5),[BlockType.Clay]=D(BlockType.Clay,"Jíl",4),[BlockType.Bricks]=D(BlockType.Bricks,"Cihly",3),[BlockType.Furnace]=new(BlockType.Furnace,"Pec",ExtraTextures[9],ExtraTextures[10],ExtraTextures[11],ExtraTextures[11]),[BlockType.CoalOre]=D(BlockType.CoalOre,"Uhelná ruda",8),
        [BlockType.OakSlab]=I(BlockType.OakSlab,"Dubová deska","oak-slab"),[BlockType.OakStairs]=I(BlockType.OakStairs,"Dubové schody","oak-stairs"),[BlockType.OakDoor]=I(BlockType.OakDoor,"Dubové dveře","oak-door"),[BlockType.OakFence]=I(BlockType.OakFence,"Dubový plot","oak-fence"),[BlockType.Chest]=I(BlockType.Chest,"Truhla","wooden-chest"),
        [BlockType.Coal]=I(BlockType.Coal,"Uhlí","coal"),[BlockType.ClayBall]=I(BlockType.ClayBall,"Hrouda jílu","clay"),[BlockType.Brick]=I(BlockType.Brick,"Vypálená cihla","brick"),[BlockType.Stick]=I(BlockType.Stick,"Klacík","stick"),
        [BlockType.WoodenPickaxe]=I(BlockType.WoodenPickaxe,"Dřevěný krumpáč","wood-pickaxe"),[BlockType.StonePickaxe]=I(BlockType.StonePickaxe,"Kamenný krumpáč","stone-pickaxe"),[BlockType.WoodenAxe]=I(BlockType.WoodenAxe,"Dřevěná sekera","wood-axe"),[BlockType.StoneAxe]=I(BlockType.StoneAxe,"Kamenná sekera","stone-axe"),[BlockType.WoodenSword]=I(BlockType.WoodenSword,"Dřevěný meč","wood-sword"),[BlockType.StoneSword]=I(BlockType.StoneSword,"Kamenný meč","stone-sword"),
        [BlockType.Bone]=I(BlockType.Bone,"Kost pro psa","bone"),[BlockType.Carrot]=I(BlockType.Carrot,"Mrkev pro zajíce","carrot"),[BlockType.Berries]=I(BlockType.Berries,"Bobule pro lišku","berries"),
        [BlockType.Torch]=new(BlockType.Torch,"Pochodeň","res://assets/icons/torch.svg",LogEndTexture,OakBarkTexture,LogEndTexture),[BlockType.Charcoal]=I(BlockType.Charcoal,"Dřevěné uhlí","coal")
    };
    static int ExtraSurface(BlockType t,Vector3 n)=>t switch {BlockType.OakPlanks=>11,BlockType.BirchPlanks=>12,BlockType.SprucePlanks=>13,BlockType.CraftingTable=>n.Y>.5f?17:n.Y<-.5f?11:18,BlockType.Bricks=>14,BlockType.Clay=>15,BlockType.CoalOre=>19,BlockType.Furnace=>n.Z<-.5f?20:n.Y>.5f?21:22,_=>16};
    public static bool IsTool(int t)=>t>=23&&t<=28;
    public static bool IsPlank(int t)=>t>=10&&t<=12;
    public static bool IsPickaxe(int t)=>t==23||t==24;
    public static float MiningDuration(int target,int tool)
    {
        float time=MiningSeconds(target);
        if(IsLog(target)||target>=10&&target<=13||BlockCatalog.IsShaped(target))time*=tool==25?.45f:tool==26?.25f:1;
        if(target==3||target==14||target==17||target==18)time=tool==24?.3f:tool==23?.65f:3f;
        return time;
    }
}
