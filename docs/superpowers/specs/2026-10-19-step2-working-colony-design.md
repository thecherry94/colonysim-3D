# Step 2: A Working Colony

**Status:** design from brainstorming, 2026-10-19. It sits under the game vision in `2026-10-18-colonysim-vision-design.md`, read that first, and builds on step 1 (`2026-10-18-step1-voxel-world-design.md`), which is finished and merged.

**Reference prototypes:**
- `docs/superpowers/specs/prototypes/house-tool-slopes.html`. Open it in a browser: it's a throwaway three.js page. It shows the start site, the slope rules and the house tool at all three research levels. The developer tried it and said it nailed the first version. Use it to see the intended behavior; don't treat its code as a model.
- `docs/superpowers/specs/prototypes/slice-view-tools.html` is the step 1 prototype. Its slice view and drag tools still apply.

*Marks like "Claude's call" flag decisions the developer didn't make personally. They can be changed cheaply if the developer disagrees.*

## Intent

Step 1 made a world. Step 2 puts a colony in it that **gathers, hauls and builds**: colonists who walk through 3D terrain, take jobs on their own, turn trees and rock into stored resources, and put up buildings the player drafts with an easy house tool. A short research tree unlocks better buildings over time.

In the developer's words, the game's arc is:
- **Early on**, you set up a lot for your colonists and steer them closely.
- **As time passes**, colonists fall into preferred roles. Some start with a strength, and they get better at whatever they do over and over.
- **Eventually**, once the player has set up production chains, basic survival runs nearly by itself. The player's attention then goes to random events and to climbing the tech tree.

Step 2 has to make that arc possible for gathering and building. Step 2 is done when the colony can keep itself supplied with wood, stone and planks and keep building, without the player babysitting each colonist.

### What changed against the vision

The vision originally put needs, mood and social life in step 2, and items and hauling in step 3. The developer reordered this during brainstorming: "First we need the basic gathering and building systems. Can't fulfill any needs if we can't collect and manage resources." Research and a tech tree are new; the vision didn't have them. The vision document is updated to match. The new step order is:

- **Step 2:** a working colony (this document).
- **Step 3:** colonists as people. Needs, mood, mental breaks, social life, traits, passions, relationships, and the food chain that needs depend on.
- **Step 4:** nature. Wildlife, weather, seasons and temperature.
- **Step 5:** the planet. World map tiles and factions.

## How step 2 is built

Step 2 is too big for one plan. It's built in **three parts, in this order**. Each part ends playable and gets its own implementation plan:

1. **2a, colonists walk and work:**
   - groundwork from step 1's reviews;
   - slopes and the start site;
   - colonists, pathfinding and simulation time;
   - the job system with work priorities and skills.

   Colonists take over the step 1 dig and build designations.
2. **2b, resources:**
   - items and stacks, hauling and storage;
   - starter crates;
   - gathering zones;
   - digging that yields stone and ore;
   - building that consumes materials.
3. **2c, building and rooms:**
   - the house tool;
   - free-build and deconstruct;
   - room zones and furniture;
   - workshops with production orders;
   - research and the tech tree.

## Goals

1. Colonists who move believably through a 3D block world, including tunnels, mines and multi-story buildings, and never get stranded by their own work.
2. Indirect control, as in RimWorld: the player marks work, zones and buildings, and colonists decide who does what.
3. A resource loop: gather, haul, store, process, build.
4. Building that is quick and intuitive. The player drafts whole buildings, not individual blocks.
5. Progression through research: crude at first, better buildings later.
6. Foundations for step 3. Jobs, time, colonist data and save files must have room for needs, mood and traits without rewrites.

## Non-goals for step 2

- **Colonists as people:** needs (hunger, thirst, rest, comfort, recreation, social), mood, mental breaks, traits, passions and relationships. That's step 3.
- **Food:** farming, hunting, cooking and eating. Kitchens and stoves come with step 3.
- **Nature and events:** animals, weather, seasons, temperature, day and night, random events and raids.
- **Health:** combat, health, injury and death. Colonists can't die or get hurt in step 2.
- **Drafting colonists** or giving direct move orders. A debug teleport or move is fine.
- **Flowing water.** Water stays static.
- **Planet and factions:** the world map, trade and other factions.
- **Ore discovery:** ore that has to be researched before it can be seen or mined. The developer mentioned discovering resources through research; it's deferred until there are enough resources for it to matter.
- **Polish:** final character models and art. Simple blocky colonists and placeholder art are fine.

