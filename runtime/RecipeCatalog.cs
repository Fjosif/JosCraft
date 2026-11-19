using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public sealed record CatalogEntry(string Id,string Kind,string File,JsonElement Data)
{
    public string OutputId{get{var first=Data.GetProperty("outputs").EnumerateArray().FirstOrDefault();return first.ValueKind==JsonValueKind.Object&&first.TryGetProperty("id",out var id)?id.GetString()??"":"";}}
    public string Name=>RecipeCatalog.Name(OutputId);
}
public static class RecipeCatalog
{
    static readonly Dictionary<string,int> ids=new(StringComparer.Ordinal){
        ["grass_block"]=1,["dirt"]=2,["stone"]=3,["oak_log"]=4,["birch_log"]=5,["spruce_log"]=6,["oak_leaves"]=7,["birch_leaves"]=8,["spruce_leaves"]=9,
        ["oak_planks"]=10,["birch_planks"]=11,["spruce_planks"]=12,["crafting_table"]=13,["cobblestone"]=14,["clay"]=15,["brick_block"]=16,["furnace"]=17,["coal_ore"]=18,
        ["coal"]=19,["clay_ball"]=20,["brick"]=21,["stick"]=22,["wooden_pickaxe"]=23,["stone_pickaxe"]=24,["wooden_axe"]=25,["stone_axe"]=26,["wooden_sword"]=27,["stone_sword"]=28,["bone"]=29,["carrot"]=30,["sweet_berries"]=31,["torch"]=32,["charcoal"]=33,["oak_slab"]=34,["oak_stairs"]=35,["wooden_door"]=36,["oak_door"]=36,["oak_fence"]=37,["chest"]=38
    };
    static JsonDocument? document;static CatalogEntry[]? entries;static Dictionary<int,ItemStack>? smelting;
    static readonly Dictionary<string,string> recipeKeys=new();
    public static string Signature(Recipe r)=>$"{r.Width}/{r.Height}/{r.Shapeless}/{string.Join(',',r.Pattern)}/{r.Output}/{r.Count}";
    public static Recipe? RuntimeRecipe(CatalogEntry e){_ = Crafting.Recipes;return recipeKeys.TryGetValue(e.File,out var key)?Crafting.Recipes.FirstOrDefault(r=>Signature(r)==key):null;}
    public static CatalogEntry[] Entries
    {
        get
        {
            if(entries!=null)return entries;
            document=JsonDocument.Parse(FileAccess.GetFileAsString("res://data/recipe-catalog.json"));
            entries=document.RootElement.GetProperty("recipes").EnumerateArray().Select(e=>new CatalogEntry(e.GetProperty("id").GetString()!,e.GetProperty("kind").GetString()!,e.GetProperty("file").GetString()!,e)).ToArray();return entries;
        }
    }
    public static int Type(string id)=>ids.TryGetValue(id.Replace("minecraft:",""),out int n)?n:0;
    public static string Name(string id)=>Type(id) is >0 and var t?BlockCatalog.Get(t).Name:id.Replace("minecraft:","").Replace('_',' ');
    static int Ingredient(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Array)
        {
            var all=e.EnumerateArray().Select(Ingredient).Distinct().ToArray();return all.Length==1?all[0]:0;
        }
        if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("id",out var id))return 0;
        string name=id.GetString()??"";
        if(e.TryGetProperty("tag",out var tag)&&tag.GetBoolean())return name switch{"minecraft:planks"=>-1,"minecraft:coals"=>-2,"minecraft:stone_crafting_materials" or "minecraft:stone_tool_materials"=>-3,_=>0};
        if(e.TryGetProperty("data",out var data)&&data.GetInt32()>0)return 0;
        return Type(name);
    }
    public static bool IngredientMatches(int expected,int type)=>expected switch{-1=>BlockCatalog.IsPlank(type),-2=>type is 19 or 33,-3=>type==14,_=>expected==type};
    public static Recipe[] Craftable()
    {
        var result=new List<Recipe>();
        foreach(var e in Entries.Where(e=>e.Kind is "shaped" or "shapeless"))
        {
            if(!e.Data.GetProperty("stations").EnumerateArray().Any(t=>t.GetString()=="crafting_table"))continue;
            var outputs=e.Data.GetProperty("outputs").EnumerateArray().ToArray();if(outputs.Length!=1)continue;
            int output=Ingredient(outputs[0]);if(output<=0)continue;int count=outputs[0].GetProperty("count").GetInt32();
            if(e.Kind=="shaped")
            {
                var rows=e.Data.GetProperty("pattern").EnumerateArray().Select(x=>x.GetString()!).ToArray();int width=rows.Max(r=>r.Length);var keys=e.Data.GetProperty("keys");var pattern=new List<int>();bool known=true;
                foreach(var row in rows)for(int x=0;x<width;x++)
                {
                    char c=x<row.Length?row[x]:' ';if(c==' '){pattern.Add(0);continue;}int input=Ingredient(keys.GetProperty(c.ToString()));if(input==0)known=false;pattern.Add(input);
                }
                if(known){var r=new Recipe(e.Name,width,rows.Length,pattern.ToArray(),output,count);result.Add(r);recipeKeys[e.File]=Signature(r);}
            }
            else
            {
                var inputs=new List<int>();bool known=true;
                foreach(var ingredient in e.Data.GetProperty("ingredients").EnumerateArray()){int input=Ingredient(ingredient);if(input==0){known=false;break;}int n=ingredient.TryGetProperty("count",out var c)?c.GetInt32():1;for(int i=0;i<n;i++)inputs.Add(input);}
                if(known&&inputs.Count<=9){var r=new Recipe(e.Name,1,1,inputs.ToArray(),output,count,true);result.Add(r);recipeKeys[e.File]=Signature(r);}
            }
        }
        return result.GroupBy(r=>$"{r.Width}/{r.Height}/{r.Shapeless}/{string.Join(',',r.Pattern)}/{r.Output}/{r.Count}").Select(g=>g.First()).ToArray();
    }
    public static ItemStack? Smelt(int input)
    {
        // This vanilla smelting rule is hardcoded outside the Bedrock sample JSON set.
        // Confirmed separately against Minecraft.net "Taking Inventory: Charcoal".
        if(BlockCatalog.IsLog(input))return new(33,1);
        if(smelting==null)
        {
            smelting=new();foreach(var e in Entries.Where(e=>e.Kind=="furnace"))
            {
                if(!e.Data.GetProperty("stations").EnumerateArray().Any(t=>t.GetString()=="furnace"))continue;
                int from=Ingredient(e.Data.GetProperty("input"));var output=e.Data.GetProperty("outputs").EnumerateArray().First();int to=Ingredient(output);
                if(from>0&&to>0)smelting[from]=new(to,output.GetProperty("count").GetInt32());
            }
        }
        return smelting.GetValueOrDefault(input);
    }
    public static bool Available(CatalogEntry entry)
    {
        if(Type(entry.OutputId)<=0)return false;
        if(entry.Kind=="furnace")return entry.Data.TryGetProperty("input",out var input)&&Ingredient(input)>0&&Smelt(Ingredient(input))?.Type==Type(entry.OutputId);
        if(entry.Kind is not ("shaped" or "shapeless")||!entry.Data.GetProperty("stations").EnumerateArray().Any(t=>t.GetString()=="crafting_table"))return false;
        return entry.Kind=="shaped"?entry.Data.GetProperty("keys").EnumerateObject().All(k=>Ingredient(k.Value)!=0):entry.Data.GetProperty("ingredients").EnumerateArray().All(i=>Ingredient(i)!=0);
    }
}
