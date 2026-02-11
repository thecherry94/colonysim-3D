# colonysim-3d — Project Bible

> **Read this entire document before writing any code.** It contains hard-won lessons and architectural decisions that will save you from known dead ends.

---

## 1. Game Vision

**Concept:** Minecraft-style voxel world + Rimworld/Dwarf Fortress colony management.

**Core loop:**
1. Player views the world from an RTS/management camera (top-down or isometric)
2. Player designates tasks: mine here, build there, haul this
3. Colonists (AI-controlled pawns) autonomously execute tasks via pathfinding
4. Manage survival, resources, and colony growth

**Key constraints:**
- The player does NOT directly control a character. This is NOT a first-person game.
- Colonists are autonomous agents. They receive task assignments and figure out pathing/execution.
- The world is fully destructible/constructible — any block can be mined or placed.

---

## 2. Technical Stack

| Component | Choice | Notes |
|-----------|--------|-------|
| Engine | Godot 4.6 (Mono/.NET) | C# scripting via .NET |
| Language | C# | All game logic in C# |
| Physics | Jolt Physics | Set in project.godot |
| Renderer | Forward Plus | Default Godot 4 renderer |
| Graphics API | D3D12 | Windows target |
| Assembly | `colonysim_3D` | .csproj assembly name |

---

## 3. Architecture Decisions

These decisions were reached through research and prototyping. They are **final** — do not revisit unless you have a concrete reason with evidence.

### 3.1 Voxel/Chunk System: Procedural ArrayMesh (NOT GridMap)

**Use procedural ArrayMesh per chunk.** Do NOT use Godot's GridMap.

Why GridMap fails:
- GridMap is designed for static level design (hand-placed tiles in editor)
- It regenerates internal octants on every cell change — extremely expensive for dynamic worlds
- No control over face culling, mesh optimization, or collision generation

Why ArrayMesh works:
- Full control over vertex/normal/index buffers
- Can cull internal faces (only render block faces adjacent to air)
- Chunk isolation: modifying one block only regenerates that chunk's mesh
- Can extend to greedy meshing, texture atlases, LOD later

**Chunk size: 16x16x16 blocks.** This is a standard choice that balances mesh rebuild cost vs. chunk count.

### 3.2 Collision: ConcavePolygonShape3D Per Chunk

Each chunk gets its own `StaticBody3D` with a `ConcavePolygonShape3D`. The collision mesh is built from the same vertex data as the render mesh, expanded from indexed triangles to a flat vertex list (ConcavePolygonShape3D requires sequential vertex triples, not indexed buffers).

Regenerate collision whenever the render mesh changes.

### 3.3 Navigation: Voxel Grid A* (NOT NavigationServer3D)

**Implement A* pathfinding directly on the block grid.** Do NOT use Godot's NavigationServer3D, NavigationMesh, NavigationRegion3D, NavigationLink3D, or NavigationAgent3D.

Why NavigationServer3D fails for voxel worlds:
- **Wall clipping:** NavMesh generates paths along polygon edges. In a block world, this means paths run flush against walls. A colonist following such a path clips into the wall because there's almost no lateral force to push it away. This is a fundamental geometric mismatch, not a tuning problem.
- **Height transitions:** NavMesh cannot natively handle 1-block step-ups. NavigationLink3D exists but requires signal-based jump timing that is unreliable — the `LinkReached` signal fires at unpredictable moments during `MoveAndSlide()`.
- **Sync issues:** After modifying a NavigationRegion3D's mesh, the NavigationServer doesn't update until the next physics frame. If you query a path in the same frame you updated the mesh, you get stale results. This creates race conditions on every block change.
- **Cross-chunk links:** NavigationLink3D works within a single chunk, but linking across chunk boundaries requires World-level orchestration that adds enormous complexity.
- **Overall:** NavigationServer3D was designed for smooth, pre-baked terrain — not dynamic voxel grids.

