// JOSCRAFT multiplayer – authoritative per-player inventory (Godot-independent).
// Mirrors the current BlockWorld/SurvivalWorld inventory rules 1:1 so a remote player
// behaves exactly like the local one. RECOMMENDATION for Codex: later make BlockWorld
// use this same class for the local player so the two rule sets cannot drift apart.
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Furnace slots seen through an open station: 0 input, 1 fuel, 2 output.</summary>
public interface IFurnaceStacks
{
    ItemStack Get(int index);
    void Set(int index, ItemStack stack);
}

public sealed class InventoryModel
{
    public const int SlotCount = 36, CraftSlots = 9, StackLimit = 64;
    public readonly ItemStack[] Slots = Enumerable.Range(0, SlotCount).Select(_ => new ItemStack()).ToArray();
    public readonly ItemStack[] Crafting = Enumerable.Range(0, CraftSlots).Select(_ => new ItemStack()).ToArray();
    public ItemStack Carried { get; private set; } = new();
    public void RestoreCarried(ItemStack stack){Carried=stack;Touch();}
    public int Selected { get; private set; }
    public int CraftSize { get; private set; } = 2;
    public bool IsOpen { get; private set; }
    /// <summary>Open furnace, if any. Set by the server only after validating the block and reach.</summary>
    public IFurnaceStacks? Furnace { get; private set; }
    public PlacedBlockSave? Chest {get;private set;}
    /// <summary>Incremented on every mutation; the server sends an update when it changes.</summary>
    public int Version { get; private set; }

    void Touch() => Version++;
    static bool ValidStack(ItemStack s) => s != null && s.Count >= 0 && s.Count <= StackLimit && (s.Count == 0) == (s.Type == 0) && BlockCatalog.IsValid((BlockType)s.Type);

    public void Load(IReadOnlyList<ItemStack> slots, int selected, IReadOnlyList<ItemStack>? crafting = null)
    {
        for (int i = 0; i < SlotCount; i++) Slots[i] = i < slots.Count && ValidStack(slots[i]) ? slots[i] : new();
        for (int i = 0; i < CraftSlots; i++) Crafting[i] = crafting != null && i < crafting.Count && ValidStack(crafting[i]) ? crafting[i] : new();
        Carried = new(); Selected = Math.Clamp(selected, 0, 8); CraftSize = 2; IsOpen = false; Furnace = null; Chest=null; Touch();
    }

    public int Count(int type) => Slots.Where(s => s.Type == type).Sum(s => s.Count) + Crafting.Where(s => s.Type == type).Sum(s => s.Count) + (Carried.Type == type ? Carried.Count : 0);
    public int Capacity(int type) => Slots.Sum(s => s.Count == 0 ? StackLimit : s.Type == type ? StackLimit - s.Count : 0);
    public ItemStack SelectedStack => Slots[Selected];

    public bool SelectHotbar(int index) { if (index < 0 || index > 8) return false; Selected = index; Touch(); return true; }