## Anti-goals

- **Building as a chore.** If putting up a house means placing walls and roof blocks one by one, step 2 failed. The house tool is the main way to build.
- **Colonists who get stuck, jitter, or trap themselves.** That covers digging out the floor they need, walling themselves in, and mining down with no way back up.
- **A job system that can't take needs later.** In step 2, colonists work all the time because they have nothing else to do. That's acceptable only if step 3 can add "I'm hungry, I go eat" without rewriting job selection. Robot-like colonists are a vision anti-goal; step 2 gets a pass only because needs come next.
- **Transparency or x-ray views.** Seeing inside buildings uses the slice view, as everywhere else.

---

## Part 2a: Colonists walk and work

### Groundwork from step 1

Step 1's final review left items that colonists will depend on. They're fixed first:

- **Jobs:**
  - Each job gets an identity, a claimed-by colonist and saved progress. A half-dug block keeps its progress.
  - The rules for designating over an existing job are defined.
- **Job overlay:** it updates incrementally or per chunk, instead of copying everything on every change.
- **Block state:** each voxel can carry state such as orientation, shape variant and open/closed. Stairs, slopes, ladders, doors and furniture all need it. This means **save format v2**.
  - *Claude's call:* step 1 saves don't need to load. They were only ever test saves.
- **Save palette:**
  - Unknown block names that no cell uses are tolerated.
  - A rename-alias table exists.
- **Threading:** world mutation stays on the main thread. Pathfinding may run on worker threads, but must not race the dirty-chunk set.
- **Ground height:** a real "current top walkable block" query is used for gameplay. The generated surface height is never used for pathing.
- **Rendering budget:** the per-frame budget for applying edit batches also covers several small batches finishing in the same frame.
- **Input:**
  - a drag is cancelled when the window loses focus;
  - a lost mouse release no longer leaves a phantom drag.
- **Rivers (cosmetic, optional):** river width is measured across the flow, so diagonal stretches aren't 2 blocks wide.

### Caves colonists can use

The developer wants caves to feed the early game: the upper caves are open enough for colonists to walk into and mine the fairly abundant basic resources, and caves get harder to get through the deeper they go.
- **Near the surface:** caves are wider and taller, with gentler floors, so a colonist with hops can get around them. Coal and iron are common along them.
- **Deeper down:** caves get narrower, steeper and more broken up, so getting through needs digging, stairs and ladders.
- **Constraints:** the generator stays deterministic, and caves still mostly stay sealed below the surface, as step 1 requires.
- *Doing this in 2a, alongside movement, is Claude's call; the developer said "at some point". The exact shapes are left to the builder.*

### Start site

- The game picks a start site on the surface: a fairly flat, dry patch, not inside a mountain, connected to most of the map.
- **If no patch is flat enough,** the generator flattens one. The patch is level in the middle and blends into the surrounding terrain, with an uneven edge so it looks natural, not like a cut-out square. The prototype's version is a radius of about 6 flat and about 12 blended, with a noisy edge. *The developer asked for this. The numbers are Claude's call.*
- It's deterministic: the same seed and settings give the same site.
- **Starting kit:** crates of starter resources sit in the middle of the site (contents in 2b), the colonists start next to them, and the camera opens there.

### Slopes

Slopes make the surface look less like a staircase, and they give colonists natural ramps to walk on. The rules are as in the prototype:

- **Where they go:** a slope sits on any single-block step of the **open surface**, whatever the material: grass, dirt, sand, or bare stone on mountains.
- **Where they don't:**
  - A drop of **2 blocks or more stays sharp.** Cliffs remain real obstacles.
  - **Underground stays fully blocky.** Caves, tunnels and mines have no slopes, so the slice view stays a clean floor plan.
