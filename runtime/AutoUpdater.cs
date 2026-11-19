using Godot;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using HttpClient=System.Net.Http.HttpClient;

public sealed class UpdateManifest
{
    public string Version{get;set;}=""; public string Package{get;set;}="JosCraft-windows-x64.zip";
    public string Sha256{get;set;}="";public long Bytes{get;set;}
    public int Format{get;set;}=1;
}
/// <summary>Downloads only published release assets; development workspace never self-modifies.</summary>
public partial class AutoUpdater : Node
{
    public const string CurrentVersion="0.4.3";
    public const string Repository="Fjosif/JosCraft";
    static readonly HttpClient http=CreateClient();
    float timer=8;bool busy,ready,applying;string installRoot="",zip="";UpdateManifest? pending;
    CanvasLayer? overlay;Label? message;
    static HttpClient CreateClient(){var h=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};h.DefaultRequestHeaders.UserAgent.ParseAdd("JosCraft/"+CurrentVersion);return h;}
    public override void _Ready()
    {
        var args=OS.GetCmdlineUserArgs();int i=Array.IndexOf(args,"--install-root");
        if(i>=0&&i+1<args.Length)installRoot=Path.GetFullPath(args[i+1]);
        if(OS.HasFeature("editor")||!OS.HasFeature("windows")||installRoot.Length==0||!File.Exists(Path.Combine(installRoot,"current.json"))){SetProcess(false);return;}
        _ = MarkHealthy();
        if(args.Contains("--update-qa"))SetProcess(false);
    }
    async Task MarkHealthy(){await ToSignal(GetTree().CreateTimer(6),SceneTreeTimer.SignalName.Timeout);try{File.WriteAllText(Path.Combine(installRoot,"healthy-"+CurrentVersion+".txt"),DateTime.UtcNow.ToString("O"));}catch{}}
    public override void _Process(double delta)
    {
        if(ready&&!applying){TryApply();return;}timer-=(float)delta;if(timer<=0&&!busy){timer=180;_ = Check();}
    }
    public static bool ValidManifest(UpdateManifest m)=>m.Format==1&&m.Version!=null&&System.Text.RegularExpressions.Regex.IsMatch(m.Version,@"^\d+\.\d+\.\d+(\.\d+)?$")&&Version.TryParse(m.Version,out _)&&m.Version.Length<=24&&m.Package=="JosCraft-windows-x64.zip"&&m.Bytes>0&&m.Bytes<1_500_000_000&&m.Sha256 is {Length:64}&&m.Sha256.All(Uri.IsHexDigit);
    async Task Check()
    {
        busy=true;
        try
        {
            // The documented release CDN link avoids unauthenticated API quotas shared by a family's IP.
            using var response=await http.GetAsync("https://github.com/"+Repository+"/releases/latest/download/update.json");
            if(response.StatusCode==HttpStatusCode.NotFound)return;response.EnsureSuccessStatusCode();
            var m=JsonSerializer.Deserialize<UpdateManifest>(await response.Content.ReadAsStringAsync());
            if(m==null||!ValidManifest(m)||Version.Parse(m.Version)<=Version.Parse(CurrentVersion))return;
            if(File.Exists(Path.Combine(installRoot,"failed-"+m.Version+".txt")))return;
            string url="https://github.com/"+Repository+"/releases/download/v"+m.Version+"/"+m.Package;
            string cache=Path.Combine(installRoot,"downloads");Directory.CreateDirectory(cache);zip=Path.Combine(cache,m.Version+".zip");
            using(var download=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead))
            {
                download.EnsureSuccessStatusCode();if(download.Content.Headers.ContentLength is {} length&&length!=m.Bytes)throw new IOException("Invalid update size");await using var input=await download.Content.ReadAsStreamAsync();await using var output=File.Create(zip+".partial");
                byte[] bytes=new byte[65536];long total=0;int read;
                while((read=await input.ReadAsync(bytes))>0){total+=read;if(total>m.Bytes)throw new IOException("Invalid update length");await output.WriteAsync(bytes.AsMemory(0,read));}
                if(total!=m.Bytes)throw new IOException("Incomplete update");
            }
            await using(var file=File.OpenRead(zip+".partial")){string sha=Convert.ToHexString(await SHA256.HashDataAsync(file));if(!sha.Equals(m.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid update checksum");}
            File.Move(zip+".partial",zip,true);pending=m;ready=true;
        }
        catch(Exception e){GD.Print("JOSCRAFT update postponed: "+e.GetType().Name);timer=300;}
        finally{busy=false;}
    }
    void TryApply()
    {
        var world=GetTree().Root.GetChildren().OfType<BlockWorld>().FirstOrDefault();
        if(world!=null)
        {
            // Defer while holding/crafting: never force-close an inventory that cannot return its items.
            if(world.UiMode==GameMode.Inventory)return;
            world.SetPaused(true);bool saved=NetSession.Mode==NetMode.Single?world.SaveSnapshot(GameSession.SavePath):NetSession.Instance!.PrepareForUpdate(()=>world.SaveSnapshot(GameSession.SavePath));
            if(!saved){ready=false;world.Notify("Aktualizace počká: svět se nepodařilo uložit.");return;}
        }
        else if(NetSession.Mode!=NetMode.Single)NetSession.Instance!.PrepareForUpdate(()=>true);
        applying=true;overlay=new CanvasLayer{Layer=110};AddChild(overlay);var bg=new ColorRect{Color=new Color("142637")};bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);overlay.AddChild(bg);
        message=new Label{Text="Aktualizace JOSCRAFTu\nSvět je uložený. Za okamžik se vrátíš do hry…",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);overlay.AddChild(message);_ = Restart(world!=null);
    }
    async Task Restart(bool resume)
    {
        await ToSignal(GetTree().CreateTimer(1.5),SceneTreeTimer.SignalName.Timeout);
        string helper=Path.Combine(OS.GetExecutablePath().GetBaseDir(),"apply-update.ps1");
        var args=new System.Collections.Generic.List<string>{"-NoProfile","-ExecutionPolicy","Bypass","-WindowStyle","Hidden","-File",helper,"-Root",installRoot,"-Zip",zip,"-Version",pending!.Version,"-Sha256",pending.Sha256,"-WaitPid",OS.GetProcessId().ToString()};if(resume)args.Add("-Resume");
        int pid=OS.CreateProcess("powershell.exe",args.ToArray(),false);
        if(pid<0){message!.Text="Aktualizaci se nepodařilo spustit. Zavři a znovu otevři hru.";applying=false;ready=false;return;}GetTree().Quit();
    }
}
