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
├── CLAUDE.md                     # This file (project bible)
├── LESSONS_LEARNED.md            # Post-mortems and debugging notes
├── scripts/
│   ├── world/
│   │   ├── World.cs              # World manager, chunk loading/unloading
│   │   ├── Chunk.cs              # Single chunk: data, mesh, collision, nav, links
│   │   ├── ChunkMeshGenerator.cs # Procedural mesh from block data
│   │   ├── ChunkNavigation.cs    # NavMesh + NavigationLink generation
│   │   ├── TerrainGenerator.cs   # FastNoiseLite procedural terrain
│   │   └── Block.cs              # Block type definitions
│   ├── colonist/
│   │   └── Colonist.cs           # CharacterBody3D + NavigationAgent3D + jump
│   ├── interaction/
│   │   └── BlockInteraction.cs   # Mouse raycast, block removal, colonist control
│   └── camera/
│       └── OrbitCamera.cs        # Debug camera that orbits target
├── scenes/
│   ├── main.tscn                 # Entry point
│   └── colonist/
│       └── Colonist.tscn         # Colonist prefab (CharacterBody3D)
└── godot-docs-master/            # Godot 4.6 documentation (reference)
```

---

## 6. Foundation Phases (COMPLETE)

> **All 7 foundation phases are complete.** The voxel engine core is functional.

| Phase | Description | Status |
|-------|-------------|--------|
| 1. Single Chunk Rendering | ArrayMesh generation, face culling | ✅ |
| 2. Chunk Collision | ConcavePolygonShape3D per chunk | ✅ |
| 3. Multi-Chunk World | World manager, 3x3 chunk loading | ✅ |
| 4. Block Modification | Click to add/remove blocks | ✅ |
| 5. Per-Chunk Navigation | NavigationRegion3D + NavMesh | ✅ |
| 6. Basic Colonist | CharacterBody3D + NavigationAgent3D | ✅ |
| 7. World Generation | FastNoiseLite procedural terrain | ✅ |

---

## 7. Feature Enhancements

### Height Traversal (1-Block Jumping) ✅ COMPLETE
- [x] NavigationLink3D connects walkable surfaces at different heights
- [x] Colonist has gravity and jump physics
- [x] `LinkReached` signal triggers jump when going up
- [x] Falling down handled by gravity (no jump needed)
- **How it works:**
  - `ChunkNavigation.FindHeightLinks()` detects adjacent blocks with 1-block height difference
  - `Chunk.RegenerateNavigationLinks()` creates bidirectional NavigationLink3D nodes
  - `Colonist.OnLinkReached()` triggers `_shouldJump = true` when going up
  - Horizontal momentum continues during jump → parabolic arc to upper platform

---

## 8. Future Development Roadmap

### World & Terrain
- [ ] **More Block Types** - Wood, ore, sand, water, etc.
- [ ] **Better Terrain Generation** - Caves, overhangs, ore veins
- [ ] **Biomes** - Different terrain styles based on location
- [ ] **Chunk Streaming** - Load/unload chunks based on camera position
- [ ] **Vertical Chunks** - Multiple Y-level chunks for taller worlds

### Camera & Controls
- [ ] **RTS Camera** - Top-down/isometric view with pan, zoom, rotate
- [ ] **Block Placement** - Re-enable placing blocks (currently only removal)
- [ ] **Selection System** - Click to select colonists, blocks, areas

### Colonist & AI (Later)
- [ ] **Multiple Colonists** - Spawn and manage several colonists
- [ ] **Task System** - Jobs like mine, build, haul
- [ ] **Colonist Needs** - Hunger, rest, mood
- [ ] **Inventory/Resources** - Blocks drop items, colonists carry them

### Infrastructure (Later)
- [ ] **Save/Load** - Serialize world and colonist state
- [ ] **UI System** - Menus, status panels, notifications

---

## 9. Key Godot API Reference (C#)

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

## 10. Known Gotchas & Warnings

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

## 11. Documentation References

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

## 12. Godot MCP Server Integration

**Available:** Godot MCP server is available and provides specialized tools for Godot project management.

### 12.1 When to Use MCP vs Default Tools

**Use Godot MCP Server for:**
- **Project management operations** - Version checks, project info, listing projects
- **Running/testing the game** - `run_project`, `stop_project`, `get_debug_output`
- **Scene file operations** - Creating new scenes, adding nodes, saving scenes
- **Visual assets** - Loading sprites into Sprite2D nodes
- **Editor operations** - Launching Godot editor for the project
- **Complex scene editing** - When you need to work with scene structure programmatically

**Use Default Tools (Read/Write/Edit) for:**
- **C# script files** - Always use Read/Edit/Write for .cs files
  - MCP server doesn't handle C# script logic
  - Direct file editing gives better control and context
- **Configuration files** - project.godot, .csproj, etc.
- **Reading scene files** - Use Read to examine .tscn structure
- **Documentation** - Reading/writing markdown files
- **Quick file searches** - Glob/Grep for finding files and code

### 12.2 Available MCP Functions

| Function | Use Case | Example |
|----------|----------|---------|
| `get_godot_version` | Verify Godot installation | Check if 4.6 is available |
| `list_projects` | Find Godot projects in directories | Scan for project.godot files |
| `get_project_info` | Get metadata about this project | Check Godot version, renderer |
| `launch_editor` | Open project in Godot editor | Visual editing session |
| `run_project` | Execute the game | Test gameplay changes |
| `stop_project` | Terminate running game | Stop after testing |
| `get_debug_output` | Capture console output | Review errors/warnings |
| `create_scene` | Generate new .tscn files | Create new game objects |
| `add_node` | Add nodes to existing scenes | Build scene hierarchy |
| `load_sprite` | Assign textures to Sprite2D | Add visual assets |
| `save_scene` | Persist scene changes | Save after modifications |
| `export_mesh_library` | Convert scenes to MeshLibrary | For GridMap usage (if needed) |
| `get_uid` | Get file UID (Godot 4.4+) | Reference tracking |
| `update_project_uids` | Resave resources with UIDs | Fix reference issues |

### 12.3 Recommended Workflow

**For testing changes:**
```
1. Edit C# scripts with Read/Edit/Write tools
2. Use `run_project` to launch the game
3. Use `get_debug_output` to check for errors
4. Use `stop_project` when done testing
```

**For scene modifications:**
```
1. Read .tscn file to understand structure
2. Either:
   a) Edit .tscn directly with Edit tool (for simple property changes)
   b) Use MCP `add_node`/`save_scene` (for complex structural changes)
