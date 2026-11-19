using System;
using System.Collections.Generic;
using System.Linq;

public sealed record Recipe(string Name,int Width,int Height,int[] Pattern,int Output,int Count,bool Shapeless=false);
public sealed record RecipeMatch(Recipe Recipe,int[] Slots);
public static class Crafting
{
    // Base recipes come from the pinned Mojang data catalogue; custom recipes are explicitly named.
    public static readonly Recipe[] Recipes=RecipeCatalog.Craftable().Concat(new[]{
        new Recipe("Klacíky z kmene · JOSCRAFT",1,1,new[]{4},22,4),new Recipe("Klacíky z kmene · JOSCRAFT",1,1,new[]{5},22,4),new Recipe("Klacíky z kmene · JOSCRAFT",1,1,new[]{6},22,4)
    }).ToArray();
    public static RecipeMatch? Match(ItemStack[] grid,int size)
        =>Matches(grid,size).FirstOrDefault();
    public static RecipeMatch[] Matches(ItemStack[] grid,int size)
    {
        var matches=new List<RecipeMatch>();
        foreach(var r in Recipes)
        {
        if(r.Shapeless)
        {
            var occupied=Enumerable.Range(0,grid.Length).Where(i=>grid[i].Count>0).ToList();var used=new List<int>();bool ok=occupied.Count==r.Pattern.Length;
            foreach(int expected in r.Pattern){int slot=occupied.FindIndex(i=>RecipeCatalog.IngredientMatches(expected,grid[i].Type));if(slot<0){ok=false;break;}used.Add(occupied[slot]);occupied.RemoveAt(slot);}
            if(ok)matches.Add(new(r,used.ToArray()));continue;
        }
        for(int oy=0;oy<=size-r.Height;oy++)for(int ox=0;ox<=size-r.Width;ox++)for(int mirror=0;mirror<2;mirror++)
        {
            var used=new List<int>();bool ok=true;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                int px=x-ox,py=y-oy,expected=px>=0&&px<r.Width&&py>=0&&py<r.Height?r.Pattern[py*r.Width+(mirror==1?r.Width-px-1:px)]:0;
                var s=grid[y*size+x];if(expected==0){if(s.Count>0)ok=false;}
                else if(s.Count==0||!RecipeCatalog.IngredientMatches(expected,s.Type))ok=false;
                else used.Add(y*size+x);
            }
            if(ok&&!matches.Any(m=>m.Recipe==r))matches.Add(new(r,used.ToArray()));
        }
        }
        return matches.ToArray();
    }
}

public sealed class FurnaceSave
{
    public int X{get;set;} public int Y{get;set;} public int Z{get;set;}
    public ItemStack Input{get;set;}=new(); public ItemStack Fuel{get;set;}=new(); public ItemStack Output{get;set;}=new();
    public float Burn{get;set;} public float Progress{get;set;}
}
