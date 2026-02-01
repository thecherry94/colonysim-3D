# colonysim-3d - Project Bible

> **Purpose:** This document is the definitive reference for the colonysim-3d project. Return here when context is lost or decisions need review.

---

## 1. Project Overview

**Project Name:** colonysim-3d

**Game Concept:** Minecraft-style voxel world combined with Rimworld/Dwarf Fortress colony management.

**Core Gameplay Loop:**
- Player controls a colony of individuals (NOT direct first-person control)
- Designate tasks (mine, build, haul, etc.)
- Colonists autonomously execute tasks using AI/pathfinding
- Manage survival, resources, and colony growth

**Perspective:** RTS/management view (top-down or isometric), NOT first-person.

---

## 2. Technical Stack

| Component | Value |
|-----------|-------|
| Engine | Godot 4.6 (Mono/.NET edition) |
| Language | C# (.NET) |
| Renderer | Forward Plus |
| Physics | Jolt Physics |
| Graphics API | D3D12 (Windows) |
| Assembly Name | colonysim_3D |

---

## 3. Core Pillars (Priority Order)

1. **Voxel World Engine** - Block-based terrain, chunk system, procedural mesh
2. **Dynamic Navigation** - NavMesh that updates when world changes (blocks mined/placed)
3. **World Generation** - Procedural terrain generation (basic initially)
4. **NPC/Colonist System** - AI pawns that receive and execute tasks

---

## 4. Architecture Decisions

### 4.1 Voxel/Chunk System

**Decision: NOT using GridMap**
- GridMap is optimized for static level design
- Expensive to update frequently (regenerates octants on every change)
- Not suitable for Minecraft-style mining/placing

**Decision: Using procedural ArrayMesh per chunk**
- Full control over geometry generation
- Can implement greedy meshing / face culling (skip internal faces)
- Chunk isolation means only affected chunk regenerates on block change

**Chunk Configuration:**
- Size: **16x16x16 blocks** per chunk
- Block data: 3D array `BlockType[16,16,16]` per chunk
- Each chunk is a separate Node3D with MeshInstance3D child

### 4.2 Collision System

- Each chunk has its own **StaticBody3D**
- Use **ConcavePolygonShape3D** generated from mesh vertices
- Collision regenerates alongside mesh when blocks change

### 4.3 Navigation System

**Per-chunk navigation:**
- Each chunk has its own **NavigationRegion3D** with procedural **NavigationMesh**
- When blocks change → regenerate that chunk's navmesh
- Regions auto-connect when edges are close enough (handled by NavigationServer3D)

**Critical synchronization requirement:**
```csharp
// After updating navmesh, MUST wait before pathfinding works
await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
```

**Key Godot classes:**
- `NavigationServer3D` - Singleton for all navigation operations
- `NavigationMesh` - Holds procedural navmesh vertex/polygon data
- `NavigationRegion3D` - Node wrapper (or use RID directly via server)
- `NavigationAgent3D` - Attached to colonists for pathfinding