```

**For project information:**
```
1. Use `get_project_info` to verify project configuration
2. Use `get_godot_version` to confirm Godot installation
```

### 12.4 Testing Protocol

When implementing or modifying features:

1. **Always test in-engine after code changes**
   - Use `run_project` to verify behavior
   - Check `get_debug_output` for errors/warnings
   - Don't assume code works without runtime verification

2. **Prefer running over reading debug output**
   - Running provides immediate visual feedback
   - Debug output is good for diagnosing issues
   - Stop cleanly with `stop_project` before making more changes

3. **Use editor for complex visual work**
   - Launch with `launch_editor` for materials, lighting, particle effects
   - Better for node positioning and visual debugging
   - Return to code editing after visual adjustments

### 12.5 Current Project Path

```
C:\hobbies\Godot\colonysim-3D
```

All MCP functions should use this as the `projectPath` parameter.

### 12.6 Debugging & Logging Strategy

**Progressive debugging approach - escalate strategically, not randomly.**

#### Level 0: Basic Development Logging

**Normal development logging is fine and encouraged:**

```csharp
// ✅ GOOD: Basic operation logging during development
public void SetDestination(Vector3 target)
{
    GD.Print($"[Colonist] Setting destination: {target}");
    _navigationAgent.TargetPosition = target;
}

// ✅ GOOD: Log important state changes
public void SetBlock(Vector3I worldPos, BlockType blockType)
{
    GD.Print($"[World] SetBlock at {worldPos}: {blockType}");
    // ... set block logic ...
}

// ✅ GOOD: Log key events
private void OnTargetReached()
{
    GD.Print("[Colonist] Target reached");
}
```

**When debugging, start here:**
1. Run with `run_project` and observe behavior
2. Check `get_debug_output` for existing logs and errors
3. Read the relevant code carefully
4. Check for common issues:
   - Null references (uninitialized nodes)
   - Wrong coordinate systems (world vs local vs chunk)
   - Missing awaits (navigation sync, physics frames)
   - Incorrect node paths or scene structure

**If existing logs aren't enough, escalate to Level 1.**

#### Level 1: Targeted Diagnostic Logging

**If existing logs aren't enough, add TARGETED logs at specific decision points:**

```csharp
// ✅ GOOD: Add conditional branch logging
private void RegenerateMesh()
{
    if (!_isDirty)
    {
        GD.Print($"[Chunk {ChunkCoord}] Skipping regeneration - not dirty");  // ADD THIS
        return;
    }
    GD.Print($"[Chunk {ChunkCoord}] Regenerating mesh with {_blocks.Length} blocks");
    // ... mesh generation ...
}

