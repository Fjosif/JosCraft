// JOSCRAFT multiplayer – wire protocol (Godot-independent, unit-testable).
// NAVRH: po kontrole přejmenovat na runtime/Net/NetProtocol.cs
using System;
using System.Buffers.Binary;
using System.Text;

/// <summary>Constants shared by host and client. Bump <see cref="ProtocolVersion"/> on ANY wire change.</summary>
public static class NetProtocol
{
    public const int ProtocolVersion = 4;
    public const int DefaultPort = 24567;
    /// <summary>Total players including the host.</summary>
    public const int MaxPlayers = 4;
    /// <summary>ENet slots: three remote players plus one spare slot used only to send a readable "full" rejection.</summary>
    public const int EnetMaxClients = MaxPlayers;
    public const int ChannelCount = 3;
    public const int ChControl = 0;   // reliable: handshake, intents, inventory, health, feedback
    public const int ChWorld = 1;     // reliable ordered: snapshot + sequenced world deltas
    public const int ChMotion = 2;    // unreliable: transforms of players, pickups and creatures
    /// <summary>Hard upper bound for any single packet we send or accept.</summary>
    public const int MaxPacketBytes = 16 * 1024;
    /// <summary>Unreliable packets stay below one ENet MTU (1400 incl. headers) so they are never fragmented.</summary>
    public const int MaxUnreliableBytes = 1200;
    public const int SnapshotEditsPerBatch = 1500;     // 4 bytes each -> ~6 KB
    public const int SnapshotPickupsPerBatch = 80;
    public const int SnapshotCreaturesPerBatch = 80;
    public const int SnapshotTreesPerBatch = 1000;
    public const int MaxBufferedDeltas = 20000;
    public const int MaxNameLength = 16;
    public const int TokenBytes = 32;
    public const int MaxEdits = 249_000;               // GameSession.Read rejects > 250000
    public const float HelloTimeoutSeconds = 5f;

    /// <summary>World geometry mirrors BlockTerrain; duplicated so this file stays Godot-free. Checked by NetSession at start.</summary>
    public const int WorldMin = -48, WorldMaxExclusive = 48, MaxBuildY = 24;
}

public enum Msg : byte
{
    // client -> server
    Hello = 1, Motion = 2, MineStart = 3, MineCancel = 4, MineFinish = 5, Place = 6,
    SelectHotbar = 7, UiClick = 8, UiDrag = 9, Craft = 10, UseBlock = 11, CloseInventory = 12,
    Interact = 13, Attack = 14, Leave = 15, OpenInventory = 16, MoveSlot = 17, RequestRespawn = 18, ChooseRecipe = 19, FillRecipe=20,
    // server -> client
    Welcome = 64, Reject = 65, SnapTrees = 66, SnapEdits = 67, SnapPickups = 68, SnapCreatures = 69, SnapEnd = 70,
    BlockChanged = 71, PickupSpawn = 72, PickupRemove = 73, CreatureSpawn = 74, CreatureRemove = 75,
    PlayerJoined = 76, PlayerLeft = 77, MotionBatch = 78, Inventory = 79, Health = 80, Feedback = 81,
    /* 82 reserved (time of day travels inside MotionBatch) */ Respawn = 83, Correction = 84, Shutdown = 85, MineRejected = 86, SnapShapes=87, ShapeChanged=88,
}

public enum RejectCode : byte
{
    ProtocolMismatch = 1, VersionMismatch = 2, Full = 3, AlreadyConnected = 4, BadHello = 5,
    WorldNotReady = 6, Kicked = 7, Timeout = 8,
}

public enum ShutdownReason : byte { HostQuit = 1, HostUpdate = 2, HostMenu = 3, Kicked = 4 }

/// <summary>Thrown for any malformed input. Callers count it as a protocol strike.</summary>
public sealed class NetFormatException : Exception { public NetFormatException(string m) : base(m) { } }

