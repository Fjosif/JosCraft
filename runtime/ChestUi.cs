using Godot;
using System;

public partial class GameUi
{
    void ShowChestInventory()
    {
        slotWidgets.Clear();bookOpen=false;recipePanel=null;ghostRecipe=null;
        foreach(Node n in modal.GetChildren()){modal.RemoveChild(n);n.QueueFree();}modalPanel=null!;
        var dim=new ColorRect{Color=new Color(0,0,0,.58f)};dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);modal.AddChild(dim);
        var theme=(Theme)root.Theme.Duplicate();theme.DefaultFontSize=16;theme.SetColor("font_color","Label",new Color("242a2d"));theme.SetColor("font_color","Button",new Color("242a2d"));theme.SetColor("font_hover_color","Button",new Color("242a2d"));theme.SetColor("font_pressed_color","Button",new Color("242a2d"));
        inventoryCard=new Control{Name="ChestInventoryCard",Size=new Vector2(344,464),Theme=theme};modal.AddChild(inventoryCard);
        var paper=new Panel{Size=inventoryCard.Size,MouseFilter=Control.MouseFilterEnum.Ignore};paper.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("c8ced0"),BorderColor=new Color("737e83"),BorderWidthTop=2,BorderWidthBottom=2,BorderWidthLeft=2,BorderWidthRight=2});inventoryCard.AddChild(paper);
        void Text(string text,float y,int size){var l=new Label{Text=text,Position=new Vector2(12,y),MouseFilter=Control.MouseFilterEnum.Ignore};l.AddThemeFontSizeOverride("font_size",size);inventoryCard.AddChild(l);}
        Text("Truhla",8,22);Text("27 míst",36,12);
        var chest=new GridContainer{Name="ChestGrid",Columns=9,Position=new Vector2(12,58)};inventoryCard.AddChild(chest);
        for(int i=0;i<27;i++)
        {
            var s=world.GetChestStack(i);var b=new Button{Name="ChestSlot"+i,CustomMinimumSize=new Vector2(32,32),ExpandIcon=true,FocusMode=Control.FocusModeEnum.None,TooltipText=s.Count>0?BlockCatalog.Get(s.Type).Name:"Prázdné místo"};LightSlot(b);chest.AddChild(b);
            if(s.Count>0){b.Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(s.Type).IconPath);AddCount(b,s.Count);}WireSlot(b,3,i);
        }
        Text("Batoh",178,14);
        var bag=new GridContainer{Name="StorageGrid",Columns=9,Position=new Vector2(12,202)};inventoryCard.AddChild(bag);
        var bar=new GridContainer{Name="InventoryHotbar",Columns=9,Position=new Vector2(12,320)};inventoryCard.AddChild(bar);
        for(int i=0;i<36;i++){var b=SlotButton(i,true);b.CustomMinimumSize=new Vector2(32,32);LightSlot(b);(i<9?bar:bag).AddChild(b);WireSlot(b,0,i);}
        Text("Táhni: rozdělit · Pravé: půlka / jeden kus",370,12);
        var close=new Button{Name="CloseInventoryButton",Text="Zavřít (E)",Position=new Vector2(12,420),Size=new Vector2(320,32)};LightSlot(close);close.Pressed+=world.ToggleInventory;inventoryCard.AddChild(close);modal.Visible=true;
    }
}