// ✅ GOOD: Add intermediate calculation logging
public Vector3I WorldToChunkCoord(Vector3I worldPos)
{
    var result = new Vector3I(
        Mathf.FloorToInt(worldPos.X / 16f),
        Mathf.FloorToInt(worldPos.Y / 16f),
        Mathf.FloorToInt(worldPos.Z / 16f)
    );
    GD.Print($"[World] WorldToChunkCoord: {worldPos} → {result}");  // ADD THIS
    return result;
}

// ❌ BAD: Panic logging hot paths
public void _Process(double delta)
{
    GD.Print($"Process called delta={delta}"); // Called 60x per second!
    // ...
}
```

**Strategic log locations:**
- Method entry points with parameters
- State changes (dirty flags, mode switches)
- Conditional branches (which path was taken?)
- Return values from important calculations
- Before/after critical operations (mesh regeneration, pathfinding)

**When to use:**
- Function is being called but producing wrong results
- Need to verify which code path executes
- Need to see parameter values at runtime

#### Level 2: Data Flow Tracking

**For nasty bugs, add logs to trace data through the system:**

```csharp
// ✅ GOOD: Track coordinate transformations
public Vector3I WorldToChunkAndLocal(Vector3I worldPos, out Vector3I localPos)
{
    var chunkCoord = new Vector3I(
        Mathf.FloorToInt(worldPos.X / 16f),
        Mathf.FloorToInt(worldPos.Y / 16f),
        Mathf.FloorToInt(worldPos.Z / 16f)
    );
    localPos = worldPos - chunkCoord * 16;

    GD.Print($"[World] WorldPos {worldPos} → Chunk {chunkCoord}, Local {localPos}");
    return chunkCoord;
}

// ✅ GOOD: Track navmesh generation
var walkableSurfaces = FindWalkableSurfaces();
GD.Print($"[ChunkNav] Found {walkableSurfaces.Count} walkable surfaces");

var navMesh = GenerateNavigationMesh(walkableSurfaces);
GD.Print($"[ChunkNav] Generated navmesh: {navMesh.Vertices.Length} vertices, " +
         $"{navMesh.GetPolygonCount()} polygons");
```

**When to use:**
- Data is being transformed incorrectly
- Need to verify calculations at multiple stages
- Tracking objects through complex systems (block coords → world → chunk → local)

#### Level 3: Deep Inspection

**For truly nasty bugs, add detailed state dumps:**

```csharp
// ✅ GOOD: Comprehensive state logging
private void DebugDumpChunkState()
{
    GD.Print($"=== Chunk {ChunkCoord} State ===");
    GD.Print($"  IsDirty: {_isDirty}");
    GD.Print($"  MeshInstance: {_meshInstance != null}");
    GD.Print($"  CollisionShape: {_collisionShape != null}");
    GD.Print($"  NavigationRegion: {_navigationRegion != null}");
    GD.Print($"  NavigationLinks: {_navigationLinks.Count}");

    int solidBlocks = 0;
    for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                if (Block.IsSolid(_blocks[x, y, z])) solidBlocks++;

    GD.Print($"  Solid blocks: {solidBlocks} / {16*16*16}");
    GD.Print($"========================");
}

// Call only when needed, not every frame
if (GD.Print("regenerate failed"))
    DebugDumpChunkState();
```

**When to use:**
- System state is corrupted or inconsistent
- Need to verify multiple related values at once
- Bug appears intermittently and need full context when it occurs

#### Godot Logging Tools

```csharp
// Standard output (shows in Output panel)
GD.Print("Info message");
GD.Print($"Formatted: {value}");

// Warnings (yellow in console)
GD.PushWarning("This shouldn't happen but isn't fatal");

// Errors (red in console, includes stack trace)
GD.PushError("Critical problem detected");