    public bool AddResource(int type, int count)
    {
        if (count < 0 || type <= 0 || !BlockCatalog.IsValid((BlockType)type) || Capacity(type) < count) return false;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < SlotCount && count > 0; i++)
            {
                var s = Slots[i]; if (pass == 0 ? s.Type != type || s.Count >= StackLimit : s.Count != 0) continue;
                int add = Math.Min(count, StackLimit - s.Count); Slots[i] = new(type, s.Count + add); count -= add;
            }
        Touch(); return count == 0;
    }

    /// <summary>Consumes exactly one item of <paramref name="type"/> from the selected hotbar slot.</summary>
    public bool ConsumeSelected(int type)
    {
        var s = Slots[Selected]; if (s.Type != type || s.Count < 1) return false;
        Slots[Selected] = s.Count == 1 ? new() : new(type, s.Count - 1); Touch(); return true;
    }

    /// <summary>Returns crafting grid and carried stack to the backpack; false if something could not fit (it stays where it was).</summary>
    public bool ReturnStaging()
    {
        for (int i = 0; i < CraftSlots; i++) if (Crafting[i].Count > 0 && AddResource(Crafting[i].Type, Crafting[i].Count)) Crafting[i] = new();
        if (Carried.Count > 0 && AddResource(Carried.Type, Carried.Count)) Carried = new();
        Touch();
        return Carried.Count == 0 && Crafting.All(s => s.Count == 0);
    }

    public void Open(int craftSize, IFurnaceStacks? furnace, PlacedBlockSave? chest=null)
    {
        CraftSize = craftSize == 3 ? 3 : 2; Furnace = furnace; Chest=chest; IsOpen = true; Touch();
    }
    /// <summary>Closing always succeeds; leftovers that do not fit stay in the grid (never deleted).</summary>
    public bool Close()
    {
        bool clean = ReturnStaging();
        // A carried stack that did not fit goes into a free crafting cell so it is never lost.
        if (Carried.Count > 0)
            for (int i = 0; i < CraftSlots; i++) if (Crafting[i].Count == 0) { Crafting[i] = Carried; Carried = new(); break; }
        IsOpen = false; Furnace = null; Chest=null; CraftSize = 2; Touch();
        return clean;
    }

    // ---- UI operations (area 0 = backpack, 1 = crafting grid, 2 = furnace) ----
    public bool ValidSlot(int area, int index) => area switch
    {
        0 => index >= 0 && index < SlotCount,
        1 => Chest==null&&index >= 0 && index < CraftSize * CraftSize,
        3 => Chest!=null&&index>=0&&index<27,
        2 => Furnace != null && index >= 0 && index < 3,
        _ => false
    };
    public ItemStack Get(int area, int index) => !ValidSlot(area, index) ? new() : area == 0 ? Slots[index] : area == 1 ? Crafting[index] : area==3?Chest!.Slots[index]:Furnace!.Get(index);
    void Set(int area, int index, ItemStack s)
    {
        if (area == 0) Slots[index] = s; else if (area == 1) Crafting[index] = s; else if(area==3)Chest!.Slots[index]=s;else Furnace!.Set(index, s);
    }
    /// <summary>Same as SurvivalWorld.AcceptsSlot: furnace input = anything RecipeCatalog can smelt; fuel = coal/charcoal/log/plank.</summary>
    public bool Accepts(int area, int index, int type) => area != 2 || index == 0 && RecipeCatalog.Smelt(type) != null || index == 1 && (type is 19 or 33 || BlockCatalog.IsLog(type) || BlockCatalog.IsPlank(type));

    /// <summary>Same semantics as BlockWorld.UiSlotClick. Returns false when nothing changed or input is invalid.</summary>
    public bool Click(int area, int index, bool right)
    {
        if (!IsOpen || !ValidSlot(area, index)) return false;
        var s = Get(area, index);
        if (area == 2 && index == 2)
        {
            if (s.Count == 0 || Carried.Count > 0 && Carried.Type != s.Type || Carried.Count + s.Count > StackLimit) return false;
            Carried = new(s.Type, Carried.Count + s.Count); Set(area, index, new());
        }
        else if (Carried.Count == 0)
        {
            if (s.Count == 0) return false;
            int count = right ? (s.Count + 1) / 2 : s.Count; Carried = new(s.Type, count); Set(area, index, s.Count == count ? new() : new(s.Type, s.Count - count));
        }
        else if (Accepts(area, index, Carried.Type))
        {
            if (s.Count == 0 || s.Type == Carried.Type)
            {
                int count = Math.Min(right ? 1 : Carried.Count, StackLimit - s.Count); if (count <= 0) return false;
                Set(area, index, new(Carried.Type, s.Count + count)); Carried = count == Carried.Count ? new() : new(Carried.Type, Carried.Count - count);
            }
            else if (!right) { Set(area, index, Carried); Carried = s; }
            else return false;
        }
        else return false;
        Touch(); return true;
    }

    /// <summary>Same allocation as BlockWorld.DragAllocation/CommitDrag.</summary>
    public bool Drag(IReadOnlyList<(int Area, int Slot)> targets, bool single)
    {
        if (!IsOpen || Carried.Count == 0 || targets.Count == 0 || targets.Count > 72) return false;
        if (targets.Any(t => !ValidSlot(t.Area, t.Slot))) return false;
        var valid = targets.Distinct().Where(t => Accepts(t.Area, t.Slot, Carried.Type)).Where(t => { var s = Get(t.Area, t.Slot); return s.Count < StackLimit && (s.Count == 0 || s.Type == Carried.Type); }).ToArray();
        var result = valid.ToDictionary(t => t, _ => 0); int left = Carried.Count;
        if (single) { foreach (var t in valid) if (left > 0) { result[t] = 1; left--; } }
        else while (left > 0)
        {
            bool changed = false;
            for (int i = valid.Length - 1; i >= 0 && left > 0; i--) { var t = valid[i]; if (Get(t.Area, t.Slot).Count + result[t] >= StackLimit) continue; result[t]++; left--; changed = true; }
            if (!changed) break;
        }
        int total = result.Values.Sum(); if (total == 0) return false;
        foreach (var p in result) if (p.Value > 0) { var s = Get(p.Key.Area, p.Key.Slot); Set(p.Key.Area, p.Key.Slot, new(Carried.Type, s.Count + p.Value)); }
        Carried = total == Carried.Count ? new() : new(Carried.Type, Carried.Count - total);
        Touch(); return true;
    }

    /// <summary>Same as BlockWorld.TransferSlot (used by middle-click pick and hotbar moves).</summary>
    public bool Move(int from, int to)
    {
        if (from < 0 || from >= SlotCount || to < 0 || to >= SlotCount || from == to) return false;
        var a = Slots[from]; var b = Slots[to]; if (a.Count == 0) return false;
        if (a.Type == b.Type && b.Count < StackLimit) { int move = Math.Min(a.Count, StackLimit - b.Count); Slots[to] = new(a.Type, b.Count + move); Slots[from] = a.Count == move ? new() : new(a.Type, a.Count - move); }
        else { Slots[to] = a; Slots[from] = b; }
        Touch(); return true;
    }

    /// <summary>Same as SurvivalWorld.RecipeOptions/CurrentRecipe/ChooseRecipe (several recipes can match one grid).</summary>
    public int RecipeChoice { get; private set; }
    public RecipeMatch[] RecipeOptions => Furnace != null||Chest!=null ? Array.Empty<RecipeMatch>() : global::Crafting.Matches(Crafting.Take(CraftSize * CraftSize).ToArray(), CraftSize);
    public RecipeMatch? CurrentRecipe { get { var m = RecipeOptions; return m.Length == 0 ? null : m[RecipeChoice % m.Length]; } }
    public void ChooseRecipe(int choice) { RecipeChoice = Math.Max(0, choice); Touch(); }

    /// <summary>Same as SurvivalWorld.CraftOnce: consumes one of each used input, output goes to the carried stack.</summary>
    public bool CraftOnce()
    {
        if (!IsOpen || CurrentRecipe is not { } m) return false;
        if (Carried.Count > 0 && Carried.Type != m.Recipe.Output || Carried.Count + m.Recipe.Count > StackLimit) return false;
        foreach (int i in m.Slots) { var s = Crafting[i]; Crafting[i] = s.Count == 1 ? new() : new(s.Type, s.Count - 1); }
        Carried = new(m.Recipe.Output, Carried.Count + m.Recipe.Count); Touch(); return true;
    }

    /// <summary>Snapshot for saving: staging is folded back first; if it cannot fit, it is kept in the crafting array.</summary>
    public (ItemStack[] Slots, ItemStack[] Crafting) ExportForSave()
    {
        // Saving is a read-only snapshot: never steal the cursor while the player is dragging.
        // Carried is persisted separately by NetServer.Export, including a full backpack.
        return (Slots.ToArray(), Crafting.ToArray());
    }

    public int TotalItems() => Slots.Sum(s => s.Count) + Crafting.Sum(s => s.Count) + Carried.Count;
    public bool FillRecipe(int recipeIndex)
    {
        if(!IsOpen||Furnace!=null||Chest!=null||recipeIndex<0||recipeIndex>=global::Crafting.Recipes.Length)return false;
        var recipe=global::Crafting.Recipes[recipeIndex];if(recipe.Width>CraftSize||recipe.Height>CraftSize||recipe.Shapeless&&recipe.Pattern.Length>CraftSize*CraftSize)return false;
        var plan=new InventoryModel();plan.Load(Slots,Selected,Crafting);plan.RestoreCarried(Carried);if(!plan.ReturnStaging())return false;
        var placed=Enumerable.Range(0,9).Select(_=>new ItemStack()).ToArray();
        for(int i=0;i<recipe.Pattern.Length;i++)
        {
            int expected=recipe.Pattern[i];if(expected==0)continue;
            int source=Array.FindIndex(plan.Slots,s=>s.Count>0&&RecipeCatalog.IngredientMatches(expected,s.Type));if(source<0)return false;
            var stack=plan.Slots[source];int target=recipe.Shapeless?i:(i/recipe.Width)*CraftSize+i%recipe.Width;placed[target]=new(stack.Type,1);plan.Slots[source]=stack.Count==1?new():new(stack.Type,stack.Count-1);
        }
        Array.Copy(plan.Slots,Slots,SlotCount);Array.Copy(placed,Crafting,CraftSlots);Carried=new();
        var choices=RecipeOptions;RecipeChoice=Array.FindIndex(choices,m=>RecipeCatalog.Signature(m.Recipe)==RecipeCatalog.Signature(recipe));if(RecipeChoice<0)RecipeChoice=0;Touch();return true;
    }
}