- **Shapes:** ramp, outer corner and inner corner, oriented to fit. They take the texture of the ground they sit on.
- **Slopes are real blocks.** *Claude's call.* They're placed once, at map generation. After that they're ordinary blocks:
  - digging removes them;
  - nothing turns new steps into slopes automatically, because a player digging a foundation shouldn't get soft edges they didn't ask for;
  - **player-built ramps** use the same shapes and can go anywhere.
- **Behavior:** slopes are walkable like stairs, can be dug, and work correctly with the slice view, caps, fog of war, ambient occlusion and the tools.

### Colonists

- **The starting group:** the colony starts with **3 colonists**, each with a random name. *Claude's call; RimWorld's default.*
- **Look:**
  - simple blocky, Minecraft-style people, with a placeholder walk cycle and distinct shirt colors;
  - better models come later, so the look must be easy to swap.
- **Size:** a colonist is 1 block wide and about 1.8 blocks tall, and needs 2 blocks of headroom.
- **Selecting a colonist:**
  - Click a colonist to select it.
  - An info panel shows name, current activity, carried item and skills.
  - A colonist bar lists everyone and jumps the camera to them.
- **Slice view:** colonists above the view level are hidden, like everything else.
- **Follow and bookmarks:** the camera can **follow** the selected colonist, and the view level follows them too. The player can save **named view levels** as bookmarks. Step 1's design promised both for step 2.
- **Bad spots:**
  - If the ground under a colonist is removed, they drop to the next floor below. There's no fall damage, since there's no health yet.
  - A colonist left in a sealed pocket waits, and is flagged as stuck in the UI.

### Movement and pathfinding

The developer's rule: **height changes on stairs, ladders and ramps**, slopes included. Hopping and climbing are a last resort, allowed **only on natural terrain** (outdoors, in caves and in mines), never on anything built.

- **Standing:** a colonist stands on the top of a solid block, a slope or a stair, with 2 non-solid blocks above.
- **Walking:** flat steps go to the 8 neighbors, with no cutting across blocked corners. *Diagonals are Claude's call.*
- **Normal height changes:** stairs, ramps and slopes go up or down one block at normal cost. **Ladders** go straight up and down.
- **Fallbacks on natural rock and earth:**
  - **Hop:** up or down one block. Allowed only when the block climbed over is natural terrain: outdoors, in caves and in mines alike. *The developer confirmed caves and mines count.* It costs much more than a stair, so it's a last resort.
  - **Climb:** a 2–3 block natural ledge, under the same conditions. It's slow (seconds) and costs a lot.
  - **Built blocks are never hopped or climbed:** walls, floors, foundations and furniture. Buildings always need stairs, ladders or ramps.
  - Movement rules work the same up and down, so any path can be walked back. *Claude's call; it prevents one-way traps.*
- **Doors** can be walked through. Colonists open them on the way, which costs a little. **Windows** can't be walked through.
- **Water** can be crossed, slowly. *Claude's call: rivers would otherwise cut the map in two before bridges exist.*
- **Unreachable work** is skipped and marked in the UI. It isn't retried every tick, and it's re-checked when the world changes.
- **Performance target:**
  - about **20 colonists** on a Large map at a steady **60 fps**, with no hitches when many re-path at once;
  - long paths across a Large map, through tunnels and buildings, found within a few frames.

  The 20-colonist target is Claude's call, and the developer accepted it for now.
- **Long-term scale: 100+ colonists per map.** *The developer's requirement.* Step 2 doesn't have to reach it, but nothing it builds may block it:
  - No per-tick work that grows with colonists × jobs, or colonists × colonists. Job, item and storage lookups use spatial indexes or similar, not scans of everything.
  - Pathfinding is built for many simultaneous requests, through caching, a hierarchy, worker threads or time-slicing. The algorithm is left to the builder.
  - Colonist rendering scales: instancing or similar, not one heavy scene per colonist.
  - **A debug command spawns colonists in bulk.** The developer's machine reports frame time and tick time at 20, 50 and 100 busy colonists on a Large map. 20 must hold 60 fps. The 100-colonist numbers are recorded as the baseline later steps must not make worse.