**Known limitation (from community research):**
Standard NavMesh does NOT handle voxel terrain well:
- Agents can't step between different block heights
- No built-in jump/climb support
- [Forum discussion](https://forum.godotengine.org/t/voxel-game-navigation/106214) confirms this is a common issue

**Planned hybrid approach for colony sim:**
- Use NavigationServer3D for flat/gradual terrain (baseline pathfinding)
- Custom logic for: climbing ladders, jumping gaps, multi-level structures
- May need voxel-grid-based A* with height costs as alternative

### 4.4 NPC/Colonist System (Future)

- `NavigationAgent3D` for pathfinding
- Task queue system (ordered list of jobs)
- Needs/survival stats (hunger, rest, etc.)

### 4.5 Existing Solutions Research (Why Build From Scratch)

**Evaluated solutions (2026-02-01):**

| Solution | Language | Status | Why Not Used |
|----------|----------|--------|--------------|
| [Zylann's godot_voxel](https://github.com/Zylann/godot_voxel) | C++ | Active (Godot 4.5) | C# builds broken, overkill for our needs |
| [VoxelFactory](https://github.com/antopilo/VoxelFactory) | C# | Inactive (~2019) | No Godot 4 support, designed for voxel models not worlds |
| [Voxel Game Demo](https://godotengine.org/asset-library/asset/2755) | GDScript | Godot 4.2 | Good reference, but GDScript only |
| [Chunkee](https://github.com/ZachJW34/chunkee) | Rust | Active | Rust-based, not C# |

**Decision: Build custom chunk system from scratch**
- Full control over C# implementation
- Understand every line of code
- Navigation will need custom work anyway (see below)
- ~200-300 lines for basic chunk rendering
- Can reference Voxel Game Demo's threading approach

**Reference materials:**
- [Godot ArrayMesh Tutorial](https://docs.godotengine.org/en/stable/tutorials/3d/procedural_geometry/arraymesh.html)
- [NavigationServer3D Article](https://godotengine.org/article/navigation-server-godot-4-0/)

---

## 5. Project File Structure

```
colonysim-3d/
├── project.godot
├── CLAUDE.md                    # This file
├── scripts/
│   ├── world/
│   │   ├── World.cs             # World manager, chunk loading/unloading
│   │   ├── Chunk.cs             # Single chunk: data, mesh, collision, nav
│   │   ├── ChunkMeshGenerator.cs # Procedural mesh from block data
│   │   ├── ChunkNavigation.cs   # Per-chunk navmesh generation
│   │   └── Block.cs             # Block type definitions
│   ├── colonist/
│   │   ├── Colonist.cs          # NPC base class
│   │   └── ColonistAI.cs        # Task execution, pathfinding
│   └── camera/
│       ├── RTSCamera.cs         # Top-down/isometric camera (future)
│       └── OrbitCamera.cs       # Debug camera that orbits target
├── scenes/
│   ├── main.tscn                # Entry point
│   ├── world/
│   │   └── Chunk.tscn           # Chunk prefab (if needed)
│   └── colonist/
│       └── Colonist.tscn        # Colonist prefab
├── resources/
│   └── blocks/                  # Block textures, materials
└── godot-docs-master/           # Godot 4.6 documentation (reference)
```

---

## 6. Implementation Phases

### Phase 1: Single Chunk Rendering (FIRST MILESTONE) ✅ COMPLETE
- [x] Create `Chunk.cs` with 16x16x16 block data array
- [x] Hardcode some blocks (e.g., fill bottom 4 layers with stone)
- [x] Generate ArrayMesh showing only exposed faces
- [x] Attach to MeshInstance3D and see cubes render
- **Success criteria:** Run game, see colored cubes

### Phase 2: Chunk Collision ✅ COMPLETE
- [x] Add StaticBody3D + collision shape to chunk
- [x] Test with a falling RigidBody3D or simple physics object
- **Success criteria:** Ball falls, lands on terrain, bounces into hole

### Phase 3: Multi-Chunk World ✅ COMPLETE
- [x] Create `World.cs` to manage multiple chunks
- [x] Load chunks in a small area (e.g., 3x3 chunks)
- [x] Basic camera to view the world
- **Success criteria:** 3x3 chunk terrain renders with seamless boundaries

### Phase 4: Block Modification ✅ COMPLETE
- [x] Implement `SetBlock(Vector3I position, BlockType type)`
- [x] Trigger mesh + collision regeneration for affected chunk
- [x] Test: click to remove/add blocks
- **Success criteria:** Left-click removes, right-click places, instant updates

### Phase 5: Per-Chunk Navigation ✅ COMPLETE
- [x] Generate NavigationMesh from walkable surfaces (top of solid blocks)
- [x] Create NavigationRegion3D per chunk
- [x] Regenerate navmesh when blocks change
- [x] Edge connection margin configured for chunk boundaries
- **Success criteria:** NavMesh on grass surfaces, updates with block changes

### Phase 6: Basic Colonist ✅ COMPLETE
- [x] Spawn a simple colonist with NavigationAgent3D
- [x] Click to set destination, colonist pathfinds and walks there
- [x] Handle navigation across chunk boundaries
- **Success criteria:** Right-click moves colonist, pathfinds across chunks

### Phase 7: World Generation ✅ COMPLETE
- [x] Implement simple noise-based terrain generation
- [x] Replace hardcoded blocks with procedural generation
- [x] Basic height variation using FastNoiseLite
- **Success criteria:** Rolling hills, seamless chunk boundaries

---

## 7. Key Godot API Reference (C#)

### Procedural Mesh Generation

```csharp
// Build surface arrays
var surfaceArray = new Godot.Collections.Array();
surfaceArray.Resize((int)Mesh.ArrayType.Max);

List<Vector3> vertices = new();
List<Vector3> normals = new();
List<Vector2> uvs = new();
List<int> indices = new();

// ... populate with face data ...

surfaceArray[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
surfaceArray[(int)Mesh.ArrayType.Normal] = normals.ToArray();
surfaceArray[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();

// Create mesh
var mesh = new ArrayMesh();
mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
meshInstance.Mesh = mesh;
```

### Dynamic Navigation (per chunk)

```csharp
// Create region and attach to world map
Rid region = NavigationServer3D.RegionCreate();
Rid map = GetWorld3D().NavigationMap;
NavigationServer3D.RegionSetMap(region, map);
NavigationServer3D.RegionSetTransform(region, GlobalTransform);

// Build navmesh from walkable surfaces
var navMesh = new NavigationMesh();
navMesh.Vertices = walkableVertices;  // PackedVector3Array
navMesh.AddPolygon(polygonIndices);   // int[] for each polygon

// Apply to region
NavigationServer3D.RegionSetNavigationMesh(region, navMesh);

// CRITICAL: Wait for sync before pathfinding works
await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
```

### Pathfinding Query

```csharp
Rid map = GetWorld3D().NavigationMap;
Vector3[] path = NavigationServer3D.MapGetPath(
    map,
    startPosition,
    targetPosition,
    optimize: true
);
```

### Block Face Vertices (for mesh generation)

```csharp
// Cube face definitions (relative to block origin)
// Each face: 4 vertices, 2 triangles (6 indices)
// Winding order: clockwise when viewed from outside

// Example: Top face (+Y)
Vector3[] topFace = {
    new Vector3(0, 1, 0),
    new Vector3(0, 1, 1),
    new Vector3(1, 1, 1),
    new Vector3(1, 1, 0)
};
int[] topIndices = { 0, 1, 2, 0, 2, 3 };
Vector3 topNormal = Vector3.Up;
```

---

## 8. Known Gotchas & Warnings

1. **Navigation sync delay**
   - NavMesh changes don't apply until next physics frame
   - Always `await physics_frame` after updating navmesh before querying paths

2. **GridMap is NOT suitable**
   - Designed for static levels, expensive to update frequently
   - Use procedural ArrayMesh instead

3. **Chunk boundaries**
   - Navigation regions auto-connect when edges align
   - Must ensure walkable surfaces align at chunk edges

4. **Mesh winding order**
   - Triangles must be clockwise (when viewed from outside) for correct normals
   - Wrong winding = faces render inside-out or culled

5. **Large worlds**
   - Need chunk streaming (load/unload based on camera/player position)
   - Don't load entire world at once

6. **Coordinate systems**
   - Block position within chunk: `Vector3I(0-15, 0-15, 0-15)`
   - World block position: `chunkPos * 16 + localPos`
   - Use `Vector3I` for block coords, `Vector3` for world positions

7. **Winding order fix**
   - Our face vertices are defined counter-clockwise (Godot expects clockwise)
   - **Fix:** Reverse the triangle indices from `{ 0, 1, 2, 0, 2, 3 }` to `{ 0, 2, 1, 0, 3, 2 }`
   - This ensures correct winding + correct normals for proper lighting
   - Use standard `CullMode.Back` (not Front workaround)

---

## 9. Documentation References

All Godot 4.6 docs are available locally at:
```
E:\hobbies\programming\godot\colonysim-3d\godot-docs-master\
```

**Key documentation files:**

| Topic | Path |
|-------|------|
| ArrayMesh (procedural geometry) | `tutorials/3d/procedural_geometry/arraymesh.rst` |
| SurfaceTool | `classes/class_surfacetool.rst` |
| NavigationServer3D | `classes/class_navigationserver3d.rst` |
| NavigationMesh | `classes/class_navigationmesh.rst` |
| Navigation regions | `tutorials/navigation/navigation_using_navigationregions.rst` |
| Navigation sync | `tutorials/navigation/navigation_using_navigationservers.rst` |
| NavigationAgent3D | `classes/class_navigationagent3d.rst` |

---

## 10. Session Notes

*Use this section to log important decisions or discoveries during development sessions.*

### Session 1 (Initial Setup)
- Created project with Godot 4.6 Mono
- Configured Jolt Physics and D3D12
- Researched voxel approaches: decided on ArrayMesh over GridMap
- Established 16x16x16 chunk size
- Documented navigation sync requirements
- **Evaluated existing voxel solutions:**
  - Zylann's godot_voxel: too complex, C# broken
  - VoxelFactory: outdated, no Godot 4
  - Voxel Game Demo: GDScript only, good reference
  - **Decision: build from scratch for full control**
- **Navigation research:**
  - Standard NavMesh has voxel height-step issues
  - Will need hybrid/custom approach for colony sim

### Session 2 (Phase 1 Implementation)
- **Implemented Phase 1: Single Chunk Rendering**
- Created `scripts/world/Block.cs` - BlockType enum (Air, Stone, Dirt, Grass) with color/solid metadata
- Created `scripts/world/ChunkMeshGenerator.cs` - Static mesh generator with face culling
  - 6-face vertex data with clockwise winding order
  - Only renders exposed faces (adjacent to Air blocks)
  - Separate mesh surfaces per block type for different materials
- Created `scripts/world/Chunk.cs` - Chunk node class
  - 16x16x16 BlockType[,,] storage
  - MeshInstance3D child for rendering
  - FillTestData() creates terrain with a hole to verify face culling
- Updated `scenes/main.tscn` with Camera3D, DirectionalLight3D, and Chunk node
- Created `colonysim_3D.csproj` for C# build
- **Build: 0 errors, 0 warnings**
- **Runtime: Godot 4.6 runs scene successfully on RTX 3080**
- **Winding order issue discovered and fixed:**
  - Chunk appeared hollow - only back faces were rendering
  - Root cause: vertex winding is counter-clockwise, Godot expects clockwise
  - Initial workaround: CullMode.Front (caused lighting issues - normals pointed wrong way)
  - **Proper fix:** Reversed triangle indices from `{ 0, 1, 2, 0, 2, 3 }` to `{ 0, 2, 1, 0, 3, 2 }`
  - This keeps vertices as-is but changes winding order, so normals point outward correctly
- Created `scripts/camera/OrbitCamera.cs` - Debug camera that orbits around a target point

### Session 3 (Phase 2 Implementation)
- **Implemented Phase 2: Chunk Collision**
- Added collision system to `Chunk.cs`:
  - `StaticBody3D` + `CollisionShape3D` child nodes
  - `InitializeCollision()` method (mirrors `InitializeMeshInstance()` pattern)
  - `RegenerateCollision()` method extracts vertices from mesh, expands indices to flat list
  - Uses `ConcavePolygonShape3D` with `BackfaceCollision = true`
- Updated `main.tscn` with test ball (RigidBody3D + SphereShape3D)
- **Test results:**
  - Ball falls from height and lands on grass surface ✅
  - Ball does NOT fall through terrain ✅
  - Ball falls into hole and collides with pit walls ✅
- **Key technical detail:** ConcavePolygonShape3D requires flat vertex list (no indices)
  - Our mesh uses indexed triangles: `vertices[] + indices[]`
  - Collision needs: sequential vertex triples `[v0, v1, v2, v3, v4, v5, ...]`
  - Solution: Loop through indices, append `vertices[indices[i]]`

### Session 4 (Phase 3 Implementation)
- **Implemented Phase 3: Multi-Chunk World**
- Created `scripts/world/World.cs` - World manager with `[Tool]` attribute
  - `Dictionary<Vector3I, Chunk>` for chunk storage
  - `LoadChunk(Vector3I)` / `UnloadChunk(Vector3I)` methods
  - `LoadChunkArea(center, radius)` for loading chunk grids
  - `ChunkToWorldPosition()` / `WorldToChunkCoord()` conversion helpers
  - `GenerateChunkTerrain()` fills chunks with stone/dirt/grass layers
- Modified `Chunk.cs`:
  - Removed `FillTestData()` call from `_Ready()` - World now handles terrain
  - Added `ForceRegenerateMesh()` public method for World to trigger mesh generation
- Updated `main.tscn`:
  - Replaced single Chunk with World node
  - TestBall repositioned to (24.5, 15, 24.5) - center of 3x3 area
- Updated `OrbitCamera.cs` for larger view:
  - Target: (24, 6, 24) - center of 3x3 chunk area
  - Distance: 60 (increased from 25)
- **Editor preview:** Added `chunk.Owner = GetTree().EditedSceneRoot` for chunks to appear in scene tree
- **Positioning math:**
  - Chunk (0,0,0) → World pos (0, 0, 0)
  - Chunk (1,0,0) → World pos (16, 0, 0)
  - 3x3 grid = 48×48 blocks total
- **Test results:**
  - 9 chunks load and render seamlessly ✅
  - Continuous grass surface across chunk boundaries ✅
  - Ball lands on terrain and can roll across chunks ✅
  - Hole in center chunk (1,0,1) works correctly ✅

### Session 5 (Phase 4 Implementation)
- **Implemented Phase 4: Block Modification**
- Added world-space block methods to `World.cs`:
  - `WorldToChunkAndLocal(Vector3I)` - converts world block pos to chunk coord + local pos
  - `GetBlock(Vector3I)` - query blocks at world position
  - `SetBlock(Vector3I, BlockType)` - modify blocks and trigger chunk regeneration
- Created `scripts/interaction/BlockInteraction.cs`:
  - Mouse raycast using `Camera3D.ProjectRayOrigin/Normal()`
  - `PhysicsDirectSpaceState3D.IntersectRay()` for collision detection
  - Hit normal determines block position (offset into block or adjacent)
  - Left-click = remove block (set to Air)
  - Right-click = place dirt block adjacent to clicked face
- Added input actions to `project.godot`:
  - `click_left` - mouse button 1
  - `click_right` - mouse button 2
- Updated `main.tscn` with BlockInteraction node under World
- **Block position math:**
  - Remove: `(hitPos - hitNormal * 0.1).Floor()` - offset INTO the block
  - Place: `(hitPos + hitNormal * 0.1).Floor()` - offset to ADJACENT space
- **Test results:**
  - Left-click removes blocks instantly ✅
  - Right-click places dirt blocks on any face ✅
  - Mesh + collision update in real-time ✅
  - Works across chunk boundaries ✅

### Session 6 (Phase 5 Implementation)
- **Implemented Phase 5: Per-Chunk Navigation**
- Created `scripts/world/ChunkNavigation.cs`:
  - Static class with `GenerateNavigationMesh(Chunk)` method
  - Iterates all blocks, finds walkable surfaces (solid block + air above)
  - Creates quad polygons at top of each walkable block (y + 1)
  - Returns NavigationMesh with vertices and polygon indices
- Modified `Chunk.cs`:
  - Added `NavigationRegion3D _navigationRegion` field
  - Added `InitializeNavigation()` method (same pattern as mesh/collision)
  - Added `RegenerateNavigation()` method - generates navmesh from block data
  - Updated `RegenerateMesh()` to call `RegenerateNavigation()` after collision
- Modified `World.cs`:
  - Added edge connection margin: `NavigationServer3D.MapSetEdgeConnectionMargin(mapRid, 1.0f)`
  - Ensures NavigationRegion3D nodes connect at chunk boundaries
- **Navigation generation flow:**
  - `SetBlock()` → `_isDirty = true`
  - `ForceRegenerateMesh()` → `RegenerateMesh()`
  - `RegenerateMesh()` → mesh + collision + navigation regenerated
- **Walkable surface logic:**
  - Block at (x, y, z) is walkable if: `IsSolid(block) && !IsSolid(blockAbove)`
  - Creates quad polygon at height `y + 1` (top of the solid block)
- **To verify in Godot:**
  - Enable Debug > Visible Navigation to see blue navmesh overlay
  - Each chunk should have ChunkNavigation child node
  - NavMesh should span all 9 chunks seamlessly

### Session 7 (Phase 6 Implementation)
- **Implemented Phase 6: Basic Colonist**
- Created `scripts/colonist/Colonist.cs`:
  - `CharacterBody3D` with `NavigationAgent3D` integration
  - `MovementSpeed` export property (default 5.0 units/sec)
  - `SetDestination(Vector3)` method sets navigation target
  - `_PhysicsProcess` moves colonist along path using `MoveAndSlide()`
  - `OnTargetReached()` callback logs arrival
- Created `scenes/colonist/Colonist.tscn`:
  - CharacterBody3D root node in "colonists" group
  - CapsuleShape3D for collision (radius=0.3, height=1.6)
  - CapsuleMesh with blue material for visibility
  - NavigationAgent3D with path/target distance = 0.5
- Modified `scripts/interaction/BlockInteraction.cs`:
  - Added `_selectedColonist` field for colonist reference
  - Added `FindColonist()` deferred method (waits for scene tree)
  - Right-click now moves colonist instead of placing blocks
  - Left-click still removes blocks
- Updated `scenes/main.tscn`:
  - Added Colonist scene instance at (24.5, 7, 24.5)
- **Controls:**
  - Left-click = Remove block
  - Right-click = Move colonist to clicked location
- **Test results:**
  - Blue capsule colonist spawns on grass ✅
  - Right-click moves colonist to target ✅
  - Pathfinding works across chunk boundaries ✅
  - Console shows movement/arrival messages ✅

### Session 8 (Phase 7 Implementation)
- **Implemented Phase 7: World Generation**
- Created `scripts/world/TerrainGenerator.cs`:
  - Static class using FastNoiseLite for noise generation
  - `GetHeight(worldX, worldZ)` - returns terrain height at position
  - `GetBlockType(worldX, worldY, worldZ)` - returns block type based on height
  - Parameters: BaseHeight=2, HeightVariation=10, StoneDepth=3
  - Noise settings: SimplexSmooth, Frequency=0.05, 4 octaves, Seed=12345
- Modified `scripts/world/World.cs`:
  - Replaced hardcoded `GenerateChunkTerrain()` with noise-based generation
  - Uses world coordinates for noise sampling (seamless chunk boundaries)
  - Removed test hole (no longer needed)
- **Terrain generation logic:**
  - Height = BaseHeight + (normalized_noise * HeightVariation) → 2-12 blocks
  - Y >= surfaceHeight: Air
  - Y == surfaceHeight - 1: Grass
  - Y >= surfaceHeight - 3: Dirt
  - Y < surfaceHeight - 3: Stone
- **Test results:**
  - Terrain has hills and valleys ✅
  - Grass/dirt/stone layers work correctly ✅
  - Chunk boundaries are seamless ✅
  - Navigation mesh follows terrain contours ✅
  - Colonist pathfinding works on varied terrain ✅
  - Screenshot confirmed: rolling hills with proper layers visible

---

*Last updated: 2026-02-01*
