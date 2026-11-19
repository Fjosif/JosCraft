using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

public sealed class BuildingQa
{
    readonly BlockWorld w;readonly SceneTree tree;readonly List<string> failures=new();readonly List<object> checks=new();
    readonly string dir=ProjectSettings.GlobalizePath("res://artifacts");
    public BuildingQa(BlockWorld world){w=world;tree=w.GetTree();}
    void Check(bool ok,string name){checks.Add(new{name,pass=ok});GD.Print("BUILD_QA "+(ok?"PASS ":"FAIL ")+name);if(!ok)failures.Add(name);}
    async Task Frames(int n=1){for(int i=0;i<n;i++)await w.ToSignal(tree,SceneTree.SignalName.PhysicsFrame);}
    async Task UiFrames(int n=1){for(int i=0;i<n;i++)await w.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    async Task Wait(double s)=>await w.ToSignal(tree.CreateTimer(s),SceneTreeTimer.SignalName.Timeout);
    async Task Capture(string name){await w.ToSignal(tree,SceneTree.SignalName.ProcessFrame);await w.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var im=w.GetViewport().GetTexture().GetImage();im.SavePng(Path.Combine(dir,"building-"+name+".png"));}
    void Aim(Vector3 from,Vector3 at){w.Camera.Position=from;w.Camera.LookAt(at);}
    bool Place(Vector3I cell){Aim(new Vector3(cell.X+.5f,cell.Y+3,cell.Z+2),new Vector3(cell.X+.5f,cell.Y-.2f,cell.Z+.5f));return w.PlaceTarget();}
    bool RayHits(Vector3 from,Vector3 to,Vector3I cell)
    {
        using var q=PhysicsRayQueryParameters3D.Create(from,to,1);var hit=w.GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count>0&&hit["collider"].AsGodotObject().HasMeta("shape_cell")&&hit["collider"].AsGodotObject().GetMeta("shape_cell").AsVector3I()==cell;
    }
    public async Task Run()
    {
        w.SimulateInput(Vector2.Zero);Directory.CreateDirectory(Path.Combine(dir,"building-qa-data"));
        for(int i=0;i<500&&(!w.CharacterReady||w.Terrain.PendingChunkCount>0);i++)await w.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
        if(OS.GetCmdlineUserArgs().Contains("--measure-heroes"))
        {
            var measures=new List<object>();for(int i=0;i<Viewer.Ids.Length;i++){w.SelectCharacter(i);for(int j=0;j<900&&!w.CharacterReady;j++)await UiFrames();var size=w.FootprintMax-w.FootprintMin;float scale=Math.Min(1,Math.Min(1.82f/w.CharacterHeight,.70f/Math.Max(size.X,size.Y)));measures.Add(new{id=Viewer.Ids[i],height=w.CharacterHeight,width=size.X,depth=size.Y,scale});}
            File.WriteAllText(Path.Combine(dir,"hero-gameplay-measurements.json"),JsonSerializer.Serialize(measures,new JsonSerializerOptions{WriteIndented=true}));tree.Quit();return;
        }
        GD.Print($"BUILD_QA_PLAYER height={w.CollisionHeight} footprint={w.FootprintMax-w.FootprintMin}");
        var slots=Enumerable.Range(0,36).Select(_=>new ItemStack()).ToArray();slots[0]=new(10,64);slots[1]=new(22,64);
        var file=Path.Combine(dir,"building-qa-data/fixture.json");
        Check(GameSession.WriteAtomic(file,new SavedGame{Slots=slots,Creatures=Array.Empty<CreatureSave>(),TreeExclusions=Array.Empty<int>(),Edits=new[]{new BlockTerrain.BlockEdit(2,4,2,13)}})&&w.LoadSnapshot(file),"Load isolated building fixture");
        await Wait(.5);w.Body.Position=new Vector3(.5f,4.05f,.5f);await Frames(8);
        w.OpenCraftingTable(new Vector3I(2,4,2));
        foreach(var (type,count) in new[]{(34,6),(35,4),(36,3),(37,3),(38,1)})
        {
            var recipe=Crafting.Recipes.FirstOrDefault(r=>r.Output==type);
            Check(recipe!=null&&recipe.Count==count,"Mojang recipe and yield: "+BlockCatalog.Get(type).Name);
            if(recipe==null)continue;
            Check(w.FillRecipe(recipe)&&w.CraftOutput==new ItemStack(type,count),"Autofill owned ingredients: "+type);
            int total=w.InventoryCount(10)+w.InventoryCount(22);
            Check(w.CraftOnce()&&w.CarriedStack==new ItemStack(type,count),"Craft real output: "+type);
            Check(w.InventoryCount(10)+w.InventoryCount(22)==total-recipe.Pattern.Count(x=>x!=0),"Consume ingredients once: "+type);
            w.ToggleInventory();w.OpenCraftingTable(new Vector3I(2,4,2));
        }
        w.ToggleInventory();
        var door=new Vector3I(2,4,-1);int doorSlot=Enumerable.Range(0,9).First(i=>w.GetSlot(i).Type==36);w.SelectHotbar(doorSlot);
        int before=w.InventoryCount(36);Check(Place(door),"Place crafted door via actual target ray");
        Check(w.InventoryCount(36)==before-1&&w.Terrain.GetBlock(door+Vector3I.Up)==BlockType.DoorUpper,"Door consumes once and reserves upper cell");
        await Wait(.5);
        Check(RayHits(new Vector3(2.5f,5,-2),new Vector3(2.5f,5,0),door),"Closed door blocks real physics ray");
        Aim(new Vector3(2.5f,5,-2),new Vector3(2.5f,5,-.95f));w.UseSelected();await Wait(.5);
        Check(w.Shapes.At(door)?.Open==true,"Right-click use opens door");
        Check(!RayHits(new Vector3(2.5f,5,-2),new Vector3(2.5f,5,0),door),"Opened door clears actual central passage");
        Check(w.Shapes.SetOpen(door,false),"Close door");await Wait(.5);
        // A player in the moving leaf's path prevents closing.
        Check(w.Shapes.SetOpen(door,true),"Reopen door");await Wait(.5);
        w.Body.Position=new Vector3(2.55f,4,-.8f);await Frames(2);
        Check(!w.Shapes.SetOpen(door,false)&&w.Shapes.At(door)!.Open,"Door cannot close through the player");
        w.Body.Position=new Vector3(.5f,4.05f,.5f);await Frames(3);
        Check(w.Shapes.SetOpen(door,false),"Close before building door frame");await Wait(.45);
        foreach(int dx in new[]{-1,1})for(int y=4;y<6;y++)w.Terrain.ApplyAuthoritativeBlock(door+new Vector3I(dx,y-4,0),BlockType.OakPlanks);
        w.Terrain.ApplyAuthoritativeBlock(door+Vector3I.Up*2,BlockType.OakPlanks);await Frames(3);
        Check(w.Shapes.SetOpen(door,true),"Door opens inside a real wall frame");await Wait(.45);
        for(int hero=0;hero<Viewer.Ids.Length;hero++)
        {
            w.SelectCharacter(hero);for(int i=0;i<500&&!w.CharacterReady;i++)await UiFrames();
            Check(Math.Abs(w.Visual.Scale.X-w.Visual.Scale.Y)<.00001f&&Math.Abs(w.Visual.Scale.Z-w.Visual.Scale.Y)<.00001f,"Original proportions preserved on all axes: "+Viewer.Ids[hero]);
            Check(w.CharacterHeight>1.80f&&w.CharacterHeight<1.90f&&w.EyeHeight>1.55f&&w.EyeHeight<1.65f,"Full-height player and eye level: "+Viewer.Ids[hero]);
            w.Body.Position=new Vector3(2.61f,4.03f,-2);w.Body.Rotation=Vector3.Zero;w.Body.Velocity=Vector3.Zero;
            using var collision=new KinematicCollision3D();bool blocked=w.Body.TestMove(w.Body.GlobalTransform,new Vector3(0,0,2),collision);
            Check(!blocked,"Physical gameplay body passes framed door: "+Viewer.Ids[hero]);
        }
        w.SelectCharacter(0);for(int i=0;i<500&&!w.CharacterReady;i++)await UiFrames();
        w.Body.Position=new Vector3(2.61f,4.03f,-.5f);w.Body.Rotation=Vector3.Zero;w.SetFirstPerson(false);w.SetPaused(true);w.GameUi.Visible=false;w.Camera.Position=new Vector3(5,6,-5);w.Camera.LookAt(new Vector3(2.5f,4.9f,-.6f));await Capture("player-full-height");w.GameUi.Visible=true;w.SetPaused(false);w.SetFirstPerson(true);w.Body.Position=new Vector3(.5f,4.05f,.5f);await Frames(3);
        var chest=new Vector3I(-2,4,-1);w.SelectHotbar(Enumerable.Range(0,9).First(i=>w.GetSlot(i).Type==38));
        Check(Place(chest),"Place crafted chest");await Wait(.4);
        Aim(new Vector3(-1.5f,5,-2.5f),new Vector3(-1.5f,4.5f,-.9f));w.UseSelected();await Wait(.5);
        Check(w.IsChestOpen&&w.Shapes.At(chest)?.Open==true,"Right-click chest opens lid and storage UI");
        Check(w.GameUi.FindChild("ChestGrid",true,false)?.GetChildCount()==27,"Chest renders 27 interactive slots");
        int plank=Enumerable.Range(0,36).First(i=>w.GetSlot(i).Type==10);int planks=w.InventoryCount(10);
        w.UiSlotClick(0,plank);w.CommitDrag(new[]{(Area:3,Slot:0),(Area:3,Slot:1),(Area:3,Slot:2)},false);
        Check(w.CarriedStack.Count==0&&Enumerable.Range(0,3).Sum(w.GetChestStackCount)==planks,"Drag distributes items into chest without duplication");
        // Drive real GUI press/release events, not just inventory model methods.
        await UiFrames(3);var source=(Control)w.GameUi.FindChild("ChestSlot0",true,false);var pos=source.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion{Position=pos});Input.ParseInputEvent(new InputEventMouseButton{Position=pos,ButtonIndex=MouseButton.Left,Pressed=true});await UiFrames(3);Input.ParseInputEvent(new InputEventMouseButton{Position=pos,ButtonIndex=MouseButton.Left,Pressed=false});await UiFrames(3);
        Check(w.CarriedStack.Count>0,"Actual GUI mouse picks up chest stack");
        var target=(Control)w.GameUi.FindChild("ChestSlot3",true,false);pos=target.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion{Position=pos});Input.ParseInputEvent(new InputEventMouseButton{Position=pos,ButtonIndex=MouseButton.Left,Pressed=true});await UiFrames(3);Input.ParseInputEvent(new InputEventMouseButton{Position=pos,ButtonIndex=MouseButton.Left,Pressed=false});await UiFrames(3);
        Check(w.CarriedStack.Count==0&&w.GetChestStack(3).Count>0,"Actual GUI mouse places chest stack");
        await Capture("chest-inventory");w.ToggleInventory();await Wait(.5);
        Check(w.Shapes.At(chest)?.Open==false,"Closing inventory animates chest closed");
        Check(!w.Shapes.Remove(chest),"Nonempty chest cannot be destroyed with items inside");
        // Overhead and rear obstructions are checked by the actual animated collision boxes.
        w.Terrain.ApplyAuthoritativeBlock(chest+Vector3I.Up,BlockType.Stone);await Frames(3);
        Check(!w.OpenChest(chest),"Overhead solid block prevents lid opening");w.Terrain.ApplyAuthoritativeBlock(chest+Vector3I.Up,BlockType.Air);
        w.Terrain.ApplyAuthoritativeBlock(chest+Vector3I.Back,BlockType.Stone);await Frames(3);Check(!w.OpenChest(chest),"Rear solid block prevents lid opening");w.Terrain.ApplyAuthoritativeBlock(chest+Vector3I.Back,BlockType.Air);await Frames(3);
        Check(w.Shapes.SetOpen(chest,true),"Reopen unblocked chest");await Wait(.4);Check(!w.CellPhysicallyClear(chest+Vector3I.Up)&&!w.CellPhysicallyClear(chest+Vector3I.Back),"Open lid reserves physical space across neighboring cells");w.Shapes.SetOpen(chest,false);await Wait(.4);
        var slab=new Vector3I(1,4,2);var stairs=new Vector3I(1,4,3);var fence=new Vector3I(-2,4,2);
        Check(w.Shapes.Place(slab,34,0)&&w.Shapes.Place(stairs,35,0),"Place slab and stairs with compound collision");
        Check(w.Shapes.Place(fence,37,0)&&w.Shapes.Place(fence+Vector3I.Right,37,0),"Place connecting fences");await Wait(.3);
        var fv=w.Shapes.Visuals[fence];Check(fv.Model.FindChild("East",true,false) is Node3D east&&east.Visible,"Fence connects toward neighbor");
        Check(fv.Model.FindChild("West",true,false) is Node3D west&&!west.Visible,"Fence hides absent neighbor branch");
        Check(RayHits(new Vector3(1.5f,6,2.5f),new Vector3(1.5f,4,2.5f),slab),"Actual slab collision is present");
        w.Body.Position=new Vector3(1.5f,4.03f,1);w.CameraYaw=0;w.Body.Velocity=Vector3.Zero;w.SimulateInput(new Vector2(0,1));await Frames(24);
        GD.Print($"BUILD_QA_STEP_SLAB pos={w.Body.Position} vel={w.Body.Velocity} floor={w.Body.IsOnFloor()}");Check(w.Body.Position.Y>4.45f&&w.Body.Position.Z>1.8f,"Controller walks up a half slab without jumping");await Frames(34);
        GD.Print($"BUILD_QA_STEP_STAIRS pos={w.Body.Position} vel={w.Body.Velocity} floor={w.Body.IsOnFloor()}");Check(w.Body.Position.Y>4.9f&&w.Body.Position.Z>3.3f,"Controller walks up both stair treads without jumping");w.SimulateInput(Vector2.Zero);w.Body.Position=new Vector3(.5f,4.03f,.5f);await Frames(3);
        w.Terrain.ApplyAuthoritativeBlock(slab+Vector3I.Up,BlockType.Stone);w.Body.Position=new Vector3(1.5f,4.03f,1);w.Body.Velocity=Vector3.Zero;w.SimulateInput(new Vector2(0,1));await Frames(28);Check(w.Body.Position.Z<1.7f&&w.Body.Position.Y<4.1f,"Step controller cannot climb through a low ceiling");w.SimulateInput(Vector2.Zero);w.Terrain.ApplyAuthoritativeBlock(slab+Vector3I.Up,BlockType.Air);w.Body.Position=new Vector3(.5f,4.03f,.5f);await Frames(3);
        // All orientations retain their real hinge and occupancy after saving.
        var rotated=new Vector3I(4,4,2);Check(w.Shapes.Place(rotated,36,1),"Place rotated door");await Wait(.4);Check(w.Shapes.SetOpen(rotated,true),"Open rotated door");await Wait(.4);
        file=Path.Combine(dir,"building-qa-data/roundtrip.json");Check(w.SaveSnapshot(file),"Save building state atomically");
        Check(w.LoadSnapshot(file),"Reload saved building state");await Wait(.5);
        Check(w.Shapes.At(rotated) is {Rotation:1,Open:true}&&w.Terrain.GetBlock(rotated+Vector3I.Up)==BlockType.DoorUpper,"Door rotation, open state and occupancy survive reload");
        Check(w.Shapes.At(chest)!.Slots.Sum(s=>s.Count)==planks,"Chest contents survive reload");
        Check(w.Shapes.Remove(door)&&w.Terrain.GetBlock(door+Vector3I.Up)==BlockType.Air,"Mining either door part releases both cells");
        w.Body.Position=new Vector3(-4,4,1);w.SetFirstPerson(false);w.SetPaused(true);w.GameUi.Visible=false;w.Camera.Position=new Vector3(6,7,6);w.Camera.LookAt(new Vector3(0,4.5f,1));
        await Capture("placed-blocks");
        File.WriteAllText(Path.Combine(dir,"building-qa.json"),JsonSerializer.Serialize(new{pass=failures.Count==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));tree.Quit(failures.Count==0?0:1);
    }
}

public partial class BlockWorld{public int GetChestStackCount(int slot)=>GetChestStack(slot).Count;}
