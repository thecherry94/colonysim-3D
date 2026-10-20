# Step 2b carry-over (from step 2a reviews)

Step 2a (colonists walk and work) finished 2026-10-20 on branch `step2a-colonists`, with 301 tests and the benchmark at 2.8 ms per frame for 20 colonists. Reviews deferred these items to 2b.

**Behaviour gaps**
- A dig can still strand *other* colonists in rare layouts. StrandCheck only checks the digger's own way back.
- Step-aside has no fallback when every move is also in someone's way. This is theoretical so far.
- `ColonistWorker` ignores `Complete`'s false return. That's safe today only because of the earlier IsInTheWay check.
- "Roofs last" isn't honoured: interior roofs can go up before walls. Handle it with the 2c house tool.
- Digging over a cave leaves blocks floating over the void as Unreachable. The player needs ladders or stairs to reach them.

**Scaling (100+ colonists)**
- This is the main risk: NavChunk rebuilds cost 6–8 ms per surface chunk, and the sync at the start of each tick has no budget. 100 colonists still run at p99 8.3 ms per frame.
- One path request can hold the 15k-node FIFO budget for up to 40 ticks, because there's no per-request slice.
- Adding a dig costs 22 index lookups, so adding 200k jobs takes about 300 ms.

**Save**
- A colonist's current job isn't saved (the spec lists it).
- A colonist saved mid-fall reloads near the ledge instead of where it lands.
- Duplicate job positions on load are accepted silently.

**Code health**
- These files are large: `VisualShots.Colony.cs` (~840 lines) and `PathFinder.cs` (~700 lines).
- Priority and shirt magic numbers are duplicated in the loader and the factory.
- The simulation RNG reseeds on load, so F9 spawns repeat the opening rolls.

**Still true from step 1**
- VoxelWorld mutation and dirty tracking are main-thread only.
- `GetSurfaceHeight` is the *generated* surface, not the current top block.

**Not yet covered by end-to-end tests:** mining ore in the shallow caves, and "gravitate to best work".


**How to use:** when planning 2b, put the behaviour gaps and the scaling risk in its early tasks. Check each item against the code first, because it may already have changed.
