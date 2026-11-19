// JOSCRAFT multiplayer – snapshot/delta codec and client-side assembler (Godot-independent).
//
// Ordering contract (solves "snapshot vs live changes"):
//  * Every world mutation on the host gets a global, gap-free sequence number (WorldSeq).
//  * When a client is admitted, the host captures a frozen copy of the world at sequence S and sends
//    Welcome(S, counts), then bounded batches over several frames on ChWorld (reliable, ordered).
//  * Live deltas with seq S+1, S+2, ... are sent to that peer immediately on the SAME channel, so they may
//    interleave with snapshot batches. The assembler buffers them, applies the frozen snapshot first and then
//    replays deltas strictly in order. A gap or a duplicate is a fatal desync → disconnect with a message.
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record EditDto(int X, int Y, int Z, int Type);
public sealed record PickupDto(uint NetId, string Id, int Type, int Count, float X, float Y, float Z, float Age, float Vx, float Vy, float Vz, float Rx, float Ry, float Rz);
public sealed record CreatureDto(uint NetId, string Id, string Kind, float X, float Y, float Z, float Yaw, bool Tamed,bool Following);
public sealed record WelcomeDto(byte Slot, int Seed, uint BaseSeq, int TreeCount, int EditCount, int PickupCount, int CreatureCount,
    float TimeOfDay, byte Character, float X, float Y, float Z, float Yaw, float Pitch, int Health, string HostVersion);

public static class Kinds
{
    // Creature kinds are a closed set; strings never come from arbitrary client input.
    public static readonly string[] All = { "dog", "rabbit", "fox", "carrot", "berries", "zombie" };
    public static byte ToByte(string kind) { int i = Array.IndexOf(All, kind); if (i < 0) throw new ArgumentException("kind"); return (byte)i; }
    public static string FromByte(byte b) => b < All.Length ? All[b] : throw new NetFormatException("kind");
}

