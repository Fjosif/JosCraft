using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Small, finite, streamed block terrain. All runtime geometry is C#.
/// BaseHeightAt is the seed terrain; GetBlock includes session edit overlays.
/// Cells use floor coordinates, including negatives. Six-way exposed-face culling.
/// </summary>
public partial class BlockTerrain : Node3D
{
    public const int ChunkSize = 16;
    public const int WorldMin = -48;
    public const int WorldMaxExclusive = 48;
    public const int StreamingRadius = 2;
    public const int MaxActiveChunks = 25;
    public const int BaseHeight = 4;
    public const int MaxColumnHeight = 11;
    public const int MaxBuildY = 24;
    public const double GrassGrowthDelaySeconds = 3.0;
    public const double LeafDecayDelaySeconds = 6.0;
    public const double LeafDecayMaxStaggerSeconds = 2.0;
    const int GrassConversionsPerFrame = 16;

    /// <summary>Exact CPU geometry submitted to the mesh and matching collider.</summary>
    public sealed class ChunkGeometry
    {
        public Vector2I Coordinate { get; init; }
        public Node3D Root { get; init; } = null!;
        public MeshInstance3D MeshInstance { get; init; } = null!;
        public StaticBody3D Body { get; init; } = null!;
        public Vector3[] Vertices { get; init; } = Array.Empty<Vector3>();
        public Vector3[] Normals { get; init; } = Array.Empty<Vector3>();
        public Color[] Colors { get; init; } = Array.Empty<Color>();
        public Vector2[] Uvs { get; init; } = Array.Empty<Vector2>();
        public int[] Indices { get; init; } = Array.Empty<int>();
        /// <summary>Surface material ID for each consecutive four-vertex quad.</summary>
        public int[] QuadSurfaces { get; init; } = Array.Empty<int>();
        public int BottomQuads { get; init; }
        public int TopQuads { get; init; }
        public int SideQuads { get; init; }
        public int TriangleCount => Indices.Length / 3;
        public double BuildMilliseconds { get; init; }
    }

    readonly Dictionary<Vector2I, ChunkGeometry> chunks = new();
    readonly List<Vector2I> pending = new();
    readonly HashSet<Vector2I> desired = new();
    readonly List<(int X, int Z, int Height)> hills = new();
    StandardMaterial3D[] materials = Array.Empty<StandardMaterial3D>();
    readonly Dictionary<Vector3I, BlockType> edits = new();
    sealed class GrassCandidate
    {
        public readonly Vector3I Cell;
        public double Elapsed;
        public GrassCandidate(Vector3I cell) => Cell = cell;
    }
    readonly Dictionary<Vector2I, GrassCandidate> grassGrowth = new();
    readonly HashSet<int> excludedTreeIds = new();
    TreeGenerator.Plan treePlan = null!;
    readonly HashSet<Vector3I> supportedLeaves = new();
    readonly Dictionary<Vector3I,double> leafDecay = new();
    bool leafSupportDirty = true;
    static readonly Vector3I[] Neighbours = {Vector3I.Left,Vector3I.Right,Vector3I.Up,Vector3I.Down,Vector3I.Forward,Vector3I.Back};
    public IReadOnlyList<TreeGenerator.TreeRecord> Trees => treePlan.Trees;
    public int[] TreeExclusions => excludedTreeIds.Order().ToArray();
    public int PendingLeafDecayCount => leafDecay.Count;
    public int TotalDecayedLeaves {get;private set;}
    public int PendingGrassGrowthCount => grassGrowth.Count;
    int seed = 20261113;
    Vector2I focusChunk = new(int.MinValue, int.MinValue);
    Vector3 focus = new(.5f, 4, .5f);