/// <summary>Little-endian bounded writer. Throws if the packet would exceed <see cref="NetProtocol.MaxPacketBytes"/>.</summary>
public sealed class NetWriter
{
    byte[] buffer = new byte[256];
    public int Length { get; private set; }
    public NetWriter(Msg type) { U8((byte)type); }
    void Ensure(int extra)
    {
        if (Length + extra > NetProtocol.MaxPacketBytes) throw new InvalidOperationException("Packet exceeds MaxPacketBytes");
        if (Length + extra <= buffer.Length) return;
        Array.Resize(ref buffer, Math.Min(NetProtocol.MaxPacketBytes, Math.Max(buffer.Length * 2, Length + extra)));
    }
    public NetWriter U8(byte v) { Ensure(1); buffer[Length++] = v; return this; }
    public NetWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);
    public NetWriter I8(sbyte v) => U8(unchecked((byte)v));
    public NetWriter U16(ushort v) { Ensure(2); BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(Length), v); Length += 2; return this; }
    public NetWriter I32(int v) { Ensure(4); BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(Length), v); Length += 4; return this; }
    public NetWriter U32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(Length), v); Length += 4; return this; }
    public NetWriter F32(float v) { Ensure(4); BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(Length), v); Length += 4; return this; }
    public NetWriter Bytes(ReadOnlySpan<byte> v) { if (v.Length > 255) throw new ArgumentException("bytes>255"); U8((byte)v.Length); Ensure(v.Length); v.CopyTo(buffer.AsSpan(Length)); Length += v.Length; return this; }
    public NetWriter Str(string v, int maxChars)
    {
        if (v.Length > maxChars) v = v[..maxChars];
        var bytes = Encoding.UTF8.GetBytes(v);
        if (bytes.Length > 255) throw new ArgumentException("string too long");
        return Bytes(bytes);
    }
    public NetWriter Cell(int x, int y, int z) { I8((sbyte)x); U8((byte)y); I8((sbyte)z); return this; }
    public NetWriter Stack(int type, int count) { U8((byte)type); U8((byte)count); return this; }
    public byte[] ToArray() => buffer.AsSpan(0, Length).ToArray();
}

/// <summary>Bounded reader: every read is range-checked; trailing garbage is rejected by <see cref="End"/>.</summary>
public ref struct NetReader
{
    readonly ReadOnlySpan<byte> data; int pos;
    public NetReader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1 || data.Length > NetProtocol.MaxPacketBytes) throw new NetFormatException("packet size");
        this.data = data; pos = 1;
    }
    public static Msg TypeOf(ReadOnlySpan<byte> data) => data.Length < 1 ? 0 : (Msg)data[0];
    public int Remaining => data.Length - pos;
    void Need(int n) { if (n < 0 || pos + n > data.Length) throw new NetFormatException("truncated"); }
    public byte U8() { Need(1); return data[pos++]; }
    public bool Bool() { byte b = U8(); if (b > 1) throw new NetFormatException("bool"); return b == 1; }
    public sbyte I8() => unchecked((sbyte)U8());
    public ushort U16() { Need(2); var v = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]); pos += 2; return v; }
    public int I32() { Need(4); var v = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]); pos += 4; return v; }
    public uint U32() { Need(4); var v = BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]); pos += 4; return v; }
    public float F32()
    {
        Need(4); var v = BinaryPrimitives.ReadSingleLittleEndian(data[pos..]); pos += 4;
        if (!float.IsFinite(v)) throw new NetFormatException("float"); return v;
    }
    public float F32(float min, float max) { var v = F32(); if (v < min || v > max) throw new NetFormatException("float range"); return v; }
    public ReadOnlySpan<byte> Bytes(int maxLength)
    {
        int n = U8(); if (n > maxLength) throw new NetFormatException("bytes length"); Need(n);
        var s = data.Slice(pos, n); pos += n; return s;
    }
    public string Str(int maxChars)
    {
        var raw = Bytes(Math.Min(255, maxChars * 4));
        string s;
        try { s = new UTF8Encoding(false, true).GetString(raw); } catch (Exception) { throw new NetFormatException("utf8"); }
        if (s.Length > maxChars) throw new NetFormatException("string length");
        return s;
    }
    public (int X, int Y, int Z) Cell()
    {
        int x = I8(), y = U8(), z = I8();
        if (x < NetProtocol.WorldMin || x >= NetProtocol.WorldMaxExclusive || z < NetProtocol.WorldMin || z >= NetProtocol.WorldMaxExclusive || y > NetProtocol.MaxBuildY + 1)
            throw new NetFormatException("cell");
        return (x, y, z);
    }
    public int Range(int value, int min, int max) { if (value < min || value > max) throw new NetFormatException("range"); return value; }
    public void End() { if (pos != data.Length) throw new NetFormatException("trailing bytes"); }
}

/// <summary>Display-name sanitising. Names are cosmetic only; identity is the token hash.</summary>
public static class NetNames
{
    public static string Sanitize(string? raw)
    {
        var sb = new StringBuilder();
        foreach (var r in (raw ?? "").Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (sb.Length >= NetProtocol.MaxNameLength) break;
            if (Rune.IsLetterOrDigit(r) || r.Value == ' ' || r.Value == '-' || r.Value == '_') sb.Append(r.ToString());
        }
        var s = sb.ToString().Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length == 0 ? "Hráč" : s;
    }
}
