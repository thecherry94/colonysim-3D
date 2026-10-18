# ColonySim 3D: Game Vision

**Status:** agreed in brainstorming on 2026-10-18. This is the north star for every later design. Each build step gets its own detailed design document, which must stay consistent with this one.

## What it is

A colony management simulation in the spirit of **RimWorld**, played in **3D with Minecraft-like block graphics**. The player looks after a small group of colonists on a generated map. The colonists dig into the earth for resources, build multi-story homes, and try to survive nature. Later they deal with other factions on a generated planet.

## Why it exists

RimWorld's flat 2D map can't do two things this game is built around:

- **Building up:** real multi-story houses and structures.
- **Digging down:** colonists tunneling into the earth to mine resources, breaking into caves, and building underground.

The vertical dimension is the reason this game exists. Every system should take advantage of it, not just put up with it.

## Ambition

A hobby project by a solo developer who knows Godot and C# and is building it with Claude. If it turns out well, it might be sold for a few dollars on Steam as an indie game. Priorities follow from that: a fun, coherent, working game beats feature breadth. The code has to stay maintainable for one person over a long time.

## Design pillars

1. **A vertical colony.** The world is made entirely of blocks that can be dug out and built on, from mountain peaks down to bedrock.
2. **Colonists are people.** Each colonist is an individual with needs, moods, traits, skills, passions and relationships. They make believable choices of their own and don't feel like robots following orders.
3. **The underground is readable.** The player can always see and give orders at any depth using a **slice view**. You pick a view level, everything above it is hidden, and solid blocks cut by the slice are drawn dark so rooms, tunnels and caves read like a floor plan. This was proven in the brainstorming prototype, `docs/superpowers/specs/prototypes/slice-view-tools.html`.
4. **Indirect control.** As in RimWorld, the player marks jobs (dig here, build a wall there), sets work priorities, and builds facilities. Colonists decide who does what and when.

## Anti-goals

These would make the game a failure even if it technically works:

- **Transparency or x-ray views for seeing underground.** The developer tried this in an earlier attempt and it was flawed. The slice view replaces it.
- **Colonists who act like robots,** executing orders with no regard for their own needs and personality.
- **Endless Minecraft-style worlds.** Maps are bounded, because a colony sim needs edges and has to run fast.
- **Drifting into a first-person or survival-crafting game.** The player is an overseer, not an avatar.

## The full game, by build step

Each step ends in something you can run and play with. Each step is designed in detail only when it's about to start, so it can be designed against a working version of the previous step.

### Step 1: A world you can dig and build in
Generated voxel maps with layers, caves, ore, mountains, valleys, rivers, trees and rocks. Chunked rendering that stays fast on large maps, the slice view with fog of war, an RTS-style camera, and designation tools for dig, wall, floor and stairs. No colonists yet. See `2026-10-18-step1-voxel-world-design.md`.

### Step 2: Colonists who live in it
- Pathfinding through 3D tunnels and multi-story buildings, using stairs and ladders.
- A job system: colonists pick jobs by weighing player-set work priorities against their own needs.
- **Needs:** hunger, thirst, rest, comfort, recreation and social, and possibly more.
- **Mood:** needs and events ("ate a fine meal", "slept on the floor", "chatted by the fire") add up to one mood value. Low mood for too long triggers **mental breaks**, as in RimWorld.
- **Social life:** the player builds dedicated gathering spots such as fireplaces and gathering zones. Colonists who need company go there, and their interactions build or damage relationships.
- **Individuality:** RimWorld-style **traits** that change behavior and mood (for example, someone who hates being underground), **skills** that improve with practice, **passions** for faster learning and mood, and **relationships** (friends, rivals, lovers, family).
- Doors, windows and similar building pieces.

### Step 3: Survival
- Food chain: gathering, hunting, farming and cooking.
- Resources: wood, stone and ore become items that are hauled, stored and used.
- Wildlife: animals to hunt, and predators.
- Weather, seasons and temperature, as in RimWorld: winters to prepare for, heat and cold that harm colonists, and seasonal crops.

### Step 4: The planet
- A whole generated planet, split into tiles. Each tile can be played as its own map.
- Other factions living on the planet, with diplomacy and war.

## Technical foundation

- **Engine:** Godot **4.7.2 .NET** (C#), installed at `~/.local/share/godot/4.7.2-mono/` and available as the `godot` command. The .NET SDK is 10.0.104.
- **Development platform:** Linux (Nobara).
- **The simulation is kept apart from presentation.** World data, generation, jobs, and later colonist AI live in plain C# that doesn't depend on Godot. That code can be unit-tested and run headless. Godot handles rendering, input and UI. This matters most from step 2 onward, when the simulation grows complicated.
- **Determinism:** map generation is fully reproducible from a seed.
- **Scale:** 1 block is 1 meter. A colonist is about 1.8 blocks tall and needs 2 blocks of headroom.
