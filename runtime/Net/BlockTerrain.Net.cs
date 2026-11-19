// JOSCRAFT multiplayer – BlockTerrain partial: authoritative edits for any (also unloaded) chunk,
// an overlay-change event for replication, extra streaming centres for remote players, and a switch that
// turns off client-side overlay simulation (grass growth / leaf decay are decided by the host only).
// NAVRH: runtime/Net/BlockTerrain.Net.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BlockTerrain
{
    /// <summary>Raised from SetOverlay (HOOK) with the final block at that cell. NetServer turns it into a sequenced delta.</summary>
    public event Action<Vector3I, BlockType>? OverlayChanged;
    int overlayEventSuppression;
    /// <summary>False on clients: UpdateGrassGrowth/UpdateLeafDecay return immediately (HOOK) – the host sends the results.</summary>
    public bool OverlaySimulation { get; set; } = true;
    readonly List<Vector3> extraStreamingCenters = new();
    public IReadOnlyList<Vector3> ExtraStreamingCenters => extraStreamingCenters;

    void RaiseOverlayChanged(Vector3I position, BlockType block)
    {
        if (overlayEventSuppression == 0) OverlayChanged?.Invoke(position, block);
    }
    /// <summary>HOOK: wrap bulk loads (RestoreChanges/Regenerate) so they are not replicated as thousands of deltas.</summary>
    public IDisposable SuppressOverlayEvents() { overlayEventSuppression++; return new Release(this); }
    sealed class Release : IDisposable { BlockTerrain? t; public Release(BlockTerrain t) { this.t = t; } public void Dispose() { if (t != null) t.overlayEventSuppression--; t = null; } }

    /// <summary>
    /// Like TrySetBlock, but valid for chunks that are not built on this machine (remote player far from the host,
    /// or a client receiving a host delta outside its streaming radius). Only already-loaded chunks are rebuilt.
    /// </summary>
    public bool ApplyAuthoritativeBlock(Vector3I position, BlockType block)
    {
        LastEditRebuiltChunks = 0;
        if (!BlockCatalog.IsBlock(block) || position.Y < 1 || position.Y > MaxBuildY || !IsInsideWorld(position.X, position.Z)) return false;
        var oldBlock = GetBlock(position); if (oldBlock == block) return false;
        SetOverlay(position, block);
        if (BlockCatalog.IsLog(oldBlock) || BlockCatalog.IsLeaves(oldBlock) || BlockCatalog.IsLog(block) || BlockCatalog.IsLeaves(block)) leafSupportDirty = true;
        RefreshGrassColumn(position.X, position.Z);
        var changed = new HashSet<Vector2I> { ChunkAt(position.X, position.Z) };
        foreach (var offset in new[] { Vector3I.Left, Vector3I.Right, Vector3I.Forward, Vector3I.Back })
            changed.Add(ChunkAt(position.X + offset.X, position.Z + offset.Z));
        foreach (var key in changed.Where(chunks.ContainsKey).OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            var old = chunks[key]; var replacement = BuildChunk(key);
            DisposeChunk(old); chunks[key] = replacement; LastEditRebuiltChunks++;
        }
        return true;
    }

    /// <summary>Host only: keep chunks around remote players built so pickups/creatures there have collision and leaves decay.</summary>
    public void SetExtraStreamingCenters(IReadOnlyList<Vector3> centers)
    {
        var next = centers.Where(c => float.IsFinite(c.X) && float.IsFinite(c.Z)).Take(NetProtocol.MaxPlayers - 1).ToArray();
        var oldChunks = extraStreamingCenters.Select(c => ChunkAt(c.X, c.Z)).ToArray();
        var newChunks = next.Select(c => ChunkAt(c.X, c.Z)).ToArray();
        extraStreamingCenters.Clear(); extraStreamingCenters.AddRange(next);
        if (!oldChunks.SequenceEqual(newChunks)) focusChunk = new(int.MinValue, int.MinValue);   // force UpdateStreaming to recompute
    }

    /// <summary>Union of the radius-2 neighbourhoods of the local focus and every extra centre (HOOK used by UpdateStreaming).</summary>
    void AddExtraDesiredChunks()
    {
        foreach (var c in extraStreamingCenters)
        {
            var center = ChunkAt(Math.Clamp(c.X, WorldMin, WorldMaxExclusive - .001f), Math.Clamp(c.Z, WorldMin, WorldMaxExclusive - .001f));
            for (int z = center.Y - StreamingRadius; z <= center.Y + StreamingRadius; z++)
                for (int x = center.X - StreamingRadius; x <= center.X + StreamingRadius; x++)
                    if (IsInsideWorld(x * ChunkSize, z * ChunkSize)) desired.Add(new Vector2I(x, z));
        }
    }
}
