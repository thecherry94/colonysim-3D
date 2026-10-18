# Step 1: Voxel World Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A generated, diggable, buildable block world in Godot. It renders fast on 512×512×256 maps and stays readable at any depth through a slice view with fog of war. It has drag designation tools, an instant-work debug mode, and save/load.

**Architecture:** All world logic lives in a plain C# library, `src/Core` (assembly `ColonySim.Core`, no Godot reference, unit-tested with xUnit). That covers block registry, chunked voxel storage, generation, discovery, the player's fogged view, jobs and designations, the instant worker, persistence, CPU-side meshing and voxel raycasting. The Godot project (`src/Game`, compiled by the root `ColonySim.csproj`) only converts Core output into meshes and nodes, and handles camera, input and UI. The slice is a global shader uniform: fragments above the cut are discarded, and a separate per-chunk-column "cap" mesh draws the dark cut surface. Changing the level therefore never remeshes chunks.

**Tech Stack:** Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`), C# on net8.0, xUnit 2.9.2 (test project on net10.0), FastNoiseLite (vendored single file, MIT).

**Spec:** `docs/superpowers/specs/2026-10-18-step1-voxel-world-design.md`, under `docs/superpowers/specs/2026-10-18-colonysim-vision-design.md`. The behavior reference is `docs/superpowers/specs/prototypes/slice-view-tools.html` (open it in a browser).

## Global Constraints

- Engine: Godot **4.7.2 .NET**, binary `godot` (→ `~/.local/share/godot/4.7.2-mono/`). .NET SDK 10.0.104 installed; Core and Game target **net8.0** with `RollForward=LatestMajor`; tests target **net10.0**.
- `src/Core` must never reference Godot. Vectors in Core use `System.Numerics`.
- World: **1 block = 1 m**, height **256** (Y 0–255), chunk size **32³**. Map sizes: **Small 256×256, Medium 384×384, Large 512×512**. Ground is typically Y 128–150; mountains reach about Y 220.
- Block (x,y,z) occupies `[x,x+1)×[y,y+1)×[z,z+1)` in Godot world space, with Y up. Godot treats **clockwise** triangles as front faces.
- Generation is deterministic from a seed. Saves store the whole world, plus a block-name palette.
- Performance targets on the dev machine (RTX 3080, i5-10600K, 32 GB): Large map generates in **< 30 s**; **60 fps** with the whole Large map in view; one view-level step **< 50 ms**; a changed block shows up within **1–2 frames**.
- Test command (excludes slow perf tests): `dotnet test tests/Core.Tests --filter "Category!=Perf"`.
- Visual verification: `COLONYSIM_SHOTS=tests/visual/shots godot --path .` (needs a real window, not `--headless`). It writes PNGs plus `metrics.txt`. Read the PNGs to verify.

## Review Focus

1. **Drags that start or run off the map, or over the sky.** If the drag starts on no block, it is ignored. A box that sticks out of the map produces only in-bounds cells. Nothing throws. Test: Task 5, `Box_OutsideMapBounds_IsClipped`.
2. **Changing level mid-drag past Y 0 or the top.** The level clamps, and the box's Y range never yields cells outside 1–255. Test: Task 10, `ExtendBox_ClampsToWorldHeight`.
3. **Enormous drags** (a whole Large map, many levels deep). The result is capped at `Designator.MaxCellsPerDrag` and stays fast. Test: Task 5, `HugeBox_IsCappedAtMaxCellsPerDrag`.
4. **Loading a corrupt, truncated or newer-version save.** You get `InvalidDataException` with a readable message, and the UI shows it instead of crashing. Test: Task 8, `Load_Truncated_ThrowsInvalidData`.
5. **Jobs whose cell changed after they were queued.** A build on a now-solid cell is discarded. A dig on a now-empty cell completes harmlessly and still reveals the area. Test: Task 7, `Step_BuildOnOccupiedCell_IsDiscarded`, `Step_DigOnAirCell_CompletesAndReveals`.

## File Map

```
ColonySim.sln, ColonySim.csproj, project.godot      Godot project (root)
src/Core/Core.csproj                                plain C# library ColonySim.Core
  Int3.cs, Hash.cs, ColonyMap.cs
  Blocks/  BlockShape.cs Facing.cs BuildMaterial.cs BlockDef.cs BlockRegistry.cs Blocks.cs
  World/   VoxelWorld.cs Chunk.cs Discovery.cs PlayerView.cs
  Generation/ FastNoiseLite.cs MapSize.cs TerrainParams.cs MapSettings.cs TerrainLayers.cs
              HeightField.cs ColumnFiller.cs CaveCarver.cs OrePlacer.cs SurfaceDecorator.cs MapGenerator.cs
  Jobs/    JobKind.cs Job.cs JobQueue.cs DesignationTool.cs DragBox.cs Designator.cs InstantWorker.cs
  Persistence/ MapSerializer.cs
  Meshing/ MeshBuffers.cs MeshPair.cs ChunkMesher.cs CapMesher.cs
  Tools/   VoxelRaycast.cs ToolTargeting.cs
tests/Core.Tests/                                   xUnit, one *Tests.cs per Core file group + TestWorlds.cs
src/Game/                                           Godot scripts (namespace ColonySim.Game)
  GameSession.cs MapScene.cs
  Rendering/ BlockTextures.cs MeshConvert.cs ChunkRenderer.cs CapRenderer.cs
  View/ CameraRig.cs SliceController.cs
  Input/ InputActions.cs
  Tools/ ToolController.cs DesignationOverlay.cs
  Work/ InstantWorkRunner.cs
  Ui/ Hud.cs DebugOverlay.cs NewGameScreen.cs
  Persistence/ SaveGames.cs
  Debug/ VisualShots.cs
assets/shaders/ block_common.gdshaderinc block.gdshader block_cap.gdshader liquid.gdshader overlay.gdshader
scenes/ map.tscn (exists) new_game.tscn
```

UI is built in code (`Hud`, `NewGameScreen`, `DebugOverlay`), so scenes stay minimal and diffable.

---

### Task 1: Solution scaffolding, Int3, Hash and the block registry

**Files:**
- Modify: `project.godot` (set `config/name="ColonySim"`, `dotnet/project/assembly_name="ColonySim"`), `.gitignore` (append `bin/`, `obj/`, `*.user`, `tests/visual/shots/`)
- Create: `ColonySim.csproj`, `ColonySim.sln`, `src/Core/Core.csproj`, `src/Core/.gdignore`, `tests/Core.Tests/Core.Tests.csproj`, `tests/.gdignore` (empty files, so Godot doesn't import these folders)
- Create: `src/Core/Int3.cs`, `src/Core/Hash.cs`, `src/Core/Blocks/{BlockShape,Facing,BuildMaterial,BlockDef,BlockRegistry,Blocks}.cs`
- Test: `tests/Core.Tests/BlocksTests.cs`, `tests/Core.Tests/HashTests.cs`
- Also commit the developer's existing uncommitted Godot files: `project.godot`, `icon.svg`, `icon.svg.import`, `scenes/map.tscn`, `.editorconfig`, `.gitattributes`.

**Interfaces:**
- Produces:
  - `readonly record struct Int3(int X, int Y, int Z)` with `+`, `-`, `* int`, `static Int3 Zero/Up/Down`.
  - `static class Hash { uint Of(int seed, int x, int y, int z); float Unit(int seed, int x, int y, int z) /* [0,1) */ }`
  - `enum BlockShape { None, Cube, Liquid, Stairs }`
  - `enum Facing { North /*-Z*/, East /*+X*/, South /*+Z*/, West /*-X*/ }` with `static class FacingExtensions { Int3 Offset(this Facing f) }`
  - `enum BuildMaterial { Wood, Stone }`
  - `sealed record BlockDef(ushort Id, string Name, BlockShape Shape, bool IsSolid, bool IsDiggable, int TexTop, int TexSide, int TexBottom, Facing Facing)` with `bool Occludes => Shape == BlockShape.Cube` and `bool IsLiquid => Shape == BlockShape.Liquid`
  - `sealed class BlockRegistry { BlockDef Get(ushort id); bool TryGetByName(string name, out ushort id); IReadOnlyList<BlockDef> All; IReadOnlyList<string> TextureNames; int TextureIndex(string name); }`
  - `static class Blocks`: `static BlockRegistry Registry`; `static readonly ushort` ids `Air` (must be 0), `Bedrock, Grass, Dirt, Sand, Stone, DeepStone, CoalOre, CopperOre, IronOre, GoldOre, Water, OakLog, OakLeaves, PineLog, PineLeaves, Cobblestone, WoodPlanks, StoneBricks`; `static ushort BuildBlock(BuildMaterial m)` (Wood→WoodPlanks, Stone→StoneBricks); `static ushort Stairs(BuildMaterial m, Facing f)`.

Block table. Names are snake_case. A single texture name means all faces use it. Every solid block except bedrock is diggable.

| Block | Shape | Solid | Textures (top / side / bottom) |
|---|---|---|---|
| air | None | no | — |
| bedrock | Cube | yes, **not diggable** | bedrock |
| grass | Cube | yes | grass_top / grass_side / dirt |
| dirt, sand, stone, deep_stone, coal_ore, copper_ore, iron_ore, gold_ore, cobblestone | Cube | yes | same as name |
| water | Liquid | no, not diggable | water |
| oak_log, pine_log | Cube | yes | oak_log_top / oak_log / oak_log_top (pine likewise) |
| oak_leaves, pine_leaves | Cube | yes | same as name |
| wood_planks | Cube | yes | planks |
| stone_bricks | Cube | yes | stone_bricks |
| wood_stairs_{north,east,south,west}, stone_stairs_{…} | Stairs | yes | planks / stone_bricks |

`Facing` on a stairs block is the direction you walk to go **up**. Non-stairs blocks use `Facing.South`.

- [ ] **Step 1: Scaffold the projects**

`ColonySim.csproj`: `Sdk="Godot.NET.Sdk/4.7.2"`, `TargetFramework net8.0`, `RootNamespace ColonySim.Game`, `Nullable enable`, `ImplicitUsings enable`, `RollForward LatestMajor`, `<Compile Remove="src\Core\**;tests\**" />`, `<ProjectReference Include="src\Core\Core.csproj" />`.
`src/Core/Core.csproj`: `Microsoft.NET.Sdk`, net8.0, `RootNamespace`/`AssemblyName` `ColonySim.Core`, Nullable, ImplicitUsings, `LangVersion latest`, `<InternalsVisibleTo Include="ColonySim.Core.Tests" />`.
`tests/Core.Tests/Core.Tests.csproj`: net10.0, `AssemblyName`/`RootNamespace` `ColonySim.Core.Tests`, `IsPackable false`, packages `Microsoft.NET.Test.Sdk 17.12.0`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, reference `..\..\src\Core\Core.csproj`.
Solution: `dotnet new sln -n ColonySim --format sln`, then `dotnet sln add` all three projects.

- [ ] **Step 2: Write the failing tests**

```csharp
public class BlocksTests
{
    static BlockDef D(ushort id) => Blocks.Registry.Get(id);

