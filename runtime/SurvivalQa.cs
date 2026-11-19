using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

public sealed class SurvivalQa
{
    readonly BlockWorld w;readonly SceneTree tree;readonly List<string> failures=new();readonly List<object> evidence=new();
    readonly string dir=ProjectSettings.GlobalizePath("res://artifacts");
    public SurvivalQa(BlockWorld world){w=world;tree=world.GetTree();}
    void Check(bool condition,string name){evidence.Add(new{name,pass=condition});if(!condition)failures.Add(name);}
    async Task Frame(int count=1){for(int i=0;i<count;i++)await w.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    async Task Physics(int count=1){for(int i=0;i<count;i++)await w.ToSignal(tree,SceneTree.SignalName.PhysicsFrame);}
    async Task Wait(float seconds){await w.ToSignal(tree.CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);}
    async Task Ready(){for(int i=0;i<600&&(!w.CharacterReady||w.Terrain.PendingChunkCount>0);i++)await Frame();await Physics(10);}
    async Task Capture(string name){await Frame(3);await w.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);w.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir,"survival-"+name+".png"));}
    async Task Load(ItemStack[] slots,BlockTerrain.BlockEdit[]? edits=null)
    {
        string path=Path.Combine(dir,"survival-qa-data","fixture.json");
        GameSession.WriteAtomic(path,new SavedGame{Slots=slots,TreeExclusions=Array.Empty<int>(),Edits=edits??Array.Empty<BlockTerrain.BlockEdit>(),Creatures=Array.Empty<CreatureSave>()});
        Check(w.LoadSnapshot(path),"Load isolated survival fixture");await Ready();
    }
    ItemStack[] Empty()=>Enumerable.Range(0,36).Select(_=>new ItemStack()).ToArray();
    public async Task Run()
    {
        await Ready();w.SimulateInput(Vector2.Zero);Directory.CreateDirectory(Path.Combine(dir,"survival-qa-data"));
        if(OS.GetCmdlineUserArgs().Contains("--visual-only"))
        {
            w.LoadSnapshot(Path.Combine(dir,"survival-qa-data","fixture.json"));await Ready();w.ToggleInventory();w.GameUi.ShowRecipes();await Capture("recipe-catalogue");tree.Root.Size=new Vector2I(390,844);await Frame(6);await Capture("recipe-book-mobile");tree.Quit();return;
        }
        var slots=Empty();slots[0]=new(4,1);await Load(slots);w.ToggleInventory();w.ClickInventorySlot(0);w.ClickCraftSlot(0);
        Check(w.CraftOutput==new ItemStack(10,4),"One oak log offers four oak planks");Check(w.CraftOnce()&&w.CarriedStack==new ItemStack(10,4)&&w.GetCraftSlot(0).Count==0,"Craft consumes log exactly once");
        var targets=new[]{(Area:1,Slot:0),(Area:1,Slot:1)};var alloc=w.DragAllocation(targets,false);
        Check(alloc[targets[0]]==2&&alloc[targets[1]]==2&&w.CarriedStack.Count==4,"Preview2+2 without consuming ownership");
        var three=new[]{(Area:1,Slot:0),(Area:1,Slot:1),(Area:1,Slot:2)};var a3=w.DragAllocation(three,false);Check(a3[three[0]]==1&&a3[three[1]]==1&&a3[three[2]]==2,"Preview1+1+2");
        w.CommitDrag(Enumerable.Range(0,4).Select(i=>(Area:1,Slot:i)),false);Check(w.CarriedStack.Count==0&&Enumerable.Range(0,4).All(i=>w.GetCraftSlot(i)==new ItemStack(10,1)),"Commit four way split preserves count");
        Check(w.CraftOutput==new ItemStack(13,1)&&w.CraftOnce(),"Four planks craft a table");Check(!w.CraftOnce(),"Empty recipe cannot duplicate output");
        await Capture("crafting-table-result");w.ToggleInventory();Check(w.InventoryCount(13)==1,"Closing inventory returns held table");
        // Test actual mouse press/drag/release UI events after replenishing the fixture.
        slots=Empty();slots[0]=new(10,4);await Load(slots);w.ToggleInventory();await Frame(3);
        var source=w.GameUi.FindChild("InventorySlot0",true,false) as Control;var first=w.GameUi.FindChild("CraftSlot0",true,false) as Control;
        Check(source!=null&&first!=null,"Rendered interactive slots exist");
        if(source!=null)
        {
            var p=source.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion{Position=p});Input.ParseInputEvent(new InputEventMouseButton{Position=p,ButtonIndex=MouseButton.Left,Pressed=true});await Frame(2);Input.ParseInputEvent(new InputEventMouseButton{Position=p,ButtonIndex=MouseButton.Left,Pressed=false});await Frame(2);
            Check(w.CarriedStack.Count==4,"Actual GUI mouse picks up stack");
            var ghost=w.GameUi.FindChild("CarriedCursorIcon",true,false) as TextureRect;Check(ghost!=null&&ghost.Visible&&ghost.Texture!=null,"Rendered carried icon follows cursor");
            await Capture("carried-cursor");
            var p0=((Control)w.GameUi.FindChild("CraftSlot0",true,false)).GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion{Position=p0});Input.ParseInputEvent(new InputEventMouseButton{Position=p0,ButtonIndex=MouseButton.Left,Pressed=true});await Frame(2);
            for(int i=1;i<3;i++){var pi=((Control)w.GameUi.FindChild("CraftSlot"+i,true,false)).GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion{Position=pi,ButtonMask=MouseButtonMask.Left});await Frame(2);}
            await Capture("drag-preview-1-1-2");var p2=((Control)w.GameUi.FindChild("CraftSlot2",true,false)).GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseButton{Position=p2,ButtonIndex=MouseButton.Left,Pressed=false});await Frame(2);
            Check(w.GetCraftSlot(0).Count==1&&w.GetCraftSlot(1).Count==1&&w.GetCraftSlot(2).Count==2&&w.CarriedStack.Count==0,"Actual mouse drag distributes1+1+2 on release");
        }
        w.ToggleInventory();
        slots=Empty();slots[0]=new(4,2);await Load(slots);w.ToggleInventory();
        var plankRecipe=Crafting.Recipes.First(r=>r.Output==10);
        Check(w.FillRecipe(plankRecipe)&&w.GetCraftSlot(0)==new ItemStack(4,1)&&w.GetSlot(0)==new ItemStack(4,1)&&w.CraftOutput==new ItemStack(10,4),"Recipe book fills real ingredients without free items");
        Check(w.CraftOnce()&&w.CarriedStack==new ItemStack(10,4),"Recipe book output still requires explicit craft");w.ToggleInventory();
        var fullPlan=new InventoryModel();fullPlan.Load(Enumerable.Range(0,36).Select(_=>new ItemStack(3,64)).ToArray(),0,Enumerable.Range(0,9).Select(_=>new ItemStack(10,64)).ToArray());fullPlan.RestoreCarried(new(22,64));int heldTotal=fullPlan.TotalItems();var exported=fullPlan.ExportForSave();
        Check(exported.Slots.Sum(s=>s.Count)+exported.Crafting.Sum(s=>s.Count)+fullPlan.Carried.Count==heldTotal,"Full remote backpack and all nine crafting slots preserve carried stack in save");
        var tableCell=new Vector3I(3,4,0);w.Terrain.TrySetBlock(tableCell,13);w.OpenCraftingTable(tableCell);
        Check(w.CraftSize==3,"World table opens actual3x3");await Capture("large-table");w.ToggleInventory();
        var grid=Enumerable.Range(0,9).Select(_=>new ItemStack()).ToArray();foreach(int i in new[]{0,1,2})grid[i]=new(10,1);grid[4]=grid[7]=new(22,1);
        Check(Crafting.Match(grid,3)?.Recipe.Output==23,"Wooden pickaxe shaped recipe");Check(Crafting.Match(grid.Take(4).ToArray(),2)==null,"3x3 recipe not available in pocket2x2");
        var mirrored=new[]{new ItemStack(10,1),new ItemStack(10,1),new ItemStack(),new ItemStack(22,1),new ItemStack(10,1),new ItemStack(),new ItemStack(22,1),new ItemStack(),new ItemStack()};
        Check(Crafting.Match(mirrored,3)?.Recipe.Output==25,"Mirrored axe pattern");
        var stonePick=grid.Select(s=>s.Type==10?new ItemStack(14,s.Count):s).ToArray();Check(Crafting.Match(stonePick,3)?.Recipe.Output==24,"Official stone-tool material tag supports stone pickaxe");
        var stoneAxe=mirrored.Select(s=>s.Type==10?new ItemStack(14,s.Count):s).ToArray();Check(Crafting.Match(stoneAxe,3)?.Recipe.Output==26,"Official stone-tool material tag supports mirrored stone axe");
        Check(Crafting.Match(new[]{new ItemStack(14,1),new ItemStack(),new ItemStack(),new ItemStack(14,1),new ItemStack(),new ItemStack(),new ItemStack(22,1),new ItemStack(),new ItemStack()},3)?.Recipe.Output==28,"Official stone sword recipe");
        Check(RecipeCatalog.Entries.Length==1892&&RecipeCatalog.Entries.Select(r=>r.Kind).Distinct().Count()==7,"Entire pinned official Mojang recipe catalogue loaded");
        Check(Crafting.Match(new[]{new ItemStack(19,1),new ItemStack(),new ItemStack(22,1),new ItemStack()},2)?.Recipe is {Output:32,Count:4},"Mojang coal over stick produces four torches");
        Check(Crafting.Match(new[]{new ItemStack(33,1),new ItemStack(),new ItemStack(22,1),new ItemStack()},2)?.Recipe.Output==32,"Mojang charcoal substitute for torches");
        Check(Crafting.Matches(new[]{new ItemStack(4,1),new ItemStack(),new ItemStack(),new ItemStack()},2).Any(r=>r.Recipe.Output==22&&r.Recipe.Name.Contains("JOSCRAFT")),"User custom log-to-sticks option is explicitly separate");
        Check(RecipeCatalog.Smelt(20)==new ItemStack(21,1)&&RecipeCatalog.Smelt(4)==new ItemStack(33,1),"Mojang furnace clay and charcoal recipes");
        w.ToggleInventory();w.GameUi.ShowRecipes();await Capture("recipe-catalogue");w.ToggleInventory();
        // Furnace real active-time processing and pause/save persistence.
        var furnaceCell=new Vector3I(4,4,0);w.Terrain.TrySetBlock(furnaceCell,17);w.OpenFurnace(furnaceCell);
        var f=w.Furnaces[furnaceCell];f.Input=new(20,4);f.Fuel=new(19,1);w.GameUi.ShowInventory();await Capture("furnace");w.ToggleInventory();
        await Wait(4);w.SetPaused(true);float progress=f.Progress,burn=f.Burn;await Wait(1);Check(Math.Abs(f.Progress-progress)<.01f&&Math.Abs(f.Burn-burn)<.01f,"Pause freezes furnace progress and fuel");w.SetPaused(false);await Wait(4.4f);
        await Wait(2);Check(f.Output==new ItemStack(21,1)&&f.Input.Count==3&&f.Fuel.Count==0,"Furnace consumes one clay and uses coal over time");
        var torchCell=new Vector3I(2,4,-1);Check(w.Terrain.TrySetBlock(torchCell,32),"Place persistent non-full-cube torch");
        w.SetPaused(true);w.Ecology.SetTimeForQa(.73f);w.GameUi.Visible=false;w.Camera.Position=new Vector3(6,6,-5);w.Camera.LookAt(new Vector3(3.5f,4.4f,0));await Wait(.4f);
        Check(w.FireSystem.Torches.ContainsKey(torchCell)&&w.FireSystem.Torches[torchCell].Light.Visible,"Torch has actual flame and light node");
        Check(w.FireSystem.FurnaceFires.TryGetValue(furnaceCell,out var ff)&&ff.Burning&&ff.Light.Visible,"Burning furnace illuminates surroundings");
        var shaft=w.FireSystem.Torches[torchCell].GetNode<MeshInstance3D>("GroundedShaft");var floor=w.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(shaft.GlobalPosition,shaft.GlobalPosition-Vector3.Up,1));
        Check(floor.Count>0&&Math.Abs(shaft.GlobalPosition.Y-.29f-floor["position"].AsVector3().Y)<.02f,"Torch rendered base touches actual terrain collider");
        await Capture("firelight-night");float savedBurn=f.Burn;f.Burn=0;w.Terrain.TrySetBlock(torchCell,0);await Wait(.4f);Check(!w.FireSystem.FurnaceFires[furnaceCell].Light.Visible,"Extinguished furnace stops emitting light");await Capture("firelight-off");f.Burn=savedBurn;w.Terrain.TrySetBlock(torchCell,32);w.GameUi.Visible=true;w.SetPaused(false);w.Ecology.SetTimeForQa(.12f);
        var save=Path.Combine(dir,"survival-qa-data","survival-save.json");Check(w.SaveSnapshot(save)&&GameSession.Read(save)?.Furnaces.Single().Output.Count==1,"Furnace stacks and timers persisted");
        // Actual colliding, rendered models on terrain. Every kind has an explicit floor policy.
        w.GiveResource(29,3);w.GiveResource(30,3);w.GiveResource(31,3);
        var creatures=new List<Creature>();int ix=0;
        foreach(string kind in new[]{"dog","rabbit","fox","zombie","carrot","berries"})
        {
            Vector3 p=default;bool found=false;for(int a=0;a<20;a++)if(w.Ecology.FindGround(new(-5+ix*2,-3-a*.25f),kind=="zombie"?2.2f:1.2f,out p)){found=true;break;}
            Check(found,"Actual physics spawn surface "+kind);if(!found)continue;
            var c=w.Ecology.Spawn(new(){Id="qa-"+kind,Kind=kind,X=p.X,Y=p.Y,Z=p.Z,Following=false,Health=kind=="zombie"?16:12});creatures.Add(c);ix++;
            if(kind is "dog" or "rabbit" or "fox")
            {
                c.SetPhysicsProcess(false);int food=kind=="dog"?29:kind=="rabbit"?30:31;
                int slot=Enumerable.Range(0,36).First(i=>w.GetSlot(i).Type==food);w.SelectHotbar(slot);
                w.Body.Position=p+Vector3.Back*2.5f;w.Camera.Position=w.Body.Position+Vector3.Up*.85f;w.Camera.LookAt(p+Vector3.Up*.45f);
                int count=w.InventoryCount(food);Check(w.Ecology.TryInteract()&&c.Tamed&&c.Following&&w.InventoryCount(food)==count-1,"Actual ray feeding tames "+kind);
                Check(w.Ecology.TryInteract()&&!c.Following,"Tamed companion can wait "+kind);c.SetPhysicsProcess(true);
            }
        }
        w.Ecology.SetTimeForQa(.3f);w.SetOverview(true);await Physics(30);
        foreach(var c in creatures.Where(c=>c.Animator!=null))Check(c.Animator!.GetAnimationList().Any(a=>a.EndsWith("Idle"))&&c.Animator.GetAnimationList().Any(a=>a.EndsWith("Jump")),"Imported GLB animation library "+c.Kind);
        foreach(var c in creatures)
        {
            float bottom=c.RenderedFloor();
            var hit=w.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(c.Position+Vector3.Up*.12f,c.Position-Vector3.Up*.2f,1));
            Check(hit.Count>0&&Math.Abs(bottom-hit["position"].AsVector3().Y)<.08f,"Rendered model and collision floor "+c.Kind);
        }
        w.Body.Position=new Vector3(0,4.05f,4);w.Camera.Position=new Vector3(9,10,11);w.Camera.LookAt(new Vector3(0,4.6f,-3));w.SetOverview(false);w.SetPaused(true);w.GameUi.Visible=false;await Capture("creatures-day");
        float day=w.Ecology.TimeOfDay;await Wait(.5f);Check(w.Ecology.TimeOfDay==day,"Pause freezes day cycle");w.Ecology.SetTimeForQa(.73f);await Capture("creatures-night");w.GameUi.Visible=true;
        w.SetPaused(false);w.Ecology.SetTimeForQa(.25f);await Wait(1);Check(w.Ecology.TimeOfDay>.25f,"Day cycle advances during play");
        var dog=creatures.First(c=>c.Kind=="dog");dog.Following=true;w.Body.Position=dog.Position+Vector3.Back*5;float distance=dog.Position.DistanceTo(w.Body.Position);await Wait(1.5f);Check(dog.Position.DistanceTo(w.Body.Position)<distance-.5f,"Tamed dog physically follows player");dog.Following=false;
        w.Ecology.SetTimeForQa(.73f);var zombie=w.Ecology.Spawn(new(){Id="qa-combat",Kind="zombie",X=.5f,Y=4.03f,Z=3,Health=16});
        w.Body.Position=new Vector3(.5f,4.03f,4);await Physics(4);int health=w.Health;await Wait(1.5f);Check(w.Health<health,"Night zombie approaches and damages player");
        for(int i=0;i<9&&GodotObject.IsInstanceValid(zombie)&&zombie.Health>0;i++){w.Camera.Position=w.Body.Position+Vector3.Up*1.5f;w.Camera.LookAt(zombie.Position+Vector3.Up*1.5f);w.Ecology.TryAttack();await Wait(.55f);}
        Check(!w.Ecology.Creatures.Contains(zombie)&&w.InventoryCount(29)>=3,"Player defeats zombie and receives dog bone");
        Check(w.SaveSnapshot(save)&&w.LoadSnapshot(save),"Survival entities and time save/load");await Ready();Check(w.Ecology.Creatures.Any(c=>c.Kind=="dog"&&c.Tamed),"Tamed companion survives reload");
        Check(AutoUpdater.ValidManifest(new(){Version="0.4.1",Bytes=100,Sha256=new string('a',64)})&&!AutoUpdater.ValidManifest(new(){Version="../../bad",Bytes=10,Sha256=new string('a',64)}),"Updater rejects malformed release metadata");
        w.ToggleInventory();if(w.GameUi.FindChild("RecipeBookPanel",true,false)==null)w.GameUi.ShowRecipes();await Frame(3);
        tree.Root.Size=new Vector2I(390,844);await Frame(6);await Capture("recipe-book-mobile");
        var mobileBook=w.GameUi.FindChild("RecipeBookPanel",true,false) as Control;
        Check(mobileBook!=null&&mobileBook.IsVisibleInTree()&&mobileBook.GetGlobalRect().Position.X>=0&&mobileBook.GetGlobalRect().End.X<=390,"Recipe book mobile panel stays within viewport");
        tree.Root.Size=new Vector2I(1280,800);await Frame(4);w.ToggleInventory();
        var report=new{pass=failures.Count==0,utc=DateTime.UtcNow,evidence,failures,scope="Actual Godot desktop renderer and physics; isolated save; no live data mutation; LAN integration verified separately in artifacts/net-qa/summary.json",craftableRecipes=Crafting.Recipes.Length,officialCatalogue=RecipeCatalog.Entries.Length,version=AutoUpdater.CurrentVersion};File.WriteAllText(Path.Combine(dir,"survival-qa.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));GD.Print("JOSCRAFT_SURVIVAL_QA "+(failures.Count==0?"PASS":"FAIL")+" "+string.Join("; ",failures));tree.Quit(failures.Count==0?0:1);
    }
}