Why voxel grid A* works:
- Your world IS a grid. Every walkable surface is at a known integer coordinate. A* on this grid is the natural fit.
- Cross-chunk pathfinding is trivial: the A* algorithm queries `World.GetBlock()` which handles chunk lookups transparently.
- Paths route through **block centers** (X+0.5, Z+0.5), guaranteeing wall clearance.
- Height transitions are just neighbor connections with different costs — no special link nodes needed.
- No sync issues — pathfinding operates on block data directly, no intermediate representation.
- Implementation is ~200 lines of straightforward C#.

**Recommended A* design:**
- 4-connected neighbors (no diagonals) to avoid corner-clipping
- Neighbor types: flat walk (same Y), step up (Y+1), step down (Y-1)
- 2-high clearance checks at every destination (colonists are ~2 blocks tall)
- Move costs: flat=1.0, step down=1.2, step up=2.0 (prefers flat routes)
- Heuristic: Manhattan distance with Y weighted higher (~1.5x)
- Hard limit on nodes explored (~10,000) to prevent runaway searches on large worlds
- .NET's `PriorityQueue<TElement, TPriority>` is available and works fine

### 3.4 Colonist Movement: State Machine on CharacterBody3D

Use `CharacterBody3D` with `MoveAndSlide()` and a simple state machine:

**States:** `Idle → Walking → JumpingUp → Falling`
- **Walking:** Follow waypoint list. Move horizontally toward current waypoint's block center. Advance when close enough. If next waypoint is higher → transition to JumpingUp.
- **JumpingUp:** Apply upward velocity + continue horizontal movement. Transition back to Walking on landing. Use a grace timer (~0.1s) after jumping to avoid false `IsOnFloor()` detection.
- **Falling:** Gravity descent (step-down, or pushed off edge). Resume Walking on landing.

Add **stuck detection**: if the colonist makes no progress for ~2 seconds, clear the path and go idle (or repath).

**Capsule dimensions:** radius=0.3, height=1.6. This gives 0.2 units of wall clearance when the colonist is at a block center (0.5 - 0.3 = 0.2).

### 3.5 Why Build From Scratch (Not Use Existing Voxel Libraries)