### Fog of war with colonists

*Claude's call:* step 1's discovery rule stays.
- **What reveals space:** open space is discovered when it connects to discovered air, so a dig that breaks into a cave reveals it. Colonist line of sight isn't needed yet.
- **Colonists only know what the player knows:**
  - Pathfinding and reachability read the discovered world: undiscovered space counts as solid.
  - So colonists never walk through a hidden cave and give it away, and colonists and items can't end up in fogged space.
- **Dig jobs over hidden space:** a dig job marked over what turns out to be a hidden cave completes as soon as its cell is revealed to be open.

### Time

- The simulation runs in **fixed ticks**, independent of frame rate. Colonists are drawn smoothly between ticks.
- **Speed controls:** pause, 1×, 2× and 3×. *Claude's call.* The hotkeys are Space and 1/2/3, so the step 1 tool hotkeys move if they collide.
- A simple day counter is shown. Day and night come in step 4.

### Jobs and work

- **Work types** *(Claude's call)*:

  | Work type | What it covers | Arrives in |
  |---|---|---|
  | Mining | dig jobs | 2a |
  | Construction | building, deconstructing | 2a |
  | Woodcutting | felling trees | 2b |
  | Gathering | rocks, water | 2b |
  | Hauling | moving items | 2b |
  | Crafting | work at workbenches | 2c |
  | Research | the research bench | 2c |

- **Work priorities:** a RimWorld-style grid of colonists × work types, each cell set to priority 1–4 or off.
- **Choosing a job:**
  1. A colonist takes the highest-priority work type that has an available, reachable job.
  2. Within the same priority, colonists **prefer the work they're best at.** This is how roles emerge without the player managing everything. *Claude's call, to match "colonists fall into a preferred role".*
  3. Then the nearest job wins.

  The selection must have a clear place where step 3's needs can outrank work.
- **Claims:** each job is claimed by one colonist at a time. If the colonist is interrupted, the claim is released and the progress kept.
- **Work time** depends on the block (dirt is faster than stone, deep stone slower still) and on the colonist's skill.
- **The step 1 designations become colonist jobs.**
  - **Reach:** colonists work on blocks they can reach from a standable spot next to them, including one level up or down. The exact reach is the builder's call.
  - **No trapping:**
    - Colonists never dig out the block they're standing on in a way that strands them.
    - They never place a block that seals them or another colonist in.
    - They never take a dig job that would leave them with no way back. Such a job is marked "needs a way back up".
  - **Order:** walls are built from outside where possible, and roofs last.
- **Stairs carved into rock** *(Claude's call)*: using the Stairs tool on solid ground marks a dig that **leaves a stair-shaped block of the same material**. One drag then digs a staircase down into a mine. Without this, mining down would mean dig, build stairs, dig again.
  - **Which mode:** the cell where the drag starts decides it. A drag starting on solid ground carves; a drag starting in open space builds, as in step 1.
  - **Direction:** a carved staircase starts at the clicked block and goes **down** one block per step in the drag direction.
  - **Headroom:** the 2 blocks of headroom above each step are dug automatically.
  - **Open air along the way:** cells that are already open are simply skipped.
- **Instant work** stays as a debug toggle.

### Skills

- **Skills** *(Claude's call)*: Mining, Construction, Woodcutting, Crafting, Research. Hauling and Gathering have no skill.
- **Levels:** 0–20, as in RimWorld. Doing the work earns experience. A higher level means faster work, and later better quality.
- **Starting skills:** each colonist starts with a random spread, usually with one or two strengths. That's the developer's "maybe they have one from the beginning".
- **No skill decay** in step 2. Passions come with step 3's traits.

---

## Part 2b: Resources

### Items

- **Item types in step 2** *(Claude's call)*:

  | Item | Comes from |
  |---|---|
  | Logs | felling trees |
  | Foliage | the leaves of felled trees; the shelter roof material |
  | Stone and deep stone | digging those blocks |
  | Coal, copper, iron and gold | digging ore |
  | Planks | crafted from logs in 2c |
  | Water | the water zone |

  Digging dirt, grass and sand yields nothing for now, so the map doesn't fill up with dirt piles.
- **Water** is stored like any item, as a stand-in until step 3 gives it a use. The developer asked for water collecting; it gets a purpose with needs and cooking.
- **Stacks:** items lie in the world as visible stacks, one stack per cell, each with a stack limit per item type. The limits are the builder's call. Stacks above the view level are hidden.
- **Starter crates** at the start site hold the starting resources. Colonists take from them like from storage. *Claude's call on contents:* enough to build a shelter and a research bench, plus a little spare.

### Hauling and storage

- **Storage zones** are painted on the ground or floor. Each has an item filter (everything by default) and a priority. Colonists with Hauling enabled carry loose items to the best storage zone with room.
- **Carrying:** a colonist carries one item type at a time, up to a carry limit.
- **Construction materials** are hauled to the building site before the building work starts. They come from storage, crates or loose stacks.

### Gathering zones

The developer described these as areas you mark on the map that colonists then work on their own.
- **Woodcutting zone:** colonists fell the trees inside it. A felled tree drops logs and foliage. Trees don't regrow in step 2; that comes with step 3's plant growth.
- **Rock gathering zone:** colonists break up boulders and collect loose surface stones inside it. *Claude's call:* the generator scatters some loose stones on the surface for this.
- **Water collecting zone:** painted next to water, where colonists collect water. Water never runs out.
- **Single targets:** like the Dig tool, single trees and boulders can be marked with a drag (chop, gather) without a zone.

---

## Part 2c: Building and rooms

### The house tool

Behavior is as in the prototype, which the developer approved:

- **Shelter** (known from the start):
  - drag a rectangle on the ground, up to 5×5;
  - four corner beams and a foliage roof, no walls;
  - work starts as soon as the mouse is released, with no editable ghost.
- **Basic house** (researched):
  - **The ghost:** drag a rectangle and a ghost of the building appears and stays. Dragging more rectangles that **touch** the building merges them into one footprint. An area that doesn't touch it is rejected.
  - **Outer walls** run around the merged shape. A footprint cell is a wall if any of its 8 neighbors is outside, so the inside corners of L-shapes close up.
  - **Openings:**
    - Doors and windows go on by clicking a wall cell, but not on a corner or where walls meet.
    - **Inside walls** are drawn freely with a straight drag inside the footprint, and can take doors.
  - **Floor:** the ground floor gets a plank floor.
  - **Roof:** generated over the whole shape. It steps up one block per ring inward, with a one-block overhang. *The roof shape is Claude's call and was in the approved prototype.*
  - **Editing:** a toolbar offers Add area, Door, Window, Inside wall, Remove and Undo area, with Build to confirm and Discard to throw the ghost away.
- **Multi-story** (researched):
  - set the number of floors;
  - place **stairs**: drag from the bottom step in the climbing direction; they need 4 free cells in a line, and a hole opens above them;
  - place **ladders**: click a cell; a hole opens above.
- **Story height:** 3 blocks of wall plus a 1-block floor or ceiling. *Claude's call; the prototype uses it.* The prototype caps buildings at 3 floors. The real cap is the builder's call.
- **Looking inside:** while editing, the view switches to the floor being edited, using the normal slice view. Floor buttons and PgUp/PgDn step between floors, and "whole building" shows the roof.
- **Uneven ground:**
  - the building sits at the height of the cell where the first drag started;
  - higher ground in the footprint is marked for digging, shown in yellow;
  - gaps under it are filled with a stone foundation.
- **Materials:** the ghost shows what it will cost. The building's material is chosen from what has been unlocked:
  - shelter: logs and foliage;
  - wooden house: planks;
  - stone house: stone blocks, after research.
- **After Build:**
  - the ghost becomes a blueprint;
  - colonists haul the materials and build it bottom-up, following the trapping rules;
  - unbuilt parts stay visible as ghost;
  - a blueprint can be cancelled whole or in parts, but its shape can't be edited after confirming. *Claude's call; re-editing can come later.*
- **Doors** are blocks with an open/closed state. Colonists open them as they pass.

### Free build and deconstruct

- **Free-build mode:** step 1's single-block tools stay as free-build mode, for patching and odd shapes. They now use materials and colonists. *Claude's call; the developer didn't object.* Dig stays as it is.
  - **Pieces:** wall, floor, stairs, **ladder**, **ramp**, door, window.
  - **Research follows the house tool:**
    - from the start, in wood: floor, stairs, ladder and ramp, so mines and slopes can be worked from day one;
    - Basic house unlocks walls, doors and windows;
    - Stonecutting unlocks the stone versions.
  - **Arrives in 2a:** ladders and ramps come in 2a along with movement, so they can be tested there. Materials and research gating come in 2b and 2c.
- **Deconstruct:** a tool that takes built blocks and furniture apart and returns part of the materials. *Claude's call.*

### Rooms and furniture

Rooms work like **Prison Architect**:
- You **paint a room zone** to say what a space is for.
- The room needs its **core furniture** to work. Each extra piece of furniture unlocks more of what the room can do. A kitchen with only a stove still works; you just can't make dishes that need an oven.

- **Room types in step 2** *(Claude's call)*, chosen for what colonists do in step 2:
  - **Wood workshop:**
    - core furniture: a **sawing bench**, which turns logs into planks;
    - extra: a **carpenter's bench**, which makes furniture.
  - **Research room:** core furniture is a **research bench**.
  - **Stone workshop:** core furniture is a **stonecutting bench**, which turns stone into blocks. It's unlocked by Stonecutting.

  Storage stays a plain storage zone. Kitchens arrive with step 3.
- **Basic items work anywhere.** *The developer's rule.* A handful of primitive pieces can be placed nearly anywhere to get the colony started, under a leaf shelter or out in the open:
  - a **simple research bench**;
  - a **sawhorse**, which turns logs into planks slowly. *Claude's call: without it, planks would need a workshop building, and the building needs planks.*

  More basic items, such as a campfire for basic cooking, come with the steps that use them.
- **Everything else must be indoors to work.**
  - The proper workstations (research bench, sawing bench, carpenter's bench, stonecutting bench) only work inside a **room zone of their type**.
  - The room zone only works if it lies **inside a building**.
  - The room zone shows what's missing, for example "needs a sawing bench" or "not indoors".
  - The proper versions are faster and can make more than the basic ones.
- **What counts as inside a building:** a space closed off on all sides by walls, doors or windows, with a roof or ceiling over it. Natural rock counts as wall and roof, so a workshop carved into a mountain works. *That detail is Claude's call.* A leaf shelter has no walls, so it doesn't count. Rooms are detected automatically and re-checked when blocks change.
- **General furniture:** chairs and tables can go anywhere. In step 2 they're only placed and built; step 3 gives them a use. Storage furniture such as shelves holds more per cell. *Which pieces exist is Claude's call.*
- **The furniture tool:**
  - a ghost preview, R to rotate;
  - multi-cell pieces are allowed;
  - a piece needs a free, standable spot to be placed and used from;
  - built by colonists from materials.

  Furniture is drawn as simple blocky models and is hidden above the view level.
- **Production orders:** each workstation has a list of orders: make N, make forever, or **keep N in stock**. "Keep 200 planks in stock" is what lets production chains run themselves. *Claude's call, modeled on RimWorld's bills.*

### Research

- **How it works:**
  - The player picks a project on a **tech tree screen**.
  - A colonist with Research enabled works at a research bench inside a research room.
  - Progress depends on their Research skill. A finished project unlocks its content.
- **Where research starts:** the very first projects are researched at the **simple research bench**, which works anywhere. A proper research room makes research faster. *That bonus is Claude's call.*
- **Known from the start:** shelter, storage zones, gathering zones, dig and chop orders, the simple research bench, the sawhorse, the proper research and sawing benches, and the free-build pieces listed above.
- **The step 2 tree** *(Claude's call: the content and costs are placeholders for the builder to tune)*:

  | Project | Unlocks | Needs |
  |---|---|---|
  | Basic house | the house tool's editable ghost, wooden walls, doors, windows, inside walls | — |
  | Carpentry | the carpenter's bench, chairs, tables, shelves | — |
  | Stonecutting | the stone workshop and stonecutting bench; stone houses and stone free-build | — |
  | Multi-story building | floors, stairs and ladders in the house tool | Basic house |

- **Data-driven:** the tree is defined as data, so later steps can add projects without code changes elsewhere. Tools, new resources and ore discovery join it in later steps.

---

## UI for step 2

- **Toolbar:**
  - **Orders:** dig, chop, gather, cancel, deconstruct;
  - **Zones:** storage, woodcutting, rock, water, the room types;
  - **Build:** house, free-build, furniture;
  - **Research;**
  - **Work:** the priorities grid.
- **Screen elements:**
  - a colonist bar;
  - the selection info panel for colonists, items, furniture, zones and blueprints;
  - time controls and the day counter;
  - the step 1 level indicator.
- **Styling:** functional placeholder styling, as in step 1. The debug overlay gains colonist count, pathfinding stats and the job count.

## Save and load

Everything new saves and loads exactly:
- colonists, with position, skills, experience, carried item and current job;
- items, zones, blueprints and job progress;
- furniture and production orders;
- research progress;
- the simulation clock and the per-voxel block state.

## Architecture constraints

- **Step 1's rules carry over:**
  - The simulation is plain C# with no Godot dependency, unit-tested and runnable headless.
  - Godot draws, animates and handles input.
  - Block types and their properties are defined in one place.
- **Defined as data, in one place each:** new content (items, work types, skills, furniture, room types, research projects). Adding one shouldn't take scattered edits.
- **Plain logic that can be tested:**
  - the house planner, which turns a footprint, openings, floors and stairs into blocks, digs and foundation;
  - slope placement;
  - the movement rules.
- **Fixed sim ticks:** the simulation advances in fixed ticks, decoupled from rendering, so speed controls and a later sim thread are possible.
- **Tests:**
  - movement rules (stairs, ladders, ramps and slopes; hops and climbs only over natural terrain, never built blocks; doors; water; unreachable targets);
  - job claims and priorities, including the skill tiebreak;
  - no-trap rules;
  - skill growth;
  - hauling and storage capacity;
  - gathering zones;
  - house planner output (merging, walls at inside corners, no doors on corners, stairs validity, roof, dig and foundation);
  - room detection (enclosed and roofed, natural rock counting as wall) and room requirements; basic items working anywhere;
  - production orders ("keep N in stock");
  - research unlocks;
  - slope placement and the flat start site;
  - save/load round trips that include colonists.

## Left to the builder

- **Pathfinding:** the algorithm, caching and threading.
- **Look and assets:** the colonist model and animation, the furniture models, and the stack limits, carry limits, work speeds and skill curves.
- **Content and balance:** research costs, starter crate contents, and the exact UI layout.
- **Data:** how block state is stored.

Any choice is fine as long as the behavior and targets above are met and step 3 isn't blocked.

## Done when

On the developer's machine, on a Large map:

**2a:**
1. A new game starts on a flat, natural-looking site with 3 colonists, and slopes soften the surface while cliffs and caves stay sharp.
2. Colonists dig a multi-level mine using carved stairs, and complete wall, floor and stair designations. They walk via stairs, ladders and slopes, hop and climb natural ledges only when there's no other way, get around the upper caves to mine coal and iron, and never strand themselves.
3. Colonists gravitate to the work they're best at, and their skills grow.
4. About 20 colonists run at 60 fps, and the bulk-spawn benchmark records frame and tick times at 20, 50 and 100 colonists.

**2b:**
5. Colonists fell trees in a woodcutting zone, mine stone and ore, gather rocks and water, and haul it all into storage.
6. Construction uses up stored materials.

**2c:**
7. With research, the player drafts an L-shaped, two-story wooden house with inside walls, doors, windows and stairs using the house tool. Colonists build it from hauled materials.
8. A wood workshop with a "keep 200 planks in stock" order keeps the colony supplied while the player does something else.

**Throughout:**
9. Saving and reloading restores everything exactly.