    [Fact] public void Air_IsIdZero_WithNoShape() { Assert.Equal(0, Blocks.Air); Assert.Equal(BlockShape.None, D(Blocks.Air).Shape); Assert.False(D(Blocks.Air).IsSolid); }
    [Fact] public void Bedrock_IsSolidButNotDiggable() { Assert.True(D(Blocks.Bedrock).IsSolid); Assert.False(D(Blocks.Bedrock).IsDiggable); }
    [Fact] public void Water_IsLiquid_NotSolid_NotDiggable() { var w = D(Blocks.Water); Assert.True(w.IsLiquid); Assert.False(w.IsSolid); Assert.False(w.IsDiggable); Assert.False(w.Occludes); }
    [Fact] public void Grass_UsesTopSideBottomTextures()
    {
        var g = D(Blocks.Grass); var r = Blocks.Registry;
        Assert.Equal(r.TextureIndex("grass_top"), g.TexTop);
        Assert.Equal(r.TextureIndex("grass_side"), g.TexSide);
        Assert.Equal(r.TextureIndex("dirt"), g.TexBottom);
    }
    [Fact] public void Stairs_OneBlockPerMaterialAndFacing()
    {
        var ids = new HashSet<ushort>();
        foreach (var m in Enum.GetValues<BuildMaterial>())
            foreach (var f in Enum.GetValues<Facing>())
            {
                var d = D(Blocks.Stairs(m, f));
                Assert.Equal(BlockShape.Stairs, d.Shape); Assert.Equal(f, d.Facing);
                Assert.True(d.IsSolid); Assert.False(d.Occludes);
                ids.Add(d.Id);
            }
        Assert.Equal(8, ids.Count);
    }
    [Fact] public void BuildBlock_MapsMaterials() { Assert.Equal(Blocks.WoodPlanks, Blocks.BuildBlock(BuildMaterial.Wood)); Assert.Equal(Blocks.StoneBricks, Blocks.BuildBlock(BuildMaterial.Stone)); }
    [Fact] public void Names_AreUnique_AndLookupRoundTrips()
    {
        foreach (var d in Blocks.Registry.All) { Assert.True(Blocks.Registry.TryGetByName(d.Name, out var id)); Assert.Equal(d.Id, id); }
        Assert.Equal(Blocks.Registry.All.Count, Blocks.Registry.All.Select(d => d.Name).Distinct().Count());
    }
    [Fact] public void TextureNames_AreUnique() => Assert.Equal(Blocks.Registry.TextureNames.Count, Blocks.Registry.TextureNames.Distinct().Count());
    [Fact] public void FacingOffsets() { Assert.Equal(new Int3(0,0,-1), Facing.North.Offset()); Assert.Equal(new Int3(1,0,0), Facing.East.Offset()); }
}

public class HashTests
{
    [Fact] public void Of_IsDeterministic_AndSeedSensitive() { Assert.Equal(Hash.Of(1,2,3,4), Hash.Of(1,2,3,4)); Assert.NotEqual(Hash.Of(1,2,3,4), Hash.Of(2,2,3,4)); }
    [Fact] public void Unit_IsInZeroOne() { for (int i = 0; i < 1000; i++) { var u = Hash.Unit(7, i, -i, i * 3); Assert.InRange(u, 0f, 0.99999994f); } }
}
```

- [ ] **Step 3: Run the tests and confirm they fail** with `dotnet test tests/Core.Tests --filter "Category!=Perf"`. Expected: a compile error (types not defined).
- [ ] **Step 4: Implement** `Int3`, `Hash` (any well-mixed integer hash, such as xxHash-style multiply/xorshift), the block types, and `Blocks` (build the registry in a static constructor, assign texture indices in order of first use).
- [ ] **Step 5: Run the tests and confirm they pass.** Also run `dotnet build ColonySim.sln`. Expected: build succeeds with 0 errors.
- [ ] **Step 6: Commit** the scaffolding, Core blocks, tests, and the developer's Godot files: `feat: scaffold solution and block registry`.

---

### Task 2: Chunked voxel storage

**Files:**
- Create: `src/Core/World/Chunk.cs` (internal), `src/Core/World/VoxelWorld.cs`
- Test: `tests/Core.Tests/VoxelWorldTests.cs`, `tests/Core.Tests/TestWorlds.cs`

**Interfaces:**
- Consumes: `Int3`, `Blocks`.
- Produces `sealed class VoxelWorld`:
  - `const int ChunkSize = 32, Height = 256`
  - `VoxelWorld(int sizeX, int sizeZ)` throws `ArgumentException` unless both are positive multiples of 32
  - `int SizeX, SizeY (=256), SizeZ, ChunksX, ChunksY, ChunksZ`
  - `bool InBounds(int x, int y, int z)`
  - `ushort GetBlock(int x, int y, int z)` (out of bounds → `Blocks.Air`); `void SetBlock(int x, int y, int z, ushort id, bool markDirty = true)` (out of bounds → ignored)
  - `bool IsDiscovered(int x, int y, int z)` (out of bounds → false); `void SetDiscovered(int x, int y, int z, bool markDirty = true)`
  - `int GetSurfaceHeight(int x, int z)`; `void SetSurfaceHeight(int x, int z, int y)`. This is the original generated ground height, kept for fog material and the level label.
  - `bool IsChunkUniform(Int3 chunk, out ushort id)`
  - `void ConsumeDirtyChunks(ICollection<Int3> into)` adds the dirty chunk coordinates and clears the set; `void MarkAllDirty()`
  - `static Int3 ChunkOf(int x, int y, int z)`
- Produces the test helper `static class TestWorlds { VoxelWorld SolidBelow(int size = 64, int surface = 150, ushort fill = 0 /*0 → Stone*/) }`: bedrock at y=0, `fill` for 1..surface, surface heights set, no dirty chunks.

Storage: a chunk is either uniform (one id, no array) or a `ushort[32768]`, allocated on the first differing write. Discovery uses a per-chunk bitset (`ulong[512]`), likewise uniform until first written. A write that marks dirty must also dirty every chunk containing any of the 26 neighbors of the cell, because ambient occlusion reads diagonals. Generation calls `SetBlock` with `markDirty: false` from parallel tasks, one chunk column per task, so writes to *different* chunks must be thread-safe. No locking is needed for that.

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests and confirm they fail** (compile error).
- [ ] **Step 3: Implement** `Chunk` and `VoxelWorld`, and add `TestWorlds.SolidBelow`.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): chunked voxel storage with discovery bits and dirty tracking`.

---

### Task 3: Map settings, terrain layers, discovery and the player's fogged view

**Files:**
- Create: `src/Core/Generation/{MapSize,TerrainParams,MapSettings,TerrainLayers}.cs`, `src/Core/World/{Discovery,PlayerView}.cs`
- Test: `tests/Core.Tests/TerrainLayersTests.cs`, `tests/Core.Tests/DiscoveryTests.cs`, `tests/Core.Tests/PlayerViewTests.cs`

**Interfaces:**
- Consumes: `VoxelWorld`, `Blocks`, `Hash`, `TestWorlds.SolidBelow`.
- Produces:
  - `enum MapSize { Small, Medium, Large }`; `static class MapSizes { int Dimension(MapSize s) }` returns 256/384/512.
  - `sealed record TerrainParams` (init properties) with defaults: `WaterLevel = 130, BaseHeight = 138, HillAmplitude = 10f, HillFrequency = 0.008f, MountainCoverage = 0.25f, MountainMaxHeight = 220, HasRiver = true, RiverWidth = 6, DeepStoneLevel = 64, MountainRockLine = 170, CaveRoof = 6`.
  - `sealed record MapSettings(int Seed, int SizeX, int SizeZ, TerrainParams Terrain)` with `static MapSettings Create(int seed, MapSize size)`.
  - `sealed class TerrainLayers(MapSettings s)` with `ushort BaseMaterial(int x, int y, int z, int surfaceY)`. This is the ground material before caves, ores and water, and it's used by both generation and fog.
  - `static class Discovery { void InitialFlood(VoxelWorld w); int RevealFrom(VoxelWorld w, int x, int y, int z); }`
  - `sealed class PlayerView(VoxelWorld world, TerrainLayers layers)` with `VoxelWorld World`, `bool FogEnabled = true`, `ushort GetVisibleBlock(int x, int y, int z)`.

`BaseMaterial` rules, top to bottom:
- `y > surfaceY` → Air.
- `y == 0` → Bedrock. For `y` in 1..3 → Bedrock if `Hash.Unit(seed,x,y,z) < (4-y)/4f`.
- Column dirt depth = `3 + Hash.Of(seed,x,0,z) % 3`.
- *Shore* columns (`surfaceY <= WaterLevel + 1`): Sand for the top `dirtDepth+1` blocks.
- *Rocky* columns (`surfaceY >= MountainRockLine`): Stone all the way down to the deep layer.
- Otherwise: `y == surfaceY` → Grass, then Dirt for the next `dirtDepth` blocks.
- Below that: DeepStone if `y < DeepStoneLevel + (int)(Hash.Of(seed,x,1,z) % 5) - 2`, otherwise Stone.

Discovery rules:
- `InitialFlood` marks every non-solid cell reachable from the top layer (y = 255) through non-solid cells, moving only between the 6 face neighbors. Outside the map counts as solid. Uses `markDirty: false`. For speed: first walk each column down from the sky until the first solid block, then breadth-first search only from cells that have an undiscovered non-solid neighbor. Use an array queue of packed ints, not `Queue<Int3>`.
- `RevealFrom` does nothing on a solid cell and returns 0. Otherwise it marks the cell discovered (even if no neighbor was discovered, because a dig the player ordered is always known) and floods through connected undiscovered non-solid cells, marking them dirty. It returns the number of cells newly discovered.

