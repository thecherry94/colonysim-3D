# Step 2a: Colonists Walk and Work — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put colonists into the step 1 world. They walk through 3D terrain, caves, mines and buildings with stairs, ramps, ladders, slopes, hops and climbs. They pick dig and build jobs on their own by work priority and skill, never strand themselves, and grow their skills. The map gains a flat start site, natural slopes and caves that are usable near the surface. All of it saves and loads, and the architecture holds at 100+ colonists.

**Architecture:** Everything simulated stays in `src/Core` (no Godot):
- **Blocks:** variant block ids (shape × material × facing) carry block state, so voxels stay one `ushort`.
- **`Navigation`:** move rules plus per-chunk "sections" (connected pieces of standable cells), with incremental updates and exact global connectivity, so unreachable work is rejected without searching.
- **`PathFinder`/`PathService`:** cell A*, made hierarchical through the section graph and time-sliced on the main thread.
- **`Simulation`:** advances fixed 20 Hz ticks. It runs colonist movement and work, job finding through the job queue's spatial index, claims, and strand checks done by previewing connectivity after an edit.

Godot (`src/Game`) owns:
- a `SimRunner` that drives ticks at pause/1×/2×/3×;
- an instanced colonist renderer;
- the colonist UI (selection, bar, info panel, follow, bookmarks, work priorities);
- the new designation tools;
- the screenshot/benchmark harness.

**Tech Stack:** Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`), C# net8.0 with `RollForward=LatestMajor`, xUnit 2.9.2 (tests on net10.0), FastNoiseLite (vendored).

**Spec:** `docs/superpowers/specs/2026-10-19-step2-working-colony-design.md`, part **2a** plus "Save and load" and "Architecture constraints". It sits under `2026-10-18-colonysim-vision-design.md` and builds on `2026-10-18-step1-voxel-world-design.md`. The behavior reference for slopes and the start site is `docs/superpowers/specs/prototypes/house-tool-slopes.html`. Open it in a browser and untick "Basic house"; the shelter tier shows the map.

## Global Constraints

- **Toolchain:** Godot **4.7.2 .NET** (`godot` command). .NET SDK 10.0.104. Core and Game target **net8.0** with `RollForward=LatestMajor`. Tests target **net10.0**.
- **Core isolation:** `src/Core` never references Godot and uses `System.Numerics` for vectors. Everything is in namespace `ColonySim.Core`, with Game in `ColonySim.Game`. New Core folders keep the single namespace.
- **World:**
  - 1 block = 1 m; height 256; chunks 32³. Map sizes are Small 256, Medium 384 and Large 512.
  - `GetSurfaceHeight(x,z)` is the y of the **top generated block**, so the standing (feet) cell is `y + 1`.
  - Facing: North = −Z, East = +X, South = +Z, West = −X. A stair's `Facing` is the direction you walk to go up.
- **Colonists:** a colonist is **1 wide and 2 tall**. Its "cell" is the feet cell C, and it occupies C and C+up.
- **Simulation:**
  - **20 ticks per second** at 1×; speeds are pause, 1×, 2× and 3×.
  - World mutation, `Navigation.Update`, path processing and all simulation run on the **main thread**.
  - Chunk meshing stays on worker threads and only reads.
- **Fog:** navigation reads the **known world**: `PlayerView.GetKnownBlock`, which always fogs undiscovered space. Undiscovered space is therefore solid, whatever the debug fog toggle says.
- **Performance targets** (dev machine: RTX 3080, i5-10600K, 32 GB; Large map):
  - 20 busy colonists at a steady **60 fps**;
  - **no hitches** when many colonists re-path at once;
  - long paths found within a few frames;
  - bulk-spawn benchmark numbers recorded at 20/50/100 colonists.
  - Step 1 targets still hold: generation < 30 s, a level step < 50 ms, and an edit visible within 1–2 frames.
- **Scaling:**
  - nothing may grow per tick with colonists × jobs or colonists × colonists;
  - job lookups use the spatial index;
  - colonist rendering is instanced.
- **Tests:** `dotnet test tests/Core.Tests --filter "Category!=Perf"` runs normal tests; Perf tests carry `[Trait("Category", "Perf")]`.
- **Build:** `dotnet build ColonySim.sln` must stay at **0 warnings**.
- **Visual verification:** `COLONYSIM_SHOTS=tests/visual/shots godot --path .` (needs a real window), optionally with `COLONYSIM_SHOTS_SIZE=Large`. It writes PNGs and `metrics.txt`. Read the PNGs.
- **TDD:** every Core step writes the failing test first and runs it to see it fail (RED) before implementing.

## Plan Rulings (decisions this plan makes on top of the spec)

1. **Block state as variant ids.** Ramps, corners, ladders and carved stairs are separate registry entries per material × facing, as stairs already are. Voxels stay `ushort`, and saves keep the name palette. The spec leaves the storage to the builder. *If wrong:* switching to a per-voxel state byte later costs one save-format bump.
2. **"Dig stairs" is its own tool** (hotkey X), instead of the Stairs tool deciding by the start cell. Unsliced, the build tools target the open cell above the clicked face, so a start-cell rule could never carve from the default view. *If wrong:* merging the two later is a small UI change.
3. **Starter crates are deferred to 2b,** since they hold items. 2a gets the flat site, the colonists spawn there, and the camera opens there.
4. **Colonists may overlap,** as in RimWorld. Above-cut hiding is per colonist: a colonist whose feet cell is above the view level is hidden whole, otherwise shown whole. Cutting colonists at the waist would hide everyone standing on the viewed floor.
5. **Hotkeys move.** The speed controls take Space and 1/2/3, so the tools move to letters:
   - Z Dig, X Dig stairs, C Wall, V Floor, B Stairs, N Ramp, M Ladder, Delete Cancel, Esc Camera;
   - P opens work priorities, F follows the selected colonist.
6. **Step 1 saves are rejected** with a readable message ("made by an older version"). The spec allows this.
7. **Movement costs and durations** are the table in Task 6. Skill speed is `0.5 + 0.075 × level`. Work time is `hardness × 40` ticks at speed 1. The experience curve is `400 × (level + 1)` per level. These are placeholders to tune.
8. **"Home"** is the start site's standing cell. The colony's home component is the connectivity component of the standable cell nearest to it. Strand checks and the "stuck" flag measure against it.

## Review Focus

1. **Designating or cancelling over a job a colonist is working on:** re-designating the same job keeps its progress and claim; cancelling it sends the colonist back to idle. Nothing throws. Tests: Task 3 `Add_SameKindAndBlock_KeepsExistingJobAndProgress`; Task 11 `CancelClaimedJob_ColonistGoesIdle`.
2. **The world changing under a walking colonist** (ground dug out, a wall built on the path): it falls to the next floor or re-paths. It never floats or walks through solid. Tests: Task 10 `GroundRemovedUnderColonist_Falls`, `WallBuiltOnPath_ColonistRepaths`.
3. **Huge designations (200k cells) with many colonists:** job search stays bounded and nothing stalls. Test: Task 11 `JobSearch_200kJobs_StaysFast` (Perf).
4. **Saving mid-work** (claimed jobs, a colonist mid-move, a half-dug block): loading drops claims, keeps progress, and puts every colonist on a standable cell. Test: Task 14 `RoundTrip_MidWork_DropsClaimsKeepsProgress`.
5. **Hidden caves:** colonists never path through undiscovered space; once a dig reveals a cave, they can walk in. Tests: Task 6 `UndiscoveredAir_IsNotPassable`; Task 9 `Path_IntoCave_OnlyAfterReveal`.

## File Map

```
src/Core/
  Blocks/      BlockShape.cs Facing.cs BlockDef.cs BlockRegistry.cs Blocks.cs        (modify: T1)
  World/       VoxelWorld.cs (Changed event, T8) PlayerView.cs (GetKnownBlock, T6) Discovery.cs (reveal callback, T3)
  Jobs/        Job.cs JobKind.cs JobSpec.cs JobQueue.cs DesignationTool.cs Designator.cs InstantWorker.cs
               WorkCompletion.cs                                                       (T3)
  Generation/  StartSite.cs StartSiteFinder.cs SlopePlacer.cs (T5)  CaveCarver.cs OrePlacer.cs HeightField.cs (T7)
               SurfaceDecorator.cs MapGenerator.cs (T5, T10)
  Navigation/  MoveKind.cs Move.cs NavRules.cs (T6)  Navigation.cs NavChunk.cs ConnectivityPreview.cs (T8)
               PathFinder.cs PathService.cs PathRequest.cs (T9)
  Colonists/   SkillType.cs Skill.cs Skills.cs WorkType.cs WorkTypes.cs Colonist.cs Colony.cs
               ColonistFactory.cs ColonistNames.cs ViewBookmark.cs ColonistPicking.cs  (T10, T13)
  Sim/         SimClock.cs Simulation.cs ColonistMover.cs (T10)  JobFinder.cs WorkRules.cs StrandCheck.cs
               ColonistWorker.cs (T11)
  Meshing/     ChunkMesher.cs MeshBuffers.cs (T2)
  Tools/       VoxelRaycast.cs ToolTargeting.cs (T1, T3)
  Persistence/ MapSerializer.cs BlockAliases.cs (T14)
  ColonyMap.cs (T5, T8, T10)
src/Game/
  Sim/SimRunner.cs (T12)  Colonists/ColonistRenderer.cs (T12)
  Ui/ColonistBar.cs ColonistPanel.cs WorkPrioritiesWindow.cs BookmarksPanel.cs (T13)  Hud.cs DebugOverlay.cs
  Tools/ToolController.cs DesignationOverlay.cs (T4, T13)  Input/InputActions.cs (T4)
  Rendering/ChunkRenderer.cs (T4)  View/CameraRig.cs (T5, T13)  MapScene.cs  Debug/VisualShots.Colony.cs (T12–T15)
assets/shaders/overlay.gdshader (T4: per-instance color)
tests/Core.Tests/  BlocksTests MeshingTests JobQueueTests DesignatorTests WorkCompletionTests StartSiteTests
                   SlopePlacerTests NavRulesTests CaveTests NavigationTests PathFinderTests SimulationTests
                   SkillsTests WorkTests ColonistPickingTests MapSerializerTests (+ TestWorlds helpers)