    public int Seed
    {
        get => seed;
        set
        {
            if (seed == value) return;
            seed = value;
            BuildHillData();
            if (IsInsideTree()) Regenerate(value);
            else BuildNaturalTrees();
        }
    }
    public IReadOnlyDictionary<Vector2I, ChunkGeometry> Chunks => chunks;
    public IReadOnlyDictionary<Vector2I, ChunkGeometry> LoadedChunks => chunks;
    public int ActiveChunkCount => chunks.Count;
    public int LoadedChunkCount => chunks.Count;
    public int PendingChunkCount => pending.Count;
    public int TotalTriangles => chunks.Values.Sum(c => c.TriangleCount);
    public int TotalVertices => chunks.Values.Sum(c => c.Vertices.Length);
    public int EditCount => edits.Count;
    public int LastEditRebuiltChunks { get; private set; }
    public double LastEditMilliseconds { get; private set; }
    public IReadOnlyDictionary<Vector3I, BlockType> Changes => edits;
    public sealed record BlockEdit(int X, int Y, int Z, int Type);
    public int TotalGeneratedChunks { get; private set; }
    public int TotalUnloadedChunks { get; private set; }
    public int PeakActiveChunks { get; private set; }
    public double LastBuildMilliseconds { get; private set; }
    public double MaxBuildMilliseconds { get; private set; }
    public double TotalBuildMilliseconds { get; private set; }
    public int MaxChunksPerFrame { get; set; } = 1;
    // Soft budget: a single atomic mesh/physics upload can exceed this time.
    public double GenerationBudgetMilliseconds { get; set; } = 3.0;
    public Vector3 SpawnPosition => new(.5f, HeightAt(0, 0) + .08f, .5f);
    public Vector3 FindSafeSpawn() => SpawnPosition;

    public BlockTerrain() {BuildHillData();BuildNaturalTrees();}