`GetVisibleBlock`:
- Out of bounds → Air.
- If `FogEnabled`, and the actual block is non-solid (air or water), and the cell is undiscovered, return `layers.BaseMaterial(x, y, z, world.GetSurfaceHeight(x, z))`.
- Otherwise return the actual block.
- Toggling `FogEnabled` is the caller's responsibility to follow with `MarkAllDirty()`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class TerrainLayersTests
{
    static TerrainLayers L(int seed = 1) => new(MapSettings.Create(seed, MapSize.Small));
    [Fact] public void Layers_TopToBottom()
    {
        var l = L();
        Assert.Equal(Blocks.Air, l.BaseMaterial(10, 151, 10, 150));
        Assert.Equal(Blocks.Grass, l.BaseMaterial(10, 150, 10, 150));
        Assert.Equal(Blocks.Dirt, l.BaseMaterial(10, 149, 10, 150));
        Assert.Equal(Blocks.Stone, l.BaseMaterial(10, 140, 10, 150));
        Assert.Equal(Blocks.DeepStone, l.BaseMaterial(10, 40, 10, 150));
        Assert.Equal(Blocks.Bedrock, l.BaseMaterial(10, 0, 10, 150));
    }
    [Fact] public void ShoreColumns_AreSand() { var l = L(); Assert.Equal(Blocks.Sand, l.BaseMaterial(5, 131, 5, 131)); Assert.Equal(Blocks.Sand, l.BaseMaterial(5, 129, 5, 131)); }
    [Fact] public void HighColumns_AreBareStone() => Assert.Equal(Blocks.Stone, L().BaseMaterial(5, 180, 5, 180));
    [Fact] public void IsDeterministic()
    {
        var a = L(9); var b = L(9);
        for (int x = 0; x < 40; x++) for (int y = 0; y < 160; y += 3) Assert.Equal(a.BaseMaterial(x, y, 7, 150), b.BaseMaterial(x, y, 7, 150));
    }
    [Fact] public void MapSettings_Create_UsesSize() { var s = MapSettings.Create(3, MapSize.Large); Assert.Equal(512, s.SizeX); Assert.Equal(512, s.SizeZ); Assert.Equal(130, s.Terrain.WaterLevel); }
}

public class DiscoveryTests
{
    // Sealed cave: air box x,z 20..24, y 100..103 inside stone (surface 150).
    static VoxelWorld WithCave()
    {
        var w = TestWorlds.SolidBelow();
        for (int x = 20; x <= 24; x++) for (int z = 20; z <= 24; z++) for (int y = 100; y <= 103; y++) w.SetBlock(x, y, z, Blocks.Air, false);
        return w;
    }
    [Fact] public void InitialFlood_DiscoversSky_NotSealedCave() { var w = WithCave(); Discovery.InitialFlood(w); Assert.True(w.IsDiscovered(5, 200, 5)); Assert.False(w.IsDiscovered(22, 101, 22)); }
    [Fact] public void InitialFlood_DiscoversCaveOpenToSky()
    {
        var w = WithCave(); for (int y = 104; y <= 150; y++) w.SetBlock(22, y, 22, Blocks.Air, false);
        Discovery.InitialFlood(w); Assert.True(w.IsDiscovered(20, 100, 20));
    }
    [Fact] public void RevealFrom_DiggingIntoCave_RevealsAllOfIt()
    {
        var w = WithCave(); Discovery.InitialFlood(w);
        w.SetBlock(22, 104, 22, Blocks.Air);
        Assert.True(Discovery.RevealFrom(w, 22, 104, 22) >= 101);
        Assert.True(w.IsDiscovered(22, 104, 22)); Assert.True(w.IsDiscovered(20, 100, 24));
    }
    [Fact] public void RevealFrom_SolidCell_DoesNothing() { var w = WithCave(); Assert.Equal(0, Discovery.RevealFrom(w, 5, 50, 5)); }
}

public class PlayerViewTests
{
    static PlayerView View(VoxelWorld w) => new(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small)));
    [Fact] public void HiddenCave_ShowsGroundMaterial()
    {
        var w = TestWorlds.SolidBelow(); w.SetBlock(5, 100, 5, Blocks.Air, false); w.SetBlock(6, 149, 6, Blocks.Air, false);
        for (int x = 0; x < 64; x++) for (int z = 0; z < 64; z++) w.SetSurfaceHeight(x, z, 150);
        Discovery.InitialFlood(w); var v = View(w);
        Assert.Equal(Blocks.Stone, v.GetVisibleBlock(5, 100, 5)); Assert.Equal(Blocks.Dirt, v.GetVisibleBlock(6, 149, 6));
    }
    [Fact] public void FogOff_ShowsActualAir() { var w = TestWorlds.SolidBelow(); w.SetBlock(5, 100, 5, Blocks.Air, false); var v = View(w); v.FogEnabled = false; Assert.Equal(Blocks.Air, v.GetVisibleBlock(5, 100, 5)); }
    [Fact] public void DiscoveredSky_IsAir() { var w = TestWorlds.SolidBelow(); Discovery.InitialFlood(w); Assert.Equal(Blocks.Air, View(w).GetVisibleBlock(5, 200, 5)); }
    [Fact] public void OutOfBounds_IsAir() => Assert.Equal(Blocks.Air, View(TestWorlds.SolidBelow()).GetVisibleBlock(-1, 100, 5));
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement** the files above. Make sure `TestWorlds.SolidBelow` sets surface heights; the `HiddenCave` test sets them again explicitly so it stays self-contained.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): terrain layers, discovery flood and fogged player view`.

---

### Task 4: Height field and column filling

**Files:**
- Create: `src/Core/Generation/FastNoiseLite.cs` (vendored unchanged from `https://raw.githubusercontent.com/Auburn/FastNoiseLite/master/CSharp/FastNoiseLite.cs`, keeping its MIT header), `src/Core/Generation/HeightField.cs`, `src/Core/Generation/ColumnFiller.cs`
- Test: `tests/Core.Tests/HeightFieldTests.cs`

**Interfaces:**
- Consumes: `MapSettings`, `TerrainParams`, `TerrainLayers`, `VoxelWorld`.
- Produces:
  - `sealed record HeightFieldResult(int[] Heights, bool RiverAlongX)`, indexed `x + z * SizeX`.
  - `static class HeightField { HeightFieldResult Generate(MapSettings s) }`
  - `static class ColumnFiller { void FillChunkColumn(VoxelWorld world, TerrainLayers layers, MapSettings s, int[] heights, int chunkX, int chunkZ) }`. It writes surface heights, `BaseMaterial` for `y <= h`, and `Water` for `h < y <= WaterLevel`, all with `markDirty: false`.

