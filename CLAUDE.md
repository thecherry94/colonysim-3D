# ColonySim 3D

A RimWorld-like colony sim with Minecraft-like voxels, built by a solo hobby developer. It might become a cheap indie game on Steam. Start fresh: never reference or reuse the developer's earlier colonysim attempt.

## Stack
- Godot 4.7.2 .NET (C#) on Linux, with the .NET 10 SDK.
- `src/Core` (`ColonySim.Core`) is plain C# with no Godot dependency, targeting net8.0 with RollForward LatestMajor. All simulation code lives here and is unit tested.
- `src/Game` is the Godot side: rendering, input, UI and the screenshot harness.
- `tests/Core.Tests` holds the xUnit tests (net10.0).

## Commands
- Build: `dotnet build ColonySim.sln` must give 0 warnings.
- Tests: `dotnet test tests/Core.Tests --filter "Category!=Perf"`.
- Perf tests: `dotnet test tests/Core.Tests -c Release --filter "Category=Perf"`. They only enforce their limits in Release.
- Screenshot harness:
  - Run `dotnet build` first, then `COLONYSIM_SHOTS=tests/visual/shots godot --path .`. It writes `tests/visual/shots/metrics.txt` and PNGs.
  - Every `*_ok` metric must be 1.
  - The harness runs with vsync off and a 60 fps cap, because the compositor throttles a hidden window. Frame-time measurements go inside `Uncapped(...)`.
- A browser running heavy pages (for example the three.js prototype) skews perf numbers. Close it before judging timings.

## Workflow
- Use the superpowers skills to keep the game coherent.
  - Each build step goes through brainstorm, then a spec in `docs/superpowers/specs/`, then a plan in `docs/superpowers/plans/`, then subagent-driven execution, then a whole-branch review, then merge.
  - Check every step against the vision doc `docs/superpowers/specs/2026-10-18-colonysim-vision-design.md`.
  - Design each step only when it is about to start.
- Subagent models: Opus for hard problems (algorithms, rendering and perf, the final review), Sonnet for normal implementation and reviews, Haiku for quick research. Always set the model explicitly.
- Work on a branch per step (for example `step2a-colonists`), then merge into `main` and push.
- Scale target: 20 colonists at 60 fps now, with 100+ colonists per map possible later. Avoid colonists × colonists or per-tick whole-map work.

## Where things stand
- Step 1 (voxel world) and step 2a (colonists walk and work) are done and merged.
- Next is step 2b (items, hauling, starter crates, gathering zones), per the step 2 spec.
- Open follow-ups to fold into 2b: `docs/superpowers/notes/step2b-carryover.md`.
- The 2a plan's "Results" and "Execution rulings" sections record benchmarks and the decisions made during execution.
