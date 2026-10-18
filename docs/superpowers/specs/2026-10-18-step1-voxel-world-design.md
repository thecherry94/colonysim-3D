# Step 1: A World You Can Dig and Build In

**Status:** design from brainstorming, 2026-10-18. It sits under the game vision in `2026-10-18-colonysim-vision-design.md`; read that first.

**Reference prototype:** `docs/superpowers/specs/prototypes/slice-view-tools.html`. Open it in a browser: it's a throwaway three.js page. It shows the intended slice view and tool behavior, which the developer has tried and approved. Use it to see the intended behavior. Don't treat its code as a model.

## Intent

Step 1 builds the foundation everything else stands on: a generated block world that can be dug out and built on, rendered fast, and readable at any depth. There are no colonists yet. The step is done when the developer can generate a large map, fly around it smoothly, slice down into it, find caves, mark a two-story house and a mine, and watch both get built and dug.

The hardest problem in the developer's earlier attempt was **seeing and giving orders underground, among caves**. The slice view in this step is the answer to that. It has to work flawlessly, not just well enough.

## Goals

1. Generate believable, varied, Minecraft-like maps from a seed.
2. Render big maps at a smooth frame rate, and update the rendering quickly when blocks change.
3. Make any depth readable through the slice view, with fog of war over undiscovered caves.
4. Let the player mark dig and build jobs at any level, including 3D volumes, using the drag tools from the prototype.
5. Lay foundations that steps 2–4 can build on without rewrites: simulation separated from rendering, a job queue that colonists will consume later, a generator that world tiles will feed with parameters later, and save/load.

## Non-goals for step 1

- Colonists, animals, items, hauling, and resources that drop from digging. Dug blocks simply disappear for now.
- Flowing water. Water is static, and digging next to it doesn't flood anything. Water behavior gets decided in step 3.
- Structural physics: no collapsing or support rules.
- Day/night, weather, temperature, lighting that makes underground areas dark, and sound.
- Doors, windows and furniture. They come in step 2, when colonists can use them.
- More than one biome, the world map, and factions.
- Polished UI or art. Functional placeholder art and UI are fine.

## The map

### Size and scale
- **1 block = 1 m.** A colonist (step 2) is about 1.8 blocks tall and needs 2 blocks of headroom, so a comfortable story is about 3 blocks tall plus a floor.
- **The player picks the map size when starting a game:** Small 256×256, Medium 384×384, Large 512×512 (X × Z). *This is Claude's call. The developer said 256×256 alone was too small, and accepted this range when it was played back.*
- **Height: 256 blocks.** Y = 0 is the bottom. The typical ground surface sits around Y 128–150, which leaves roughly 128 diggable blocks under the surface. Mountains can rise to roughly Y 220. *The exact numbers are Claude's call.*
- Every size, Large included, must meet the performance targets below.

### Terrain
- **Layers, top to bottom:** surface block (grass, or sand at water edges, or bare stone on high mountains), several blocks of dirt, stone, then darker deep stone in the lower part of the map, and an uneven unbreakable bedrock floor.
- **Landforms:** rolling ground, valleys, and **mountains**. Mountains are large masses of rock rising well above the ground, as in RimWorld, and are prime places to mine into.
- **Water:** rivers that cross the map, sitting in valleys they carve, plus ponds and lakes where the ground dips below water level. Water is static in step 1.
- **Caves:** underground networks of winding tunnels plus larger open caverns. Caves normally stay sealed below the surface, so they're found by digging. A few may open onto a hillside or mountain face.
- **Ores** are scattered through the stone in veins, and which ores appear depends on depth: common ores near the top, valuable ones deep down. The exact ore list is up to the builder. At least three ores across the depth range.
- **Surface objects:** trees (a couple of kinds), boulders, and optionally small plants. Trees and boulders are made of blocks, so the slice view handles them naturally.
- **One temperate biome** in step 1.

### Generation requirements
- **Deterministic:** the same seed and settings produce the same map.
- **Parameterized** so that step 4's planet can drive it later. Biome, base elevation and roughness, whether there's a river, mountain coverage, and similar settings should be inputs, not constants buried in code.
- **Time target:** a Large map generates in **under about 30 seconds** on the developer's machine, with a progress display. Faster is better.

## Rendering

- **Blocky Minecraft-like look:** textured block faces using small pixel-art textures (16×16 is the default) from a texture atlas. Placeholder textures are fine as long as block types are easy to tell apart. Simple per-face shading plus vertex ambient occlusion keeps shapes readable.
- **Chunked:** the world is split into chunks that are meshed independently. Meshing runs on background threads, and changing a block only remeshes the affected chunks.
- **Water:** semi-transparent surface.
- **Performance targets:** a steady **60 fps** on a Large map with the whole map in view on the developer's machine: Linux, RTX 3080, i5-10600K (6 cores, 12 threads), 32 GB RAM. Changing a single block shows up within one or two frames.

## Slice view

Behavior is as in the prototype:

- The player sets a **view level** (a Y value). Every block above it is hidden: terrain, trees and structures. In later steps, the same goes for colonists, items and animals.
- **Cut caps:** a solid block at the view level that had solid ground above it is drawn **dark**, keeping a hint of its material color so ore veins remain visible in cross-section. Rock therefore reads as solid mass, and open space (rooms, tunnels, caves) reads like a floor plan. The player can turn caps off.
- **Fog of war:** open space the player hasn't discovered is drawn as solid, using the material that would be there otherwise (dirt in dirt, deep stone in deep stone), including inside cut caps, so hidden caves don't give themselves away. Once discovered, space stays discovered. Outside the map counts as solid for discovery. The tools also see the fogged world, so a dig preview can't reveal a hidden cave. *These details are Claude's calls.* In step 1, "discovered" means connected to open air through non-solid space. When a dig opens into an undiscovered space, everything connected to it is revealed. In step 2 the trigger becomes colonist exposure, and the rule may be refined then. Ores inside rock stay visible in cut caps. *That's Claude's call, so the player can plan mines.*
- **Changing the level feels instant:** stepping one level up or down takes well under 50 ms, even on a Large map. The prototype rebuilt the whole mesh. The real implementation has to touch only what the cut actually changes. How to do that is left to the builder.
- **Controls:** PgUp/PgDn, Shift + mouse wheel, a level indicator showing the current Y and its depth relative to ground, a key to return to "top, nothing hidden", and **double-click a block to jump** there. Clicking the top of a block sets the level to the space standing on it.
- **The camera follows the level:** when the view level changes, the camera's focus point moves vertically with it, so the area being looked at stays in frame. The prototype didn't do this. *Claude's call.*
- Following a colonist and player-named level bookmarks come in step 2.

## Camera

An RTS-style overseer camera: WASD or arrow keys pan, Q/E rotate, the mouse wheel zooms, and dragging with the right or middle mouse button rotates and pans. It's limited to the map area and a sensible zoom range. *The exact bindings are Claude's call; the builder may adjust them.*

## Designation tools

Behavior is as in the prototype:

- **Tools:** Dig, Wall, Floor, Stairs, Cancel, plus a camera/no-tool mode. Hotkeys are fine.
- **Tools act at the current view level.** At the top level, with nothing hidden, they act on the surface the player clicks.
- **Left-drag draws a rectangle.** Pressing PgUp/PgDn while still dragging stretches it into a 3D box, so you can dig deeper or build walls taller. Release to confirm, Esc to cancel.
- **What each tool marks:**
  - **Dig** marks solid, non-bedrock blocks.
  - **Wall** marks only the rectangle's outline, so one drag outlines a room.
  - **Floor** fills the rectangle.
  - **Stairs** places a staircase running along the drag direction, rising one block per step. Each step is a stair-shaped block with a facing direction stored in its block data. That way step 2 can decide later whether colonists climb only on stairs or can step up any single block. *Claude's call. How it's drawn and dragged is left to the builder.*
  - **Cancel** removes marked jobs.
  - Build tools only mark empty space.
- **Materials:** wall, floor and stairs can be built in at least two materials (wood and stone), chosen in the toolbar. *Claude's call: materials will matter for colonists and resources later.*
- **Marked jobs** show as colored translucent overlays, a different color per kind, and are hidden above the view level like everything else.
- **Jobs go into a job queue in the simulation.** In step 1 nobody does them, so a debug **"instant work" mode** completes queued jobs progressively in a sensible order, a few per tick, as in the prototype: all dig jobs first, top-down, then build jobs, bottom-up. Instant work must take jobs from the same queue colonists will use in step 2.

## Save and load

Save and load the map, including modifications, discovery state and queued jobs, plus a "new map from seed" option. *Claude's call: every later step needs it, and adding it to the world format later is painful.* Saves store the whole world, not the seed plus a list of changes, so old saves don't depend on the generator producing identical output across versions. The format is up to the builder.

## UI for step 1

- A new-game screen: seed (random by default, editable), map size, and Generate, with a progress display.
- An in-game HUD: level indicator, toolbar with tools and material picker, queued job count, an instant-work toggle, and a slice-cap and fog-of-war toggle (debug).
- A debug overlay: FPS, chunk counts, and meshing queue length.
- Functional placeholder styling.

## Architecture constraints

- **The simulation is separate from presentation.** World storage, generation, discovery, designations and the job queue live in plain C# with no Godot dependency, are unit-tested, and run headless. The Godot side handles meshing and rendering, the camera, input, UI and the slice view.
- **Block types are defined in one place,** with their properties: solid, diggable, transparent, texture or texture-per-face, material. Adding a block type shouldn't take edits scattered across the code.
- **Tests:** generation determinism, layer and ore placement rules, discovery and flooding, designation rules (what each tool may mark, outlines versus fills, 3D drags), job ordering for instant work, and save/load round-trips.

## Left to the builder

Chunk dimensions, the meshing algorithm, how slicing and capping are implemented, noise functions and parameters, threading model, file and save formats, project and solution layout, texture art, and the exact UI layout. Any choice is fine as long as the requirements and performance targets above are met and the step 2–4 foundations aren't blocked.

## Done when

On the developer's machine, a Large map can be:

1. generated from a seed within the time target, with the same seed giving the same map;
2. viewed at 60 fps, with smooth camera movement;
3. sliced to any level, one step at a time, with no noticeable delay, while caps and fog of war behave as described;
4. dug into, revealing a hidden cave when a dig breaks into it;
5. used to mark a two-story building (walls, a floor on top, stairs between floors) and a multi-level mine, then complete both with instant work;
6. saved, reloaded, and found exactly as it was left.
