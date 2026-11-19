using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public partial class BlockWorld
{
    public bool GiveResource(int type,int count)=>BlockCatalog.IsCollectible(type)&&AddResource(type,count);
    public void Notify(string message)=>ShowFeedback(message);
    async void StartSurvivalQa(){try{await new SurvivalQa(this).Run();}catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}}
    public int CraftSize {get;private set;}=2;
    public Vector3I? OpenStation {get;private set;}
    public bool IsFurnaceOpen=>OpenStation.HasValue&&Terrain.GetBlock(OpenStation.Value)==BlockType.Furnace;
    public readonly Dictionary<Vector3I,FurnaceSave> Furnaces=new();
    public int Health {get;private set;}=20;
    public float HurtCooldown;
    public Ecology Ecology {get;private set;}=null!;
    public int RecipeChoice{get;private set;}
    public RecipeMatch[] RecipeOptions=>Crafting.Matches(crafting.Take(CraftSize*CraftSize).ToArray(),CraftSize);
    public RecipeMatch? CurrentRecipe{get{var matches=RecipeOptions;return matches.Length==0?null:matches[RecipeChoice%matches.Length];}}
    public void ChooseRecipe(int choice){if(NetChooseRecipe(choice))return;RecipeChoice=Math.Max(0,choice);RefreshInventory();}
    public ItemStack CraftOutput=>CurrentRecipe is {} r?new(r.Recipe.Output,r.Recipe.Count):new();
    public void OpenCraftingTable(Vector3I cell)
    {
        if(!GameplayActive||Terrain.GetBlock(cell)!=BlockType.CraftingTable)return;
        if(!ReturnStaging())return;OpenStation=cell;CraftSize=3;SetMode(GameMode.Inventory);
    }
    public void OpenFurnace(Vector3I cell)
    {
        if(!GameplayActive||Terrain.GetBlock(cell)!=BlockType.Furnace)return;
        if(!ReturnStaging())return;OpenStation=cell;CraftSize=2;
        if(!Furnaces.ContainsKey(cell))Furnaces[cell]=new(){X=cell.X,Y=cell.Y,Z=cell.Z};
        SetMode(GameMode.Inventory);
    }
    public bool CraftOnce()
    {
        if(NetCraft())return true;
        if(UiMode!=GameMode.Inventory||IsChestOpen||IsFurnaceOpen||CurrentRecipe is not {} m)return false;
        if(CarriedStack.Count>0&&CarriedStack.Type!=m.Recipe.Output||CarriedStack.Count+m.Recipe.Count>64)return false;
        foreach(int i in m.Slots){var s=crafting[i];crafting[i]=s.Count==1?new():new(s.Type,s.Count-1);}
        CarriedStack=new(m.Recipe.Output,CarriedStack.Count+m.Recipe.Count);RefreshInventory();return true;
    }
    void RefreshInventory(){GameUi?.ClearRecipeGhost();GameUi?.RefreshHotbar();if(UiMode==GameMode.Inventory)GameUi?.ShowInventory();}
    InventoryModel RecipePlan(){var plan=new InventoryModel();plan.Load(inventory,SelectedHotbar,crafting);plan.RestoreCarried(CarriedStack);plan.Open(CraftSize,null);return plan;}
    public bool CanFillRecipe(Recipe recipe)=>!IsChestOpen&&!IsFurnaceOpen&&RecipePlan().FillRecipe(Array.IndexOf(Crafting.Recipes,recipe));
    public bool FillRecipe(Recipe recipe)
    {
        if(UiMode!=GameMode.Inventory||IsChestOpen||IsFurnaceOpen)return false;int index=Array.IndexOf(Crafting.Recipes,recipe);var plan=RecipePlan();if(!plan.FillRecipe(index))return false;
        if(IsNetClient){NetC?.FillRecipe(index);return true;}
        Array.Copy(plan.Slots,inventory,36);Array.Copy(plan.Crafting,crafting,9);CarriedStack=plan.Carried;RecipeChoice=plan.RecipeChoice;RefreshInventory();return true;
    }
    public ItemStack GetUiStack(int area,int index)=>area==0?GetSlot(index):area==1?GetCraftSlot(index):area==3?GetChestStack(index):GetFurnaceStack(index);
    public ItemStack GetFurnaceStack(int index)
    {
        if(OpenStation is not {} cell||!Furnaces.TryGetValue(cell,out var f))return new();
        return index==0?f.Input:index==1?f.Fuel:f.Output;
    }
    void SetUiStack(int area,int index,ItemStack stack)
    {
        if(area==3){SetChestStack(index,stack);return;}
        if(area==0)inventory[index]=stack;else if(area==1)crafting[index]=stack;
        else if(OpenStation is {} c&&Furnaces.TryGetValue(c,out var f)){if(index==0)f.Input=stack;else if(index==1)f.Fuel=stack;else f.Output=stack;}
    }
    bool ValidUiSlot(int area,int index)=>area switch{0=>index>=0&&index<36,1=>!IsChestOpen&&index>=0&&index<CraftSize*CraftSize,2=>IsFurnaceOpen&&index>=0&&index<3,3=>IsChestOpen&&index>=0&&index<27,_=>false};
    public bool AcceptsSlot(int area,int index,int type)=>ValidUiSlot(area,index)&&(area!=2||index==0&&RecipeCatalog.Smelt(type)!=null||index==1&&(type is 19 or 33||BlockCatalog.IsLog(type)||BlockCatalog.IsPlank(type)));
    public void UiSlotClick(int area,int index,bool right=false)
    {
        if(UiMode!=GameMode.Inventory||!ValidUiSlot(area,index))return;
        if(NetUiClick(area,index,right))return;
        var s=GetUiStack(area,index);
        if(area==2&&index==2)
        {
            if(s.Count==0||CarriedStack.Count>0&&CarriedStack.Type!=s.Type||CarriedStack.Count+s.Count>64)return;
            CarriedStack=new(s.Type,CarriedStack.Count+s.Count);SetUiStack(area,index,new());
        }
        else if(CarriedStack.Count==0)
        {
            int count=right?(s.Count+1)/2:s.Count;CarriedStack=count==0?new():new(s.Type,count);SetUiStack(area,index,s.Count==count?new():new(s.Type,s.Count-count));
        }
        else if(AcceptsSlot(area,index,CarriedStack.Type))
        {
            if(s.Count==0||s.Type==CarriedStack.Type)
            {
                int count=Math.Min(right?1:CarriedStack.Count,64-s.Count);SetUiStack(area,index,new(CarriedStack.Type,s.Count+count));CarriedStack=count==CarriedStack.Count?new():new(CarriedStack.Type,CarriedStack.Count-count);
            }
            else if(!right){SetUiStack(area,index,CarriedStack);CarriedStack=s;}
        }
        RefreshInventory();
    }
    // Allocation is previewed without mutating ownership. The entire operation commits on release.
    public Dictionary<(int Area,int Slot),int> DragAllocation(IEnumerable<(int Area,int Slot)> targets,bool single)
    {
        var valid=targets.Distinct().Where(t=>AcceptsSlot(t.Area,t.Slot,CarriedStack.Type)).Where(t=>{var s=GetUiStack(t.Area,t.Slot);return s.Count<64&&(s.Count==0||s.Type==CarriedStack.Type);}).ToArray();
        var result=valid.ToDictionary(t=>t,_=>0);int left=CarriedStack.Count;
        if(single){foreach(var t in valid)if(left>0){result[t]=1;left--;}}
        else while(left>0)
        {
            bool changed=false;for(int i=valid.Length-1;i>=0&&left>0;i--){var t=valid[i];if(GetUiStack(t.Area,t.Slot).Count+result[t]>=64)continue;result[t]++;left--;changed=true;}
            if(!changed)break;
        }
        return result;
    }
    public void CommitDrag(IEnumerable<(int Area,int Slot)> targets,bool single)
    {
        if(UiMode!=GameMode.Inventory)return;if(NetUiDrag(targets,single))return;var a=DragAllocation(targets,single);int total=a.Values.Sum();
        foreach(var p in a)if(p.Value>0){var s=GetUiStack(p.Key.Area,p.Key.Slot);SetUiStack(p.Key.Area,p.Key.Slot,new(CarriedStack.Type,s.Count+p.Value));}
        CarriedStack=total==CarriedStack.Count?new():new(CarriedStack.Type,CarriedStack.Count-total);RefreshInventory();
    }
    public bool ConsumeSelected(int type)
    {
        var s=inventory[SelectedHotbar];if(s.Type!=type||s.Count<1)return false;
        inventory[SelectedHotbar]=s.Count==1?new():new(type,s.Count-1);GameUi.RefreshHotbar();return true;
    }
    public void Damage(int amount)
    {
        if(!SimulationActive||HurtCooldown>0)return;Health=Math.Max(0,Health-amount);HurtCooldown=1;
        if(Health==0){Health=20;Respawn();ShowFeedback("Byl jsi poražen. Vrátil ses na start se svými zásobami.");}
    }
    void SurvivalTick(float delta)
    {
        HurtCooldown=Math.Max(0,HurtCooldown-delta);
        if(IsNetClient)return;
        foreach(var pair in Furnaces.ToArray())
        {
            var f=pair.Value;if(Terrain.GetBlock(pair.Key)!=BlockType.Furnace)continue;
            var output=RecipeCatalog.Smelt(f.Input.Type);
            bool can=output!=null&&f.Input.Count>0&&(f.Output.Count==0||f.Output.Type==output.Type&&f.Output.Count+output.Count<=64);
            if(can&&f.Burn<=0&&f.Fuel.Count>0)
            {
                f.Burn=f.Fuel.Type is 19 or 33?80:BlockCatalog.IsLog(f.Fuel.Type)||BlockCatalog.IsPlank(f.Fuel.Type)?15:0;
                if(f.Burn>0)f.Fuel=f.Fuel.Count==1?new():new(f.Fuel.Type,f.Fuel.Count-1);
            }
            if(can&&f.Burn>0){f.Progress+=Math.Min(delta,f.Burn);if(f.Progress>=10){f.Progress-=10;f.Input=f.Input.Count==1?new():new(f.Input.Type,f.Input.Count-1);f.Output=new(output!.Type,f.Output.Count+output.Count);if(IsFurnaceOpen&&OpenStation==pair.Key)RefreshInventory();}}
            else if(!can)f.Progress=0;
            f.Burn=Math.Max(0,f.Burn-delta);
        }
    }
    bool UseTarget()
    {
        if(IsNetClient)return NetClientUse();
        if(Ecology.TryInteract())return true;
        var t=RayTarget();if(t!=null&&UseShaped(t.Cell))return true;if(t?.Type==13){OpenCraftingTable(t.Cell);return true;}if(t?.Type==17){OpenFurnace(t.Cell);return true;}return false;
    }
    public void UseSelected(){if(GameplayActive&&!UseTarget())PlaceTarget();}
}
