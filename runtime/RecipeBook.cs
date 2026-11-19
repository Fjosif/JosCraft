using Godot;
using System;
using System.Linq;
using System.Text.Json;

public partial class GameUi
{
    string recipeSearch="";
    bool bookOpen,onlyCraftable,wholeCatalogue;int bookCategory,bookPage;
    Control? recipePanel;Recipe? ghostRecipe;
    public void ClearRecipeGhost(){ghostRecipe=null;}
    public void ShowRecipes(){CancelDrag();bookOpen=!bookOpen;ShowInventory();}
    static StyleBoxFlat BookSurface(string color)=>new(){BgColor=new Color(color),BorderColor=new Color("6c7478"),BorderWidthLeft=2,BorderWidthTop=2,BorderWidthRight=2,BorderWidthBottom=2,ContentMarginLeft=3,ContentMarginRight=3,ContentMarginTop=2,ContentMarginBottom=2};
    void BuildRecipePanel()
    {
        recipePanel=new Control{Name="RecipeBookPanel",Size=new Vector2(252,320),Theme=inventoryCard.Theme};modal.AddChild(recipePanel);
        var paper=new Panel{Size=recipePanel.Size,MouseFilter=Control.MouseFilterEnum.Ignore};paper.AddThemeStyleboxOverride("panel",BookSurface("c8ced0"));recipePanel.AddChild(paper);
        var search=new LineEdit{Name="RecipeSearch",Text=recipeSearch,PlaceholderText="Hledat…",Position=new Vector2(12,12),Size=new Vector2(190,30)};
        var searchStyle=BookSurface("747e82");search.AddThemeStyleboxOverride("normal",searchStyle);search.AddThemeStyleboxOverride("focus",searchStyle);search.AddThemeColorOverride("font_color",Colors.White);search.AddThemeColorOverride("font_placeholder_color",new Color("e2e6e7"));search.AddThemeFontSizeOverride("font_size",14);recipePanel.AddChild(search);
        var filter=new Button{Icon=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(13).IconPath),ExpandIcon=true,Position=new Vector2(208,12),Size=new Vector2(32,30),TooltipText=onlyCraftable?"Zobrazit všechny recepty":"Jen recepty, na které mám suroviny"};filter.AddThemeStyleboxOverride("normal",BookSurface(onlyCraftable?"6d9860":"969fa3"));recipePanel.AddChild(filter);
        string[] categories={"Vše","Stavební bloky","Nástroje a výbava","Jídlo","Ostatní"};int[] icons={0,16,24,30,19};
        for(int i=0;i<5;i++)
        {
            int category=i;var tab=new Button{Position=new Vector2(-30,54+i*44),Size=new Vector2(34,40),ExpandIcon=true,TooltipText=categories[i],Icon=ResourceLoader.Load<Texture2D>(i==0?"res://assets/icons/recipe-book.svg":BlockCatalog.Get(icons[i]).IconPath)};tab.AddThemeStyleboxOverride("normal",BookSurface(i==bookCategory?"a9b49e":"90999d"));recipePanel.AddChild(tab);tab.Pressed+=()=>{bookCategory=category;bookPage=0;ShowInventory();};
        }
        var grid=new GridContainer{Name="RecipeIconGrid",Columns=5,Position=new Vector2(12,54)};grid.AddThemeConstantOverride("h_separation",4);grid.AddThemeConstantOverride("v_separation",4);recipePanel.AddChild(grid);
        var pageLabel=new Label{Position=new Vector2(72,234),Size=new Vector2(108,28),HorizontalAlignment=HorizontalAlignment.Center};pageLabel.AddThemeFontSizeOverride("font_size",14);recipePanel.AddChild(pageLabel);
        var prev=new Button{Text="◀",Position=new Vector2(24,234),Size=new Vector2(32,28),FocusMode=Control.FocusModeEnum.None};var next=new Button{Text="▶",Position=new Vector2(196,234),Size=new Vector2(32,28),FocusMode=Control.FocusModeEnum.None};LightSlot(prev);LightSlot(next);recipePanel.AddChild(prev);recipePanel.AddChild(next);
        var all=new CheckBox{Text="Celý katalog",ButtonPressed=wholeCatalogue,Position=new Vector2(12,274),Size=new Vector2(190,28),TooltipText="Zahrnout také materiály, které ještě přidáme do hry."};all.AddThemeColorOverride("font_color",new Color("242a2d"));all.AddThemeColorOverride("font_hover_color",new Color("242a2d"));all.AddThemeFontSizeOverride("font_size",13);foreach(string state in new[]{"normal","hover","pressed","focus"})all.AddThemeStyleboxOverride(state,new StyleBoxEmpty());recipePanel.AddChild(all);
        var close=new Button{Text="×",TooltipText="Zavřít receptář",Position=new Vector2(214,274),Size=new Vector2(26,28)};LightSlot(close);recipePanel.AddChild(close);close.Pressed+=()=>{bookOpen=false;ShowInventory();};
        void Populate()
        {
            foreach(Node n in grid.GetChildren()){grid.RemoveChild(n);n.QueueFree();}
            bool Category(CatalogEntry e)
            {
                string id=e.OutputId;int t=RecipeCatalog.Type(id);
                return bookCategory switch{1=>BlockCatalog.IsPlaceable(t)||id.Contains("planks")||id.Contains("stairs")||id.Contains("brick")||id.Contains("slab"),2=>id.Contains("axe")||id.Contains("sword")||id.Contains("helmet")||id.Contains("boots")||id.Contains("chestplate")||id.Contains("shovel")||id.Contains("hoe"),3=>t is 30 or 31||id.Contains("bread")||id.Contains("stew")||id.Contains("cooked")||id.Contains("cake")||id.Contains("cookie"),4=>!BlockCatalog.IsPlaceable(t)&&!id.Contains("axe")&&!id.Contains("sword"),_=>true};
            }
            bool CanMake(CatalogEntry e){var r=RecipeCatalog.RuntimeRecipe(e);return r!=null&&world.CanFillRecipe(r);}
            var groups=RecipeCatalog.Entries.Where(e=>(wholeCatalogue||RecipeCatalog.Available(e))&&(world.IsFurnaceOpen?e.Kind=="furnace":e.Kind is "shaped" or "shapeless")&&Category(e)&&(recipeSearch.Length==0||$"{e.Id} {e.Name}".Contains(recipeSearch,StringComparison.OrdinalIgnoreCase))).GroupBy(e=>e.OutputId).Select(g=>g.OrderByDescending(CanMake).ThenByDescending(RecipeCatalog.Available).First()).Where(e=>!onlyCraftable||CanMake(e)).OrderByDescending(CanMake).ThenBy(e=>e.Name).ToArray();
            int pages=Math.Max(1,(groups.Length+19)/20);bookPage=Math.Clamp(bookPage,0,pages-1);pageLabel.Text=$"{bookPage+1} / {pages}";prev.Disabled=bookPage==0;next.Disabled=bookPage==pages-1;
            foreach(var entry in groups.Skip(bookPage*20).Take(20))
            {
                int type=RecipeCatalog.Type(entry.OutputId);bool can=CanMake(entry);var tile=new Button{Name="RecipeTile_"+entry.File.Replace('.','_'),CustomMinimumSize=new Vector2(40,40),Icon=ResourceLoader.Load<Texture2D>(type>0?BlockCatalog.Get(type).IconPath:"res://assets/icons/unknown-block.svg"),ExpandIcon=true,IconAlignment=HorizontalAlignment.Center,FocusMode=Control.FocusModeEnum.None,TooltipText=entry.Name+(can?"":RecipeCatalog.Available(entry)?" · chybí suroviny nebo větší mřížka":" · materiály připravujeme")};
                tile.AddThemeConstantOverride("icon_max_width",32);tile.AddThemeStyleboxOverride("normal",BookSurface(can?"8d9b85":"a87676"));var hover=BookSurface("c0c8b6");tile.AddThemeStyleboxOverride("hover",hover);grid.AddChild(tile);
                tile.Pressed+=()=>{
                    var recipe=RecipeCatalog.RuntimeRecipe(entry);
                    if(recipe==null){ShowRecipeDetail(entry);return;}
                    bool filled=world.FillRecipe(recipe);ghostRecipe=filled?null:recipe;
                    if(recipe.Width>world.CraftSize||recipe.Height>world.CraftSize)world.Notify("Tenhle recept potřebuje pracovní stůl3×3.");
                    if(GetViewport().GetVisibleRect().Size.X<740)bookOpen=false;ShowInventory();
                };
            }
            while(grid.GetChildCount()<20){var empty=new Panel{CustomMinimumSize=new Vector2(40,40),MouseFilter=Control.MouseFilterEnum.Ignore};empty.AddThemeStyleboxOverride("panel",BookSurface("aeb7b9"));grid.AddChild(empty);}
        }
        search.TextChanged+=text=>{recipeSearch=text;bookPage=0;Populate();};filter.Pressed+=()=>{onlyCraftable=!onlyCraftable;bookPage=0;ShowInventory();};all.Toggled+=value=>{wholeCatalogue=value;bookPage=0;Populate();};prev.Pressed+=()=>{bookPage--;Populate();};next.Pressed+=()=>{bookPage++;Populate();};Populate();
    }
    void LayoutRecipePanel(Vector2 size)
    {
        if(world.UiMode!=GameMode.Inventory||inventoryCard==null||recipePanel==null||!IsInstanceValid(recipePanel)||!recipePanel.IsInsideTree())return;
        if(size.X<740)
        {
            inventoryCard.Visible=false;float scale=Math.Min(1.4f,Math.Min((size.X-24)/282,(size.Y-24)/320));recipePanel.Scale=Vector2.One*scale;recipePanel.Position=(size-new Vector2(282,320)*scale)*.5f+new Vector2(30,0)*scale;
        }
        else
        {
            inventoryCard.Visible=true;float scale=Math.Min(1.4f,Math.Min((size.X-24)/636,(size.Y-24)/464));var origin=(size-new Vector2(636,464)*scale)*.5f;inventoryCard.Scale=recipePanel.Scale=Vector2.One*scale;inventoryCard.Position=origin+new Vector2(292,0)*scale;recipePanel.Position=origin+new Vector2(28,16)*scale;
        }
    }
    void BuildRecipeGhost()
    {
        if(ghostRecipe==null||world.IsFurnaceOpen||ghostRecipe.Width>world.CraftSize||ghostRecipe.Height>world.CraftSize)return;var r=ghostRecipe;
        for(int i=0;i<r.Pattern.Length;i++)
        {
            int expected=r.Pattern[i];if(expected==0)continue;int slot=r.Shapeless?i:(i/r.Width)*world.CraftSize+i%r.Width;
            var button=inventoryCard.FindChild("CraftSlot"+slot,true,false) as Control;if(button==null)continue;
            int type=expected==-1?10:expected==-2?19:expected==-3?14:expected;
            var ghost=new TextureRect{Texture=ResourceLoader.Load<Texture2D>(BlockCatalog.Get(type).IconPath),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore,Modulate=new Color(1,.55f,.55f,.65f)};ghost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);button.AddChild(ghost);
        }
    }
    static string Station(string kind)=>kind switch{"shaped"=>"mřížka","shapeless"=>"volné rozložení","furnace"=>"vypalování","brewing_mix" or "brewing_container"=>"vaření lektvarů",_=>"kovářství"};
    static string IngredientText(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Array)return string.Join(" / ",e.EnumerateArray().Select(IngredientText));
        if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("id",out var id))return "—";
        string name=id.GetString()??"";bool tag=e.TryGetProperty("tag",out var t)&&t.GetBoolean();
        return tag?name.Replace("minecraft:","libovolné ").Replace('_',' '):RecipeCatalog.Name(name);
    }
    void ShowRecipeDetail(CatalogEntry entry)
    {
        var box=Modal(entry.Name);box.AddChild(new Label{Text=entry.Id+"\n"+Station(entry.Kind),AutowrapMode=TextServer.AutowrapMode.WordSmart});var data=entry.Data;
        if(data.TryGetProperty("pattern",out var pattern))
        {
            var rows=pattern.EnumerateArray().Select(r=>r.GetString()!).ToArray();var keys=data.GetProperty("keys");int width=rows.Max(r=>r.Length);
            var grid=new GridContainer{Columns=width};box.AddChild(grid);
            foreach(var row in rows)for(int x=0;x<width;x++)
            {
                char c=x<row.Length?row[x]:' ';var panel=new PanelContainer{CustomMinimumSize=new Vector2(92,66)};grid.AddChild(panel);var label=new Label{Text=c==' '?"—":IngredientText(keys.GetProperty(c.ToString())),AutowrapMode=TextServer.AutowrapMode.WordSmart,HorizontalAlignment=HorizontalAlignment.Center};label.AddThemeFontSizeOverride("font_size",13);panel.AddChild(label);
            }
        }
        else if(data.TryGetProperty("ingredients",out var ingredients))box.AddChild(new Label{Text=string.Join("\n",ingredients.EnumerateArray().Select(IngredientText)),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        foreach(string field in new[]{"input","reagent","base","addition","template"})if(data.TryGetProperty(field,out var input))box.AddChild(new Label{Text=field+": "+IngredientText(input),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        box.AddChild(new Label{Text="Výsledek: "+string.Join(" + ",data.GetProperty("outputs").EnumerateArray().Select(e=>IngredientText(e)+" × "+e.GetProperty("count").GetInt32())),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        box.AddChild(new Label{Text=RecipeCatalog.Available(entry)?"Tento recept má v JOSCRAFTu potřebné materiály a stanici.":"Pro tento recept ještě doplníme materiály nebo pracovní stanici.",AutowrapMode=TextServer.AutowrapMode.WordSmart});
        AddButton("Zpět do receptáře",()=>{bookOpen=true;ShowInventory();},box);AddButton("Zpět do inventáře",()=>{bookOpen=false;ShowInventory();},box);
    }
}