public static class WorldCodec
{
    public static bool ValidPickupId(string id) => id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    public static byte[] Welcome(WelcomeDto w) => new NetWriter(Msg.Welcome)
        .U16(NetProtocol.ProtocolVersion).U8(w.Slot).I32(w.Seed).U32(w.BaseSeq).I32(w.TreeCount).I32(w.EditCount).I32(w.PickupCount).I32(w.CreatureCount)
        .F32(w.TimeOfDay).U8(w.Character).F32(w.X).F32(w.Y).F32(w.Z).F32(w.Yaw).F32(w.Pitch).U8((byte)w.Health).Str(w.HostVersion, 32).ToArray();
    public static WelcomeDto ReadWelcome(ReadOnlySpan<byte> p)
    {
        var r = new NetReader(p);
        if (r.U16() != NetProtocol.ProtocolVersion) throw new NetFormatException("protocol");
        var w = new WelcomeDto(r.U8(), r.I32(), r.U32(), r.I32(), r.I32(), r.I32(), r.I32(), r.F32(0, 1), r.U8(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.U8(), r.Str(32));
        r.End();
        if (w.Slot is < 1 or >= NetProtocol.MaxPlayers || w.TreeCount is < 0 or > 10000 || w.EditCount is < 0 or > NetProtocol.MaxEdits + 1000 || w.PickupCount is < 0 or > 10000 || w.CreatureCount is < 0 or > 512 || w.Health > 20)
            throw new NetFormatException("welcome counts");
        return w;
    }

    public static IEnumerable<byte[]> TreeBatches(IReadOnlyList<int> trees)
    {
        for (int i = 0; i < trees.Count; i += NetProtocol.SnapshotTreesPerBatch)
        {
            int n = Math.Min(NetProtocol.SnapshotTreesPerBatch, trees.Count - i);
            var w = new NetWriter(Msg.SnapTrees).U16((ushort)n);
            for (int k = 0; k < n; k++) w.U16((ushort)trees[i + k]);
            yield return w.ToArray();
        }
    }
    public static IEnumerable<byte[]> EditBatches(IReadOnlyList<EditDto> edits)
    {
        for (int i = 0; i < edits.Count; i += NetProtocol.SnapshotEditsPerBatch)
        {
            int n = Math.Min(NetProtocol.SnapshotEditsPerBatch, edits.Count - i);
            var w = new NetWriter(Msg.SnapEdits).U16((ushort)n);
            for (int k = 0; k < n; k++) { var e = edits[i + k]; w.Cell(e.X, e.Y, e.Z).U8((byte)e.Type); }
            yield return w.ToArray();
        }
    }
    public static IEnumerable<byte[]> PickupBatches(IReadOnlyList<PickupDto> pickups)
    {
        for (int i = 0; i < pickups.Count; i += NetProtocol.SnapshotPickupsPerBatch)
        {
            int n = Math.Min(NetProtocol.SnapshotPickupsPerBatch, pickups.Count - i);
            var w = new NetWriter(Msg.SnapPickups).U16((ushort)n);
            for (int k = 0; k < n; k++) WritePickup(w, pickups[i + k]);
            yield return w.ToArray();
        }
    }
    public static IEnumerable<byte[]> CreatureBatches(IReadOnlyList<CreatureDto> creatures)
    {
        for (int i = 0; i < creatures.Count; i += NetProtocol.SnapshotCreaturesPerBatch)
        {
            int n = Math.Min(NetProtocol.SnapshotCreaturesPerBatch, creatures.Count - i);
            var w = new NetWriter(Msg.SnapCreatures).U16((ushort)n);
            for (int k = 0; k < n; k++) WriteCreature(w, creatures[i + k]);
            yield return w.ToArray();
        }
    }
    public static byte[] SnapEnd(uint baseSeq) => new NetWriter(Msg.SnapEnd).U32(baseSeq).ToArray();

    public static void WritePickup(NetWriter w, PickupDto p) => w.U32(p.NetId).Str(p.Id, 64).U8((byte)p.Type).U8((byte)p.Count)
        .F32(p.X).F32(p.Y).F32(p.Z).F32(p.Age).F32(p.Vx).F32(p.Vy).F32(p.Vz).F32(p.Rx).F32(p.Ry).F32(p.Rz);
    public static PickupDto ReadPickup(ref NetReader r)
    {
        var p = new PickupDto(r.U32(), r.Str(64), r.U8(), r.U8(), r.F32(-60, 60), r.F32(-20, 50), r.F32(-60, 60), r.F32(0, 1e7f),
            r.F32(-100, 100), r.F32(-100, 100), r.F32(-100, 100), r.F32(-1e5f, 1e5f), r.F32(-1e5f, 1e5f), r.F32(-1e5f, 1e5f));
        if (!ValidPickupId(p.Id) || !BlockCatalog.IsCollectible(p.Type) || p.Count is < 1 or > 64) throw new NetFormatException("pickup");
        return p;
    }
    public static void WriteCreature(NetWriter w, CreatureDto c) => w.U32(c.NetId).Str(c.Id, 64).U8(Kinds.ToByte(c.Kind)).F32(c.X).F32(c.Y).F32(c.Z).F32(c.Yaw).Bool(c.Tamed).Bool(c.Following);
    public static CreatureDto ReadCreature(ref NetReader r)
    {
        var c = new CreatureDto(r.U32(), r.Str(64), Kinds.FromByte(r.U8()), r.F32(-80, 80), r.F32(-20, 60), r.F32(-80, 80), r.F32(-100, 100), r.Bool(),r.Bool());
        if (!ValidPickupId(c.Id)) throw new NetFormatException("creature id");
        return c;
    }

    // ---- sequenced deltas (ChWorld) ----
    public static byte[] BlockChanged(uint seq, int x, int y, int z, int type) => new NetWriter(Msg.BlockChanged).U32(seq).Cell(x, y, z).U8((byte)type).ToArray();
    public static byte[] PickupSpawn(uint seq, PickupDto p) { var w = new NetWriter(Msg.PickupSpawn).U32(seq); WritePickup(w, p); return w.ToArray(); }
    public static byte[] PickupRemove(uint seq, uint netId) => new NetWriter(Msg.PickupRemove).U32(seq).U32(netId).ToArray();
    public static byte[] CreatureSpawn(uint seq, CreatureDto c) { var w = new NetWriter(Msg.CreatureSpawn).U32(seq); WriteCreature(w, c); return w.ToArray(); }
    public static byte[] CreatureRemove(uint seq, uint netId) => new NetWriter(Msg.CreatureRemove).U32(seq).U32(netId).ToArray();

    public static bool IsSequencedDelta(Msg m) => m is Msg.BlockChanged or Msg.PickupSpawn or Msg.PickupRemove or Msg.CreatureSpawn or Msg.CreatureRemove or Msg.ShapeChanged;
    public static uint DeltaSeq(ReadOnlySpan<byte> packet) { var r = new NetReader(packet); return r.U32(); }
}

/// <summary>Client-side assembly of the join snapshot plus buffered, strictly ordered deltas.</summary>
public sealed class SnapshotAssembler
{
    public enum Stage { WaitingWelcome, Trees, Edits, Pickups, Creatures, Complete, Failed }
    public Stage Current { get; private set; } = Stage.WaitingWelcome;
    public WelcomeDto? Welcome { get; private set; }
    public readonly List<int> Trees = new();
    public readonly List<EditDto> Edits = new();
    public readonly List<PickupDto> Pickups = new();
    public readonly List<CreatureDto> Creatures = new();
    public readonly List<PlacedBlockSave> Shapes=new();
    readonly Queue<byte[]> buffered = new();
    uint nextSeq;
    public string Error { get; private set; } = "";
    public uint NextExpectedSeq => nextSeq;

    public float Progress
    {
        get
        {
            if (Welcome == null) return 0; if (Current == Stage.Complete) return 1;
            float total = Welcome.TreeCount + Welcome.EditCount + Welcome.PickupCount + Welcome.CreatureCount + 1;
            return (Trees.Count + Edits.Count + Pickups.Count + Creatures.Count) / total;
        }
    }

    bool Fail(string message) { Current = Stage.Failed; Error = message; return false; }