```

**Suggested models**, for the controller (per the developer's rule: Opus for hard tasks, Sonnet otherwise): T2, T6, T8, T9, T10 and T11 → **Opus**. All others → **Sonnet**.

---

### Task 1: Block variants, block properties and picking

**Files:**
- Modify: `src/Core/Blocks/BlockShape.cs`, `Facing.cs`, `BlockDef.cs`, `BlockRegistry.cs`, `Blocks.cs`, `src/Core/Tools/VoxelRaycast.cs`
- Test: `tests/Core.Tests/BlocksTests.cs` (extend), `tests/Core.Tests/ToolTargetingTests.cs` (one raycast test)

**Interfaces:**
- **Produces:**
  - `enum BlockShape { None, Cube, Liquid, Stairs, Ramp, CornerOuter, CornerInner, Ladder }`. Append-only, so step 1's values keep their numbers.
  - `FacingExtensions.Clockwise(this Facing f)` (N→E→S→W→N) and `Opposite(this Facing f)`.
  - `FacingExtensions.FromOffset(int dx, int dz)` takes a unit offset; anything else throws `ArgumentException`.
  - `BlockDef` gains three appended positional parameters, `bool IsNatural, float Hardness, ushort BaseBlock`, plus these members:
    - `bool IsSlope => Shape is Ramp or CornerOuter or CornerInner`
    - `bool IsLadder => Shape == Ladder`
    - `bool AscendsToward(Facing dir)`: Stairs and Ramp return `Facing == dir`; corners return `Facing == dir || Facing.Clockwise() == dir`; everything else returns false.
  - `BaseBlock` is the material block a variant is made of. For a plain block it's the block's own id.
  - `Blocks.NaturalSlope(ushort baseBlock, BlockShape shape, Facing f)`. `baseBlock` must be one of Grass, Dirt, Sand or Stone, and `shape` one of the three slope shapes; anything else throws `ArgumentException`.
  - `Blocks.SlopeBaseFor(ushort topBlock)`: Grass, Dirt and Sand map to themselves; everything else maps to Stone.
  - `Blocks.BuiltRamp(BuildMaterial m, Facing f)`.
  - `Blocks.Ladder(Facing f)`, where `f` is the side of the cell the ladder is mounted against.
  - `Blocks.CarvedStairs(ushort dugBlock, Facing f)`: Grass, Dirt and Sand give carved dirt; DeepStone gives carved deep stone; anything else gives carved stone.

**Corner semantics** (document them on `BlockShape`):
- **`CornerOuter`** with `Facing f`: the single raised corner is the one between the `f` side and the `f.Clockwise()` side.
- **`CornerInner`** with `Facing f`: the `f` and `f.Clockwise()` sides are fully raised, and the lowered corner is the opposite one.
- **Climbing:** both corners ascend toward `f` and toward `f.Clockwise()`.

**New registry entries**, appended after the step 1 blocks in this order (99 blocks in total):

| Names | Shape | Solid | Diggable | Natural | Textures (top / side / bottom) |
|---|---|---|---|---|---|
| `{grass,dirt,sand,stone}_{ramp,corner_outer,corner_inner}_{north,east,south,west}` (48) | Ramp / CornerOuter / CornerInner | yes | yes | **yes** | as the base block |
| `{wood,stone}_ramp_{f}` (8) | Ramp | yes | yes | no | planks / stone_bricks |
| `ladder_{f}` (4) | Ladder | **no** | yes | no | planks |
| `carved_{dirt,stone,deep_stone}_stairs_{f}` (12) | Stairs | yes | yes | no | dirt / stone / deep_stone |

**`IsNatural` = true** for: grass, dirt, sand, stone, deep_stone, all four ores, cobblestone, bedrock, and the 48 natural slopes. Everything else is false (logs, leaves, planks, bricks, built and carved stairs, ramps, ladders, water, air).

**`Hardness`** (step 1 blocks):

| Hardness | Blocks |
|---|---|
| 0 | air, water, bedrock |
| 0.5 | leaves |
| 1.0 | grass, dirt, sand |
| 1.5 | wood_planks, wood stairs |
| 2.0 | logs |
| 2.5 | stone, cobblestone |
| 3.0 | ores, stone_bricks, stone stairs |
| 3.5 | deep_stone |

New variants take their `BaseBlock`'s hardness, except ladders, which are 1.0.

- [ ] **Step 1: Write the failing tests** in `BlocksTests.cs`:

```csharp
[Fact] public void Registry_Has99Blocks() => Assert.Equal(99, Blocks.Registry.All.Count);
[Fact] public void Facing_ClockwiseAndOpposite()
{ Assert.Equal(Facing.East, Facing.North.Clockwise()); Assert.Equal(Facing.North, Facing.West.Clockwise()); Assert.Equal(Facing.South, Facing.North.Opposite()); Assert.Equal(Facing.West, FacingExtensions.FromOffset(-1, 0)); }
[Fact] public void NaturalSlopes_OnePerBaseShapeFacing()
{
    var ids = new HashSet<ushort>();
    foreach (var b in new[] { Blocks.Grass, Blocks.Dirt, Blocks.Sand, Blocks.Stone })
        foreach (var s in new[] { BlockShape.Ramp, BlockShape.CornerOuter, BlockShape.CornerInner })
            foreach (var f in Enum.GetValues<Facing>())
            {
                var d = Blocks.Registry.Get(Blocks.NaturalSlope(b, s, f));
                Assert.Equal(s, d.Shape); Assert.Equal(f, d.Facing); Assert.True(d.IsSolid); Assert.False(d.Occludes);
                Assert.True(d.IsNatural); Assert.True(d.IsSlope); Assert.Equal(b, d.BaseBlock);
                Assert.Equal(Blocks.Registry.Get(b).TexTop, d.TexTop); ids.Add(d.Id);
            }
    Assert.Equal(48, ids.Count);
}
[Fact] public void SlopeBaseFor_MapsTopBlocks() { Assert.Equal(Blocks.Grass, Blocks.SlopeBaseFor(Blocks.Grass)); Assert.Equal(Blocks.Stone, Blocks.SlopeBaseFor(Blocks.CoalOre)); Assert.Equal(Blocks.Stone, Blocks.SlopeBaseFor(Blocks.DeepStone)); }
[Fact] public void AscendsToward_StairsRampsAndCorners()
{
    var r = Blocks.Registry;
    Assert.True(r.Get(Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East)).AscendsToward(Facing.East));
    Assert.False(r.Get(Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East)).AscendsToward(Facing.North));
    var corner = r.Get(Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerInner, Facing.North));
    Assert.True(corner.AscendsToward(Facing.North)); Assert.True(corner.AscendsToward(Facing.East)); Assert.False(corner.AscendsToward(Facing.South));
    Assert.False(r.Get(Blocks.Stone).AscendsToward(Facing.North));
}
[Fact] public void Ladder_IsNotSolid_ButDiggable_NotNatural()
{ var d = Blocks.Registry.Get(Blocks.Ladder(Facing.West)); Assert.Equal(BlockShape.Ladder, d.Shape); Assert.False(d.IsSolid); Assert.True(d.IsDiggable); Assert.False(d.IsNatural); Assert.Equal(Facing.West, d.Facing); }
[Fact] public void CarvedStairs_MapDugMaterial()
{
    Assert.Equal("carved_dirt_stairs_north", Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.Grass, Facing.North)).Name);
    Assert.Equal("carved_stone_stairs_east", Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.IronOre, Facing.East)).Name);
    Assert.Equal("carved_deep_stone_stairs_west", Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.DeepStone, Facing.West)).Name);
    Assert.Equal(BlockShape.Stairs, Blocks.Registry.Get(Blocks.CarvedStairs(Blocks.Dirt, Facing.South)).Shape);
}
[Theory]
[InlineData("grass", 1.0f, true)] [InlineData("stone", 2.5f, true)] [InlineData("deep_stone", 3.5f, true)] [InlineData("iron_ore", 3.0f, true)]
[InlineData("oak_log", 2.0f, false)] [InlineData("oak_leaves", 0.5f, false)] [InlineData("wood_planks", 1.5f, false)] [InlineData("cobblestone", 2.5f, true)]
[InlineData("stone_ramp_north", 3.0f, false)] [InlineData("ladder_east", 1.0f, false)] [InlineData("dirt_corner_inner_west", 1.0f, true)]
public void HardnessAndNatural(string name, float hardness, bool natural)
{ Assert.True(Blocks.Registry.TryGetByName(name, out var id)); var d = Blocks.Registry.Get(id); Assert.Equal(hardness, d.Hardness); Assert.Equal(natural, d.IsNatural); }
```

In `ToolTargetingTests.cs`, add `Raycast_HitsRampsAndLadders`: a ramp and a ladder each on a flat world, with a downward ray over each, both hit at the expected cell.

- [ ] **Step 2: Run the tests and see them fail.** Run `dotnet test tests/Core.Tests --filter "FullyQualifiedName~BlocksTests|FullyQualifiedName~ToolTargetingTests"`. Expected: compile errors, then failures.
- [ ] **Step 3: Implement.**
  - `BlockRegistry.Add` takes `natural`, `hardness` and `baseBlock` (default: its own id).
  - `Blocks` registers the table above with lookup arrays like `StairIds`.
  - `VoxelRaycast.IsPickable` accepts any shape except None and Liquid.
- [ ] **Step 4: Run all non-Perf tests; they pass.** Run `dotnet build ColonySim.sln` and confirm 0 warnings. The Game compiles unchanged, because `BlockTextures` builds a layer per texture name and adds none.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): block variants for slopes, ramps, ladders and carved stairs"`

---

### Task 2: Meshing the new shapes

**Files:**
- Modify: `src/Core/Meshing/ChunkMesher.cs`, `src/Core/Meshing/MeshBuffers.cs`
- Test: `tests/Core.Tests/MeshingTests.cs`

**Interfaces:**
- **Consumes:** T1 shapes and `Facing` helpers.
- **Produces:** `MeshBuffers.AddTriangle(Vector3 normal, float layer, Vector3 p0, Vector2 uv0, float ao0, Vector3 p1, Vector2 uv1, float ao1, Vector3 p2, Vector2 uv2, float ao2)`. The triangle winds clockwise seen from outside, like `AddQuad`.

**Geometry, inside the unit cell.** "Culled" means the face is skipped when the neighbor in front occludes it, which is a Cube.

**Ramp, facing `f`:**
- **Sloped top:** a quad from the low edge (the `f.Opposite()` side, y = 0) to the high edge (the `f` side, y = 1). Its normal is `normalize(up + f.Opposite())`, never culled, with top texture.
- **High side:** a full quad with normal `f`, culled.
- **Two side triangles:** normals ±(the axis across `f`), each culled.
- **Bottom:** full, culled.
- The low side has no face.

**CornerOuter, facing `f`:**
- **Corner heights:** 1 at the raised corner R, 0 elsewhere.
- **Top:** one sloped triangle (R and its two neighboring corners), never culled. The flat zero-height half is not emitted.
- **Sides:** a vertical triangle on the `f` side and one on the `f.Clockwise()` side, each culled.
- **Bottom:** culled.