Height algorithm (noise via FastNoiseLite, OpenSimplex2, seeded from `s.Seed` plus a per-layer salt):
- **Hills:** `BaseHeight + HillAmplitude * fbm(4 octaves, HillFrequency)`.
- **Mountains:** a low-frequency mask (≈0.003) is remapped so that about `MountainCoverage` of the area is above 0, and smoothstepped. The mountain height is `mask * ridged fbm`, scaled so peaks approach `MountainMaxHeight`.
- **River** (if `HasRiver`): the seed picks the axis. The centerline is the map's midline plus a low-frequency 1D noise offset of up to ±25% of the map. Within `RiverWidth/2` of the centerline, the bed is `WaterLevel - 3` to `WaterLevel - 5`. Banks blend back to the terrain over 12 blocks using smoothstep. The river overrides mountains.
- **Clamp** all heights to `[WaterLevel - 8, 236]`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class HeightFieldTests
{
    static MapSettings S(int seed = 42, TerrainParams? t = null) => new(seed, 128, 128, t ?? new TerrainParams());
    [Fact] public void IsDeterministic() => Assert.Equal(HeightField.Generate(S()).Heights, HeightField.Generate(S()).Heights);
    [Fact] public void SeedsDiffer() => Assert.NotEqual(HeightField.Generate(S(1)).Heights, HeightField.Generate(S(2)).Heights);
    [Fact] public void StaysInBounds() { foreach (var h in HeightField.Generate(S()).Heights) Assert.InRange(h, 122, 236); }
    [Fact] public void River_CrossesWholeMap()
    {
        var s = S(); var r = HeightField.Generate(s);
        for (int a = 0; a < 128; a++)
        {
            bool wet = false;
            for (int b = 0; b < 128 && !wet; b++) { int x = r.RiverAlongX ? a : b, z = r.RiverAlongX ? b : a; wet = r.Heights[x + z * 128] < s.Terrain.WaterLevel; }
            Assert.True(wet, $"no water at {a}");
        }
    }
    [Fact] public void NoRiver_HighBase_IsDry() { var t = new TerrainParams { HasRiver = false, BaseHeight = 160 }; Assert.All(HeightField.Generate(S(t: t)).Heights, h => Assert.True(h > t.WaterLevel)); }
    [Fact] public void HighCoverage_MakesMountainsAboveRockLine() { var t = new TerrainParams { MountainCoverage = 0.6f }; Assert.True(HeightField.Generate(S(t: t)).Heights.Max() >= t.MountainRockLine); }
    [Fact] public void FillChunkColumn_LayersAndWater()
    {
        var s = S(); var r = HeightField.Generate(s); var w = new VoxelWorld(128, 128); var l = new TerrainLayers(s);
        for (int cx = 0; cx < 4; cx++) for (int cz = 0; cz < 4; cz++) ColumnFiller.FillChunkColumn(w, l, s, r.Heights, cx, cz);
        for (int x = 0; x < 128; x += 7) for (int z = 0; z < 128; z += 7)
        {
            int h = r.Heights[x + z * 128]; Assert.Equal(h, w.GetSurfaceHeight(x, z));
            Assert.Equal(l.BaseMaterial(x, h, z, h), w.GetBlock(x, h, z));
            Assert.Equal(h < s.Terrain.WaterLevel ? Blocks.Water : Blocks.Air, w.GetBlock(x, h + 1, z));
            Assert.Equal(Blocks.Air, w.GetBlock(x, Math.Max(h, s.Terrain.WaterLevel) + 1, z));
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Vendor FastNoiseLite and implement `HeightField` and `ColumnFiller`.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): height field with hills, mountains and river; column filling`.

---

### Task 5: Jobs, ColonyMap and the designator

**Files:**
- Create: `src/Core/ColonyMap.cs`, `src/Core/Jobs/{JobKind,Job,JobQueue,DesignationTool,DragBox,Designator}.cs`
- Modify: `tests/Core.Tests/TestWorlds.cs` (add `FlatMap`)
- Test: `tests/Core.Tests/JobQueueTests.cs`, `tests/Core.Tests/DesignatorTests.cs`

**Interfaces:**
- Consumes: `VoxelWorld`, `PlayerView`, `TerrainLayers`, `MapSettings`, `Blocks`, `Discovery`.
- Produces:
  - `sealed class ColonyMap(MapSettings settings, VoxelWorld world, JobQueue? jobs = null)` with properties `Settings, World, Layers (new TerrainLayers(settings)), View (new PlayerView(world, Layers)), Jobs`.
  - `enum JobKind { Dig, BuildWall, BuildFloor, BuildStairs }`; `readonly record struct Job(Int3 Position, JobKind Kind, ushort Block)` (Block is `Air` for digs).
  - `sealed class JobQueue`: `void Add(Job j)` replaces any job at the same position; `bool Remove(Int3 p)`; `bool Contains(Int3 p)`; `bool TryGet(Int3 p, out Job j)`; `int Count`; `IEnumerable<Job> All`; `int Version`, which increments on every change.
  - `enum DesignationTool { Dig, Wall, Floor, Stairs, Cancel }`; `readonly record struct DragBox(Int3 Start, Int3 End)`.
  - `static class Designator`: `const int MaxCellsPerDrag = 200_000`; `IReadOnlyList<Job> Preview(ColonyMap map, DesignationTool tool, BuildMaterial material, DragBox box)`, which for Cancel returns the existing jobs that would be removed; `int Apply(ColonyMap map, DesignationTool tool, BuildMaterial material, DragBox box)`, which returns the number of jobs added or removed.
  - `TestWorlds.FlatMap(int size = 64, int surface = 150)` builds `SolidBelow`, runs `InitialFlood`, and returns `new ColonyMap(new MapSettings(1, size, size, new TerrainParams()), world)`.

Rules. The box spans `min..max` of Start and End on each axis, and is clipped to x/z in-bounds and y in 1..255. All checks use `map.View.GetVisibleBlock`, so the fogged world is what counts.
- **Dig:** a cell where the visible block is diggable.
- **Wall:** cells on the x/z outline of the box at every y level, that are empty (visible block is `Air`). Block is `Blocks.BuildBlock(material)`.
- **Floor:** every empty cell in the box. Block is `Blocks.BuildBlock(material)`.
- **Stairs:** ignores `End.Y`. The dominant x/z axis of `End - Start` gives the ascent direction (a tie picks X; a single cell faces South). The length is the box extent along that axis, and the width is the extent along the other axis. Step `i` puts cells at `y = Start.Y + i`, `i` blocks from Start along the direction, across the full width. Only empty cells are used. Block is `Blocks.Stairs(material, facing)`.
- **Cancel:** existing jobs inside the box.
- **Cap:** stop collecting once `MaxCellsPerDrag` cells are produced.

- [ ] **Step 1: Write the failing tests**

```csharp
public class JobQueueTests
{
    [Fact] public void Add_ReplacesAtSamePosition_AndBumpsVersion()
    {
        var q = new JobQueue(); var p = new Int3(1, 2, 3);
        q.Add(new Job(p, JobKind.Dig, Blocks.Air)); var v = q.Version;
        q.Add(new Job(p, JobKind.BuildWall, Blocks.WoodPlanks));
        Assert.Equal(1, q.Count); Assert.True(q.Version > v); Assert.True(q.TryGet(p, out var j)); Assert.Equal(JobKind.BuildWall, j.Kind);
    }
    [Fact] public void Remove_MissingReturnsFalse() => Assert.False(new JobQueue().Remove(new Int3(0, 0, 0)));
}

public class DesignatorTests
{
    static DragBox B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new(x0, y0, z0), new(x1, y1, z1));
    static IReadOnlyList<Job> P(ColonyMap m, DesignationTool t, DragBox b, BuildMaterial mat = BuildMaterial.Wood) => Designator.Preview(m, t, mat, b);

    [Fact] public void Dig_Volume() { var j = P(TestWorlds.FlatMap(), DesignationTool.Dig, B(10, 150, 10, 12, 148, 12)); Assert.Equal(27, j.Count); Assert.All(j, x => Assert.Equal(JobKind.Dig, x.Kind)); }
    [Fact] public void Dig_SkipsAirAndBedrock() { var m = TestWorlds.FlatMap(); Assert.Empty(P(m, DesignationTool.Dig, B(0, 160, 0, 5, 160, 5))); Assert.All(P(m, DesignationTool.Dig, B(0, 0, 0, 1, 1, 1)), j => Assert.Equal(1, j.Position.Y)); }
    [Fact] public void Dig_HiddenCaveCellsCountAsRock() { var m = TestWorlds.FlatMap(); m.World.SetBlock(30, 100, 30, Blocks.Air, false); Assert.Single(P(m, DesignationTool.Dig, B(30, 100, 30, 30, 100, 30))); }
    [Fact] public void Wall_IsOutline() { var j = P(TestWorlds.FlatMap(), DesignationTool.Wall, B(10, 151, 10, 14, 151, 13)); Assert.Equal(14, j.Count); Assert.All(j, x => Assert.Equal(Blocks.WoodPlanks, x.Block)); }
    [Fact] public void Wall_RepeatsPerLevel() => Assert.Equal(42, P(TestWorlds.FlatMap(), DesignationTool.Wall, B(10, 151, 10, 14, 153, 13)).Count);
    [Fact] public void Floor_Fills() { var j = P(TestWorlds.FlatMap(), DesignationTool.Floor, B(10, 151, 10, 14, 151, 13), BuildMaterial.Stone); Assert.Equal(20, j.Count); Assert.All(j, x => Assert.Equal(Blocks.StoneBricks, x.Block)); }
    [Fact] public void Build_SkipsOccupied() => Assert.Empty(P(TestWorlds.FlatMap(), DesignationTool.Floor, B(10, 150, 10, 14, 150, 13)));
    [Fact] public void Stairs_RiseAlongDominantAxis()
    {
        var j = P(TestWorlds.FlatMap(), DesignationTool.Stairs, B(10, 151, 10, 14, 151, 11));
        Assert.Equal(10, j.Count);
        Assert.Contains(j, x => x.Position == new Int3(14, 155, 11) && x.Block == Blocks.Stairs(BuildMaterial.Wood, Facing.East));
    }
    [Fact] public void Stairs_NegativeX_FacesWest() => Assert.All(P(TestWorlds.FlatMap(), DesignationTool.Stairs, B(14, 151, 10, 10, 151, 10)), x => Assert.Equal(Blocks.Stairs(BuildMaterial.Wood, Facing.West), x.Block));
    [Fact] public void Stairs_SingleCell_FacesSouth() => Assert.Equal(Blocks.Stairs(BuildMaterial.Wood, Facing.South), Assert.Single(P(TestWorlds.FlatMap(), DesignationTool.Stairs, B(5, 151, 5, 5, 151, 5))).Block);
    [Fact] public void Apply_QueuesThenCancelRemoves()
    {
        var m = TestWorlds.FlatMap(); var box = B(10, 150, 10, 12, 150, 12);
        Assert.Equal(9, Designator.Apply(m, DesignationTool.Dig, BuildMaterial.Wood, box)); Assert.Equal(9, m.Jobs.Count);
        Assert.Equal(4, Designator.Apply(m, DesignationTool.Cancel, BuildMaterial.Wood, B(10, 150, 10, 11, 150, 11))); Assert.Equal(5, m.Jobs.Count);
    }
    [Fact] public void Box_OutsideMapBounds_IsClipped()
    {
        var j = P(TestWorlds.FlatMap(), DesignationTool.Dig, B(-5, 148, -5, 2, 152, 2));
        Assert.Equal(27, j.Count); Assert.All(j, x => Assert.True(x.Position.X >= 0 && x.Position.Z >= 0));
    }
    [Fact] public void HugeBox_IsCappedAtMaxCellsPerDrag()
    {
        var m = new ColonyMap(MapSettings.Create(1, MapSize.Large), TestWorlds.SolidBelow(512));
        Assert.Equal(Designator.MaxCellsPerDrag, P(m, DesignationTool.Dig, B(0, 150, 0, 511, 100, 511)).Count);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement** the types above, plus `TestWorlds.FlatMap`.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): job queue, colony map and drag designations`.

---

### Task 6: Caves, ores, surface decoration and the generator

**Files:**
- Create: `src/Core/Generation/{CaveCarver,OrePlacer,SurfaceDecorator,MapGenerator}.cs`
- Modify: `tests/Core.Tests/TestWorlds.cs` (add `static ulong WorldHash(VoxelWorld w)`, an FNV hash over all blocks)
- Test: `tests/Core.Tests/MapGeneratorTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–5.
- Produces:
  - `sealed class CaveCarver(MapSettings s) { void CarveChunkColumn(VoxelWorld world, int chunkX, int chunkZ); }`. It builds its noise once in the constructor; reads are thread-safe.
  - `sealed record OreBand(ushort Block, int MinY, int MaxY, int VeinsPerChunk, int MinSize, int MaxSize)`; `static class OrePlacer { IReadOnlyList<OreBand> Bands; void Place(VoxelWorld world, MapSettings s); }`
  - `static class SurfaceDecorator { void Decorate(VoxelWorld world, MapSettings s); }`
  - `readonly record struct GenerationProgress(string Stage, float Fraction)`; `static class MapGenerator { ColonyMap Generate(MapSettings s, IProgress<GenerationProgress>? progress = null, CancellationToken ct = default); }`

Generator pipeline. Report progress per stage; the last report is `Fraction == 1`.
1. Run `HeightField`.
2. In parallel (`Parallel.For` over chunk columns): `ColumnFiller.FillChunkColumn`, then `CaveCarver.CarveChunkColumn`.
3. Run `OrePlacer` (sequential).
4. Run `SurfaceDecorator` (sequential).
5. Run `Discovery.InitialFlood`.
6. Return `new ColonyMap(s, world)`.

Caves only carve cells with `6 <= y <= surface - roof`, where `roof` is 2 for columns at or above `MountainRockLine` (so caves can open onto mountain faces) and `CaveRoof` otherwise. Columns with `surface <= WaterLevel` are skipped. Two shapes are carved:
- **Spaghetti tunnels:** two 3D noises both near zero (`n1² + n2² < 0.0045`, with y frequency about 1.5× the xz frequency).
- **Caverns:** a third noise above 0.6, only where `y < surface - 12`.

Ore bands. Random-walk veins with a seeded `Random(Hash.Of(seed, oreIndex, chunkX*64+chunkY, chunkZ))`. They replace only Stone or DeepStone, inside their band.

| Ore | Y range | Veins per 32³ | Size |
|---|---|---|---|
| Coal | 70–180 | 6 | 6–14 |
| Copper | 50–140 | 4 | 4–10 |
| Iron | 20–110 | 4 | 4–9 |
| Gold | 5–50 | 2 | 3–7 |

Decoration:
- **Candidate spots:** a jittered 5×5 grid. Forest-density noise decides how many candidates get trees.
- **A tree needs:** a Grass surface below `MountainRockLine`, its 3×3 neighborhood within ±1 height, and air above.
- **Tree type:** Oak below Y 160 (4–6 trunk, leaf blob of radius 2). Pine at or above 160, or where a noise picks it (6–9 trunk, cone of leaves).
- **Boulders:** rare clusters of 2–4 Cobblestone on grass.
- Trunks replace nothing below the surface.

- [ ] **Step 1: Write the failing tests**

```csharp
public class MapGeneratorTests
{
    static readonly MapSettings S = new(42, 128, 128, new TerrainParams());
    static readonly Lazy<ColonyMap> M = new(() => MapGenerator.Generate(S));

    [Fact] public void IsDeterministic() => Assert.Equal(TestWorlds.WorldHash(M.Value.World), TestWorlds.WorldHash(MapGenerator.Generate(S).World));
    [Fact] public void SeedsDiffer() => Assert.NotEqual(TestWorlds.WorldHash(M.Value.World), TestWorlds.WorldHash(MapGenerator.Generate(S with { Seed = 43 }).World));
    [Fact] public void Caves_Exist_AreHidden_AndStaySealedOutsideMountains()
    {
        var w = M.Value.World; var t = S.Terrain; int hidden = 0;
        for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++)
        {
            int h = w.GetSurfaceHeight(x, z);
            for (int y = 6; y < h; y++) if (w.GetBlock(x, y, z) == Blocks.Air && !w.IsDiscovered(x, y, z)) hidden++;
            if (h < t.MountainRockLine && h > t.WaterLevel)
                for (int y = h - t.CaveRoof + 1; y <= h; y++) Assert.NotEqual(Blocks.Air, w.GetBlock(x, y, z));
        }
        Assert.True(hidden > 500);
    }
    [Fact] public void Ores_AppearOnlyInTheirBands()
    {
        var w = M.Value.World; var seen = new HashSet<ushort>();
        for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++) for (int y = 0; y < 256; y++)
        {
            var b = w.GetBlock(x, y, z); var band = OrePlacer.Bands.FirstOrDefault(o => o.Block == b);
            if (band is null) continue; seen.Add(b); Assert.InRange(y, band.MinY, band.MaxY);
        }
        Assert.Equal(OrePlacer.Bands.Count, seen.Count);
    }
    [Fact] public void Trees_StandOnGround()
    {
        var w = M.Value.World; int trunks = 0;
        for (int x = 0; x < 128; x++) for (int z = 0; z < 128; z++) for (int y = 1; y < 256; y++)
        {
            var b = w.GetBlock(x, y, z); var below = w.GetBlock(x, y - 1, z);
            if ((b == Blocks.OakLog || b == Blocks.PineLog) && below != b) { trunks++; Assert.True(below == Blocks.Grass || below == Blocks.Dirt); }
        }
        Assert.True(trunks > 10);
    }
    [Fact] public void ReportsProgress_EndingAtOne()
    {
        var reports = new List<GenerationProgress>();
        MapGenerator.Generate(new MapSettings(1, 64, 64, new TerrainParams()), new SyncProgress<GenerationProgress>(reports.Add));
        Assert.Equal(1f, reports[^1].Fraction);
    }
    [Fact, Trait("Category", "Perf")] public void Large_GeneratesUnder30s()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); MapGenerator.Generate(MapSettings.Create(7, MapSize.Large));
        Assert.True(sw.Elapsed.TotalSeconds < 30, $"{sw.Elapsed.TotalSeconds:F1}s");
    }
}
// In TestWorlds.cs: sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T v) => report(v); }
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement** the files above, plus `TestWorlds.WorldHash` and `SyncProgress`.
- [ ] **Step 4: Run the tests and confirm they pass**, then also run `dotnet test tests/Core.Tests --filter "Category=Perf"`. Expected: PASS, and note the time in the commit message.
- [ ] **Step 5: Commit** `feat(core): caves, ores, trees and full map generator (Large: <N>s)`.

---

### Task 7: Instant worker

**Files:**
- Create: `src/Core/Jobs/InstantWorker.cs`
- Test: `tests/Core.Tests/InstantWorkerTests.cs`

**Interfaces:**
- Consumes: `ColonyMap`, `JobQueue`, `Discovery`.
- Produces: `static class InstantWorker { int Step(ColonyMap map, int maxJobs); }`. It completes up to `maxJobs` jobs and returns how many.

Order: every Dig job first, highest Y first. Then build jobs, lowest Y first. Ties are broken by X, then Z, so the order is deterministic.
- **Dig:** `SetBlock(Air)` then `Discovery.RevealFrom`.
- **Build:** if the actual block is `Air`, `SetBlock(job.Block)`; otherwise drop the job.

Every processed job is removed. One call must stay under 2 ms with 200 000 queued jobs. Pick the batch with a single pass and a bounded heap, not a full sort.

- [ ] **Step 1: Write the failing tests**

```csharp
public class InstantWorkerTests
{
    static Job Dig(int x, int y, int z) => new(new(x, y, z), JobKind.Dig, Blocks.Air);
    static Job Wall(int x, int y, int z) => new(new(x, y, z), JobKind.BuildWall, Blocks.WoodPlanks);

    [Fact] public void DigsTopDown_ThenBuildsBottomUp()
    {
        var m = TestWorlds.FlatMap();
        m.Jobs.Add(Wall(1, 152, 1)); m.Jobs.Add(Wall(1, 151, 1)); m.Jobs.Add(Dig(5, 148, 5)); m.Jobs.Add(Dig(5, 150, 5)); m.Jobs.Add(Dig(5, 149, 5));
        InstantWorker.Step(m, 1); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 150, 5)); Assert.Equal(Blocks.Stone, m.World.GetBlock(5, 149, 5));
        InstantWorker.Step(m, 2); Assert.Equal(Blocks.Air, m.World.GetBlock(5, 148, 5)); Assert.Equal(Blocks.Air, m.World.GetBlock(1, 151, 1));
        InstantWorker.Step(m, 1); Assert.Equal(Blocks.WoodPlanks, m.World.GetBlock(1, 151, 1));
        Assert.Equal(1, InstantWorker.Step(m, 5)); Assert.Equal(0, m.Jobs.Count);
    }
    [Fact] public void DigIntoHiddenCave_RevealsIt()
    {
        var m = TestWorlds.FlatMap(); m.World.SetBlock(20, 100, 20, Blocks.Air, false); m.World.SetBlock(21, 100, 20, Blocks.Air, false);
        m.Jobs.Add(Dig(20, 101, 20)); InstantWorker.Step(m, 1);
        Assert.True(m.World.IsDiscovered(21, 100, 20));
    }
    [Fact] public void Step_BuildOnOccupiedCell_IsDiscarded() { var m = TestWorlds.FlatMap(); m.Jobs.Add(Wall(3, 150, 3)); Assert.Equal(1, InstantWorker.Step(m, 1)); Assert.Equal(Blocks.Stone, m.World.GetBlock(3, 150, 3)); Assert.Equal(0, m.Jobs.Count); }
    [Fact] public void Step_DigOnAirCell_CompletesAndReveals()
    {
        var m = TestWorlds.FlatMap(); m.World.SetBlock(9, 90, 9, Blocks.Air, false);
        m.Jobs.Add(Dig(9, 90, 9)); InstantWorker.Step(m, 1); Assert.True(m.World.IsDiscovered(9, 90, 9));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement `InstantWorker`.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): instant worker completes queued jobs in order`.

---

### Task 8: Save and load

**Files:**
- Create: `src/Core/Persistence/MapSerializer.cs` (may add `internal` chunk accessors to `VoxelWorld`)
- Test: `tests/Core.Tests/MapSerializerTests.cs`

**Interfaces:**
- Consumes: `ColonyMap`, `VoxelWorld`, `JobQueue`, `Blocks.Registry`.
- Produces: `static class MapSerializer { const int FormatVersion = 1; void Save(ColonyMap map, Stream stream); ColonyMap Load(Stream stream); }`

Format: the ASCII magic `CSIM`, then an int32 version, then a body compressed with `BrotliStream`. The body contains:
- the settings (seed, sizes, every `TerrainParams` field);
- a block palette (count + names);
- surface heights;
- per chunk: blocks (a uniform flag + id, or 32768 ids, mapped through the palette) and discovery (a uniform flag, or 512 ulongs);
- jobs (count; x, y, z, kind, palette index).

Load throws `InvalidDataException` for: a bad magic, `version > FormatVersion` (message names both versions), unknown block names, or a truncated or corrupt body (any non-`InvalidDataException` thrown while reading the body is wrapped in `InvalidDataException`). Load must not mark chunks dirty, because the renderer meshes everything when the map loads.

- [ ] **Step 1: Write the failing tests**

```csharp
public class MapSerializerTests
{
    static ColonyMap RoundTrip(ColonyMap m) { var ms = new MemoryStream(); MapSerializer.Save(m, ms); ms.Position = 0; return MapSerializer.Load(ms); }
    [Fact] public void RoundTripsEverything()
    {
        var m = MapGenerator.Generate(new MapSettings(5, 64, 64, new TerrainParams()));
        m.World.SetBlock(10, 140, 10, Blocks.StoneBricks); Discovery.RevealFrom(m.World, 10, 141, 10);
        Designator.Apply(m, DesignationTool.Dig, BuildMaterial.Wood, new DragBox(new(1, 120, 1), new(4, 118, 4)));
        var r = RoundTrip(m);
        Assert.Equal(m.Settings, r.Settings); Assert.Equal(TestWorlds.WorldHash(m.World), TestWorlds.WorldHash(r.World));
        for (int x = 0; x < 64; x += 3) for (int z = 0; z < 64; z += 3) { Assert.Equal(m.World.GetSurfaceHeight(x, z), r.World.GetSurfaceHeight(x, z)); for (int y = 0; y < 256; y += 5) Assert.Equal(m.World.IsDiscovered(x, y, z), r.World.IsDiscovered(x, y, z)); }
        Assert.Equal(m.Jobs.All.OrderBy(j => j.Position.ToString()), r.Jobs.All.OrderBy(j => j.Position.ToString()));
    }
    [Fact] public void Load_BadMagic_ThrowsInvalidData() => Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream("NOPE1234"u8.ToArray())));
    [Fact] public void Load_Truncated_ThrowsInvalidData()
    {
        var ms = new MemoryStream(); MapSerializer.Save(TestWorlds.FlatMap(), ms);
        Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream(ms.ToArray()[..(int)(ms.Length / 2)])));
    }
    [Fact] public void Load_NewerVersion_ThrowsInvalidData()
    {
        var ms = new MemoryStream(); MapSerializer.Save(TestWorlds.FlatMap(), ms); var bytes = ms.ToArray();
        BitConverter.GetBytes(MapSerializer.FormatVersion + 1).CopyTo(bytes, 4);
        Assert.Contains("version", Assert.Throws<InvalidDataException>(() => MapSerializer.Load(new MemoryStream(bytes))).Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement `MapSerializer`.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): binary save/load with block palette`.

---

### Task 9: Chunk and cap meshing

**Files:**
- Create: `src/Core/Meshing/{MeshBuffers,MeshPair,ChunkMesher,CapMesher}.cs`
- Test: `tests/Core.Tests/MeshingTests.cs`

**Interfaces:**
- Consumes: `PlayerView`, `Blocks.Registry`, `VoxelWorld`.
- Produces:
  - `sealed class MeshBuffers { List<Vector3> Vertices; List<Vector3> Normals; List<Vector2> Uvs; List<Vector2> Uv2s; List<int> Indices; bool IsEmpty; }` (System.Numerics). `Uv2.X` = texture layer, and `Uv2.Y` = ambient occlusion from 0 to 1, where 1 = unoccluded.
  - `sealed record MeshPair(MeshBuffers Opaque, MeshBuffers Liquid)`
  - `static class ChunkMesher { MeshPair Build(PlayerView view, Int3 chunk); }` (vertices are in world coordinates)
  - `static class CapMesher { MeshPair Build(PlayerView view, int chunkX, int chunkZ, int cutY); }`

ChunkMesher:
- Read only `view.GetVisibleBlock`.
- **Cube:** emit each face whose neighbor does not `Occlude`.
- **Liquid:** emit a face only where the neighbor is `Air`.
- **Stairs:** emit two boxes. The bottom slab spans the full width at height 0–0.5. The upper half-block covers the half nearer the ascent direction (`Facing`) at height 0.5–1. Faces on a block boundary are culled against occluding neighbors.
- **Texture per face:** top uses `TexTop`, bottom uses `TexBottom`, sides use `TexSide`. Each face uses UVs 0–1, with V=0 at the top of side faces.
- **Ambient occlusion:** standard per-vertex AO from the three neighbors in front of the face (side1, side2, corner), counting `Occludes` blocks. Values are `(3 - n) / 3`, where n is 3 if both sides are occluded.
- Triangles wind clockwise as seen from outside.
- Uniform-air chunks return empty buffers immediately.

CapMesher: for each column of the chunk column, let `b = visible(x, cutY, z)` and `a = visible(x, cutY+1, z)`.
- If `b` is solid and `a` is solid, emit an up-facing quad at height `cutY+1`, using `b`'s top texture and AO 1, into Opaque.
- If `b` is liquid and `a` is liquid or solid, emit the quad into Liquid.

- [ ] **Step 1: Write the failing tests**

```csharp
public class MeshingTests
{
    static PlayerView V(VoxelWorld w) { var v = new PlayerView(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small))); return v; }
    static int Faces(MeshBuffers b) => b.Indices.Count / 6;

    [Fact] public void SingleBlock_SixFaces() { var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); var m = ChunkMesher.Build(V(w), new Int3(0, 0, 0)); Assert.Equal(6, Faces(m.Opaque)); Assert.Equal(24, m.Opaque.Vertices.Count); }
    [Fact] public void AdjacentBlocks_CullSharedFaces() { var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); w.SetBlock(6, 5, 5, Blocks.Stone); Assert.Equal(10, Faces(ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque)); }
    [Fact] public void Triangles_AreClockwiseFromOutside()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        for (int i = 0; i < b.Indices.Count; i += 3)
        {
            var (a, c, d) = (b.Vertices[b.Indices[i]], b.Vertices[b.Indices[i + 1]], b.Vertices[b.Indices[i + 2]]);
            Assert.True(Vector3.Dot(Vector3.Cross(c - a, d - a), b.Normals[b.Indices[i]]) < 0);
        }
    }
    [Fact] public void GrassTop_UsesGrassTopLayer()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Grass); var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque;
        int top = b.Normals.FindIndex(n => n == Vector3.UnitY); Assert.Equal(Blocks.Registry.TextureIndex("grass_top"), (int)b.Uv2s[top].X);
    }
    [Fact] public void HiddenCave_HasNoInternalFaces_UntilFogOff()
    {
        var w = TestWorlds.SolidBelow(32, 60); w.SetBlock(10, 30, 10, Blocks.Air, false); Discovery.InitialFlood(w);
        var v = V(w); int fogged = Faces(ChunkMesher.Build(v, new Int3(0, 0, 0)).Opaque);
        v.FogEnabled = false; Assert.Equal(fogged + 6, Faces(ChunkMesher.Build(v, new Int3(0, 0, 0)).Opaque));
    }
    [Fact] public void Water_FacesAirOnly() { var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Water); w.SetBlock(5, 4, 5, Blocks.Stone); Discovery.InitialFlood(w); Assert.Equal(5, Faces(ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Liquid)); }
    [Fact] public void MapEdge_EmitsSideFaces() { var w = new VoxelWorld(32, 32); w.SetBlock(0, 5, 5, Blocks.Stone); Assert.Contains(-Vector3.UnitX, ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque.Normals); }
    [Fact] public void AO_DarkensCornerNextToWall()
    {
        var w = new VoxelWorld(32, 32); w.SetBlock(5, 5, 5, Blocks.Stone); w.SetBlock(6, 6, 5, Blocks.Stone);
        var b = ChunkMesher.Build(V(w), new Int3(0, 0, 0)).Opaque; var tops = Enumerable.Range(0, b.Normals.Count).Where(i => b.Normals[i] == Vector3.UnitY && b.Vertices[i].Y == 6);
        Assert.Contains(tops, i => b.Uv2s[i].Y < 1f);
    }
    [Fact] public void Cap_OneQuadPerSolidColumn_NoneWhereOpen()
    {
        var w = TestWorlds.SolidBelow(32, 60); Discovery.InitialFlood(w); var v = V(w);
        Assert.Equal(32 * 32, Faces(CapMesher.Build(v, 0, 0, 40).Opaque));
        Assert.True(CapMesher.Build(v, 0, 0, 60).Opaque.IsEmpty);
        w.SetBlock(3, 40, 3, Blocks.Air); Discovery.RevealFrom(w, 3, 40, 3);
        Assert.Equal(32 * 32 - 1, Faces(CapMesher.Build(v, 0, 0, 40).Opaque));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the meshers.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit** `feat(core): chunk and slice-cap meshing with AO`.

---

### Task 10: Voxel raycast and tool targeting

**Files:**
- Create: `src/Core/Tools/{VoxelRaycast,ToolTargeting}.cs`
- Test: `tests/Core.Tests/ToolTargetingTests.cs`

**Interfaces:**
- Consumes: `PlayerView`, `DesignationTool`, `DragBox`.
- Produces:
  - `readonly record struct RaycastHit(Int3 Block, Int3 Normal, Vector3 Point)`
  - `static class VoxelRaycast { bool Cast(PlayerView view, Vector3 origin, Vector3 direction, float maxDistance, int maxY, out RaycastHit hit); }`. This is an Amanatides–Woo DDA. Cells with `y > maxY` count as empty. Air and liquid are passed through. It works from origins outside the map.
  - `static class ToolTargeting`:
    - `Int3 Target(DesignationTool tool, RaycastHit hit, int viewLevel, int topLevel)`. When `viewLevel < topLevel`, it returns `(hit.Block.X, viewLevel, hit.Block.Z)`. At the top level, it returns `hit.Block` for Dig/Cancel and `hit.Block + hit.Normal` for Wall/Floor/Stairs.
    - `float DragPlaneHeight(DesignationTool tool, int targetY)` returns `targetY` for build tools and `targetY + 1` for Dig/Cancel.
    - `bool IntersectPlane(Vector3 origin, Vector3 direction, float planeY, out int x, out int z)`
    - `int JumpLevel(RaycastHit hit, int topLevel)` returns `Block.Y + 1` if `Normal.Y > 0`, otherwise `Block.Y`, clamped to `[1, topLevel]`.
    - `DragBox ExtendBox(DragBox box, int levelAtStart, int currentLevel)` sets `End.Y = Start.Y + (currentLevel - levelAtStart)`, clamped to 1..255.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ToolTargetingTests
{
    static PlayerView Flat() { var w = TestWorlds.SolidBelow(64, 150); Discovery.InitialFlood(w); return new PlayerView(w, new TerrainLayers(MapSettings.Create(1, MapSize.Small))); }
    [Fact] public void Raycast_DownHitsSurfaceTop()
    {
        Assert.True(VoxelRaycast.Cast(Flat(), new(10.5f, 300, 10.5f), -Vector3.UnitY, 1000, 255, out var h));
        Assert.Equal(new Int3(10, 150, 10), h.Block); Assert.Equal(new Int3(0, 1, 0), h.Normal); Assert.Equal(151f, h.Point.Y, 3);
    }
    [Fact] public void Raycast_RespectsMaxY() { VoxelRaycast.Cast(Flat(), new(10.5f, 300, 10.5f), -Vector3.UnitY, 1000, 120, out var h); Assert.Equal(120, h.Block.Y); }
    [Fact] public void Raycast_FromOutsideMap_HitsSideFace() { Assert.True(VoxelRaycast.Cast(Flat(), new(-20, 100.5f, 5.5f), Vector3.UnitX, 1000, 255, out var h)); Assert.Equal(new Int3(-1, 0, 0), h.Normal); Assert.Equal(0, h.Block.X); }
    [Fact] public void Raycast_SkyMisses() => Assert.False(VoxelRaycast.Cast(Flat(), new(10, 300, 10), Vector3.UnitY, 1000, 255, out _));
    [Fact] public void Target_SlicedUsesViewLevel() => Assert.Equal(new Int3(4, 120, 6), ToolTargeting.Target(DesignationTool.Wall, new(new(4, 110, 6), new(0, 1, 0), default), 120, 255));
    [Fact] public void Target_TopLevel_BuildUsesFaceNeighbor() => Assert.Equal(new Int3(4, 151, 6), ToolTargeting.Target(DesignationTool.Floor, new(new(4, 150, 6), new(0, 1, 0), default), 255, 255));
    [Fact] public void Target_TopLevel_DigUsesHitBlock() => Assert.Equal(new Int3(4, 150, 6), ToolTargeting.Target(DesignationTool.Dig, new(new(4, 150, 6), new(0, 1, 0), default), 255, 255));
    [Fact] public void DragPlane_BuildAtLevel_DigAboveIt() { Assert.Equal(120f, ToolTargeting.DragPlaneHeight(DesignationTool.Wall, 120)); Assert.Equal(121f, ToolTargeting.DragPlaneHeight(DesignationTool.Dig, 120)); }
    [Fact] public void IntersectPlane_Floors() { Assert.True(ToolTargeting.IntersectPlane(new(0.5f, 10, 0.5f), Vector3.Normalize(new(1, -1, 0)), 5, out var x, out var z)); Assert.Equal(5, x); Assert.Equal(0, z); }
    [Fact] public void JumpLevel_TopFaceGoesToStandingLevel() { Assert.Equal(151, ToolTargeting.JumpLevel(new(new(1, 150, 1), new(0, 1, 0), default), 255)); Assert.Equal(150, ToolTargeting.JumpLevel(new(new(1, 150, 1), new(1, 0, 0), default), 255)); }
    [Fact] public void ExtendBox_ClampsToWorldHeight()
    {
        var b = new DragBox(new(1, 3, 1), new(5, 3, 5));
        Assert.Equal(1, ToolTargeting.ExtendBox(b, 3, -10).End.Y); Assert.Equal(255, ToolTargeting.ExtendBox(b, 3, 400).End.Y); Assert.Equal(6, ToolTargeting.ExtendBox(b, 10, 13).End.Y);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement both classes.**
- [ ] **Step 4: Run the full Core suite and confirm it passes.**
- [ ] **Step 5: Commit** `feat(core): voxel raycast and tool targeting rules`.

---

### Task 11: Godot rendering, block textures and the screenshot harness

**Files:**
- Create: `src/Game/{GameSession,MapScene}.cs`, `src/Game/Rendering/{BlockTextures,MeshConvert,ChunkRenderer}.cs`, `src/Game/Debug/VisualShots.cs`, `assets/shaders/{block_common.gdshaderinc,block.gdshader,liquid.gdshader}`
- Modify: `scenes/map.tscn` (attach `res://src/Game/MapScene.cs` to the root `World`), `project.godot` (add a `[shader_globals]` entry for `slice_height`, type `float`, value `100000.0`; window size 1600×900)

**Interfaces:**
- Consumes: `ColonyMap`, `MapGenerator`, `ChunkMesher`, `MeshBuffers`, `Blocks.Registry`, `VoxelWorld.ConsumeDirtyChunks`.
- Produces:
  - `static class GameSession { ColonyMap? Map; }`
  - `partial class MapScene : Node3D` exposes `ColonyMap Map`, `ChunkRenderer Chunks`, and later `CameraRig Camera`, `SliceController Slice`, `ToolController Tools`, `Hud Hud`. In `_Ready`:
    - If `GameSession.Map` is null, generate synchronously, so launching the scene with F6 works. Use seed 1, Small; or, if `COLONYSIM_SHOTS` is set, seed 12345 at `COLONYSIM_SHOTS_SIZE`, recording `generate_ms`.
    - Add the sun (`DirectionalLight3D`, rotation (-55°, -35°, 0), shadows on), a `WorldEnvironment` (background and ambient sky color `#9cc7e8`, ambient energy 0.6), and the `ChunkRenderer`.
    - If env `COLONYSIM_SHOTS` is set, add `VisualShots`.
    - For this task only, add a plain `Camera3D` looking at the map center from (0.3·SizeX, 220, 1.3·SizeZ). Task 12 replaces it.
  - `static class BlockTextures { Texture2DArray Build(BlockRegistry registry); }`. For each name in `registry.TextureNames` order, it loads `res://assets/textures/blocks/<name>.png` (16×16) if that file exists. Otherwise it generates a deterministic 16×16 placeholder. All images are RGBA8 with mipmaps.
  - `static class MeshConvert { ArrayMesh? ToArrayMesh(MeshBuffers b); }` returns null when empty. Arrays: Vertex, Normal, TexUV, TexUV2, Index.
  - `partial class ChunkRenderer : Node3D`:
    - `void Initialize(ColonyMap map)` queues every chunk that isn't uniform air.
    - `void SetSliceLevel(int level)` hides chunks whose minimum Y is above `level`.
    - `int TotalChunks, MeshedChunks, PendingChunks`
    - `event Action<IReadOnlyCollection<Int3>>? ChunksDirtied`, raised each frame with that frame's dirty set before it is scheduled.
    - Static materials `BlockMaterial`, `LiquidMaterial` (shared with `CapRenderer` in Task 13).

Placeholder texture palette (base color, ±8% per-pixel noise, seeded by name):

| Texture | Color | Pattern |
|---|---|---|
| grass_top | `#5fa83a` | noise |
| grass_side | `#8a5f3c` | dirt, with the top 3 rows grass green |
| dirt | `#8a5f3c` | noise |
| sand | `#d8c98c` | noise |
| stone | `#8c8c8c` | noise |
| deep_stone | `#5d5f66` | noise |
| bedrock | `#2b2b2e` | noise |
| coal_ore, copper_ore, iron_ore, gold_ore | stone base | 6 specks of 2×2 in `#2a2a2e` / `#c8703a` / `#c9a48a` / `#f2d24b` |
| water | `#3d7fd6` | noise |
| oak_log | `#6b4a2b` | vertical stripes |
| oak_log_top | `#9c7444` | rings |
| pine_log | `#4a3320` | vertical stripes |
| pine_log_top | `#7a5a36` | rings |
| oak_leaves | `#3d7d2a` | noise |
| pine_leaves | `#2d5e2a` | noise |
| cobblestone | `#7a7a7a` | blotches |
| planks | `#b48a52` | horizontal boards, darker line every 4 px |
| stone_bricks | `#9a9a9a` | mortar lines |

Shaders. Every shader declares `global uniform float slice_height;` and in `fragment()` runs `if (world_pos.y > slice_height + 0.001) discard;`, using a world position passed from `vertex()`.
- `block.gdshader`: lit, `sampler2DArray blocks : filter_nearest_mipmap`, albedo = `texture(blocks, vec3(UV, UV2.x)).rgb * mix(0.55, 1.0, UV2.y)`.
- `liquid.gdshader`: the same, plus alpha 0.7, `blend_mix`, `depth_draw_never`, `cull_disabled`.
- `block_common.gdshaderinc` holds the shared parts.

ChunkRenderer threading:
- Mesh on the thread pool, with at most `max(1, ProcessorCount - 2)` jobs running at once.
- Each chunk keeps a `requestedVersion`. A finished result is applied only if its version is still the latest.
- Apply results on the main thread within a 6 ms budget per frame.
- Each chunk gets one opaque and one liquid `MeshInstance3D`, positioned at the origin.
- Reads during writes need no locks, because any write re-dirties the chunk.

`VisualShots`:
- Activated by `COLONYSIM_SHOTS=<dir>`. It optionally reads `COLONYSIM_SHOTS_SIZE=Small|Large` (default Small, seed 12345).
- It pins the window to 1280×720 with `ContentScaleFactor = 1`.
- It waits until `PendingChunks == 0` and 5 more frames, then runs a list of named scenarios. Each scenario sets up the view and saves `<dir>/<name>.png`.
- It appends `name=value` lines to `<dir>/metrics.txt`, then quits.
- Task 11 adds the scenario `overview` and the metrics `generate_ms` and `initial_mesh_ms`. Later tasks add scenarios to the same list.
- The harness's map comes from `MapScene`'s fallback generation described above.

- [ ] **Step 1: Implement** everything above.
- [ ] **Step 2: Build.** Run `dotnet build ColonySim.sln`. Expected: 0 errors.
- [ ] **Step 3: Run the harness.** `godot --headless --path . --import`, then `COLONYSIM_SHOTS=tests/visual/shots godot --path . res://scenes/map.tscn`. Expected: it exits on its own, and `tests/visual/shots/overview.png` exists.
- [ ] **Step 4: Verify visually.** Read `overview.png`. Expected: blocky textured terrain, hills, a river with semi-transparent water, trees, map-edge cross-sections showing layers, and no holes or inside-out faces. Fix and repeat until it looks right.
- [ ] **Step 5: Commit** `feat(game): chunk rendering with block textures and screenshot harness`.

---

### Task 12: Overseer camera and input actions

**Files:**
- Create: `src/Game/View/CameraRig.cs`, `src/Game/Input/InputActions.cs`
- Modify: `src/Game/MapScene.cs` (replace the temporary camera; call `InputActions.Register()` once), `src/Game/Debug/VisualShots.cs` (add a scenario)

**Interfaces:**
- Consumes: `MapScene.Map` (sizes, `World.GetSurfaceHeight`).
- Produces:
  - `static class InputActions { void Register(); }` with constant action names. Existing actions are skipped.
    - `cam_forward` W/Up, `cam_back` S/Down, `cam_left` A/Left, `cam_right` D/Right, `cam_rot_left` Q, `cam_rot_right` E
    - `level_up` PageUp, `level_down` PageDown, `level_top` Home
    - `tool_camera` Escape, `tool_dig` 1, `tool_wall` 2, `tool_floor` 3, `tool_stairs` 4, `tool_cancel` 5, `material_cycle` Tab, `save` Ctrl+S
  - `partial class CameraRig : Node3D`:
    - `void Setup(ColonyMap map)`, which starts at the map center, focused at the surface.
    - `float FocusY { get; set; }`, smoothed toward the target at 10/s.
    - `void FocusOnLevel(int level, bool sliced)`. Sliced targets `level + 1`. Not sliced targets the surface height at the focus column + 1.
    - `Camera3D Camera`
    - `Vector3 FocusPoint`

Camera feel:
- **Distance:** 10–450, default 120.
- **Pitch:** 30°–85°, default 50°.
- **Pan speed:** distance × 1.0 per second, relative to yaw.
- **Q/E:** 90° per second.
- **Mouse wheel:** zoom ×1.1 per notch. Ignored while Shift is held, because that's level change.
- **Right-drag:** yaw/pitch at 0.3° per pixel. **Middle-drag:** pans.
- **Bounds:** focus x/z clamped to the map.

- [ ] **Step 1: Implement** both classes, and wire them into `MapScene`.
- [ ] **Step 2: Add scenario** `camera_rotated` (yaw 135°, pitch 35°, distance 80) to `VisualShots`.
- [ ] **Step 3: Build and run the harness.** Read `camera_rotated.png`. Expected: the same map from a lower angle on the opposite side, and the focus area is centered.
- [ ] **Step 4: Manual check.** Run `godot --path . res://scenes/map.tscn`, drive the camera, and confirm the controls behave as specified. Note any issues and fix them.
- [ ] **Step 5: Commit** `feat(game): RTS overseer camera and input actions`.

---

### Task 13: Slice view, caps and level indicator

**Files:**
- Create: `src/Game/View/SliceController.cs`, `src/Game/Rendering/CapRenderer.cs`, `src/Game/Ui/Hud.cs`, `assets/shaders/block_cap.gdshader`
- Modify: `src/Game/MapScene.cs`, `src/Game/Rendering/ChunkRenderer.cs` (only if a hook is missing), `src/Game/Debug/VisualShots.cs`

**Interfaces:**
- Consumes: `ChunkRenderer.SetSliceLevel`, `ChunkRenderer.ChunksDirtied`, `CapMesher.Build`, `MeshConvert`, `CameraRig.FocusOnLevel`, `CameraRig.FocusPoint`, `ToolTargeting.JumpLevel`, `VoxelRaycast.Cast`.
- Produces:
  - `partial class SliceController : Node`:
    - `void Setup(ColonyMap map, ChunkRenderer chunks, CapRenderer caps, CameraRig camera)`
    - `int ViewLevel`, `int TopLevel` (255), `bool IsSliced`
    - `void SetLevel(int level)` clamps to `[1, TopLevel]`.
    - `bool CapsEnabled`, `float LastLevelChangeMs`
    - `event Action<int>? LevelChanged`
    - Handles `level_up`/`level_down`/`level_top`, and Shift+wheel (use `deltaY`, or `deltaX` when the platform swaps the axis). Double-click is handled by `ToolController` in Task 14.
  - `partial class CapRenderer : Node3D`:
    - `void Setup(ColonyMap map)`
    - `void Rebuild(int cutY)`: all chunk columns, built in parallel, with meshes applied in the same call.
    - `void RebuildColumns(IEnumerable<(int cx, int cz)> columns, int cutY)`, `void Clear()`
  - `partial class Hud : CanvasLayer`: `void Setup(MapScene scene)`; in this task it shows only the level label.

On `SetLevel`:
- Set the global shader parameter `slice_height`, via `RenderingServer.GlobalShaderParameterSet`, to `level + 1`, or to `100000` at the top.
- Call `chunks.SetSliceLevel`.
- Rebuild the caps if sliced and caps are enabled; otherwise clear them.
- Call `camera.FocusOnLevel`.
- Record `LastLevelChangeMs` with a Stopwatch around all of this, and raise `LevelChanged`.

When `ChunksDirtied` fires while sliced, rebuild the cap columns of the dirty chunks whose Y range contains `cutY` or `cutY + 1`.

`block_cap.gdshader` is the block shader, darkened with `albedo = mix(albedo, vec3(0.078, 0.090, 0.114), 0.68)`. The liquid caps use the liquid material.

Level label text, where `rel = level - surfaceHeightAtCameraFocus`:
- At the top: `View level: top (nothing hidden)`
- Otherwise: `View level: Y {level} · {N below ground | ground level | N above ground}`

- [ ] **Step 1: Implement** the files above, and wire them in `MapScene`.
- [ ] **Step 2: Add scenarios.** Pick the river-free point with the most undiscovered cave cells under it in the top-down view, and focus the camera there. Add `slice_mid_caps` (level 100), `slice_mid_nocaps` (level 100, caps off), `slice_fog_off` (level 100, fog off with `MarkAllDirty`, then wait for meshing), and `slice_surface` (surface + 1 at the focus). Also add the metric `level_step_ms_avg`: the average `LastLevelChangeMs` over 20 single steps.
- [ ] **Step 3: Build and run the harness.** Read the four PNGs. Expected:
  - `slice_mid_caps`: dark rock with ore specks; no caves visible.
  - `slice_mid_nocaps`: the same cut with raw block colors.
  - `slice_fog_off`: caves visible as open floor plans.
  - `slice_surface`: trees and terrain cut at ground level.
  - `metrics.txt`: `level_step_ms_avg` well under 50 on Small.
- [ ] **Step 4: Manual check.** PgUp/PgDn, Shift+wheel and Home all work, and the camera follows the level.
- [ ] **Step 5: Commit** `feat(game): slice view with dark caps, fog and level indicator`.

---

### Task 14: Designation tools, overlay, HUD toolbar and instant work

**Files:**
- Create: `src/Game/Tools/{ToolController,DesignationOverlay}.cs`, `src/Game/Work/InstantWorkRunner.cs`, `assets/shaders/overlay.gdshader`
- Modify: `src/Game/Ui/Hud.cs`, `src/Game/MapScene.cs`, `src/Game/Debug/VisualShots.cs`

**Interfaces:**
- Consumes: `VoxelRaycast`, `ToolTargeting` (all members), `Designator`, `DragBox`, `InstantWorker`, `SliceController` (`ViewLevel`, `TopLevel`, `SetLevel`, `LevelChanged`), `CameraRig.Camera`, `JobQueue.Version`, `PlayerView.FogEnabled`, `VoxelWorld.MarkAllDirty`.
- Produces:
  - `partial class ToolController : Node3D`:
    - `void Setup(MapScene scene)`
    - `DesignationTool? ActiveTool` (null means Camera), `BuildMaterial Material`
    - `string Status`, `event Action? StatusChanged`
  - `partial class DesignationOverlay : Node3D`:
    - `void Setup(ColonyMap map)`
    - `void ShowPreview(IReadOnlyList<Job> jobs, DesignationTool tool)`, `void ClearPreview()`
    - One `MultiMeshInstance3D` per `JobKind`, plus one for the preview. Unit cubes of 1.04, rebuilt when `Jobs.Version` changes.
  - `partial class InstantWorkRunner : Node`:
    - `bool Enabled`, `int SpeedMultiplier` (1, 5 or 25)
    - Ticks at 10 Hz and calls `InstantWorker.Step(map, 4 * SpeedMultiplier)`.

Overlay colors (`overlay.gdshader`: unshaded, alpha 0.45, `depth_draw_never`, plus the same slice discard):

| Kind | Color |
|---|---|
| Dig | `#ffd23f` |
| BuildWall | `#4aa3ff` |
| BuildFloor | `#3fe0c5` |
| BuildStairs | `#b58cff` |
| Cancel preview | `#ff5a5a` |

Mouse behavior (left button only; right and middle stay with the camera):
- **Hover:** a white wireframe box shows the cell under the cursor at `ToolTargeting.Target`.
- **Press:** if the raycast misses, nothing happens. Otherwise start a drag: the `Start` cell, `levelAtStart`, and the plane height from `DragPlaneHeight`.
- **Move:** the `End` x/z comes from `IntersectPlane` at that plane height. Call `ShowPreview(Designator.Preview(...))`.
- **Level change mid-drag:** apply `ExtendBox` and refresh the preview.
- **Release:** `Designator.Apply`.
- **Esc:** cancels the drag, or switches to the Camera tool when no drag is active.
- **Double-click** with the Camera tool calls `SetLevel(JumpLevel(hit))`.

Hotkeys come from `InputActions`. Status texts (shown at bottom center) follow the prototype's wording, with this game's keys, plus a drag summary: `{tool}: {w} × {d}[ × {levels} levels] → {n} blocks (release to confirm, Esc to cancel)`.

HUD additions:
- **Toolbar** (bottom): Camera [Esc], Dig [1], Wall [2], Floor [3], Stairs [4], Cancel [5]. The active tool is highlighted.
- **Controls:** a Wood/Stone material picker [Tab]; an "Instant work" toggle with speed 1×/5×/25×; a "{n} jobs queued" label.
- **Toggles:** "Cut caps" and "Fog of war". Fog calls `MarkAllDirty` and then re-slices.

- [ ] **Step 1: Implement** the files above, and wire them in `MapScene`.
- [ ] **Step 2: Add the scenario** `designations`. At the surface focus point, apply a Wall box of 9×7 over 3 levels, a Floor at the level above it, a 6-long Stairs inside, and a Dig box of 8×8 going 12 levels down. Then shoot at the surface level. Add `built`: run `InstantWorker.Step` until the queue is empty, wait for meshing, slice at the dig bottom + 1, and shoot.
- [ ] **Step 3: Build and run the harness.** Read the PNGs. Expected:
  - `designations`: colored translucent overlays in the right shapes.
  - `built`: a walled room with a floor on top and a staircase, plus a pit whose bottom is visible in the slice.
- [ ] **Step 4: Manual check against the prototype's behavior:**
  - Dragging works at the top level and in a slice.
  - Holding PgDn mid-drag deepens a dig, and PgUp makes walls taller.
  - Cancel removes jobs.
  - Instant work digs and builds.
  - Digging into a hidden cave reveals it.
- [ ] **Step 5: Commit** `feat(game): drag designation tools, job overlay and instant work`.

---

### Task 15: New-game screen, save/load, debug overlay and final verification

**Files:**
- Create: `scenes/new_game.tscn` (root `Control` named `NewGame` with script `res://src/Game/Ui/NewGameScreen.cs`), `src/Game/Ui/{NewGameScreen,DebugOverlay}.cs`, `src/Game/Persistence/SaveGames.cs`
- Modify: `project.godot` (`run/main_scene="res://scenes/new_game.tscn"`), `src/Game/Ui/Hud.cs` (Save and Menu buttons, error and status messages), `src/Game/MapScene.cs`, `src/Game/Debug/VisualShots.cs`

**Interfaces:**
- Consumes: `MapGenerator.Generate` with progress, `MapSerializer`, `GameSession`, `ChunkRenderer` counters, `SliceController.LastLevelChangeMs`, `JobQueue.Count`.
- Produces:
  - `static class SaveGames`:
    - `const string Dir = "user://saves"`
    - `string Save(ColonyMap map)` writes `seed-{seed}-{yyyyMMdd-HHmmss}.csim` and returns the file name.
    - `IReadOnlyList<string> List()` (newest first)
    - `ColonyMap Load(string fileName)` uses `ProjectSettings.GlobalizePath` together with `File` streams.

New-game screen:
- **Fields:** seed (`LineEdit`, prefilled with a random positive int), size (`OptionButton`: Small / Medium / Large), and Generate.
- **Generate:** runs `Task.Run(MapGenerator.Generate)` and feeds a `ProgressBar` plus a stage label via `CallDeferred`. On success it sets `GameSession.Map` and changes scene to `res://scenes/map.tscn`. On an exception it shows the message.
- **Saves:** an `ItemList` of saves with a Load button. A load error (`InvalidDataException`) shows its message in red and the screen stays usable.
- **Harness hookup:** if `COLONYSIM_SHOTS` is set, it immediately changes to `map.tscn` with `GameSession.Map` left null, so `MapScene` generates the harness map.

HUD additions:
- **Save** (also Ctrl+S): calls `SaveGames.Save` and shows "Saved {file}" for 3 seconds.
- **Menu:** returns to `new_game.tscn`.

`DebugOverlay` (top-right label, F3 toggles it, on by default in debug builds) shows: FPS, meshed/total chunks, pending meshing, last level-change ms, and job count.

- [ ] **Step 1: Implement** the files above.
- [ ] **Step 2: Add the scenario** `save_roundtrip`. Save the `built` state to a temp file, load it, and compare with `TestWorlds`-style hashing: compute the hash inline in `VisualShots`, using the same FNV over `GetBlock`. Write `save_roundtrip_equal=true|false`. Also add the metric `overview_fps_avg` (average FPS over 120 frames of the overview).
- [ ] **Step 3: Run the Large verification.** Run `COLONYSIM_SHOTS=tests/visual/shots COLONYSIM_SHOTS_SIZE=Large godot --path .`, then read `metrics.txt`. Expected:
  - `generate_ms < 30000`
  - `overview_fps_avg >= 60`
  - `level_step_ms_avg < 50`
  - `save_roundtrip_equal=true`

  If any target is missed, profile and fix before moving on. Typical levers: greedy meshing in `ChunkMesher`, cap meshes merged per 2×2 columns, lower shadow distance.
- [ ] **Step 4: Run the full Core test suite,** including perf (`dotnet test tests/Core.Tests`). Expected: all pass.
- [ ] **Step 5: Walk the spec's "Done when" list manually** on a Large map started from the menu:
  1. Generate a map from a seed, and confirm the same seed gives the same map (`generate` twice, compare visually).
  2. Fly around at 60 fps.
  3. Slice through levels, checking caps and fog.
  4. Dig into a hidden cave and watch it get revealed.
  5. Build a two-story building with stairs and a multi-level mine using instant work.
  6. Save, return to the menu, load, and confirm everything is unchanged.

  Record the results in the commit message.
- [ ] **Step 6: Commit** `feat(game): new-game screen, save/load UI and debug overlay — step 1 complete`.