    public bool OnWelcome(WelcomeDto w)
    {
        if (Current != Stage.WaitingWelcome) return Fail("duplicitní uvítání");
        Welcome = w; nextSeq = w.BaseSeq + 1; Current = Stage.Trees; Advance(); return true;
    }
    void Advance()
    {
        var w = Welcome!;
        if (Current == Stage.Trees && Trees.Count == w.TreeCount) Current = Stage.Edits;
        if (Current == Stage.Edits && Edits.Count == w.EditCount) Current = Stage.Pickups;
        if (Current == Stage.Pickups && Pickups.Count == w.PickupCount) Current = Stage.Creatures;
    }

    /// <summary>Feed every ChWorld packet here (snapshot batches and deltas alike).</summary>
    public bool OnWorldPacket(byte[] packet)
    {
        if (Current == Stage.Failed) return false;
        var type = NetReader.TypeOf(packet);
        try
        {
            if (WorldCodec.IsSequencedDelta(type))
            {
                if (Welcome == null) return Fail("změna před uvítáním");
                if (buffered.Count >= NetProtocol.MaxBufferedDeltas) return Fail("příliš mnoho změn během stahování světa");
                buffered.Enqueue(packet); return true;
            }
            var r = new NetReader(packet);
            switch (type)
            {
                case Msg.SnapTrees:
                    if (Current != Stage.Trees) return Fail("pořadí snímku (stromy)");
                    { int n = r.U16(); if (Trees.Count + n > Welcome!.TreeCount) return Fail("počet stromů");
                      for (int i = 0; i < n; i++) { int id = r.U16(); if (id >= 64) return Fail("id stromu"); Trees.Add(id); } }
                    break;
                case Msg.SnapEdits:
                    if (Current != Stage.Edits) return Fail("pořadí snímku (bloky)");
                    { int n = r.U16(); if (n > NetProtocol.SnapshotEditsPerBatch || Edits.Count + n > Welcome!.EditCount) return Fail("počet bloků");
                      for (int i = 0; i < n; i++)
                      { var c = r.Cell(); int t = r.U8();
                        if (c.Y < 1 || c.Y > NetProtocol.MaxBuildY || !BlockCatalog.IsBlock((BlockType)t)) return Fail("neplatný blok");
                        Edits.Add(new(c.X, c.Y, c.Z, t)); } }
                    break;
                case Msg.SnapPickups:
                    if (Current != Stage.Pickups) return Fail("pořadí snímku (dřevo)");
                    { int n = r.U16(); if (n > NetProtocol.SnapshotPickupsPerBatch || Pickups.Count + n > Welcome!.PickupCount) return Fail("počet kusů dřeva");
                      for (int i = 0; i < n; i++) Pickups.Add(WorldCodec.ReadPickup(ref r)); }
                    break;
                case Msg.SnapCreatures:
                    if (Current != Stage.Creatures) return Fail("pořadí snímku (zvířata)");
                    { int n = r.U16(); if (n > NetProtocol.SnapshotCreaturesPerBatch || Creatures.Count + n > Welcome!.CreatureCount) return Fail("počet zvířat");
                      for (int i = 0; i < n; i++) Creatures.Add(WorldCodec.ReadCreature(ref r)); }
                    break;
                case Msg.SnapShapes:
                    {if(Current!=Stage.Creatures) return Fail("pořadí tvarovaných bloků");int n=r.U16();if(n>200||Shapes.Count+n>10000)return Fail("počet tvarovaných bloků");for(int i=0;i<n;i++)Shapes.Add(ShapeCodec.Read(ref r));}
                    break;
                case Msg.SnapEnd:
                    { uint s = r.U32(); r.End();
                      if (Current != Stage.Creatures || Creatures.Count != Welcome!.CreatureCount || s != Welcome.BaseSeq) return Fail("neúplný snímek světa");
                      if (Pickups.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != Pickups.Count) return Fail("duplicitní dřevo");
                      Current = Stage.Complete; return true; }
                default: return Fail("neznámá zpráva světa");
            }
            r.End(); Advance(); return true;
        }
        catch (NetFormatException e) { return Fail("poškozená data světa (" + e.Message + ")"); }
    }

    /// <summary>After <see cref="Stage.Complete"/>: returns buffered deltas in order; also used for every later delta.</summary>
    public bool TryAcceptDelta(byte[] packet, out byte[]? accepted)
    {
        accepted = null;
        if (Current != Stage.Complete) { return OnWorldPacket(packet); }
        uint seq;
        try { seq = WorldCodec.DeltaSeq(packet); } catch (NetFormatException) { return Fail("poškozená změna"); }
        if (seq != nextSeq) return Fail($"svět se rozešel (čekáno {nextSeq}, přišlo {seq})");
        nextSeq++; accepted = packet; return true;
    }

    public IEnumerable<byte[]> DrainBuffered()
    {
        while (buffered.Count > 0)
        {
            var p = buffered.Dequeue();
            if (!TryAcceptDelta(p, out var ok) || ok == null) yield break;
            yield return ok;
        }
    }
}
