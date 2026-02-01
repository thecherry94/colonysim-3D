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
│   │   └── Block.cs             # Block type definitions
│   ├── navigation/
│   │   └── ChunkNavigation.cs   # Per-chunk navmesh generation
│   ├── colonist/
│   │   ├── Colonist.cs          # NPC base class
│   │   └── ColonistAI.cs        # Task execution, pathfinding
│   └── camera/
│       └── RTSCamera.cs         # Top-down/isometric camera
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

### Phase 1: Single Chunk Rendering (FIRST MILESTONE)
- [ ] Create `Chunk.cs` with 16x16x16 block data array
- [ ] Hardcode some blocks (e.g., fill bottom 4 layers with stone)
- [ ] Generate ArrayMesh showing only exposed faces
- [ ] Attach to MeshInstance3D and see cubes render
- **Success criteria:** Run game, see colored cubes

### Phase 2: Chunk Collision
- [ ] Add StaticBody3D + collision shape to chunk
- [ ] Test with a falling RigidBody3D or simple physics object

### Phase 3: Multi-Chunk World
- [ ] Create `World.cs` to manage multiple chunks
- [ ] Load chunks in a small area (e.g., 3x3 chunks)
- [ ] Basic camera to view the world

### Phase 4: Block Modification
- [ ] Implement `SetBlock(Vector3I position, BlockType type)`
- [ ] Trigger mesh + collision regeneration for affected chunk
- [ ] Test: click to remove/add blocks

### Phase 5: Per-Chunk Navigation
- [ ] Generate NavigationMesh from walkable surfaces (top of solid blocks)
- [ ] Create NavigationRegion3D per chunk
- [ ] Regenerate navmesh when blocks change
- [ ] Verify regions connect across chunk boundaries

### Phase 6: Basic Colonist
- [ ] Spawn a simple colonist with NavigationAgent3D
- [ ] Click to set destination, colonist pathfinds and walks there
- [ ] Handle navigation across chunk boundaries

### Phase 7: World Generation
- [ ] Implement simple noise-based terrain generation
- [ ] Replace hardcoded blocks with procedural generation
- [ ] Basic biome or height variation

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

---

*Last updated: 2026-02-01*
