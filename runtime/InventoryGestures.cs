using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GameUi
{
    readonly Dictionary<(int Area,int Slot),Button> slotWidgets=new();
    readonly List<(int Area,int Slot)> dragTargets=new();
    (int Area,int Slot)? dragSource;
    bool dragActive;
    TextureRect? cursorIcon;Label? cursorCount;
    Vector2 cursorPosition;
    void WireSlot(Button b,int area,int slot)
    {
        slotWidgets[(area,slot)]=b;b.ActionMode=BaseButton.ActionModeEnum.Release;
        b.GuiInput+=e=>
        {
            if(e is not InputEventMouseButton m||!m.Pressed)return;
            if(m.ButtonIndex==MouseButton.Right){CancelDrag();world.UiSlotClick(area,slot,true);GetViewport().SetInputAsHandled();return;}
            if(m.ButtonIndex!=MouseButton.Left)return;
            dragActive=true;dragTargets.Clear();
            if(world.CarriedStack.Count==0){dragSource=(area,slot);world.UiSlotClick(area,slot);}
            else{dragSource=null;dragTargets.Add((area,slot));}
            GetViewport().SetInputAsHandled();
        };
        b.MouseEntered+=()=>{if(dragActive&&dragSource!=(area,slot)&&!dragTargets.Contains((area,slot)))dragTargets.Add((area,slot));};
    }
    public override void _Input(InputEvent e)
    {
        if(e is InputEventMouseMotion motion)cursorPosition=motion.Position;
        if(e is InputEventMouseButton mouse)cursorPosition=mouse.Position;
        if(e is InputEventMouseButton b&&b.ButtonIndex==MouseButton.Left&&!b.Pressed&&dragActive)
        {
            var targets=dragTargets.ToArray();CancelDrag();world.CommitDrag(targets,false);
        }
    }
    void CancelDrag(){dragActive=false;dragTargets.Clear();dragSource=null;}
    void UpdateInventoryCursor()
    {
        if(cursorIcon==null)
        {
            cursorIcon=new TextureRect{Name="CarriedCursorIcon",Size=new Vector2(32,32),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore,ZIndex=100};root.AddChild(cursorIcon);
            cursorCount=new Label{Position=new Vector2(16,17),MouseFilter=Control.MouseFilterEnum.Ignore};cursorCount.AddThemeColorOverride("font_color",Colors.White);cursorCount.AddThemeColorOverride("font_outline_color",Colors.Black);cursorCount.AddThemeConstantOverride("outline_size",5);cursorIcon.AddChild(cursorCount);
        }
        var s=world.CarriedStack;cursorIcon.Visible=world.UiMode==GameMode.Inventory&&s.Count>0;
        if(cursorIcon.Visible){cursorIcon.Texture=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(s.Type).IconPath);cursorIcon.Position=cursorPosition+new Vector2(12,12);cursorCount!.Text=s.Count.ToString();}
        if(world.UiMode!=GameMode.Inventory){CancelDrag();return;}
        if(world.IsFurnaceOpen&&world.OpenStation is {} cell&&world.Furnaces.TryGetValue(cell,out var furnace)&&inventoryCard!=null)
        {
            var progress=inventoryCard.GetNodeOrNull<Label>("FurnaceProgress");if(progress!=null)progress.Text=$"{furnace.Progress/10*100:0} %\n{furnace.Burn:0} s paliva";
        }
        if(!dragActive)return;
        var allocation=world.DragAllocation(dragTargets,false);
        foreach(var entry in allocation)
        {
            if(!slotWidgets.TryGetValue(entry.Key,out var b))continue;
            int count=world.GetUiStack(entry.Key.Area,entry.Key.Slot).Count+entry.Value;
            if(count>0){b.Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(s.Type).IconPath);var label=b.GetNodeOrNull<Label>("StackCount");if(label==null)AddCount(b,count);else label.Text=count.ToString();}
        }
        int remaining=s.Count-allocation.Values.Sum();cursorCount!.Text=remaining.ToString();cursorIcon.Visible=remaining>0;
    }
    void BuildFurnaceSlots(Control card)
    {
        for(int i=0;i<3;i++)
        {
            var s=world.GetFurnaceStack(i);var b=new Button{Name="FurnaceSlot"+i,Position=new Vector2(i==2?280:200,i==0?77:i==1?151:110),Size=new Vector2(32,32),ExpandIcon=true,TooltipText=i==0?"Hroudy jílu":i==1?"Palivo: uhlí, kmeny nebo prkna":"Vypálené cihly"};LightSlot(b);card.AddChild(b);if(s.Count>0){b.Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(s.Type).IconPath);AddCount(b,s.Count);}WireSlot(b,2,i);
        }
        var label=new Label{Name="FurnaceProgress",Text="10 s / cihla",Position=new Vector2(240,152),MouseFilter=Control.MouseFilterEnum.Ignore};label.AddThemeFontSizeOverride("font_size",11);card.AddChild(label);
    }
}
