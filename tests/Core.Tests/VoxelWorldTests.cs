namespace ColonySim.Core.Tests;

public class VoxelWorldTests
{
    [Fact] public void NewWorld_IsAllAir_Height256() { var w = new VoxelWorld(64, 96); Assert.Equal(256, w.SizeY); Assert.Equal(3, w.ChunksZ); Assert.Equal(Blocks.Air, w.GetBlock(10, 100, 70)); }
    [Theory] [InlineData(0, 64)] [InlineData(65, 64)] [InlineData(64, 31)]
    public void Constructor_RejectsBadSizes(int sx, int sz) => Assert.Throws<ArgumentException>(() => new VoxelWorld(sx, sz));
    [Fact] public void SetGet_RoundTrips_AcrossChunks()
    {
        var w = new VoxelWorld(64, 64);
        w.SetBlock(31, 40, 32, Blocks.Stone); w.SetBlock(32, 40, 32, Blocks.Dirt);
        Assert.Equal(Blocks.Stone, w.GetBlock(31, 40, 32)); Assert.Equal(Blocks.Dirt, w.GetBlock(32, 40, 32)); Assert.Equal(Blocks.Air, w.GetBlock(33, 40, 32));
    }
    [Fact] public void OutOfBounds_ReadsAir_WritesIgnored() { var w = new VoxelWorld(32, 32); w.SetBlock(-1, 5, 5, Blocks.Stone); w.SetBlock(5, 256, 5, Blocks.Stone); Assert.Equal(Blocks.Air, w.GetBlock(-1, 5, 5)); Assert.False(w.InBounds(5, 256, 5)); }
    [Fact] public void SetBlock_DirtiesNeighborChunks_OnBorderOnly()
    {
        var w = new VoxelWorld(64, 64); var d = new HashSet<Int3>();
        w.SetBlock(10, 40, 10, Blocks.Stone); w.ConsumeDirtyChunks(d);
        Assert.Equal(new[] { new Int3(0, 1, 0) }, d);
        d.Clear(); w.SetBlock(31, 40, 10, Blocks.Stone); w.ConsumeDirtyChunks(d);
        Assert.Contains(new Int3(0, 1, 0), d); Assert.Contains(new Int3(1, 1, 0), d); Assert.Equal(2, d.Count);
    }
    [Fact] public void MarkDirtyFalse_LeavesNothingDirty() { var w = new VoxelWorld(32, 32); var d = new List<Int3>(); w.SetBlock(1, 1, 1, Blocks.Stone, markDirty: false); w.ConsumeDirtyChunks(d); Assert.Empty(d); }
    [Fact] public void ConsumeDirtyChunks_Clears() { var w = new VoxelWorld(32, 32); var d = new List<Int3>(); w.SetBlock(1, 1, 1, Blocks.Stone); w.ConsumeDirtyChunks(d); d.Clear(); w.ConsumeDirtyChunks(d); Assert.Empty(d); }
    [Fact] public void Discovery_DefaultsFalse_AndMarksDirty() { var w = new VoxelWorld(32, 32); var d = new List<Int3>(); Assert.False(w.IsDiscovered(3, 3, 3)); w.SetDiscovered(3, 3, 3); Assert.True(w.IsDiscovered(3, 3, 3)); w.ConsumeDirtyChunks(d); Assert.Single(d); }
    [Fact] public void SurfaceHeight_RoundTrips() { var w = new VoxelWorld(32, 32); w.SetSurfaceHeight(4, 9, 141); Assert.Equal(141, w.GetSurfaceHeight(4, 9)); }
    [Fact] public void ChunkUniform_UntilMixedWrite()
    {
        var w = new VoxelWorld(32, 32);
        Assert.True(w.IsChunkUniform(new Int3(0, 0, 0), out var id)); Assert.Equal(Blocks.Air, id);
        w.SetBlock(0, 0, 0, Blocks.Stone); Assert.False(w.IsChunkUniform(new Int3(0, 0, 0), out _));
    }
}