**CornerInner, facing `f`:**
- **Corner heights:** 0 at the lowered corner L, 1 elsewhere.
- **Top:** a sloped triangle (L and its two neighbors), never culled, plus a flat triangle at y = 1 (normal up), culled by the block above.
- **Sides:** full quads on the `f` and `f.Clockwise()` sides, and triangles on the other two sides, all culled.
- **Bottom:** culled.

**Ladder, facing `f`.** Six boxes against the `f` side of the cell, all faces drawn, ambient occlusion 1, side texture. Coordinates are for `f` = North; rotate them for the other facings:
- two rails: x ∈ [0.15, 0.25] and [0.75, 0.85], y ∈ [0, 1], z ∈ [0.02, 0.12];
- four rungs: x ∈ [0.25, 0.75], z ∈ [0.04, 0.10], y centered at 0.125, 0.375, 0.625 and 0.875, height 0.06.

**Ambient occlusion:**
- vertical faces interpolate the full face's corner AO bilinearly, as `EmitBoxFace` does;
- sloped and flat tops use the top face's corner AO at their xz corners.

Slopes, ramps and ladders never occlude neighbors (`Occludes` is Cube-only), so the ground's top face under a slope is still emitted.

- [ ] **Step 1: Write the failing tests.** Set up a 32-wide world, stone at y ≤ 5, the shape at (5,6,5), `Discovery.InitialFlood`, and mesh chunk (0,0,0). Add a helper `Tris(MeshBuffers b, Vector3 n)` that counts triangles whose first vertex normal ≈ `n`. Normals here are the face normals of the shape.

```csharp
[Fact] public void RampNorth_SlopedTopHighSideTwoTrianglesNoBottom()
{
    var b = MeshShape(Blocks.BuiltRamp(BuildMaterial.Stone, Facing.North));
    Assert.Equal(2, Tris(b, Vector3.Normalize(new Vector3(0, 1, 1))));   // up + toward the low (south) side
    Assert.Equal(2, Tris(b, -Vector3.UnitZ));                             // high side
    Assert.Equal(1, Tris(b, Vector3.UnitX)); Assert.Equal(1, Tris(b, -Vector3.UnitX));
    Assert.Equal(0, Tris(b, -Vector3.UnitY));                             // bottom culled by stone
}
[Fact] public void CornerOuterNorth_OneSlopedTriangleAndTwoSideTriangles() { var b = MeshShape(Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerOuter, Facing.North)); Assert.Equal(1, Tris(b, -Vector3.UnitZ)); Assert.Equal(1, Tris(b, Vector3.UnitX)); Assert.Equal(3, ShapeTris(b)); }
[Fact] public void CornerInnerNorth_EightTriangles() { var b = MeshShape(Blocks.NaturalSlope(Blocks.Grass, BlockShape.CornerInner, Facing.North)); Assert.Equal(2, Tris(b, -Vector3.UnitZ)); Assert.Equal(2, Tris(b, Vector3.UnitX)); Assert.Equal(8, ShapeTris(b)); }
[Fact] public void Ladder_SixBoxesAgainstItsWall()
{ var b = MeshShape(Blocks.Ladder(Facing.East)); Assert.Equal(72, ShapeTris(b)); Assert.All(ShapeVertices(b), v => Assert.InRange(v.X, 5.85f, 6f)); }
[Fact] public void Slope_GroundTopFaceUnderneathStillEmitted() { var b = MeshShape(Blocks.BuiltRamp(BuildMaterial.Wood, Facing.East)); Assert.Contains(TopFaceAt(b, 5, 6, 5), t => true); }
[Fact] public void NewShapes_WindClockwise_AndNormalsMatchGeometry() { /* for every triangle of every new shape: Dot(Cross(b-a, c-a), n) < 0 and Cross is parallel to n */ }
```

`ShapeTris` and `ShapeVertices` select the triangles and vertices that lie inside the shape's cell. The ladder's rail x-range for an East-facing ladder is [5.88, 5.98].

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/Core.Tests --filter FullyQualifiedName~MeshingTests`.
- [ ] **Step 3: Implement** `EmitRamp`, `EmitCorner` and `EmitLadder` in `ChunkMesher`, plus `AddTriangle`.
- [ ] **Step 4: Run all non-Perf tests; they pass.** The build has 0 warnings.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): mesh ramps, slope corners and ladders"`

---

### Task 3: Jobs with identity, claims and progress; new designation tools

**Files:**
- Create: `src/Core/Jobs/JobSpec.cs`, `src/Core/Jobs/WorkCompletion.cs`
- Modify: `Job.cs`, `JobKind.cs`, `JobQueue.cs`, `DesignationTool.cs`, `Designator.cs`, `InstantWorker.cs`, `src/Core/Tools/ToolTargeting.cs`, `src/Core/World/Discovery.cs`, `src/Core/Persistence/MapSerializer.cs` (make v1 compile against the new `Job`; Task 14 rewrites it)
- Modify (compile fixes only): `src/Game/Tools/DesignationOverlay.cs` and `ToolController.cs` (preview takes `JobSpec`), `src/Game/Debug/*.cs`
- Test: `JobQueueTests.cs`, `DesignatorTests.cs`, `InstantWorkerTests.cs`, `WorkCompletionTests.cs` (new), `ToolTargetingTests.cs`

**Interfaces:**
- **Produces:**
  - `enum JobKind { Dig, BuildWall, BuildFloor, BuildStairs, BuildRamp, BuildLadder, CarveStairs }`.
  - `enum DesignationTool { Dig, Wall, Floor, Stairs, Cancel, Ramp, Ladder, DigStairs }`.
  - `readonly record struct JobSpec(Int3 Position, JobKind Kind, ushort Block)`. This is the old `Job` record, renamed. `Block` is Air for digs; for builds and carves it's the block to place.
  - `enum JobStatus { Open, Unreachable, NeedsWayBack }`.
  - `sealed class Job`:
    - `long Id`, `Int3 Position`, `JobKind Kind`, `ushort Block`;
    - `int ClaimedBy` (colonist id, 0 = free), `float Progress` (work ticks done);
    - `JobStatus Status`, `int StatusVersion` (the navigation version the status was found at);
    - `JobSpec Spec`.
  - **`JobQueue`, existing members (kept):** `Count`, `All`, `Version`, `Remove(Int3)`, `Contains(Int3)` and `TryGet(Int3, out Job)` keep their meaning.
  - **`JobQueue`, adding jobs:**
    - `Job Add(JobSpec spec)`. If a job at that position has the same `Kind` and `Block`, the **existing job is kept and returned unchanged**, progress and claim included. Otherwise a new job with a new id replaces it. The replaced job is released, and that bumps `Version`.
    - `internal void Restore(Job job)` and `long NextId { get; internal set; }`, for loading.
  - **`JobQueue`, claims and progress:**
    - `bool TryGetById(long id, out Job job)`;
    - `void Claim(Job j, int colonistId)`, `void Release(Job j)`, `void AddProgress(Job j, float ticks)`;
    - `void SetStatus(Job j, JobStatus s, int navVersion)`.

    Claim, Release and SetStatus mark the job's chunk changed. AddProgress doesn't, because the overlay doesn't show progress.
  - **`JobQueue`, spatial queries:**
    - `IEnumerable<Job> InChunk(Int3 chunk)`;
    - `IEnumerable<Job> ByRing(Int3 cell, int maxRing)`: jobs in chunk rings of increasing Chebyshev distance (in chunks) around the cell's chunk. Order within a ring is unspecified.
    - `void ConsumeChangedChunks(ICollection<Int3> into)`: chunks whose jobs were added, removed, claimed, released or changed status since the last call.
  - **`Designator`:** `IReadOnlyList<JobSpec> Preview(ColonyMap, DesignationTool, BuildMaterial, DragBox)` and `int Apply(...)`. Apply uses `JobQueue.Add`; Cancel removes.
  - **`ToolTargeting`:** `Target` and `DragPlaneHeight` treat `DigStairs` like `Dig` (they act on the solid block).
  - **`Discovery.RevealFrom(VoxelWorld w, int x, int y, int z, Action<Int3>? onDiscovered = null)`:** calls back for every newly discovered cell.
  - **`static class WorkCompletion`:**
    - `bool Complete(ColonyMap map, Job job)`, shared by the instant worker and colonists. It applies a job's effect and removes it, and returns false if the job was invalid and just removed.
    - **Dig:** sets Air and reveals. Every newly discovered cell that holds a Dig or CarveStairs job has that job removed, since its target turned out to be open.
    - **CarveStairs:** if the cell is solid, places `job.Block`; otherwise removes the job.
    - **Builds:** place `job.Block` if the real cell is Air or Water; otherwise remove the job.

**Designator rules for the new tools:**
- **Ramp:** like Stairs (`CollectStairs` logic), placing `BuiltRamp(material, facing)` with `JobKind.BuildRamp`.
- **Ladder:** every in-box cell that the known world shows as empty gets a `BuildLadder` job. The ladder faces the first horizontally adjacent known-solid cell, checked in the order North, East, South, West; with none, it faces South.
- **DigStairs:**
  - `Start` is the clicked solid block. The drag's dominant horizontal axis gives the direction `D`. Length is the extent along `D` plus 1, and width is the extent across it plus 1. The box's Y extent is ignored.
  - Step `i` of each parallel run is `Start + D·i − up·i + across·k`, going down while y ≥ 1.
  - If the known block there is solid and diggable, the step becomes a `CarveStairs` job with `Block = CarvedStairs(thatBlock, D.Opposite())`. Open cells are skipped.
  - Each step's two cells above (+1 and +2) that are solid and diggable get `Dig` jobs.

- [ ] **Step 1: Write the failing tests.** Highlights:

```csharp
[Fact] public void Add_SameKindAndBlock_KeepsExistingJobAndProgress()
{ var q = new JobQueue(); var a = q.Add(new JobSpec(new(1,2,3), JobKind.Dig, Blocks.Air)); q.Claim(a, 7); q.AddProgress(a, 12);
  var b = q.Add(new JobSpec(new(1,2,3), JobKind.Dig, Blocks.Air)); Assert.Same(a, b); Assert.Equal(7, b.ClaimedBy); Assert.Equal(12, b.Progress); }
[Fact] public void Add_DifferentKind_ReplacesWithNewIdAndReleases()
{ var q = new JobQueue(); var a = q.Add(new JobSpec(new(1,2,3), JobKind.Dig, Blocks.Air)); q.Claim(a, 7);
  var b = q.Add(new JobSpec(new(1,2,3), JobKind.BuildWall, Blocks.WoodPlanks)); Assert.NotEqual(a.Id, b.Id); Assert.Equal(0, b.ClaimedBy); Assert.False(q.TryGetById(a.Id, out _)); }
[Fact] public void ByRing_ReturnsNearerChunksFirst() { /* jobs in chunks at ring 0, 2 and 1 → enumeration yields ring 0, then 1, then 2 */ }
[Fact] public void ConsumeChangedChunks_ReportsTouchedChunksOnce() { /* add 3 jobs in one chunk, claim one → one chunk reported; second call empty */ }
[Fact] public void DigStairs_DescendsAlongDrag_WithHeadroom()
{
    var map = TestWorlds.FlatMap();                                         // stone to y=150
    var jobs = Designator.Preview(map, DesignationTool.DigStairs, BuildMaterial.Wood, new DragBox(new(10,150,10), new(14,150,10)));
    var carves = jobs.Where(j => j.Kind == JobKind.CarveStairs).Select(j => j.Position).ToList();
    Assert.Equal(Enumerable.Range(0, 5).Select(i => new Int3(10 + i, 150 - i, 10)), carves);
    Assert.All(jobs.Where(j => j.Kind == JobKind.CarveStairs), j => Assert.Equal(Blocks.CarvedStairs(Blocks.Stone, Facing.West), j.Block));
    Assert.Contains(new JobSpec(new(12,149,10), JobKind.Dig, Blocks.Air), jobs);   // headroom of step 2
    Assert.Contains(new JobSpec(new(12,150,10), JobKind.Dig, Blocks.Air), jobs);
    Assert.DoesNotContain(jobs, j => j.Position == new Int3(10,151,10));            // already open
}
[Fact] public void Ladder_FacesAdjacentWall() { /* 1×1 shaft dug into stone with its east wall solid only → ladder jobs face East */ }
[Fact] public void Ramp_RisesOneLevelPerStep_FacingDragDirection() { /* like the stairs test, with BuildRamp + BuiltRamp ids */ }
[Fact] public void Complete_DigIntoHiddenCave_RemovesDigJobsInsideRevealedCave()
{ /* SolidBelow + an undiscovered air pocket; dig jobs on the pocket's air cell and on the wall cell that opens it;
     completing the wall job reveals the pocket and removes the pocket job; Count == 0 */ }
[Fact] public void Complete_CarveOnOpenCell_JustRemovesJob() { }
```

Port the existing Designator and InstantWorker tests to `JobSpec`. `InstantWorker` now finishes jobs via `WorkCompletion.Complete`, digs top-down then builds bottom-up as before (CarveStairs counts as a dig), and also removes claimed jobs.

- [ ] **Step 2: Run them and see them fail** (compile errors first).
- [ ] **Step 3: Implement.**
  - **Spatial index:** a `Dictionary<Int3, List<Job>>` keyed by chunk.
  - **Ring enumeration:** walk the chunks of each Chebyshev ring around the start chunk, clamped to the map.
  - **Game side:** `DesignationOverlay.ShowPreview(IReadOnlyList<JobSpec>, DesignationTool)` and its colors for the new kinds:
    - BuildRamp `#ff9f43`;
    - BuildLadder `#c8d6e5`;
    - CarveStairs `#f368e0`;
    - DigStairs tool preview: the CarveStairs color.
- [ ] **Step 4: Verify.** All non-Perf tests pass, the build has 0 warnings, and the existing harness still runs: `COLONYSIM_SHOTS=tests/visual/shots godot --path .` finishes and writes `metrics.txt`.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): jobs with identity, claims, progress, spatial index; ramp, ladder and dig-stairs designations"`

---

### Task 4: Game groundwork — incremental overlay, edit budget, input safety, new tools

**Files:**
- Modify: `src/Game/Tools/DesignationOverlay.cs`, `assets/shaders/overlay.gdshader`, `src/Game/Rendering/ChunkRenderer.cs`, `src/Game/Tools/ToolController.cs`, `src/Game/Input/InputActions.cs`, `src/Game/Ui/Hud.cs`, `src/Game/Debug/VisualShots*.cs`

**Interfaces:**
- **Consumes:** `JobQueue.ConsumeChangedChunks`, `Job.Status`, and the T3 tools.
- **Produces:**
  - **`InputActions`:** `ToolDigStairs`, `ToolRamp`, `ToolLadder`, `SimPause`, `SimSpeed1`, `SimSpeed2`, `SimSpeed3`, `WorkPriorities`, `FollowColonist`.
  - **Bindings:**
    - tools: Z Dig, X DigStairs, C Wall, V Floor, B Stairs, N Ramp, M Ladder, Delete Cancel, Esc Camera;
    - speed: Space, 1, 2, 3;
    - P priorities, F follow.
  - The existing tool action names are kept with their new keys.

**Behavior:**
1. **Overlay:**
   - One `MultiMeshInstance3D` per chunk that has jobs. Only chunks reported by `ConsumeChangedChunks` (and the preview) are rebuilt.
   - The MultiMesh uses per-instance colors (`UseColors = true`), and `overlay.gdshader` multiplies the uniform color by the instance `COLOR`.
   - Color comes from the job kind. A job with `Status == Unreachable` is drawn at 50% brightness. A job with `Status == NeedsWayBack` is drawn in `#ff5a5a`.
2. **`ChunkRenderer`:**
   - Complete small (atomic) edit batches apply one batch at a time while the frame's `ApplyBudgetMs` lasts. At least one batch applies per frame.
   - Any left over wait for the next frame. A batch never applies partially.
3. **`ToolController`:**
   - A drag in progress is cancelled on window or application focus loss (`NotificationWMWindowFocusOut`, `NotificationApplicationFocusOut`).
   - It is also cancelled on any mouse motion whose `ButtonMask` lacks Left, which is a lost release.
   - Toolbar buttons and hints are added for Ramp, Ladder and Dig stairs.
4. **HUD:** the toolbar lists all tools with their new keys.

- [ ] **Step 1: Add harness checks** in `VisualShots.Review.cs`:
  - `overlay_incremental_ms`: the time to apply a one-job change while 50,000 jobs exist. It must be < 2 ms.
  - `edit_burst_apply_ms_max`: 30 single-block edits in different chunks, spread over 3 frames. The max per-frame apply time must be ≤ `ApplyBudgetMs + 2`.
  - `focus_out_cancels_drag=1`: start a drag, send `NotificationApplicationFocusOut`, and check that `Tools.IsDragging` is false.
  - `phantom_drag_cancelled=1`: start a drag, inject a motion event with an empty button mask, and check that the drag is gone.
  - Screenshot `overlay_kinds.png`, with one job of each kind plus one Unreachable and one NeedsWayBack job, set by hand.
- [ ] **Step 2: Run the harness and see the new metrics missing or failing** (RED for the harness).
- [ ] **Step 3: Implement the four behaviors above.**
- [ ] **Step 4: Verify.**
  - Run the harness. All four metrics pass; check `overlay_kinds.png` (each color distinct, the unreachable job dim, NeedsWayBack red).
  - The build has 0 warnings and Core tests pass.
  - Manually: the hotkeys select the right tools.
- [ ] **Step 5: Commit:** `git commit -m "feat(game): per-chunk job overlay, batched edit budget, drag safety, new tool hotkeys"`

---

### Task 5: Start site, cleared clearing and natural slopes

**Files:**
- Create: `src/Core/Generation/StartSite.cs`, `StartSiteFinder.cs`, `SlopePlacer.cs`
- Modify: `MapGenerator.cs`, `SurfaceDecorator.cs`, `src/Core/ColonyMap.cs`, `src/Game/View/CameraRig.cs`
- Test: `tests/Core.Tests/StartSiteTests.cs`, `tests/Core.Tests/SlopePlacerTests.cs`, `MapGeneratorTests.cs`

**Interfaces:**
- **Produces:**
  - **`StartSite`:** `readonly record struct StartSite(int X, int Z, int Level)`, where `Level` is the standing y (flattened surface + 1).
  - **`StartSiteFinder`:**
    - `const int CoreRadius = 6, BlendRadius = 12, ClearRadius = 8, SearchStep = 8`;
    - `(int X, int Z) Find(int[] heights, MapSettings s)`;
    - `int Flatten(int[] heights, MapSettings s, int x, int z)`, which returns the flattened surface y.
  - **`SurfaceDecorator.Decorate(VoxelWorld w, MapSettings s, StartSite site)`:** no trees or boulders within `ClearRadius` of the site.
  - **`SlopePlacer.Place(VoxelWorld w, MapSettings s)`:** runs after `Decorate` and before `Discovery.InitialFlood`.
  - **`ColonyMap.StartSite`:** a property set by the generator and by loading. The constructor gains an optional `StartSite site` parameter.
  - **`CameraRig.Setup`** focuses on `map.StartSite` instead of the map center.

**Rules:**
- **Find:**
  - Candidates lie on a grid every `SearchStep` blocks inside the central 70% of the map (`[0.15, 0.85]` of each side).
  - **Excluded:** any column within 12 has a height ≤ `WaterLevel + 1`, or any column within 8 has a height ≥ `MountainRockLine`.
  - **Score:** the height range (max − min) within radius 8; lower is better. Ties go to the candidate nearer the map center.
  - **No candidate:** fall back to the map center.
- **Flatten:**
  - The target height T is the median height within `CoreRadius`, clamped to ≥ `WaterLevel + 2`.
  - For each column at distance r, compute `r' = r + 8·(noise(x,z) − 0.5)`. The noise is `HeightField.Noise(seed, 31, 0.15f, FBm, 2)` mapped to [0,1].
  - With `w = 1 − smoothstep(CoreRadius, BlendRadius, r')`, the new height is `round(h + (T − h)·w)`, and columns with `r ≤ CoreRadius` are forced to exactly T.
  - Flattening runs on the height array **before** `ColumnFiller`, so layers, caves and fog all see it.
- **Slopes:** for each column (x, z) with generated surface h:
  - **The cell:** the cell (x, h+1, z) must be Air, and the block at h must be natural.
  - **Corner heights:** a corner point is **raised** if any of the (up to 3) other columns touching that corner has generated surface exactly h+1, its block at h+1 is natural, and the block at h+2 above it is not a log. That last condition keeps tree trunks from counting.
  - **Shape:**

    | Raised corners | Block placed at h+1 |
    |---|---|
    | 2 adjacent | a Ramp facing the raised side |
    | 1 | a CornerOuter whose raised corner is that one |
    | 3 | a CornerInner |
    | 0, 2 opposite, or 4 | nothing |

  - **Material:** `NaturalSlope(SlopeBaseFor(block at h), shape, facing)`. All writes use `markDirty: false`.
- **Generator order:** heights → `Find` and `Flatten` → fill → carve → ores → decorate (with the site) → slopes → discovery. The progress stage names gain "Choosing a start site" and "Smoothing slopes".

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Flatten_CoreIsLevel_EdgeBlends()
{ /* synthetic 256² heights from HeightField(seed 3); Flatten at the found site; every column with r ≤ 6 == T;
     every adjacent pair of columns within r ≤ 14 differs by ≤ max(original adjacent difference in that ring, 1) + 1 */ }
