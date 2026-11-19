using System.Threading.Tasks;

// Backward-compatible entry. Historical stage-two evidence stays unchanged;
// current editable-voxel verification writes only game-qa results.
public sealed class WorldQa
{
    readonly BlockWorld world;
    public WorldQa(BlockWorld world) { this.world=world; }
    public Task Run() => new GameQa(world).Run();
}
