using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum TreeSpecies { Oak, Birch, Spruce }

/// <summary>Pure deterministic global voxel plan. No scenes, physics or random state.</summary>
public static class TreeGenerator
{
    public sealed record TreeRecord(int Id, TreeSpecies Species, Vector3I Root,
        BlockType LogType, BlockType LeavesType, Vector3I[] Logs, Vector3I[] Leaves);

    public sealed class Plan
    {
        public IReadOnlyList<TreeRecord> Trees { get; }
        public IReadOnlyDictionary<Vector3I, BlockType> Blocks { get; }
        public IReadOnlyDictionary<Vector3I, BlockType> NaturalLeaves { get; }
        internal Plan(List<TreeRecord> trees, Dictionary<Vector3I, BlockType> blocks,
            Dictionary<Vector3I, BlockType> leaves)
        { Trees = trees; Blocks = blocks; NaturalLeaves = leaves; }
    }

    public static Plan Generate(int seed, IEnumerable<int>? excluded = null)
    {
        var exclusions = excluded?.ToHashSet() ?? new HashSet<int>();
        var trees = new List<TreeRecord>();
        var blocks = new Dictionary<Vector3I, BlockType>();
        var naturalLeaves = new Dictionary<Vector3I, BlockType>();
        // Stable slot IDs never renumber when another slot is excluded.
        for (int gz = 0; gz < 8; gz++)
            for (int gx = 0; gx < 8; gx++)
            {
                int id = gz * 8 + gx;
                if (exclusions.Contains(id)) continue;
                uint hash = BlockTerrain.StableHash(gx, gz, seed ^ 0x315A77);
                int x = -42 + gx * 12 + (int)(hash % 5) - 2;
                int z = -42 + gz * 12 + (int)((hash >> 8) % 5) - 2;
                // Account for the outer voxel edge, not only its centre: the
                // full crown remains outside the seven-metre spawn clearing.
                if (x * x + z * z < 121) continue;
                int ground = BlockTerrain.SeedHeightAt(seed, x, z);
                var root = new Vector3I(x, ground, z);
                var species = (TreeSpecies)((hash >> 17) % 3);
                var log = (BlockType)((int)BlockType.OakLog + (int)species);
                var leaf = (BlockType)((int)BlockType.OakLeaves + (int)species);
                int trunk = species == TreeSpecies.Oak ? 4 + (int)((hash >> 22) & 1)
                    : species == TreeSpecies.Birch ? 6 + (int)((hash >> 22) & 1)
                    : 7 + (int)((hash >> 22) & 1);
                var logs = new HashSet<Vector3I>(); var leaves = new HashSet<Vector3I>();
                for (int y = 0; y < trunk; y++) logs.Add(root + new Vector3I(0,y,0));
                void Layer(int y, int radius, int cornerCut)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (Math.Abs(dx) + Math.Abs(dz) > cornerCut) continue;
                            var cell = root + new Vector3I(dx,y,dz);
                            if (!logs.Contains(cell)) leaves.Add(cell);
                        }
                }
                if (species == TreeSpecies.Oak)
                {
                    // Broad, rounded, low crown; its edges stay <=6 leaf steps from a log.
                    Layer(trunk-2,2,3); Layer(trunk-1,3,4);
                    Layer(trunk,2,3); Layer(trunk+1,1,1);
                }
                else if (species == TreeSpecies.Birch)
                {
                    // Tall white trunk with a narrow, high oval canopy.
                    Layer(trunk-2,1,2); Layer(trunk-1,2,3);
                    Layer(trunk,2,2); Layer(trunk+1,1,1);
                }
                else
                {
                    // Descending tiers form a recognisable pointed evergreen cone.
                    Layer(2,3,4); Layer(3,2,3); Layer(4,2,3);
                    Layer(5,2,2); Layer(6,1,2); Layer(7,1,1);
                    Layer(8,0,0);
                }
                bool Legal(Vector3I p)
                {
                    float dx=.5f-Math.Clamp(.5f,p.X,p.X+1f),dz=.5f-Math.Clamp(.5f,p.Z,p.Z+1f);
                    return dx*dx+dz*dz>=49 && BlockTerrain.IsInsideWorld(p.X,p.Z) && p.Y <= BlockTerrain.MaxBuildY
                        && p.Y >= BlockTerrain.SeedHeightAt(seed,p.X,p.Z);
                }
                if (logs.Any(p => !Legal(p)) || leaves.Any(p => !Legal(p))) continue;
                // Grid spacing keeps crowns disjoint; retain an explicit collision
                // guard so future silhouette changes cannot silently duplicate voxels.
                if (logs.Concat(leaves).Any(blocks.ContainsKey)) continue;
                var orderedLogs = logs.OrderBy(p=>p.Y).ThenBy(p=>p.Z).ThenBy(p=>p.X).ToArray();
                var orderedLeaves = leaves.OrderBy(p=>p.Y).ThenBy(p=>p.Z).ThenBy(p=>p.X).ToArray();
                trees.Add(new TreeRecord(id,species,root,log,leaf,orderedLogs,orderedLeaves));
                foreach (var p in orderedLogs) blocks.Add(p,log);
                foreach (var p in orderedLeaves) {blocks.Add(p,leaf);naturalLeaves.Add(p,leaf);}
            }
        return new Plan(trees,blocks,naturalLeaves);
    }

    /// <summary>
    /// One-time migration of a pre-tree save. Existing edited air is as important
    /// as existing solid construction. Never derive this mask again on normal load.
    /// </summary>
    public static int[] LegacyExclusions(int seed, BlockTerrain.BlockEdit[] edits, Vector3 playerPosition)
    {
        var plan = Generate(seed);
        var edited = edits.Select(e=>new Vector3I(e.X,e.Y,e.Z)).ToHashSet();
        bool NearPlayer(Vector3I p)
        {
            float dx = playerPosition.X - Math.Clamp(playerPosition.X,p.X,p.X+1f);
            float dz = playerPosition.Z - Math.Clamp(playerPosition.Z,p.Z,p.Z+1f);
            return dx*dx+dz*dz <= 9;
        }
        return plan.Trees.Where(t => t.Logs.Concat(t.Leaves).Any(p=>edited.Contains(p)||NearPlayer(p))
            || edits.Any(e=>e.X==t.Root.X&&e.Z==t.Root.Z&&e.Y<t.Root.Y))
            .Select(t=>t.Id).Order().ToArray();
    }
}