    public override void _Ready()
    {
        materials = Enumerable.Range(0, BlockCatalog.SurfaceCount).Select(i => new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White,
            AlbedoTexture = ResourceLoader.Exists(BlockCatalog.SurfaceTexture(i)) ? ResourceLoader.Load<Texture2D>(BlockCatalog.SurfaceTexture(i)) : null,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            TextureRepeat = false, // each face uses 0..1; keep side's green top from wrapping into its dirt bottom
            Roughness = 1,
            Metallic = 0,
            CullMode = BaseMaterial3D.CullModeEnum.Back
        }).ToArray();
        Regenerate(seed);
    }

    /// <summary>Rebuilds only this node's chunks; never reads or changes files.</summary>
    public void Regenerate(int newSeed, IEnumerable<int>? excludedTrees = null)
    {
        seed = newSeed;
        edits.Clear(); grassGrowth.Clear(); leafDecay.Clear(); supportedLeaves.Clear();
        TotalDecayedLeaves = 0; LastEditRebuiltChunks = 0; LastEditMilliseconds = 0;
        excludedTreeIds.Clear();
        if(excludedTrees!=null)foreach(int id in excludedTrees)if(id>=0&&id<64)excludedTreeIds.Add(id);
        BuildHillData();
        BuildNaturalTrees();
        foreach (var c in chunks.Values) DisposeChunk(c);
        chunks.Clear(); pending.Clear(); desired.Clear();
        focusChunk = new(int.MinValue, int.MinValue);
        focus = new(.5f, 4, .5f);
        TotalGeneratedChunks = 0; TotalUnloadedChunks = 0; PeakActiveChunks = 0;
        LastBuildMilliseconds = 0; MaxBuildMilliseconds = 0; TotalBuildMilliseconds = 0;
        if (!IsInsideTree() || materials.Length == 0) return;
        UpdateStreaming(focus);
        // A safe initial collision exists immediately; the remaining 24 are queued.
        if (pending.Count > 0) BuildNextChunk();
    }

    // Explicit unchecked integer hash: stable across .NET/OS versions and seeds.
    public static uint StableHash(int x, int z, int worldSeed)
    {
        unchecked
        {
            uint h = (uint)worldSeed ^ 0x9E3779B9u;
            h ^= (uint)x * 0x85EBCA6Bu;
            h = (h ^ (h >> 16)) * 0x7FEB352Du;
            h ^= (uint)z * 0xC2B2AE35u;
            h = (h ^ (h >> 15)) * 0x846CA68Bu;
            return h ^ (h >> 16);
        }
    }

    void BuildHillData()
    {
        hills.Clear();
        for (int i = 0; i < 14; i++)
        {
            hills.Add(SeedHill(seed,i));
        }
    }
    static (int X,int Z,int Height) SeedHill(int worldSeed,int i)
        => (WorldMin+(int)(StableHash(i,733,worldSeed)%(WorldMaxExclusive-WorldMin)),
            WorldMin+(int)(StableHash(i,991,worldSeed)%(WorldMaxExclusive-WorldMin)),
            3+(int)(StableHash(i,107,worldSeed)%5));
    public static int SeedHeightAt(int worldSeed,int x,int z)
    {
        if(!IsInsideWorld(x,z))return 0;
        int hill=0;
        for(int i=0;i<14;i++)
        {
            var h=SeedHill(worldSeed,i);int d=Math.Abs(x-h.X)+Math.Abs(z-h.Z);
            hill=Math.Max(hill,Math.Max(0,h.Height-d/4));
        }
        return BaseHeight+Math.Min(hill,Math.Max(0,Math.Max(Math.Abs(x),Math.Abs(z))-6)/4);
    }
    void BuildNaturalTrees()
    {
        treePlan=TreeGenerator.Generate(seed,excludedTreeIds);leafSupportDirty=true;
    }

    public static bool IsInsideWorld(int x, int z) => x >= WorldMin && z >= WorldMin && x < WorldMaxExclusive && z < WorldMaxExclusive;
    public int HeightAt(float x, float z) => HeightAt(Mathf.FloorToInt(x), Mathf.FloorToInt(z));

    /// <summary>
    /// Highest actual solid top, including edits; a column may contain cavities.
    /// </summary>
    public int HeightAt(int x, int z)
    {
        if (!IsInsideWorld(x, z)) return 0;
        int baseHeight = BaseHeightAt(x, z);
        for (int y = MaxBuildY; y >= 0; y--)
            if (BlockCatalog.IsFullCube(SampleBlock(x, y, z, baseHeight))) return y + 1;
        return 0;
    }

    /// <summary>
    /// Original integer diamond terraces. Each hill and the spawn cap are
    /// 1-Lipschitz on adjacent cells; their max/min keeps the one-metre step bound.
    /// </summary>
    public int BaseHeightAt(int x, int z)
    {
        if (!IsInsideWorld(x, z)) return 0;
        int hill = 0;
        foreach (var h in hills)
        {
            int distance = Math.Abs(x - h.X) + Math.Abs(z - h.Z);
            hill = Math.Max(hill, Math.Max(0, h.Height - distance / 4));
        }
        int spawnDistance = Math.Max(Math.Abs(x), Math.Abs(z));
        int spawnCap = Math.Max(0, spawnDistance - 6) / 4;
        return BaseHeight + Math.Min(hill, spawnCap);
    }
    BlockType BaseBlock(int y, int height)
    {
        if (y < 0 || y > MaxBuildY || y >= height) return BlockType.Air;
        if (y == 0) return BlockType.Stone;
        if (y == height - 1) return BlockType.Grass;
        return y >= height - 3 ? BlockType.Dirt : BlockType.Stone;
    }
    BlockType SampleBlock(int x, int y, int z, int baseHeight)
    {
        if (!IsInsideWorld(x, z) || y < 0 || y > MaxBuildY) return BlockType.Air;
        if (y == 0) return BlockType.Stone;
        var cell=new Vector3I(x,y,z);
        return edits.TryGetValue(cell, out var block) ? block : NaturalBlock(cell,baseHeight);
    }
    BlockType NaturalBlock(Vector3I cell,int baseHeight)
    {
        var soil=BaseBlock(cell.Y,baseHeight);
        if(Math.Max(Math.Abs(cell.X),Math.Abs(cell.Z))>8)
        {
            uint h=StableHash(cell.X/4,cell.Z/4,seed+722);
            if(soil==BlockType.Grass&&h%7==0)soil=BlockType.Clay;
            if(soil==BlockType.Stone&&cell.Y>0&&StableHash(cell.X,cell.Z,seed+cell.Y*91)%19==0)soil=BlockType.CoalOre;
        }
        return soil!=BlockType.Air?soil:treePlan.Blocks.TryGetValue(cell,out var treeBlock)?treeBlock:BlockType.Air;
    }
    public BlockType GetBlock(Vector3I position) => SampleBlock(position.X, position.Y, position.Z, BaseHeightAt(position.X, position.Z));
    public int GetBlockId(Vector3I position) => (int)GetBlock(position);
    public bool IsSolid(int x, int y, int z) => BlockCatalog.IsFullCube(GetBlock(new Vector3I(x, y, z)));

    public bool TrySetBlock(Vector3I position, int block) => TrySetBlock(position, (BlockType)block);
    public bool TrySetBlock(Vector3I position, BlockType block)
    {
        LastEditRebuiltChunks = 0;
        if (!BlockCatalog.IsBlock(block) || position.Y < 1 || position.Y > MaxBuildY || !IsLoadedAt(position.X, position.Z) || GetBlock(position) == block) return false;
        var timer = Stopwatch.StartNew();
        var oldBlock=GetBlock(position);
        SetOverlay(position, block);
        if(BlockCatalog.IsLog(oldBlock)||BlockCatalog.IsLeaves(oldBlock)||BlockCatalog.IsLog(block)||BlockCatalog.IsLeaves(block))leafSupportDirty=true;
        RefreshGrassColumn(position.X, position.Z);
        var changed = new HashSet<Vector2I> { ChunkAt(position.X, position.Z) };
        foreach (var offset in new[] { Vector3I.Left, Vector3I.Right, Vector3I.Forward, Vector3I.Back })
            changed.Add(ChunkAt(position.X + offset.X, position.Z + offset.Z));
        foreach (var key in changed.Where(chunks.ContainsKey).OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            var old = chunks[key];
            var replacement = BuildChunk(key);
            DisposeChunk(old); chunks[key] = replacement;
            LastEditRebuiltChunks++;
        }
        LastEditMilliseconds = timer.Elapsed.TotalMilliseconds;
        return true;
    }
    void SetOverlay(Vector3I position, BlockType block)
    {
        if (block == NaturalBlock(position, BaseHeightAt(position.X, position.Z))) edits.Remove(position);
        else edits[position] = block;
        RaiseOverlayChanged(position,block);
    }
    // Only the highest solid cell has an unobstructed column to the sky.
    // Re-evaluate on edits/load, rather than scanning every voxel every frame.
    void RefreshGrassColumn(int x, int z)
    {
        var column = new Vector2I(x, z);
        if (!IsLoadedAt(x, z)) { grassGrowth.Remove(column); return; }
        var top = new Vector3I(x, HeightAt(x, z) - 1, z);
        if (top.Y < 1 || GetBlock(top) != BlockType.Dirt) { grassGrowth.Remove(column); return; }
        if (!grassGrowth.TryGetValue(column, out var old) || old.Cell != top)
            grassGrowth[column] = new GrassCandidate(top);
    }
    void RefreshGrassChunk(Vector2I coordinate)
    {
        for (int z = coordinate.Y * ChunkSize; z < (coordinate.Y + 1) * ChunkSize; z++)
            for (int x = coordinate.X * ChunkSize; x < (coordinate.X + 1) * ChunkSize; x++)
                RefreshGrassColumn(x, z);
    }
    void UpdateGrassGrowth(double delta)
    {
        if(!OverlaySimulation)return;
        if (grassGrowth.Count == 0) return;
        var ready = new List<Vector2I>();
        foreach (var entry in grassGrowth)
        {
            entry.Value.Elapsed += Math.Max(0, delta);
            if (entry.Value.Elapsed >= GrassGrowthDelaySeconds) ready.Add(entry.Key);
        }
        // Share the main-thread upload budget with streaming. At most one
        // grass chunk is rebuilt per frame, batching up to sixteen changes.
        if (pending.Count > 0 || ready.Count == 0) return;
        var first = ready[0]; var chunk = ChunkAt(first.X, first.Y);
        int changed = 0;
        foreach (var column in ready)
        {
            if (changed >= GrassConversionsPerFrame) break;
            if (ChunkAt(column.X, column.Y) != chunk) continue;
            var cell = grassGrowth[column].Cell;
            grassGrowth.Remove(column);
            if (!IsLoadedAt(cell.X, cell.Z) || HeightAt(cell.X, cell.Z) != cell.Y + 1 || GetBlock(cell) != BlockType.Dirt) continue;
            SetOverlay(cell, BlockType.Grass); changed++;
        }
        if (changed == 0 || !chunks.TryGetValue(chunk, out var oldChunk)) return;
        // Occupancy stays solid: neighbouring faces do not change. The same
        // builder updates the actual material/UV and identical collision data.
        var replacement = BuildChunk(chunk); DisposeChunk(oldChunk); chunks[chunk] = replacement;
    }
    bool IsNaturalLeaf(Vector3I cell) => treePlan.NaturalLeaves.TryGetValue(cell,out var original) && GetBlock(cell)==original;
    HashSet<Vector2I> AffectedChunks(Vector3I cell)
    {
        var affected=new HashSet<Vector2I>{ChunkAt(cell.X,cell.Z)};
        foreach(var offset in Neighbours)
            affected.Add(ChunkAt(cell.X+offset.X,cell.Z+offset.Z));
        affected.RemoveWhere(c=>!chunks.ContainsKey(c));return affected;
    }
    void RefreshLeafSupport()
    {
        supportedLeaves.Clear();
        var logs=treePlan.Blocks.Where(e=>BlockCatalog.IsLog(e.Value)&&BlockCatalog.IsLog(GetBlock(e.Key))).Select(e=>e.Key).ToHashSet();
        foreach(var e in edits)if(BlockCatalog.IsLog(e.Value))logs.Add(e.Key);
        var queue=new Queue<(Vector3I Cell,int Distance)>();
        foreach(var log in logs)queue.Enqueue((log,0));
        // Global occupancy is intentional: an unloaded adjacent trunk continues
        // to support a loaded crown across a chunk seam.
        while(queue.Count>0)
        {
            var current=queue.Dequeue();if(current.Distance>=6)continue;
            foreach(var offset in Neighbours)
            {
                var next=current.Cell+offset;
                if(!BlockCatalog.IsLeaves(GetBlock(next))||!supportedLeaves.Add(next))continue;
                queue.Enqueue((next,current.Distance+1));
            }
        }
        foreach(var cell in leafDecay.Keys.ToArray())
            if(!IsLoadedAt(cell.X,cell.Z)||supportedLeaves.Contains(cell)||!IsNaturalLeaf(cell))leafDecay.Remove(cell);
        foreach(var cell in treePlan.NaturalLeaves.Keys)
            if(IsLoadedAt(cell.X,cell.Z)&&!supportedLeaves.Contains(cell)&&IsNaturalLeaf(cell))leafDecay.TryAdd(cell,0);
        leafSupportDirty=false;
    }
    void RefreshLeafChunk(Vector2I chunk)
    {
        if(leafSupportDirty)return;
        foreach(var cell in treePlan.NaturalLeaves.Keys)
            if(ChunkAt(cell.X,cell.Z)==chunk&&!supportedLeaves.Contains(cell)&&IsNaturalLeaf(cell))leafDecay.TryAdd(cell,0);
    }
    public double LeafDecayStagger(Vector3I cell)
        => (StableHash(cell.X,cell.Z,seed^(cell.Y*733))%2001)/1000.0;
    bool UpdateLeafDecay(double delta)
    {
        if(!OverlaySimulation)return false;
        if(leafSupportDirty)RefreshLeafSupport();
        if(leafDecay.Count==0)return false;
        var ready=new List<Vector3I>();
        foreach(var cell in leafDecay.Keys.ToArray())
        {
            leafDecay[cell]+=Math.Max(0,delta);
            if(leafDecay[cell]>=LeafDecayDelaySeconds+LeafDecayStagger(cell))ready.Add(cell);
        }
        if(pending.Count>0||ready.Count==0)return false;
        // Batch <=8 leaves from one chunk, with at most three affected loaded
        // chunks (same atomic seam rule as an edit). Share uploads with grass.
        var primary=ChunkAt(ready[0].X,ready[0].Z);
        var dirty=new HashSet<Vector2I>();int changed=0;
        foreach(var cell in ready)
        {
            if(changed>=8)break;
            if(ChunkAt(cell.X,cell.Z)!=primary)continue;
            var affected=AffectedChunks(cell);
            if(dirty.Union(affected).Count()>3)continue;
            leafDecay.Remove(cell);
            if(!IsLoadedAt(cell.X,cell.Z)||supportedLeaves.Contains(cell)||!IsNaturalLeaf(cell))continue;
            SetOverlay(cell,BlockType.Air);dirty.UnionWith(affected);changed++;TotalDecayedLeaves++;
            RefreshGrassColumn(cell.X,cell.Z);
        }
        foreach(var chunk in dirty.OrderBy(c=>c.Y).ThenBy(c=>c.X))
        {var old=chunks[chunk];var replacement=BuildChunk(chunk);DisposeChunk(old);chunks[chunk]=replacement;}
        // A leaf outside distance6 cannot be part of a supported leaf's shortest
        // path, so removing these leaves does not invalidate the support field.
        return changed>0;
    }
    public Dictionary<Vector3I, int> ExportEdits() => edits.ToDictionary(e => e.Key, e => (int)e.Value);
    public BlockEdit[] GetChanges() => edits.OrderBy(e => e.Key.Z).ThenBy(e => e.Key.X).ThenBy(e => e.Key.Y).Select(e => new BlockEdit(e.Key.X, e.Key.Y, e.Key.Z, (int)e.Value)).ToArray();
    public void ImportEdits(Dictionary<Vector3I, int> values)
        => RestoreChanges(values.Select(e => new BlockEdit(e.Key.X, e.Key.Y, e.Key.Z, e.Value)));
    public int RestoreChanges(IEnumerable<BlockEdit> values)
    {
        edits.Clear(); grassGrowth.Clear(); leafDecay.Clear(); leafSupportDirty=true;
        foreach (var e in values)
        {
            var position = new Vector3I(e.X, e.Y, e.Z); var type = (BlockType)e.Type;
            if (e.Y >= 1 && e.Y <= MaxBuildY && IsInsideWorld(e.X, e.Z) && BlockCatalog.IsValid(type)) SetOverlay(position, type);
        }
        // Import is a save-load operation; replace each loaded chunk once.
        foreach (var key in chunks.Keys.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray())
        {
            var old = chunks[key]; var replacement = BuildChunk(key);
            DisposeChunk(old); chunks[key] = replacement;
            RefreshGrassChunk(key);
        }
        return edits.Count;
    }
    public static Vector2I ChunkAt(float x, float z) => new(Mathf.FloorToInt(x / ChunkSize), Mathf.FloorToInt(z / ChunkSize));
    public bool IsLoadedAt(float x, float z) => IsInsideWorld(Mathf.FloorToInt(x), Mathf.FloorToInt(z)) && chunks.ContainsKey(ChunkAt(x, z));
    public bool IsReadyAt(float x, float z) => IsLoadedAt(x, z);

    public void UpdateAround(Vector3 position) => UpdateStreaming(position);

    /// <summary>
    /// Requests a Chebyshev-radius-two neighbourhood, clipped to the finite map.
    /// Drop outgoing geometry before queueing incoming nodes: at most 25 active.
    /// This method performs no mesh build; _Process advances the bounded queue.
    /// </summary>
    public void UpdateStreaming(Vector3 position)
    {
        focus = new Vector3(Math.Clamp(position.X, WorldMin, WorldMaxExclusive - .001f), position.Y,
            Math.Clamp(position.Z, WorldMin, WorldMaxExclusive - .001f));
        var next = ChunkAt(focus.X, focus.Z);
        if (next == focusChunk) return;
        focusChunk = next; desired.Clear();
        for (int z = next.Y - StreamingRadius; z <= next.Y + StreamingRadius; z++)
            for (int x = next.X - StreamingRadius; x <= next.X + StreamingRadius; x++)
                if (IsInsideWorld(x * ChunkSize, z * ChunkSize)) desired.Add(new Vector2I(x, z));
        AddExtraDesiredChunks();
        foreach (var key in chunks.Keys.Where(k => !desired.Contains(k)).ToArray())
        {
            DisposeChunk(chunks[key]); chunks.Remove(key); TotalUnloadedChunks++;
            foreach (var column in grassGrowth.Keys.Where(c => ChunkAt(c.X, c.Y) == key).ToArray()) grassGrowth.Remove(column);
            foreach(var cell in leafDecay.Keys.Where(c=>ChunkAt(c.X,c.Z)==key).ToArray())leafDecay.Remove(cell);
        }
        pending.Clear();
        pending.AddRange(desired.Where(c => !chunks.ContainsKey(c)).OrderBy(c => DistanceSquared(c, focus)).ThenBy(c => c.Y).ThenBy(c => c.X));
    }

    static float DistanceSquared(Vector2I c, Vector3 point)
    {
        float dx = (c.X + .5f) * ChunkSize - point.X;
        float dz = (c.Y + .5f) * ChunkSize - point.Z;
        // The focus chunk is always first, even near a corner.
        if (c == ChunkAt(point.X, point.Z)) return -1;
        return dx * dx + dz * dz;
    }

    public override void _Process(double delta)
    {
        if (materials.Length == 0) return;
        if(UpdateLeafDecay(delta))return;
        UpdateGrassGrowth(delta);
        if (pending.Count == 0) return;
        var clock = Stopwatch.StartNew();
        int built = 0;
        while (pending.Count > 0 && built < Math.Clamp(MaxChunksPerFrame, 1, 2))
        {
            BuildNextChunk(); built++;
            if (clock.Elapsed.TotalMilliseconds >= Math.Max(.1, GenerationBudgetMilliseconds)) break;
        }
    }

    void BuildNextChunk()
    {
        var key = pending[0]; pending.RemoveAt(0);
        if (!desired.Contains(key) || chunks.ContainsKey(key)) return;
        var geometry = BuildChunk(key); chunks.Add(key, geometry);
        RefreshGrassChunk(key);
        RefreshLeafChunk(key);
        TotalGeneratedChunks++; PeakActiveChunks = Math.Max(PeakActiveChunks, chunks.Count);
        LastBuildMilliseconds = geometry.BuildMilliseconds;
        TotalBuildMilliseconds += LastBuildMilliseconds;
        MaxBuildMilliseconds = Math.Max(MaxBuildMilliseconds, LastBuildMilliseconds);
    }

    void DisposeChunk(ChunkGeometry c)
    {
        // Immediate removal releases the chunk from rendering and physics now.
        // QueueFree alone would leave outgoing physics until the next frame.
        if (c.Root.GetParent() == this) RemoveChild(c.Root);
        c.Root.QueueFree();
    }

    ChunkGeometry BuildChunk(Vector2I coordinate)
    {
        var clock = Stopwatch.StartNew();
        int originX = coordinate.X * ChunkSize, originZ = coordinate.Y * ChunkSize;
        var vertices = new List<Vector3>(4096);
        var normals = new List<Vector3>(4096);
        var colors = new List<Color>(4096);
        var uvs = new List<Vector2>(4096);
        var indices = new List<int>(6144);
        var quadSurfaces = new List<int>();
        var surfaceVertices = Enumerable.Range(0,BlockCatalog.SurfaceCount).Select(_ => new List<Vector3>()).ToArray();
        var surfaceNormals = Enumerable.Range(0,BlockCatalog.SurfaceCount).Select(_ => new List<Vector3>()).ToArray();
        var surfaceColors = Enumerable.Range(0,BlockCatalog.SurfaceCount).Select(_ => new List<Color>()).ToArray();
        var surfaceUvs = Enumerable.Range(0,BlockCatalog.SurfaceCount).Select(_ => new List<Vector2>()).ToArray();
        var surfaceIndices = Enumerable.Range(0,BlockCatalog.SurfaceCount).Select(_ => new List<int>()).ToArray();
        int topQuads = 0, sideQuads = 0, bottomQuads = 0;

        // Cache globally sampled seed columns, including the one-cell halo.
        // All six occupancy tests include edits, even across unloaded borders.
        var baseColumns = new int[ChunkSize + 2, ChunkSize + 2];
        for (int z = -1; z <= ChunkSize; z++)
            for (int x = -1; x <= ChunkSize; x++)
                baseColumns[x + 1, z + 1] = BaseHeightAt(originX + x, originZ + z);
        BlockType Sample(int x, int y, int z) => SampleBlock(originX + x, y, originZ + z, baseColumns[x + 1, z + 1]);

        // Godot front faces use clockwise winding (as viewed from outside).
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float variation, BlockType block)
        {
            int n = vertices.Count, surface = BlockCatalog.SurfaceFor(block, normal);
            int sn = surfaceVertices[surface].Count;
            var color = new Color(variation, variation, variation, 1); // linear 0.95..1 texture tint
            var points = new[] { a, b, c, d };
            // The authored grass-side texture has green at UV.y=0, so its band
            // stays at the top of every vertical block face, on all four sides.
            var quadUvs = new[] { new Vector2(0,1), new Vector2(1,1), new Vector2(1,0), new Vector2(0,0) };
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(points[i]); normals.Add(normal); colors.Add(color); uvs.Add(quadUvs[i]);
                surfaceVertices[surface].Add(points[i]); surfaceNormals[surface].Add(normal);
                surfaceColors[surface].Add(color); surfaceUvs[surface].Add(quadUvs[i]);
            }
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
            { indices.Add(n + i); surfaceIndices[surface].Add(sn + i); }
            quadSurfaces.Add(surface);
            if (normal.Y > .5f) topQuads++; else if (normal.Y < -.5f) bottomQuads++; else sideQuads++;
        }

        for (int z = 0; z < ChunkSize; z++)
            for (int x = 0; x < ChunkSize; x++)
                for (int y = 0; y <= MaxBuildY; y++)
            {
                var block = Sample(x, y, z);
                if (!BlockCatalog.IsFullCube(block)) continue;
                float tint = .95f + (StableHash(originX + x, originZ + z, seed ^ (y * 971)) & 255) / 255f * .05f;
                if (!BlockCatalog.IsFullCube(Sample(x,y + 1,z)))
                    Quad(new(x,y+1,z),new(x+1,y+1,z),new(x+1,y+1,z+1),new(x,y+1,z+1),Vector3.Up,tint,block);
                if (!BlockCatalog.IsFullCube(Sample(x,y - 1,z)))
                    Quad(new(x,y,z+1),new(x+1,y,z+1),new(x+1,y,z),new(x,y,z),Vector3.Down,tint,block);
                if (!BlockCatalog.IsFullCube(Sample(x + 1,y,z)))
                    Quad(new(x+1,y,z),new(x+1,y,z+1),new(x+1,y+1,z+1),new(x+1,y+1,z),Vector3.Right,tint,block);
                if (!BlockCatalog.IsFullCube(Sample(x - 1,y,z)))
                    Quad(new(x,y,z+1),new(x,y,z),new(x,y+1,z),new(x,y+1,z+1),Vector3.Left,tint,block);
                if (!BlockCatalog.IsFullCube(Sample(x,y,z + 1)))
                    Quad(new(x+1,y,z+1),new(x,y,z+1),new(x,y+1,z+1),new(x+1,y+1,z+1),Vector3.Back,tint,block);
                if (!BlockCatalog.IsFullCube(Sample(x,y,z - 1)))
                    Quad(new(x,y,z),new(x+1,y,z),new(x+1,y+1,z),new(x,y+1,z),Vector3.Forward,tint,block);
            }

        var vArray = vertices.ToArray(); var nArray = normals.ToArray(); var cArray = colors.ToArray(); var iArray = indices.ToArray();
        var mesh = new ArrayMesh();
        for (int surface = 0; surface < BlockCatalog.SurfaceCount; surface++)
        {
            if (surfaceIndices[surface].Count == 0) continue;
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = surfaceVertices[surface].ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = surfaceNormals[surface].ToArray();
            arrays[(int)Mesh.ArrayType.Color] = surfaceColors[surface].ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = surfaceUvs[surface].ToArray();
            arrays[(int)Mesh.ArrayType.Index] = surfaceIndices[surface].ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials[surface]);
        }
        // Index expansion is the only collision conversion: no separate height
        // formula or simplified proxy can diverge from the rendered geometry.
        var collisionTriangles = new Vector3[iArray.Length];
        for (int i = 0; i < iArray.Length; i++) collisionTriangles[i] = vArray[iArray[i]];
        var root = new Node3D { Name = $"Chunk_{coordinate.X}_{coordinate.Y}", Position = new Vector3(originX, 0, originZ) };
        var instance = new MeshInstance3D { Name = "RenderedBlocks", Mesh = mesh };
        var body = new StaticBody3D { Name = "IdenticalBlockCollision", CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = collisionTriangles, BackfaceCollision = false } });
        root.AddChild(instance); root.AddChild(body); AddChild(root);
        clock.Stop();
        return new ChunkGeometry
        {
            Coordinate = coordinate, Root = root, MeshInstance = instance, Body = body,
            Vertices = vArray, Normals = nArray, Colors = cArray, Indices = iArray, Uvs = uvs.ToArray(), QuadSurfaces = quadSurfaces.ToArray(),
            TopQuads = topQuads, SideQuads = sideQuads, BottomQuads = bottomQuads, BuildMilliseconds = clock.Elapsed.TotalMilliseconds
        };
    }
}
