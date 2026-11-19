// JOSCRAFT multiplayer – server validation rules (Godot-independent, unit-testable).
using System;
using System.Collections.Generic;

public readonly record struct V3(float X, float Y, float Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public float HorizontalLength() => MathF.Sqrt(X * X + Z * Z);
    public bool Finite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}
public readonly record struct C3(int X, int Y, int Z)
{
    public static C3 operator +(C3 a, C3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static C3 operator -(C3 a, C3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public V3 Center => new(X + .5f, Y + .5f, Z + .5f);
}

public static class NetRules
{
    public const float Reach = 5.0f;              // BlockWorld.RayTarget
    public const float ReachSlack = 1.25f;        // latency + eye-height differences between heroes
    public const float CreatureReach = 3.6f;      // Ecology.TryInteract
    public const float AttackReach = 3.4f;        // Ecology.TryAttack
    public const float PickupRadius = 1.5f;       // WoodPickup.CollectionRadius
    public const float MaxRunSpeed = 4.5f;        // BlockWorld run speed
    public const float MaxFallSpeed = 45f;
    public const float JumpVelocity = 7f;
    public const float MiningEarlyToleranceSeconds = 1.0f; // early finish is deferred, not rejected
    public const float EyeHeight = 1.6f;          // conservative average of 1.32 / 1.72

    public static bool InsideWorld(int x, int z) => x >= NetProtocol.WorldMin && z >= NetProtocol.WorldMin && x < NetProtocol.WorldMaxExclusive && z < NetProtocol.WorldMaxExclusive;
    public static bool ValidEditCell(C3 c) => InsideWorld(c.X, c.Z) && c.Y >= 1 && c.Y <= NetProtocol.MaxBuildY;

    /// <summary>Distance from a point to the nearest point of a unit voxel.</summary>
    public static float DistanceToCell(V3 p, C3 c)
    {
        float dx = MathF.Max(MathF.Max(c.X - p.X, 0), p.X - (c.X + 1));
        float dy = MathF.Max(MathF.Max(c.Y - p.Y, 0), p.Y - (c.Y + 1));
        float dz = MathF.Max(MathF.Max(c.Z - p.Z, 0), p.Z - (c.Z + 1));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    public static bool InReach(V3 eye, C3 cell, float reach = Reach) => DistanceToCell(eye, cell) <= reach + ReachSlack;

    /// <summary>
    /// Voxel DDA (Amanatides–Woo). Returns true if the segment eye→center(target) reaches the target
    /// without passing through another solid voxel. Uses voxel data only, so it also works for chunks
    /// the host has not built. <paramref name="solid"/> must treat out-of-world as air.
    /// </summary>
    public static bool LineOfSight(V3 eye, C3 target, Func<int, int, int, bool> solid, int maxSteps = 64,V3? targetPoint=null)
    {
        var to = targetPoint??target.Center; var d = to - eye; float len = d.Length();
        if (len < 1e-4f) return true;
        var dir = d * (1f / len);
        int x = (int)MathF.Floor(eye.X), y = (int)MathF.Floor(eye.Y), z = (int)MathF.Floor(eye.Z);
        int sx = MathF.Sign(dir.X), sy = MathF.Sign(dir.Y), sz = MathF.Sign(dir.Z);
        float tdx = sx != 0 ? MathF.Abs(1f / dir.X) : float.PositiveInfinity;
        float tdy = sy != 0 ? MathF.Abs(1f / dir.Y) : float.PositiveInfinity;
        float tdz = sz != 0 ? MathF.Abs(1f / dir.Z) : float.PositiveInfinity;
        float Next(float p, int cell, int s, float td) => s > 0 ? (cell + 1 - p) * td : s < 0 ? (p - cell) * td : float.PositiveInfinity;
        float tx = Next(eye.X, x, sx, tdx), ty = Next(eye.Y, y, sy, tdy), tz = Next(eye.Z, z, sz, tdz);
        for (int i = 0; i < maxSteps; i++)
        {
            if (x == target.X && y == target.Y && z == target.Z) return true;
            // The eye's own voxel never blocks (head may sit inside a leaf/edge voxel).
            if (i > 0 && solid(x, y, z)) return false;
            if (tx <= ty && tx <= tz) { if (tx > len + 1e-3f) break; x += sx; tx += tdx; }
            else if (ty <= tz) { if (ty > len + 1e-3f) break; y += sy; ty += tdy; }
            else { if (tz > len + 1e-3f) break; z += sz; tz += tdz; }
        }
        return x == target.X && y == target.Y && z == target.Z;
    }

    /// <summary>Six-neighbour check: placement must be face-adjacent to the clicked solid block.</summary>
    public static bool FaceAdjacent(C3 a, C3 b)
    {
        var d = a - b; return Math.Abs(d.X) + Math.Abs(d.Y) + Math.Abs(d.Z) == 1;
    }

    /// <summary>
    /// Plausibility of a client-reported move. Movement is client-simulated in this first version;
    /// the server only rejects teleports, speed hacks, out-of-world and non-finite positions.
    /// </summary>
    public static bool PlausibleMove(V3 from, V3 to, float dtSeconds, out string reason)
    {
        reason = "";
        if (!to.Finite) { reason = "nan"; return false; }
        if (to.X < NetProtocol.WorldMin - 1 || to.X > NetProtocol.WorldMaxExclusive + 1 || to.Z < NetProtocol.WorldMin - 1 || to.Z > NetProtocol.WorldMaxExclusive + 1 || to.Y < -9 || to.Y > NetProtocol.MaxBuildY + 10) { reason = "bounds"; return false; }
        float dt = Math.Clamp(dtSeconds, .02f, 1.0f);
        var delta = to - from;
        if (delta.HorizontalLength() > MaxRunSpeed * 1.35f * dt + .6f) { reason = "speed"; return false; }
        if (delta.Y > JumpVelocity * dt + 1.6f) { reason = "climb"; return false; }   // jump ≈1.36 m apex + step
        if (-delta.Y > MaxFallSpeed * dt + 1.0f) { reason = "fall"; return false; }
        return true;
    }

    /// <summary>Required mining time; mirrors BlockCatalog.MiningDuration(target, tool).</summary>
    public static float MiningSeconds(int blockType, int toolType) => BlockCatalog.MiningDuration(blockType, toolType);

    /// <summary>AABB of a player standing at <paramref name="feet"/>. Generous so blocks cannot be placed into anyone.</summary>
    public static bool CellIntersectsPlayer(C3 cell, V3 feet, float halfWidth = .45f, float height = 1.9f)
    {
        return cell.X < feet.X + halfWidth && cell.X + 1 > feet.X - halfWidth &&
               cell.Z < feet.Z + halfWidth && cell.Z + 1 > feet.Z - halfWidth &&
               cell.Y < feet.Y + height && cell.Y + 1 > feet.Y;
    }
}

/// <summary>Token bucket. Not thread-safe (server is single-threaded).</summary>
public sealed class RateLimiter
{
    readonly double rate, burst; double tokens, last;
    public RateLimiter(double perSecond, double burst) { rate = perSecond; this.burst = burst; tokens = burst; last = double.NaN; }
    public bool Allow(double nowSeconds, double cost = 1)
    {
        if (double.IsNaN(last)) last = nowSeconds;
        tokens = Math.Min(burst, tokens + Math.Max(0, nowSeconds - last) * rate); last = nowSeconds;
        if (tokens < cost) return false; tokens -= cost; return true;
    }
}

/// <summary>Counts protocol violations in a sliding window; the server kicks when <see cref="Exceeded"/>.</summary>
public sealed class StrikeCounter
{
    readonly Queue<double> times = new(); readonly int limit; readonly double window;
    public StrikeCounter(int limit = 12, double windowSeconds = 30) { this.limit = limit; window = windowSeconds; }
    public int Count => times.Count;
    public bool Add(double now)
    {
        times.Enqueue(now);
        while (times.Count > 0 && now - times.Peek() > window) times.Dequeue();
        return Exceeded;
    }
    public bool Exceeded => times.Count >= limit;
}