// Assertions (only in debug builds)
Debug.Assert(condition, "Condition should be true here");
```

#### Using get_debug_output

After running with logs:
```
1. Use `run_project` to start the game
2. Reproduce the bug
3. Use `get_debug_output` to capture console output
4. Analyze the log sequence to find the issue
5. Use `stop_project` when done
```

**What to look for in debug output:**
- Error messages (red) - immediate problems
- Warning messages (yellow) - potential issues
- Missing log messages - code not executing as expected
- Unexpected log sequences - wrong execution order
- Repeated messages - infinite loops or excessive calls

#### Best Practices

**DO:**
- ✅ Add context to logs: `[Colonist]`, `[Chunk (1,0,1)]`, `[World]`
- ✅ Log meaningful data: coordinates, counts, state changes
- ✅ Use descriptive messages: "Setting destination to {pos}" not "pos={pos}"
- ✅ Keep basic logging for key operations (helps with future debugging)
- ✅ Remove or comment out detailed diagnostic logs once bug is fixed
- ✅ Use PushError for serious problems, PushWarning for concerns

**DON'T:**
- ❌ Log in _Process or _PhysicsProcess unless conditionally (called 60x per second!)
- ❌ Panic-add random prints everywhere hoping something sticks
- ❌ Log without context: `GD.Print(x)` (what is x? where is this? why?)
- ❌ Leave excessive diagnostic logs that clutter output
- ❌ Log sensitive or massive data dumps that make output unreadable

#### Bug-Specific Strategies

| Bug Type | Logging Strategy |
|----------|------------------|
| **Null reference** | Log node initialization, check _Ready() order |
| **Wrong position/movement** | Log all coordinate transformations and conversions |
| **Pathfinding issues** | Log navmesh generation, path queries, agent state |
| **Mesh not updating** | Log dirty flag, regeneration calls, vertex counts |
| **Collision problems** | Log collision shape setup, body types, layers |
| **Performance issues** | Profile with Godot profiler, log counts (not individual items) |
| **Intermittent bugs** | Add state dumps when bug condition detected |

#### Example: Debugging a Nasty Pathfinding Bug

```csharp
// Level 0: Check existing logs
// → Colonist not moving to destination
// → "SetDestination called: (10, 5, 15)" appears in logs
// → But no "Target reached" message

// Level 1: Add diagnostic logs to narrow down the problem
public void SetDestination(Vector3 target)
{
    GD.Print($"[Colonist] SetDestination called: {target}");
    _navigationAgent.TargetPosition = target;
}
// → Log shows method IS being called with correct target

// Level 2: Track data flow
public override void _PhysicsProcess(double delta)
{
    if (!_navigationAgent.IsNavigationFinished())
    {
        var nextPos = _navigationAgent.GetNextPathPosition();
        var currentPos = GlobalPosition;
        GD.Print($"[Colonist] Moving: {currentPos} → {nextPos}, " +
                 $"Distance: {currentPos.DistanceTo(nextPos)}");
        // ...
    }
}
// → Log shows nextPos is always equal to currentPos!

// Level 3: Deep inspection
GD.Print($"[Colonist] Agent state:");
GD.Print($"  IsNavigationFinished: {_navigationAgent.IsNavigationFinished()}");
GD.Print($"  PathDesiredDistance: {_navigationAgent.PathDesiredDistance}");
GD.Print($"  TargetDesiredDistance: {_navigationAgent.TargetDesiredDistance}");
GD.Print($"  TargetPosition: {_navigationAgent.TargetPosition}");
GD.Print($"  Navigation map: {_navigationAgent.GetNavigationMap()}");
// → Discovers navigation map RID is invalid!
// → Root cause: NavigationAgent3D not properly connected to navigation map

// Fix: Ensure agent is added to scene tree before setting destination
// Remove all debug logs after fix
```

---

## 13. Session Notes

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

### Session 9 (Height Traversal Feature)
- **Implemented 1-block height traversal (jumping)**
- Modified `scripts/colonist/Colonist.cs`:
  - Added `JumpVelocity` (6.0) and `Gravity` (20.0) export properties
  - Added gravity in `_PhysicsProcess`: `velocity.Y -= Gravity * delta` when not on floor
  - Connected `LinkReached` signal to detect height transitions
  - Trigger jump when link exit is higher than entry: `_shouldJump = true`
  - Horizontal movement continues during jump → parabolic arc
- Modified `scripts/world/ChunkNavigation.cs`:
  - Added `HeightLink` struct with LowerPosition/UpperPosition
  - Added `FindHeightLinks(Chunk)` method
  - Checks 4 horizontal neighbors for walkable surface 1 block higher
- Modified `scripts/world/Chunk.cs`:
  - Added `List<NavigationLink3D> _navigationLinks` field
  - Added `RegenerateNavigationLinks()` method
  - Creates bidirectional links with `EnterCost = 0.5f` (prefer flat paths)
  - Links regenerate when blocks change
- **How NavigationLink3D works:**
  - Bridges disconnected NavMesh polygons at different heights
  - NavigationServer3D includes links in pathfinding graph
  - Agent path can now include link waypoints
  - `LinkReached` signal fires when agent approaches link
- **Movement physics:**
  - Going UP: Jump triggered, horizontal momentum carries colonist in arc
  - Going DOWN: Gravity handles fall naturally (no jump needed)

---

*Last updated: 2026-02-02*