Evaluated options:
- **Zylann's godot_voxel** (C++): Powerful but C# bindings are broken, and it's overkill for colony sim needs
- **VoxelFactory** (C#): Abandoned, no Godot 4 support
- **Godot Voxel Game Demo** (GDScript): Good reference for threading/chunking patterns, but GDScript only
- **Chunkee** (Rust): Wrong language

Building from scratch gives full control over the C# implementation and keeps the codebase understandable. The core chunk rendering is only ~200-300 lines.

---

## 4. Coordinate Systems

Three coordinate systems are used throughout. Mixing them up causes bugs.

| System | Type | Example | Used For |
|--------|------|---------|----------|
| **Local block** | `int (0-15)` | `(3, 7, 12)` | Indexing into `Chunk._blocks[x,y,z]` |
| **World block** | `Vector3I` | `(19, 7, 28)` | Identifying any block globally |
| **World position** | `Vector3` | `(19.5, 8.0, 28.5)` | Colonist position, camera, raycasts |

**Conversions:**
- World block → Chunk coord: `chunkCoord = FloorToInt(worldBlock / 16)` (per axis)
- World block → Local block: `local = ((worldBlock % 16) + 16) % 16` (handles negatives!)
- Local block → World block: `worldBlock = chunkCoord * 16 + local`
- Block center world position: `(worldBlockX + 0.5, worldBlockY + 1.0, worldBlockZ + 0.5)` — the +1 on Y is because the colonist stands ON TOP of the block

**Critical:** When converting to local coords, C# `%` operator returns negative values for negative inputs. Use the `((x % 16) + 16) % 16` pattern or you'll get array-out-of-bounds on negative world coordinates.

---

## 5. Lessons Learned (Critical — Read Before Coding)

These are concrete mistakes made during prototyping. Each one wasted significant time.

### 5.1 Mesh Winding Order

**Problem:** Chunk rendered hollow — only back faces were visible.

**Root cause:** Triangle indices were counter-clockwise. Godot expects clockwise winding (when viewed from outside) to determine the front face.

**Fix:** Reverse the triangle index order for quads:
- Wrong: `{ 0, 1, 2, 0, 2, 3 }` (CCW)
- Right: `{ 0, 2, 1, 0, 3, 2 }` (CW)

**Do NOT** use `CullMode.Front` as a workaround. It makes faces appear solid but normals point inward, breaking all lighting.

**Debug technique:** Set `CullMode.Disabled` temporarily. If the chunk looks solid with culling disabled, the winding is wrong. If it still has holes, the vertices are wrong.

### 5.2 NavigationServer3D Is Wrong for Voxel Worlds

See section 3.3. Do not attempt to use Godot's built-in navigation system. It was tried extensively and failed due to structural mismatches with voxel geometry. Use grid-based A* instead.

### 5.3 General Debugging Principles

1. **Understand the system before changing code.** Random trial-and-error on 6 cube faces with 4 vertices each produces chaos.
2. **Don't accept workarounds that "look right."** Verify the fix is actually correct (check lighting, normals, physics behavior — not just visual appearance).
3. **Change one thing at a time.** When debugging, isolate variables. Test with the simplest case (one block, one face).
4. **Search the web first.** Most Godot/voxel issues have known solutions.

---

## 6. Implementation Roadmap

Build in this order. Each phase should compile and run before moving to the next.

### Phase 1: Project Setup
- Create Godot 4.6 Mono project
- Configure Jolt Physics, D3D12 renderer
- Set up C# project structure (namespaces, folders)
- Add a Camera3D and DirectionalLight3D to the main scene

### Phase 2: Block Definitions
- Create `BlockType` enum: Air, Stone, Dirt, Grass
- Create `BlockData` static class: color per type, `IsSolid()` check
- Air = not solid, everything else = solid

### Phase 3: Single Chunk Rendering
- Create `Chunk` class (Node3D): holds `BlockType[16,16,16]` array
- Create `ChunkMeshGenerator`: builds ArrayMesh from block data
  - 6 face definitions (top, bottom, north, south, east, west)
  - Face culling: only render faces adjacent to Air blocks
  - Correct CW winding order (see Lesson 5.1)
  - Separate mesh surface per block type (for different materials/colors)
- Fill chunk with test data (flat terrain with a hole) to verify rendering
- **Verify:** Chunk renders as solid colored blocks with correct lighting

### Phase 4: Chunk Collision
- Add `StaticBody3D` + `CollisionShape3D` to each chunk
- Build `ConcavePolygonShape3D` from mesh vertex data
- **Verify:** Drop a RigidBody3D ball onto terrain — it should land and not fall through

### Phase 5: Multi-Chunk World
- Create `World` class (Node3D): manages `Dictionary<Vector3I, Chunk>`
- Chunk loading/unloading by chunk coordinate
- `LoadChunkArea(center, radius)` for loading grids of chunks
- World-space block access: `GetBlock(Vector3I)`, `SetBlock(Vector3I, BlockType)`
- Proper coordinate conversion: world → chunk + local (handle negatives!)
- Start with 3x3 chunk grid (48x48 blocks)
- **Verify:** 9 chunks render seamlessly, ball can roll across chunk boundaries

### Phase 6: Terrain Generation
- Create `TerrainGenerator` using Godot's `FastNoiseLite`
- Noise-based height map: `GetHeight(worldX, worldZ)` returns surface height
- Layer assignment: grass on top, dirt below, stone at depth
- Use world coordinates for noise sampling (seamless across chunks)
- **Verify:** Rolling hills with visible grass/dirt/stone layers

### Phase 7: Block Modification
- Create `BlockInteraction` class: mouse raycast → block identification
- Left-click: remove block (set to Air), regenerate chunk mesh + collision
- Hit normal math: offset slightly into the block to identify which block was clicked
- **Verify:** Click to dig holes in terrain, mesh and collision update in real-time

### Phase 8: A* Pathfinding
- Create `VoxelNode` struct: (X, Y, Z) block coordinate + `StandPosition` property
- Create `PathResult` class: success bool + waypoint list
- Create `VoxelPathfinder` class: A* implementation
  - Takes World reference, queries `GetBlock()` for neighbor checks
  - 4-connected neighbors with flat/step-up/step-down logic
  - 2-high clearance validation at every destination
  - `WorldPosToVoxelNode()` conversion (scan downward for solid ground)
- **Verify:** Call `FindPath()` from test code, print waypoint list to console

### Phase 9: Basic Colonist
- Create `Colonist` scene: CharacterBody3D + CapsuleMesh + CapsuleShape3D
- State machine movement: Idle/Walking/JumpingUp/Falling
- Follow waypoint list from pathfinder
- Jump physics: upward velocity + horizontal momentum + grace timer
- Gravity when not on floor
- `SetDestination(Vector3)` triggers pathfinding and starts movement
- Right-click on terrain → colonist walks there
- **Verify:** Colonist walks across terrain, climbs 1-block steps, descends, crosses chunk boundaries

### Phase 10: RTS Camera
- Replace debug orbit camera with RTS-style camera
- WASD/arrow key panning, scroll wheel zoom, middle-mouse rotate
- Optional: edge-of-screen panning

### Future Phases (not yet planned in detail):
- Multiple colonists
- Task/job system (mine, build, haul designations)
- Inventory and resource system (mined blocks become items)
- Colonist needs (hunger, rest, mood)
- More block types (wood, ore, sand, water)
- Better terrain (caves, overhangs, biomes, ore veins)
- Chunk streaming (load/unload based on camera)
- Vertical chunks (worlds taller than 16 blocks)
- Selection system (click to select colonists, area designation)
- Save/load system
- UI (menus, status panels, notifications)

---

## 7. Suggested File Structure

```
colonysim-3d/
├── project.godot
├── CLAUDE.md
├── scripts/
│   ├── world/
│   │   ├── World.cs              # Chunk manager, world-space block access
│   │   ├── Chunk.cs              # 16x16x16 block storage, mesh, collision
│   │   ├── ChunkMeshGenerator.cs # Procedural ArrayMesh from block data
│   │   ├── TerrainGenerator.cs   # FastNoiseLite height map + block layers
│   │   └── Block.cs              # BlockType enum + BlockData utilities
│   ├── navigation/
│   │   ├── VoxelPathfinder.cs    # A* on voxel grid
│   │   └── PathRequest.cs        # VoxelNode, PathResult data structures
│   ├── colonist/
│   │   └── Colonist.cs           # CharacterBody3D + state machine movement
│   ├── interaction/
│   │   └── BlockInteraction.cs   # Mouse raycast, block modification, colonist commands
│   └── camera/
│       └── CameraController.cs   # RTS camera (pan, zoom, rotate)
├── scenes/
│   ├── main.tscn                 # Entry scene
│   └── colonist/
│       └── Colonist.tscn         # Colonist prefab
└── godot-docs-master/            # Local Godot 4.6 docs (reference)
```

---

## 8. Gotchas & Warnings

1. **Do NOT use NavigationServer3D** for pathfinding. Use grid-based A*. See section 3.3 for why.

2. **Do NOT use GridMap** for the voxel world. Use procedural ArrayMesh. See section 3.1 for why.

3. **Mesh winding order must be clockwise** (viewed from outside). If blocks render inside-out, reverse the index order. See section 5.1.

4. **ConcavePolygonShape3D wants a flat vertex list**, not indexed triangles. Expand indices to sequential vertex triples before passing to `SetFaces()`.

5. **Negative coordinate modulo in C#** returns negative values. Use `((x % 16) + 16) % 16` for chunk-local conversion.

6. **`Vector3.Floor()` rounds toward negative infinity**, which is correct for block position math. But casting to `int` truncates toward zero — use `Mathf.FloorToInt()` instead.

7. **Chunk.GetBlock() should return Air for out-of-bounds coordinates**, not throw. This simplifies face culling (boundary blocks treat out-of-chunk neighbors as air, rendering their exposed faces).

8. **Colonist capsule radius** (0.3) + **block center routing** (X+0.5) = **0.2 units wall clearance**. This is tight but sufficient. If you increase capsule radius, you need to account for clearance in pathfinding neighbor checks.

9. **Blocks are 1x1x1 units.** Colonists are ~2 blocks tall. All clearance checks must verify 2 air blocks above a walkable surface.

10. **After `MoveAndSlide()`, `IsOnFloor()` may return true for 1-2 frames after a jump** due to the character still touching the launch surface. Use a grace timer (~0.1s) before checking `IsOnFloor()` in jump state.

---

## 9. Reference Materials

Godot 4.6 documentation is available locally at:
```
E:\hobbies\programming\godot\colonysim-3D\godot-docs-master\
```

Key docs:
- ArrayMesh procedural geometry: `tutorials/3d/procedural_geometry/arraymesh.rst`
- CharacterBody3D: `classes/class_characterbody3d.rst`
- FastNoiseLite: `classes/class_fastnoiselite.rst`
- SurfaceTool: `classes/class_surfacetool.rst`

---

## 10. Testing & Verification

You cannot run the Godot project yourself. The **user** runs the game and reports back. This means you must make it easy for the user to verify that things work.

### Debug Logging

**Use `GD.Print()` liberally.** Every significant action should log to the Godot console so the user can confirm behavior without reading code. Examples:

- Chunk loaded: `"Loaded chunk at (1, 0, 2): 847 solid blocks"`
- Mesh generated: `"Chunk (1,0,2): 1,204 triangles, 312 collision faces"`
- Block modified: `"Removed block at (19, 7, 28) — chunk (1,0,1) regenerated"`
- Path found: `"Path: (19,7,28) → (25,5,30), 14 waypoints, 3 height changes"`
- Path failed: `"No path from (19,7,28) to (25,5,30) — explored 847 nodes"`
- Colonist state: `"Colonist: Walking → JumpingUp at (22, 8, 29)"`
- Colonist stuck: `"Colonist: stuck for 2.1s at (22.4, 8.0, 29.5), clearing path"`
- Colonist arrived: `"Colonist: reached destination (25, 6, 30)"`

### Asking the User to Verify

After completing each phase, **ask the user to run the game and report back.** Be specific about what to look for:

- "Please run the scene. You should see a 48x48 block terrain with rolling hills. Can you confirm the grass/dirt/stone layers are visible?"
- "Try left-clicking on different blocks. The console should print which block was removed. Does the terrain update visually?"
- "Right-click on a distant block. The console should show a path with waypoint count. Does the colonist walk there?"
- "Try right-clicking on a block that's 1 block higher than the colonist. The console should show a jump state transition. Does the colonist jump up?"

If something doesn't work, **ask the user to paste the console output.** The debug logs will tell you what happened without needing to guess.

### Verification Checklist Per Phase

Don't move to the next phase until the user confirms the current one works. Ask them to:

1. **Report console output** — are the expected log messages appearing?
2. **Describe visual result** — does it look right? Any visual artifacts?
3. **Test edge cases** — click near chunk boundaries, click on the highest/lowest terrain, etc.
4. **Report any errors** — red text in the Godot console means something broke

---

## 11. Quality Standards

- Every phase must **compile with 0 errors and 0 warnings** before moving to the next.
- Test each feature in the running game, not just in theory.
- Prefer simple, readable code over clever abstractions. This project will grow — understandability matters more than cleverness.
- Do not over-engineer for future requirements. Build what's needed now.
- If stuck on a bug for more than 2-3 attempts, stop and analyze the root cause before trying more fixes. Random permutations waste time.
