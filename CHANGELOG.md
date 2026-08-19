# Changelog

## 0.4.0

Everything below is on top of 0.3.2, the last released version. Machines built
with that version load and behave as they did, apart from the fixes.

**Fixed**

- Four particle textures — Hex, Light1, Light2 and Fire — never loaded on Linux.
  Their paths in `Mod.xml` were capitalised differently from the files on disk,
  which only matters on a case-sensitive filesystem.
- The Particle Emitter's texture list was shared between blocks. A machine with
  two emitters offered every texture twice, three offered them three times, and
  the texture a machine had saved was no longer the one it got back.
- Nothing was reset between simulation runs. From the second run on, the Text
  block never hid its own mesh, the Glass pane picked up mid-pattern with its
  visibility stuck wherever the previous run left it, the Spot Light kept the
  first run's lens style and strobe position, and the Particle Emitter ignored
  every setting changed since.
- The heat shimmer looked up a chain of the game's own objects and assumed every
  step was there. It is checked now, so a future change on Besiege's side costs
  the Heatwave toggle rather than the whole block.

**Changed**

- Rebuilt against current Besiege. `ExplodeOnCollideBlock.explosionEffectPrefab`
  had been renamed since 2018, which is what stopped the mod compiling at all.
- The spot light entity's event no longer prints its own arguments to the mod
  console every time it fires.
- One console message said "Fod Density Set."; it says "Fog" now.

The source was recovered from the shipped assembly — the original was lost. See
[docs/RECOVERY.md](docs/RECOVERY.md).

## 0.3.2

The last released Workshop version, built in 2018.