[Fact] public void Find_IsDeterministic_AvoidsWaterAndMountains() { /* seeds 1..5: same result twice; no column within 12 at ≤ WaterLevel+1 */ }
[Fact] public void Generated_StartSite_IsClearOfTreesAndBoulders() { /* Small map seed 1: no log/leaves/cobblestone within ClearRadius of the site in y ∈ [Level, Level+12] */ }
[Fact] public void Slope_OneStep_PlacesRampFacingUp()
{ /* 64² SolidBelow(surface 150) with columns x ≥ 20 raised to 151 (set blocks + surface height); Place;
     block at (19,151,10) is NaturalSlope(Stone, Ramp, East) */ }
[Fact] public void Slope_OuterAndInnerCorners() { /* a raised 10×10 square: the outside diagonal of its NE corner gets CornerOuter; an L-shaped notch gets CornerInner */ }
[Fact] public void Slope_TwoBlockCliff_StaysSharp() { /* columns raised by 2: no slope blocks anywhere */ }
[Fact] public void Slope_NotNextToTreeTrunk_NotInWater() { }
[Fact] public void Generated_SlopesOnlyOnOpenSurface() { /* Small map: every slope block has a natural solid block below and Air or slope-free sky above at y+1 */ }
```

Existing determinism tests keep passing: `Same seed → same world hash`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement the rules above, and wire `CameraRig.Setup`.**
- [ ] **Step 4: Verify.**
  - All non-Perf tests pass. `Large_GeneratesUnder30s` (Perf) still passes; run it once with `--filter "FullyQualifiedName~Large_GeneratesUnder30s"`.
  - Harness: add screenshots `start_site.png` (looking down at the site from 60 blocks, pitch 50) and `slopes_closeup.png` (distance 30, pitch 35, on hilly ground). Read them and check that the site is flat with an organic edge and that the slopes have no cracks or z-fighting.
- [ ] **Step 5: Commit:** `git commit -m "feat(gen): flat start site and natural slopes"`

---

### Task 6: Movement rules (`NavRules`)

**Files:**
- Create: `src/Core/Navigation/MoveKind.cs`, `Move.cs`, `NavRules.cs`
- Modify: `src/Core/World/PlayerView.cs`
- Test: `tests/Core.Tests/NavRulesTests.cs` (and a `TestWorlds.Dig(map, x, y, z)` helper that sets Air and calls `RevealFrom`)

**Interfaces:**
- **Produces:**
  - `PlayerView.GetKnownBlock(int x, int y, int z)`: like `GetVisibleBlock`, but always fogged, ignoring `FogEnabled`.
  - `enum MoveKind : byte { Walk, Diagonal, Step, Ladder, Hop, Climb, Fall }`. Fall is used only by the simulation.
  - `readonly record struct Move(Int3 To, MoveKind Kind, float Cost, int Ticks)`.
  - `sealed class NavRules(PlayerView view)`:
    - `const int MaxMoves = 48`;
    - `bool IsPassable(Int3 c)`: in bounds, and the known block is not solid. Air, water and ladders pass.
    - `bool IsStandable(Int3 c)`;
    - `int GetMoves(Int3 from, Span<Move> into)`;
    - `bool TryGetMove(Int3 from, Int3 to, out Move move)`;
    - `int TopStandable(int x, int z)`: the highest standable y, or −1.
    - `bool FindStandableNear(Int3 around, int radius, out Int3 cell)`: the nearest standable cell by squared distance within a cube of `radius`. Ties go to higher y, then lower x, then lower z.
    - **`NavRules` itself never caches.** Every call reads the view, so later tasks can use it on any thread that only reads.

**Rules.** C is the feet cell; "up" is +Y; `dir` is one of the 4 horizontal facings. Every move must end on a standable cell D.

- **Standable(C):** C.y ∈ [1, 254], `Passable(C)`, `Passable(C+up)`, and either the known block at C−up is solid (any shape) or a ladder, or C itself is a ladder.
- **Walk:** D = C + dir.
- **Diagonal:** D = C + dx + dz. C+dx, C+dx+up, C+dz and C+dz+up must all be passable.
- **Step up:** D = C + dir + up, when the block at C+dir `AscendsToward(dir)`.
- **Step down:** D = C + dir − up, when the block at C−up `AscendsToward(dir.Opposite())`.
- **Ladder:**
  - up: D = C+up when C is a ladder;
  - down: D = C−up when C−up is a ladder.
- **Hop up:** D = C + dir + up. The block at C+dir must be natural and solid, and must not be one that `AscendsToward(dir)` (that's a Step). C+2up must be passable.
- **Hop down:** D = C + dir − up. The block at C−up must be natural and solid, and must not `AscendsToward(dir.Opposite())`. C+dir+up must be passable.
- **Climb up, k ∈ {2, 3}:** D = C + dir + k·up. The blocks C+dir+j·up for j = 0..k−1 must all be natural and solid. C + j·up must be passable for j = 2..k+1.
- **Climb down:** the exact reverse. D = C + dir − k·up; the blocks D+(−dir)+j·up for j = 0..k−1 must all be natural and solid. D + j·up must be passable for j = 2..k+1.
- Both hop and climb are also reachable when a Step move exists for the same pair; then only the Step is emitted.

**Costs and ticks:**

| Kind | Cost | Ticks |
|---|---|---|
| Walk | 1.0 | 6 |
| Diagonal | 1.4142 | 8 |
| Step | 1.5 | 8 |
| Ladder | 2.0 | 10 |
| Hop | 6.0 | 20 |
| Climb | 10 + 4k | 30·k |

If D's feet cell is liquid, both cost and ticks are multiplied by 3. Every rule is symmetric: if A→B exists, B→A exists. Costs may differ, because of water.

- [ ] **Step 1: Write the failing tests.** Base world: `TestWorlds.FlatMap()` (stone to y = 150, so the standing y is 151).

```csharp
[Fact] public void Standable_OnGround_NotWithOneHeadroom() { /* (5,151,5) standable; put stone at (5,152,5) → not */ }
[Fact] public void Diagonal_BlockedCorner_NotAllowed() { /* stone at (6,151,5) → no move (5,151,5)→(6,151,6); orthogonal (5,151,6) still fine */ }
[Fact] public void Stairs_AscendOnlyAlongFacing() { /* wood stairs East at (6,151,5): (5,151,5)→(6,152,5) Step; from (6,151,4) moving South onto it: none */ }
[Fact] public void Stairs_DescendReverse() { /* from (6,152,5) to (5,151,5) is Step */ }
[Fact] public void Corner_AscendsAlongBothDirections() { }
[Fact] public void Ladder_ShaftUpDownAndExitAtTop() { /* dig a 1×1 shaft (5,147..150,5), ladders in it; (5,147,5)→(5,148,5) Ladder … top (5,150,5)→(5,151,5) Ladder; (5,151,5)→(6,151,5) Walk */ }
[Fact] public void Hop_OnNaturalOnly() { /* stone block at (6,151,5): (5,151,5)→(6,152,5) Hop; same with wood planks: none */ }
[Fact] public void Hop_UnderRoof_InCave_Allowed() { /* a dug, revealed cave room 2 tall with a one-block natural step: Hop present */ }
[Fact] public void Hop_NeedsHeadClearance() { /* stone at (5,153,5) above the hopper → no Hop */ }
[Fact] public void Climb_TwoAndThree_FourNot() { }
[Fact] public void Climb_BuiltWall_NotAllowed() { }
[Fact] public void Water_TriplesCostAndTicks() { }
[Fact] public void UndiscoveredAir_IsNotPassable() { /* air pocket set without reveal → IsPassable false; after RevealFrom → true */ }
[Fact] public void Moves_AreSymmetric_OnRandomWorld()
{ /* 64² world, seed-random blocks from {stone, planks, stairs/ramps/corners any facing, ladders, water} in y 150..156, then InitialFlood;
     for every standable cell A and each move A→B: TryGetMove(B, A) succeeds */ }
[Fact] public void TopStandable_AndFindStandableNear() { }
```

- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.** Use one helper per rule and a fixed `Span<Move>`, with no allocation per call.
- [ ] **Step 4: All non-Perf tests pass.** Add a Perf test `GetMoves_1M_Under300ms`: one million calls on a Small generated map; the time is recorded in the test output.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): navigation move rules"`

---

### Task 7: Caves colonists can use, ores along them, river width

**Files:**
- Modify: `src/Core/Generation/CaveCarver.cs`, `OrePlacer.cs`, `HeightField.cs`
- Test: `tests/Core.Tests/CaveTests.cs` (new), `HeightFieldTests.cs`

**Interfaces:**
- **Consumes:** `NavRules` (T6), for the walkability test only.
- **Produces:** no new API. `MapGenerator` output changes. Determinism is kept.

