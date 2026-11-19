// JOSCRAFT multiplayer – automated two-process QA (no GDScript). Writes JSON into res://artifacts/net-qa/.
// Roles (Godot user args after "--"):
//   --net-qa=host  --net-qa-port=24601 [--net-qa-seconds=60]
//   --net-qa=join  --net-qa-port=24601 --net-profile=A          (second instance: --net-profile=B)
//   --net-qa=badversion --net-qa-port=24601 --net-qa-version=qa-old --net-profile=C
//   --net-qa=garbage    --net-qa-port=24601 --net-profile=D
// Every role runs with GameSession.IsQa=true (HOOK: add "--net-qa" to the IsQa lists), so the host save goes to
// artifacts/game-qa-data/session.json and saves/ is never touched.
// NAVRH: runtime/Net/NetQa.cs
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class NetQa
{
    public static string? Role => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--net-qa="))?["--net-qa=".Length..];
    public static bool Active => Role != null;
    static int Arg(string name, int fallback) => int.TryParse(OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..], out int v) ? v : fallback;
    static string Tag => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--net-qa-tag="))?["--net-qa-tag=".Length..] is { Length: > 0 and <= 8 } t && t.All(char.IsAsciiLetterOrDigit) ? "-" + t : "";
    public static int Port => Math.Clamp(Arg("--net-qa-port", 24601), 1024, 65535);
    static string OutDir => ProjectSettings.GlobalizePath("res://artifacts/net-qa");
    static void Write(string file, object data)
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static string WorldDigest(BlockWorld w)
    {
        var sb = new StringBuilder();
        sb.Append(w.Terrain.Seed).Append('|').Append(string.Join(",", w.Terrain.TreeExclusions)).Append('|');
        foreach (var e in w.Terrain.GetChanges()) sb.Append(e.X).Append(',').Append(e.Y).Append(',').Append(e.Z).Append(':').Append(e.Type).Append(';');
        sb.Append('|').Append(string.Join(",", w.WoodPickups.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    /// <summary>HOOK: GameMenu._Ready – start the role straight from the menu scene.</summary>
    public static bool StartFromMenu(GameMenu menu)
    {
        if (!Active) return false;
        var session = NetSession.Ensure(menu.GetTree());
        switch (Role)
        {
            case "host": session.PrepareHost(Port); menu.StartNew(4242, 0); return true;
            case "join": case "badversion":
                session.BeginJoin("127.0.0.1", Port, Role == "join" ? 3 : 1, _ => { },
                    failed: text => { Write($"{Role}-{NetIdentity.Profile}.json", new { role = Role, rejected = true, message = text }); menu.GetTree().Quit(Role == "badversion" ? 0 : 1); });
                return true;
            case "garbage": _ = Garbage(menu); return true;
        }
        return false;
    }

    /// <summary>HOOK: BlockWorld._Ready – `else if (NetQa.Active) CallDeferred(StartNetQa)` before the IsQa fallback.</summary>
    public static async Task RunInWorld(BlockWorld world)
    {
        var tree = world.GetTree();
        async Task Wait(double seconds) { var t = Time.GetTicksMsec() + seconds * 1000; while (Time.GetTicksMsec() < t) await world.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
        while (!world.CharacterReady || world.Terrain.PendingChunkCount > 0) await world.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (Role == "host") await Host(world, Wait); else if (Role == "join") await Join(world, Wait);
    }

    static async Task Host(BlockWorld world, Func<double, Task> wait)
    {
        // A world that is ALREADY modified before anyone joins: 24 mined cells, 6 placed stones, 3 lying logs.
        var spawn = world.Terrain.SpawnPosition; int placed = 0, mined = 0;
        for (int i = 0; i < 24; i++) { int x = 6 + i % 6, z = -8 + i / 6; var c = new Vector3I(x, world.Terrain.HeightAt(x, z) - 1, z); if (c.Y >= 1 && world.Terrain.ApplyAuthoritativeBlock(c, BlockType.Air)) mined++; }
        for (int i = 0; i < 6; i++) { int x = -10 + i, z = 9; var c = new Vector3I(x, world.Terrain.HeightAt(x, z), z); if (world.Terrain.ApplyAuthoritativeBlock(c, BlockType.Stone)) placed++; }
        for (int i = 0; i < 3; i++) world.SpawnWoodPickup((int)BlockType.OakLog, new Vector3(12.5f + i, world.Terrain.HeightAt(12 + i, 12) + 1.2f, 12.5f), "qa-log-" + i);
        world.Shapes.Place(new Vector3I(3,4,-2),36,1);world.Shapes.Place(new Vector3I(3,4,2),38,0);world.Shapes.Place(new Vector3I(5,4,2),34,2);
        var equipped=new HashSet<string>();
        double seconds = Math.Clamp(Arg("--net-qa-seconds", 60), 10, 600), end = Time.GetTicksMsec() / 1000.0 + seconds;
        var timeline = new List<object>();
        while (Time.GetTicksMsec() / 1000.0 < end)
        {
            await wait(1);
            var s = NetSession.Instance?.Server;
            if(s!=null)foreach(var p in s.Players)if(equipped.Add(p.Key))p.Inv.AddResource(10,8);
            timeline.Add(new { t = Math.Round(end - Time.GetTicksMsec() / 1000.0, 1), digest = WorldDigest(world), seq = s?.WorldSeq ?? 0, players = s?.Players.Count() ?? 0 });
            Write("host.json", new
            {
                role = "host", mined, placed, digest = WorldDigest(world), edits = world.Terrain.EditCount, pickups = world.WoodPickups.Count,
                players = s?.Players.Select(p => new { slot = p.Slot, state = p.State.ToString(), items = p.Inv.TotalItems(), key8 = p.Key[..8] }).ToArray(),
                remoteSaves = s?.ExportAll().Select(r => new { key8 = r.Key[..8], items = r.Slots.Sum(x => x.Count) + r.Crafting.Sum(x => x.Count) }).ToArray(),
                timeline = timeline.TakeLast(20).ToArray()
            });
        }
        bool saved = world.SaveSnapshot(GameSession.SavePath);   // IsQa → artifacts/game-qa-data/session.json
        var check = GameSession.Read(GameSession.SavePath);
        Write("host-final.json", new { saved, savePath = GameSession.SavePath, remotePlayersInSave = check?.RemotePlayers?.Length ?? -1, digest = WorldDigest(world) });
        NetSession.Instance?.EndSession(ShutdownReason.HostQuit, "QA hostitel končí.");
        await wait(.5); world.GetTree().Quit(0);
    }

    static async Task Join(BlockWorld world, Func<double, Task> wait)
    {
        var client = NetSession.Instance?.Client; var results = new Dictionary<string, object>();
        await wait(2);
        results["digestAfterJoin"] = WorldDigest(world);
        results["editsAfterJoin"] = world.Terrain.EditCount; results["pickupsAfterJoin"] = world.WoodPickups.Count;
        results["slot"] = client?.Slot ?? 0;
        results["itemsAfterJoin"] = Enumerable.Range(0, 36).Sum(i => world.GetSlot(i).Count);   // reconnect check: equals itemsEnd of the previous run
        world.SetPaused(true);world.GameUi.Visible=false;world.Camera.Position=new Vector3(9,10,11);world.Camera.LookAt(new Vector3(0,4.5f,0));await wait(.3);
        var avatars=world.GetChildren().OfType<RemoteAvatar>().ToArray();results["renderedRemotePlayers"]=avatars.Length;
        results["remoteFeetOnPhysics"]=avatars.All(a=>{
            float bottom=PoseGeometry.Floor(a);using var query=PhysicsRayQueryParameters3D.Create(a.Position+Vector3.Up*.15f,a.Position-Vector3.Up*.2f,1);
            var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(query);return hit.Count>0&&Math.Abs(bottom-hit["position"].AsVector3().Y)<.1f;
        });
        await world.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);world.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(OutDir,"players-"+NetIdentity.Profile+Tag+".png"));world.GameUi.Visible=true;world.SetPaused(false);await wait(.2);
        results["shapeSnapshot"] = world.Shapes.At(new Vector3I(3,4,-2)) is {Type:36,Rotation:1}&&world.Shapes.At(new Vector3I(5,4,2)) is {Type:34,Rotation:2};
        if(NetIdentity.Profile=="A"&&Tag.Length==0)
        {
            var door=new Vector3I(3,4,-2);var chest=new Vector3I(3,4,2);
            client?.UseBlock(door);await wait(.7);results["doorToggleReplicates"]=world.Shapes.At(door)?.Open==true;
            client?.UseBlock(door);await wait(.7);
            client?.UseBlock(chest);await wait(.7);results["chestOpensRemotely"]=world.IsChestOpen&&world.UiMode==GameMode.Inventory&&world.Shapes.At(chest)?.Open==true;
            int plank=Enumerable.Range(0,36).FirstOrDefault(i=>world.GetSlot(i).Type==10);int beforePlanks=world.InventoryCount(10);
            client?.UiClick(0,plank,false);await wait(1.3);results["autosavePreservesCarried"]=world.CarriedStack==new ItemStack(10,beforePlanks);client?.UiClick(3,0,false);await wait(.3);
            results["chestStoreDebug"]=new{plank,beforePlanks,slot=world.GetSlot(plank),stored=world.GetChestStack(0),carried=world.CarriedStack,station=world.OpenStation?.ToString(),mode=world.UiMode.ToString(),feedback=world.Feedback};
            results["chestStoresOnHost"]=world.GetChestStack(0)==new ItemStack(10,beforePlanks)&&world.CarriedStack.Count==0&&world.InventoryCount(10)==0;
            world.ToggleInventory();await wait(.7);
            client?.UseBlock(chest);await wait(.7);results["chestReopenRetainsContents"]=world.GetChestStack(0)==new ItemStack(10,beforePlanks);
            client?.UiClick(3,0,false);await wait(.3);client?.UiClick(0,plank,false);await wait(.3);world.ToggleInventory();await wait(.7);
            results["chestWithdrawPreservesItems"]=world.InventoryCount(10)==beforePlanks&&world.CarriedStack.Count==0;
        }
        // 1) legit mining of the block under our feet (reach + LOS are trivially fine)
        var feet = world.Body.Position; var cell = new Vector3I(Mathf.FloorToInt(feet.X), Mathf.FloorToInt(feet.Y - .1f) - 0, Mathf.FloorToInt(feet.Z));
        if (world.Terrain.GetBlock(cell) == BlockType.Air) cell += Vector3I.Down;
        var type = world.Terrain.GetBlock(cell); int before = world.InventoryCount(BlockCatalog.DropFor((int)type));
        client?.MineStart(cell); await wait(BlockCatalog.MiningDuration((int)type, 0) + .4); client?.MineFinish(cell); await wait(1.2);
        results["mineCell"] = new[] { cell.X, cell.Y, cell.Z }; results["mineType"] = (int)type;
        results["mineRemovedBlock"] = world.Terrain.GetBlock(cell) == BlockType.Air;
        results["mineGaveOne"] = world.InventoryCount(BlockCatalog.DropFor((int)type)) - before;
        // 2) cheat attempts – must all be refused by the host
        var far = new Vector3I(-40, world.Terrain.HeightAt(-40, -40) - 1, -40);
        var farType = world.Terrain.GetBlock(far);
        client?.MineStart(far); client?.MineFinish(far); await wait(1);
        results["farMineRefused"] = world.Terrain.GetBlock(far) == farType;
        var near = cell + new Vector3I(1, 0, 0); var nearType = world.Terrain.GetBlock(near);
        client?.MineFinish(near); await wait(1);                      // finish without start → refused
        results["instantMineRefused"] = world.Terrain.GetBlock(near) == nearType;
        int itemsBefore = Enumerable.Range(0, 36).Sum(i => world.GetSlot(i).Count);
        client?.Place(cell + Vector3I.Down, cell, 5);                 // wrong slot (not selected) → strike, no change
        await wait(1);
        results["wrongSlotPlaceRefused"] = world.Terrain.GetBlock(cell) == BlockType.Air && Enumerable.Range(0, 36).Sum(i => world.GetSlot(i).Count) == itemsBefore;
        if(NetIdentity.Profile=="A"&&Tag.Length==0)
        {
            int slot=Enumerable.Range(0,9).FirstOrDefault(i=>world.GetSlot(i).Count>0);
            client?.SelectHotbar(slot);await wait(.2);var clicked=cell+(feet.X>0?Vector3I.Right:Vector3I.Left)*2;var placed=clicked+Vector3I.Up;int material=world.GetSlot(slot).Type,owned=world.InventoryCount(material);
            client?.Place(clicked,placed,slot);await wait(.7);client?.Place(clicked,placed,slot);await wait(.4);
            results["legitPlaceConsumesOnce"]=world.Terrain.GetBlockId(placed)==material&&world.InventoryCount(material)==owned-1;
            results["placementDebug"]=new{slot,material,owned,now=world.InventoryCount(material),target=new[]{clicked.X,clicked.Y,clicked.Z},targetType=world.Terrain.GetBlockId(clicked),atType=world.Terrain.GetBlockId(placed),feedback=world.Feedback};
        }
        results["digestEnd"] = WorldDigest(world);
        results["itemsEnd"] = Enumerable.Range(0, 36).Sum(i => world.GetSlot(i).Count);
        Write($"join-{NetIdentity.Profile}{Tag}.json", results);
        NetSession.Instance?.EndSession(ShutdownReason.HostMenu, "");
        await wait(.5); world.GetTree().Quit(0);
    }

    static string DescribeReject(byte[] p) { var r = new NetReader(p); var code = (RejectCode)r.U8(); return code + ": " + r.Str(200); }

    /// <summary>Raw ENet client that sends malformed data instead of a Hello. Expects a readable rejection, never a crash.</summary>
    static async Task Garbage(GameMenu menu)
    {
        var tree = menu.GetTree(); var peer = new ENetMultiplayerPeer(); peer.CreateClient("127.0.0.1", Port);
        double end = Time.GetTicksMsec() / 1000.0 + 8; bool sent = false; string? reply = null;
        while (Time.GetTicksMsec() / 1000.0 < end && reply == null)
        {
            await menu.ToSignal(tree, SceneTree.SignalName.ProcessFrame); peer.Poll();
            if (!sent && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
            {
                peer.TransferChannel = 0; peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable; peer.SetTargetPeer(1);
                peer.PutPacket(new byte[] { (byte)Msg.Place, 1, 2, 3 });                    // intent before handshake
                sent = true;
            }
            while (peer.GetAvailablePacketCount() > 0)
            {
                var p = peer.GetPacket();
                if (p.Length > 0 && (Msg)p[0] == Msg.Reject) reply = DescribeReject(p);
            }
        }
        Write($"garbage-{NetIdentity.Profile}.json", new { role = "garbage", sent, rejected = reply != null, reply });
        peer.Close(); tree.Quit(reply != null ? 0 : 1);
    }
}