**Rules** (shapes are the builder's call; the tests pin the outcome):
- **Depth profile:** cave carving depends on the depth below the local surface, d = surface − y.
  - **Shallow band, d ∈ [CaveRoof, 40]:** tunnels are wider (raise the tunnel width threshold) and taller (less vertical squash).
  - **Deeper than 90:** tunnels are narrower and more broken up.
  - **In between:** interpolate between the two.
- **Ores along shallow caves:** after the normal bands, an extra pass seeds coal and iron veins that start on stone touching cave air in the shallow band. They may extend iron above its normal band, up to the surface minus `CaveRoof`. Veins only replace Stone or DeepStone.
- **River width:** the half-width is measured across the flow, using `|b − center[a]| · cos(atan(dcenter/da))` with a central-difference slope.

- [ ] **Step 1: Write the failing tests.** Small maps with seeds 1, 2 and 3; the slow ones are tagged Perf only if they take over 5 s.

```csharp
[Fact] public void ShallowTunnels_AreTallerThanDeep() { /* mean vertical run of contiguous cave air per column: shallow band ≥ 3.0 and > deep band (y < DeepStoneLevel) */ }
[Fact] public void ShallowCaves_AreMostlyWalkable()
{ /* TestWorlds.RevealAll(world) marks every non-solid cell discovered; then NavRules floods the standable cave cells in the shallow band;
     ≥ 70% of them lie in components of ≥ 300 cells */ }
[Fact] public void ShallowCaveWalls_RichInCoalAndIron() { /* among solid blocks 6-adjacent to shallow cave air: (coal+iron) share ≥ 8% */ }
[Fact] public void CavesStillSealed() { /* no cave air directly under open sky outside mountains (step 1 rule kept) */ }
[Fact] public void River_WidthMeasuredAcrossFlow() { /* where |dcenter/da| > 0.5, the bed run across ≤ ceil(RiverWidth·sqrt(1+slope²)) + 1 */ }
```

- [ ] **Step 2: Run them and see them fail** (mainly the walkability and ore share tests).
- [ ] **Step 3: Implement and tune until they pass.**
- [ ] **Step 4: Verify.**
  - All non-Perf tests pass, and `Large_GeneratesUnder30s` passes.
  - Harness: add screenshot `cave_shallow.png`, a slice 20 below the surface over the largest shallow cave, with fog off. Wide tunnels with visible coal and iron should be readable.
- [ ] **Step 5: Commit:** `git commit -m "feat(gen): roomy shallow caves with coal and iron, tighter deep caves; river width across flow"`

---

### Task 8: Navigation sections and connectivity

**Files:**
- Create: `src/Core/Navigation/Navigation.cs`, `NavChunk.cs`, `ConnectivityPreview.cs`
- Modify: `src/Core/World/VoxelWorld.cs`, `src/Core/ColonyMap.cs`
- Test: `tests/Core.Tests/NavigationTests.cs`

**Interfaces:**
- **Consumes:** `NavRules` (T6).
- **Produces:**
  - **`VoxelWorld.Changed`:** `event Action<int, int, int>? Changed`, raised for every write with `markDirty: true` (blocks and discovery), on the main thread.
  - **`ColonyMap.Nav`:** `Navigation Nav { get; }`, created lazily on first access. After that it tracks the world through `Changed`.
  - **`Navigation(ColonyMap map)`** builds every chunk in parallel at construction.
  - **`Navigation` members:**
    - `NavRules Rules`, `int Version`, `void Update()`;
    - `int ComponentOf(Int3 c)` and `int SectionOf(Int3 c)` (both −1 when not standable);
    - `int SectionCount`, `Int3 SectionAnchor(int section)`, `IReadOnlyList<SectionLink> SectionLinks(int section)`;
    - `int HomeComponent(Int3 anchor)`: the component of `FindStandableNear(anchor, 8)`, or −1;
    - `ConnectivityPreview Preview(Int3 cell, ushort block)`.
  - **`Update()` and `Version`:** `Update()` recomputes everything dirtied since the last call and bumps `Version` if anything changed.
  - **`SectionAnchor`** returns a standable cell of the section.
  - **`readonly record struct SectionLink(int ToSection, Int3 From, Int3 To, float Cost)`**, deduplicated to the cheapest link per section pair.
  - **`ConnectivityPreview`:** `int ComponentOf(Int3 c)` as it would be if `cell` were set to `block` in the known world. A preview doesn't change the navigation state.

**Rules:**
- **Sections:** a section is a connected set of standable cells of one chunk under the T6 moves whose ends are both inside that chunk.
- **Links:** moves that cross a chunk boundary, in any direction including vertical (ladders and climbs), are links.
- **Components:** the transitive closure of sections over links, which is exact because moves are symmetric. **Connectivity must equal a brute-force flood over `NavRules` after any sequence of edits.**
- **Dirty area:** a world change at (x, y, z) dirties every chunk overlapping x ±1, z ±1, y ∈ [y − 4, y + 1]. That covers every cell whose standability or moves read that block.
  - The chunks bordering a recomputed chunk get their border links re-derived, so links that were merged by section never go stale.
- **Data limits:** each chunk stores its standable cells' local section ids sparsely, since only standable cells matter. Navigation data stays **< 64 MB on Large**, measured in the Perf test with `GC.GetTotalMemory` before and after building.
- **Preview cost:** a preview recomputes the affected chunks with an overlay of the one changed cell, then derives components from those chunks plus the cached rest.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Components_MatchBruteForce_AfterRandomEdits()
{ /* 64² FlatMap; 400 random edits (stone, air+reveal, stairs, ramps, ladders, water) in y 146..156, across chunk borders x,z ∈ {30..33};
     after every 20 edits: Update(); for all standable cells, ComponentOf partitions equal a BFS partition built with NavRules.GetMoves */ }
[Fact] public void StairsAcrossChunkYBorder_Connect() { /* stair run from y 158 to 162 crossing the y=160 chunk border joins the two floors */ }
[Fact] public void LadderAcrossChunkYBorder_Connects() { }
[Fact] public void Wall_SplitsComponent_RemovalRejoins() { }
[Fact] public void Preview_PredictsComponentsAfterEdit() { /* for 50 random single edits: Preview(edit).ComponentOf(x) partition == actual after applying + Update */ }
[Fact] public void Version_BumpsOnlyOnChange() { }
[Fact, Trait("Category","Perf")] public void Large_BuildUnder3s_Under64MB() { }
[Fact, Trait("Category","Perf")] public void Large_SingleEditUpdate_Avg3ms_Preview5ms() { /* 200 random surface digs */ }
```

- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.** Keep it main-thread only (apart from the parallel initial build), and document that on the class.
- [ ] **Step 4: All tests pass, Perf included** (`dotnet test tests/Core.Tests --filter "FullyQualifiedName~NavigationTests"`). Record the Perf numbers in the commit message.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): navigation sections with exact incremental connectivity and edit preview"`

---

### Task 9: Pathfinding and the path service

**Files:**
- Create: `src/Core/Navigation/PathFinder.cs`, `PathRequest.cs`, `PathService.cs`
- Test: `tests/Core.Tests/PathFinderTests.cs`

**Interfaces:**
- **Consumes:** `Navigation`, `NavRules`, `Move` (T6, T8).
- **Produces:**
  - **`PathStatus`:** `enum PathStatus { Pending, Found, Unreachable, TooLong }`.
  - **`PathRequest`:**
    - `Int3 From`, `Int3 To`, `PathStatus Status`;
    - `IReadOnlyList<Move> Moves`, where the first move starts at `From`;
    - `float Cost`, `int NodesExpanded`, and `int NavVersion` (the version it was computed at).
  - **`PathFinder(Navigation nav)`:** `PathRequest FindNow(Int3 from, Int3 to)`, synchronous, for tests and benchmarks.
  - **`PathService(Navigation nav)`:**
    - `PathRequest Request(Int3 from, Int3 to)`;
    - `void Process(int nodeBudget)`: advances pending requests in FIFO order, time-sliced across calls;
    - `int Pending`, `void Cancel(PathRequest r)`.

**Rules:**
- **Immediate answers:** `Unreachable` immediately if either end isn't standable or `ComponentOf` differs.
- **Short paths:** if the Chebyshev distance is ≤ 32, plain cell A* (octile-3D heuristic, admissible: the minimum cost per unit distance is 1), which is optimal.
- **Long paths:**
  1. A* over sections, using `SectionLinks` and anchor distances.
  2. Cell A* restricted to the corridor's sections plus their direct neighbors.
  3. If that fails, unrestricted cell A*.
- **Per-request limit:** 600,000 expanded nodes, after which the request is `TooLong`.
- **No per-call allocation** beyond pooled buffers.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Straight_And_AroundWall_AreOptimal() { }
[Fact] public void UsesStairsAndLadders_BetweenFloors() { }
[Fact] public void PrefersStairs_OverHop() { /* a hop and a 3-step stair detour both available: path cost < hop route cost and uses Step moves */ }
[Fact] public void CliffOfFour_IsUnreachable_UntilStairsBuilt() { }
[Fact] public void Path_IntoCave_OnlyAfterReveal() { }
[Fact] public void EveryMove_IsValid() { /* for found paths on a random world: each consecutive Rules.TryGetMove succeeds */ }
[Fact] public void LongPath_WithinFactorOfOptimal() { /* 256² generated Small map, 20 pairs > 100 apart: hierarchical cost ≤ 1.25× unrestricted A* cost */ }
[Fact] public void Service_TimeSlices_AndCompletes() { /* Process(500) repeatedly: a long request stays Pending for several calls, then Found with the same path as FindNow */ }
[Fact, Trait("Category","Perf")] public void Large_LongPaths_Fast() { /* Large seed 12345, 50 pairs ≥ 200 apart in the home component: all Found, mean < 5 ms, max < 40 ms */ }
```

- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: All tests pass, Perf included.** Record the Perf numbers in the commit message.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): hierarchical pathfinding with a time-sliced path service"`

---

### Task 10: Colonists, skills, the clock and movement

**Files:**
- Create: `src/Core/Colonists/{SkillType,Skill,Skills,WorkType,WorkTypes,Colonist,Colony,ColonistFactory,ColonistNames,ViewBookmark}.cs`, `src/Core/Sim/{SimClock,Simulation,ColonistMover}.cs`
- Modify: `src/Core/ColonyMap.cs`, `MapGenerator.cs`
- Test: `tests/Core.Tests/SkillsTests.cs`, `SimulationTests.cs`

**Interfaces:**
- **Consumes:** T6, T8, T9 and `ColonyMap.StartSite`.
- **Produces:**
  - **Skills:**
    - `enum SkillType { Mining, Construction, Woodcutting, Crafting, Research }`;
    - `sealed class Skill { int Level; float Xp; }`;
    - `static class Skills { const int MaxLevel = 20; float XpForNextLevel(int level) => 400f * (level + 1); float SpeedFactor(int level) => 0.5f + 0.075f * level; void AddXp(Skill s, float xp); }`. Levels carry over and cap at 20, with Xp then frozen at 0.
  - **Work types:**
    - `sealed record WorkType(string Id, string Label, SkillType? Skill, IReadOnlyList<JobKind> Kinds)`;
    - `static class WorkTypes { WorkType Mining, Construction; IReadOnlyList<WorkType> All; }`;
    - Mining covers Dig and CarveStairs; Construction covers BuildWall, BuildFloor, BuildStairs, BuildRamp and BuildLadder. This data list is the only place to edit when adding types.
  - **`Colonist`:**
    - `int Id`, `string Name`, `int ShirtColor` (palette index 0–7);
    - `Int3 Cell`, `Motion? Motion`;
    - `Skill[] Skills` (indexed by `SkillType`), `int[] Priorities` (by `WorkTypes.All` index; 0 = off, 1..4);
    - `ColonistActivity Activity`, `long JobId`, `bool Stuck`.
  - **`ColonistActivity`:** `enum ColonistActivity { Idle, Walking, Working, Falling }`.
  - **`Motion`:** `readonly record struct Motion(Int3 From, Int3 To, MoveKind Kind, int TotalTicks, int Elapsed)`.
  - **`Colony`:**
    - `List<Colonist> Colonists`, `int NextColonistId` (starts at 1; id 0 means "none"), `long Tick`;
    - `Int3 Home` (= `(StartSite.X, StartSite.Level, StartSite.Z)`);
    - `List<ViewBookmark> Bookmarks`.
  - **`ViewBookmark`:** `readonly record struct ViewBookmark(string Name, int Level)`.
  - **`ColonyMap.Colony`:** created by the generator or by loading. The constructor gains an optional `Colony colony` parameter.
  - **`SimClock`:** `const int TicksPerSecond = 20, TicksPerDay = 24000; static int Day(long tick) => (int)(tick / TicksPerDay) + 1`.
  - **`ColonistFactory`:**
    - `Colonist Create(Colony colony, Random rng)`. Names come from `ColonistNames.All` (at least 40), unique within the colony.
    - **Starting skills:** 1–2 random strengths at level 6–12, the others 0–4, Xp 0.
    - **Starting priorities:** all 3.
    - `ShirtColor = id % 8`.
  - **`ColonistFactory.SpawnStarting(ColonyMap map, int count = 3)`:** places colonists on distinct standable cells found by `FindStandableNear(home, 6)` (spiral outward). The generator calls it as stage "Arriving", seeded by `Hash.Of(seed, 41, 0, 0)`.
  - **`Simulation(ColonyMap map)`:**
    - `ColonyMap Map`, `Navigation Nav`, `PathService Paths`, `void Tick()`, `int HomeComponent`;
    - `Colonist Spawn(Int3 near)` for debug and tests;
    - `internal void GoTo(Colonist c, Int3 target)`, a test hook (Task 11 makes work use it).
  - **`Tick()` order:** `Nav.Update()` → `Paths.Process(15000)` → for each colonist, the mover, then the worker (T11) → `Colony.Tick++`.

**Mover rules (`ColonistMover`):**
- **Fall:** when not mid-motion, if the colonist's cell isn't standable, it falls. The target is the first standable cell straight down. Fall motion takes 2 ticks per block, with Activity = Falling. If there is none, the colonist stays put and is marked `Stuck`.
- **Before each move:** the next move is re-validated with `Rules.TryGetMove(cell, next.To)`. If it's invalid, the colonist re-paths to the same target (at most 3 times), then gives up and goes Idle.
- **Motion:** `Elapsed` advances 1 per tick. When it reaches `TotalTicks`, `Cell = To`.
- **Stuck:** whenever `Nav.Version` changes, `Stuck = ComponentOf(Cell) != HomeComponent`. `HomeComponent` is recomputed when the version changes.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Skills_LevelUpCarriesOver_CapsAt20() { }
[Fact] public void SpeedFactor_Endpoints() { Assert.Equal(0.5f, Skills.SpeedFactor(0)); Assert.Equal(2.0f, Skills.SpeedFactor(20), 3); }
[Fact] public void Factory_StartingColonists_UniqueNames_OneOrTwoStrengths() { }
[Fact] public void Generated_Small_HasThreeColonistsAtStartSite() { /* all standable, within 6 of Home */ }
[Fact] public void GoTo_WalksThereInExpectedTicks() { /* flat: 10 cells east → arrives in 60 ticks (+1 for path) */ }
[Fact] public void GroundRemovedUnderColonist_Falls() { /* dig a 3-deep shaft under an idle colonist (with reveal) → after ticks, Cell.Y == 148, Activity back to Idle */ }
[Fact] public void WallBuiltOnPath_ColonistRepaths() { /* place stone across the straight path mid-walk; still arrives, never inside a solid cell (assert every tick) */ }
[Fact] public void SealedPocket_IsStuck() { }
[Fact] public void HundredColonists_TickUnder2ms() { /* Trait Perf: Small flat map, 100 colonists walking random GoTo targets; mean Tick < 2 ms after warmup */ }
```

- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: All non-Perf tests pass, and the Perf test passes.**
- [ ] **Step 5: Commit:** `git commit -m "feat(core): colonists, skills, simulation clock and movement"`

---

### Task 11: Work — choosing, reaching, doing and never stranding

**Files:**
- Create: `src/Core/Sim/{JobFinder,WorkRules,StrandCheck,ColonistWorker}.cs`
- Modify: `src/Core/Sim/Simulation.cs`
- Test: `tests/Core.Tests/WorkTests.cs`

**Interfaces:**
- **Consumes:** T3 (`JobQueue`, `WorkCompletion`), T8 (`Preview`), T9, T10.
- **Produces:**
  - **`WorkRules`:**
    - `IEnumerable<Int3> WorkSpots(ColonyMap map, Job job)`: standable cells S with |dx| ≤ 1, |dz| ≤ 1, `job.Position.y ∈ [S.y−1, S.y+2]`, where the position is none of S, S+up or S−up.
    - `bool IsReady(ColonyMap map, Job job)`;
    - `int WorkTicks(Job job, BlockDef target) => (int)MathF.Ceiling(target.Hardness * 40)`, with a minimum of 10;
    - `WorkType TypeOf(JobKind k)`.
  - **`StrandCheck`:** `bool Allows(Simulation sim, Colonist worker, Job job, Int3 workSpot)`.
  - **`JobFinder`:** `bool TryFind(Simulation sim, Colonist c, out Job job, out Int3 workSpot)`.
  - **`ColonistWorker`:** the per-tick state machine. It is called by `Simulation.Tick` after the mover.

**Rules:**
- **Readiness (bottom-up builds, top-down digs):**
  - A build job is **not ready** while any other build job exists at (x+dx, y−k, z+dz) with dx, dz ∈ [−1, 1] and k ∈ [1, 4].
  - A Dig or CarveStairs job is not ready while a Dig or CarveStairs job exists at (x, y+1..y+3, z).
- **Search:**
  - **When:** an idle colonist searches only on ticks where `(tick + id) % 10 == 0`.
  - **Order:**
    1. Priorities 1→4.
    2. Within a priority, work types sorted by the colonist's skill level for them, descending; ties keep `WorkTypes.All` order.
    3. Within a work type, `ByRing` from the colonist's cell. Gather candidates from the first ring that has any valid job plus the next ring, then take the nearest by squared distance.
  - **A candidate is valid when:**
    - its kind belongs to the work type;
    - it's unclaimed;
    - it's ready;
    - it isn't marked `Unreachable` or `NeedsWayBack` at the current `Nav.Version`;
    - it has at least one work spot whose `ComponentOf` equals the colonist's.

    If a job has no such spot while the colonist is in the home component, mark it `Unreachable` at the current version.
  - **Strand check:** the chosen job's work spots are tried nearest first. The first that passes `StrandCheck.Allows` wins. If none pass, mark the job `NeedsWayBack` at the current version and continue the search.
- **`StrandCheck.Allows`:**
  - **The edit:** take `Preview(job.Position, resultBlock)`. Digs give Air; builds and carves give `job.Block`.
  - **Digs and carves:** after the edit, the work spot must still be in the colonist's current component. If that component is home, it must stay home.
  - **Builds:** additionally, every colonist in the home component before the edit must still be in it after, and the cell must not overlap any colonist's body (cell or cell+up). An overlap means wait, not reject.
- **Spot order:** spots are tried nearest first. For BuildWall jobs, spots open to the sky (no solid block within 8 above) come first, so walls are built from outside where possible.
- **Doing the work:**
  - **Walking:** claim the job, then walk with `GoTo(workSpot)`.
  - **Working:** each tick, `AddProgress(job, SpeedFactor(level))` and `AddXp(skill, 1)`.
  - **Finishing:** at `WorkTicks`, call `WorkCompletion.Complete` and go Idle.
  - **Builds** wait (no progress) while a colonist's body overlaps the cell.
- **Abandoning:** if the job disappears (cancelled, completed by instant work) or the path fails, release the claim and go Idle.
- **Unclaimed jobs** don't need any per-tick processing.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Colonist_DigsDesignatedBlock_GainsMiningXp() { }
[Fact] public void Priorities_SplitWork() { /* A: Mining 1 / Construction 3; B: the reverse; one dig, one wall → A digs, B builds */ }
[Fact] public void SamePriority_PrefersBestSkill() { /* Mining 10, Construction 2, both priority 3; dig and wall jobs equidistant → dig first */ }
[Fact] public void BuildsBottomUp_DigsTopDown() { /* a 3-high wall: completion order by y ascending; a 3-deep dig column: y descending */ }
[Fact] public void CarvedStaircase_LeadsDown_AndIsWalkable() { /* DigStairs 6 long; colonists finish; FindNow(surface → bottom step) is Found */ }
[Fact] public void Unreachable_JobMarked_ColonistIdles() { }
[Fact] public void DiggingOnlyLadder_IsNeedsWayBack() { /* a colonist in a 4-deep shaft whose only way out is the ladder; a dig job on the bottom ladder block, worked from inside → NeedsWayBack, not taken */ }
[Fact] public void WallGap_BuiltFromOutside_ColonistNotSealedIn() { /* a 5×5 stone room with one gap, colonist inside, wall job on the gap → finished; colonist ends in the home component */ }
[Fact] public void CancelClaimedJob_ColonistGoesIdle() { }
[Fact] public void BuildOnColonist_Waits() { }
[Fact, Trait("Category","Perf")] public void JobSearch_200kJobs_StaysFast() { /* 200k dig jobs in a 100×100×20 box, 100 colonists: mean TryFind < 1 ms, Tick mean < 4 ms */ }
```

- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: All non-Perf tests pass, and the Perf test passes.**
- [ ] **Step 5: Commit:** `git commit -m "feat(core): colonists choose, reach and do work by priority and skill without stranding"`

---

### Task 12: Game — simulation runner, colonist rendering, time controls

**Files:**
- Create: `src/Game/Sim/SimRunner.cs`, `src/Game/Colonists/ColonistRenderer.cs`, `src/Game/Debug/VisualShots.Colony.cs`
- Modify: `src/Game/MapScene.cs`, `Ui/Hud.cs`, `Ui/DebugOverlay.cs`, `Work/InstantWorkRunner.cs` (unchanged behavior; it now coexists with colonists)

**Interfaces:**
- **Consumes:** `Simulation` (T10–T11) and the T4 input actions.
- **Produces:**
  - **`SimRunner : Node`:**
    - `Simulation Sim`, `int Speed` (0 = paused, 1–3), `float Alpha` (0..1, the fraction toward the next tick);
    - `double LastTickMs` and `AvgTickMs` (averaged over 2 s);
    - the update runs `Speed × 20` ticks per second, capped at 8 ticks per frame.
    - **Input:** Space toggles pause (restoring the last speed); 1, 2 and 3 set the speed.
  - **`MapScene.SimRunner`** and **`MapScene.ColonistRenderer`**, both created before the HUD, which reads them.
  - **`ColonistRenderer`:**
    - six `MultiMeshInstance3D` body parts (head, torso, two arms, two legs) with per-instance colors (skin, shirt palette, pants);
    - `Vector3 RenderPosition(Colonist c)`, the interpolated feet position used by picking and following.
  - **Motion interpolation:** linear from `From` to `To` over `(Elapsed + Alpha) / TotalTicks`. Hops add a parabola peaking 0.4 above; falls are linear.
  - **Animation:** colonists face their direction of travel, and limbs swing ±30° at 2 Hz while moving.
  - **Visibility:** a colonist whose feet cell `y > Slice.ViewLevel` is hidden (its instance scaled to zero); otherwise it's drawn whole.

**HUD:**
- time controls `⏸ 1× 2× 3×` and "Day N", at the top right of the toolbar;
- the instant work toggle is kept, labelled "Instant work (debug)".

**Debug overlay** additions:
- colonists;
- tick ms (average and last);
- pending paths;
- nav sections and nav version.

- [ ] **Step 1: Add harness scenarios** in `VisualShots.Colony.cs`:
  - `colonists_idle.png`: the three colonists at the start site, close up.
  - `colonists_digging.png`: a 10×10×3 dig next to the site at 3× for 30 simulated seconds, then shot.
  - `colonists_sliced.png`: the view level set to the dig's middle; colonists below it are visible whole, colonists above are hidden.
  - **Metrics:**
    - `sim_tick_ms_avg`;
    - `colonists_digging_blocks_done` (> 0);
    - `speed_controls_ok=1`: Space pauses (no tick for 1 s), and 3 runs 3× (60 ± 6 ticks per second over 2 s).
- [ ] **Step 2: Run the harness; these fail or are missing** (RED).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** Run the harness, read the three PNGs and check the metrics. The build has 0 warnings and Core tests pass.
- [ ] **Step 5: Commit:** `git commit -m "feat(game): simulation runner, instanced colonists, time controls"`

---

### Task 13: Game — selecting, following, bookmarks and work priorities

**Files:**
- Create: `src/Core/Colonists/ColonistPicking.cs`, `src/Game/Ui/{ColonistBar,ColonistPanel,WorkPrioritiesWindow,BookmarksPanel}.cs`
- Modify: `src/Game/Tools/ToolController.cs`, `View/CameraRig.cs`, `View/SliceController.cs`, `MapScene.cs`, `Ui/Hud.cs`
- Test: `tests/Core.Tests/ColonistPickingTests.cs`

**Interfaces:**
- **Produces:**
  - **`ColonistPicking.Pick`:** `static int Pick(Vector3 origin, Vector3 dir, IReadOnlyList<(int Id, Vector3 Feet)> colonists, float maxDistance)` returns the nearest hit's id, or 0. The hit box is 0.6 × 1.8 × 0.6, centered on the feet x/z and rising from the feet y.
  - **`MapScene.Selected`:** `Colonist? Selected { get; set; }`, plus `event Action? SelectionChanged`.
  - **`CameraRig.Follow(Func<Vector3>? target)`:** the focus tracks the target every frame. Following stops on WASD panning, middle-drag, or `Follow(null)`.
  - **`SliceController`, while following and sliced:** the view level follows the colonist's feet y. It only changes when that y changes.

**UI:**
- **Selecting:**
  - With the Camera tool, a left click on a colonist selects it; the camera doesn't move.
  - A click on empty ground clears the selection.
  - A double-click on a block still jumps the view level.
- **`ColonistBar`** (top center): one button per colonist with name and activity (Idle, Walking, Digging, Building, Falling); a stuck colonist shows "⚠ stuck" in red. Click selects and centers the camera; double-click follows.
- **`ColonistPanel`** (bottom left, visible while a colonist is selected):
  - name;
  - an activity line, for example "Digging stone at (x, y, z)" or "Stuck — can't reach the colony";
  - the five skills with level and an Xp bar;
  - a "Follow [F]" toggle.
- **`BookmarksPanel`** (under the level indicator):
  - "＋ Bookmark" adds `ViewBookmark(name, Slice.ViewLevel)`, where the name comes from a LineEdit and defaults to "Level {Y}". It's stored in `Colony.Bookmarks`.
  - Each bookmark is a button that jumps to its level, with an ✕ to delete it.
  - The LineEdit releases focus on Enter or Esc, so hotkeys keep working.
- **`WorkPrioritiesWindow`** (P toggles it): a grid of colonists × `WorkTypes.All`.
  - A left click cycles 1 → 2 → 3 → 4 → off → 1; a right click cycles backwards.
  - Each cell shows the number and is tinted by the colonist's skill level. The tooltip shows the skill.

- [ ] **Step 1: Write the failing Core test** `ColonistPickingTests`: a hit, a miss, the nearest of two, and a hit through the head height. Run it and see it fail.
- [ ] **Step 2: Implement `ColonistPicking`; the test passes.**
- [ ] **Step 3: Add harness checks:**
  - `select_by_click=1`: project a colonist's position to the screen and inject a click.
  - `follow_tracks=1`: after 2 s of walking, the camera focus is within 1 block of the colonist.
  - `bookmark_roundtrip=1`: add, jump, delete.
  - `priorities_cycle=1`.
  - Screenshots `colonist_panel.png` and `work_priorities.png`.
- [ ] **Step 4: Implement the UI; the harness checks pass.** Read the screenshots. The build has 0 warnings.
- [ ] **Step 5: Commit:** `git commit -m "feat(game): colonist selection, bar, panel, follow, bookmarks and work priorities"`

---

### Task 14: Save format v2

**Files:**
- Create: `src/Core/Persistence/BlockAliases.cs`
- Modify: `src/Core/Persistence/MapSerializer.cs`, `src/Core/ColonyMap.cs`, `src/Game/Persistence/SaveGames.cs` (if the API changes), `src/Game/Ui/Hud.cs` (unsaved-changes tracking includes colony changes), `src/Game/Ui/WorkPrioritiesWindow.cs` and `BookmarksPanel.cs` (bump `ColonyVersion` on edits)
- Test: `tests/Core.Tests/MapSerializerTests.cs`

**Interfaces:**
- **Produces:**
  - **`MapSerializer`:** `FormatVersion = 2`; `Load(Stream stream, IReadOnlyDictionary<string, string>? aliases = null)`, with aliases defaulting to `BlockAliases.Default`, which is empty.
  - **`ColonyMap.ColonyVersion`:** `int ColonyVersion`, which increments on colonist, priority and bookmark changes. The HUD's unsaved-changes check includes it. Changes from simulation ticks alone, such as positions, don't count; only player-made changes and the world/job versions do.

**Format v2 body**, after "CSIM" and version 2, Brotli-compressed:
1. settings;
2. the palette;
3. surface heights;
4. chunks (as v1);
5. the start site;
6. jobs: id, position, kind, block palette index, progress. Claims and status are not saved.
7. `JobQueue.NextId`;
8. the colony:
   - tick, home, next colonist id;
   - per colonist: id, name, shirt color, cell, skills (level and Xp), priorities;
   - bookmarks: name, level.

**Loading rules:**
- **Version 1** → `InvalidDataException("This save was made by an older version of ColonySim (format 1) and can't be loaded.")`.
- **Palette names:** unknown names are tolerated if no cell or job uses them. If one does, the exception names the block. A name found in the aliases is mapped first.
- **Colonist position:** a loaded colonist whose cell isn't standable is moved with `FindStandableNear(cell, 8)`, falling back to `home`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void RoundTrip_ColonyJobsSiteBookmarks_Exact() { }
[Fact] public void RoundTrip_MidWork_DropsClaimsKeepsProgress() { /* run a sim until a dig is half done and a colonist is mid-move; save, load: progress equal, ClaimedBy 0, every colonist on a standable cell */ }
[Fact] public void Load_Version1_ThrowsReadableMessage() { }
[Fact] public void Load_UnknownUnusedPaletteName_Tolerated() { /* save, rewrite the stream with an extra palette name, load ok */ }
[Fact] public void Load_UnknownUsedName_ThrowsNamingIt() { }
[Fact] public void Load_Alias_MapsRenamedBlock() { }
```

Keep the existing corrupt/truncated/newer-version tests.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify.** All non-Perf tests pass. The harness menu checks (save, menu, load) still pass and show colonists restored. Add metric `save_roundtrip_colonists=1`.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): save format v2 with colonists, job progress, start site and bookmarks"`

---

### Task 15: Bulk spawn, the colony benchmark and the 2a done-when run

**Files:**
- Modify: `src/Game/Debug/VisualShots.Colony.cs`, `src/Game/Ui/DebugOverlay.cs`, `src/Game/Input/InputActions.cs`, `src/Game/MapScene.cs`

**Interfaces:**
- **Produces:** the debug action `DebugSpawnColonists` (F9) spawns 10 colonists at home with `Simulation.Spawn`.
- **Benchmark scenario** (`COLONYSIM_SHOTS_SIZE=Large`):
  - **Setup:** designate a 60×60 area, 8 levels deep, next to the start site, plus 20 Dig-stairs runs.
  - **Measure:** for N in 20, 50 and 100 (spawning up to N), run 1× for 15 s, discard the first 3 s, and record:
    - `bench_N_frame_ms_avg` and `bench_N_frame_ms_p99`;
    - `bench_N_tick_ms_avg`;
    - `bench_N_paths_pending_max`.
  - **Pass:** `bench_20_frame_ms_avg ≤ 16.7` and `bench_20_frame_ms_p99 ≤ 25`. The 50 and 100 numbers are recorded only, as the baseline.
- **Done-when scenario** (spec 2a, items 1–4):
  - `done_start_site.png`;
  - `done_mine.png`: a multi-level mine dug through carved stairs by the three starting colonists at 3×, with up to 10 simulated minutes of timeout;
  - `done_ladder_shaft=1`: a colonist climbs out of a laddered shaft;
  - `done_outdoor_climb=1`: a colonist climbs a 3-block natural ledge only when no stairs exist;
  - `done_never_stranded=1`: no colonist ever Stuck during the mine scenario;
  - `done_skill_growth=1`: some colonist's Mining level rose.

- [ ] **Step 1: Add the scenarios and metrics.** Run them against the current build. Expect them to run; failures here are real defects to fix in this task.
- [ ] **Step 2: Fix what fails.** Perf fixes may touch T8–T12 code; every Core test must still pass.
- [ ] **Step 3: Full verification:**
  - `dotnet build ColonySim.sln` has 0 warnings;
  - `dotnet test tests/Core.Tests --filter "Category!=Perf"` passes;
  - all Perf tests pass;
  - the harness passes on Small and on Large.
  - Read every new PNG.
- [ ] **Step 4: Record the numbers** in `docs/superpowers/plans/2026-10-19-step2a-colonists-walk-and-work.md` under a new "Results" heading at the end.
- [ ] **Step 5: Commit:** `git commit -m "test(game): colony benchmark and step 2a done-when run"`
