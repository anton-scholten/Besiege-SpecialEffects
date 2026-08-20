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
- The Particle Emitter's Heatwave drew drifting grey triangles instead of heat
  haze. It borrowed an opaque stained-glass material off the bomb's explosion,
  which fills a particle's quad with flat colour rather than bending what is
  behind it. It is built for the job now, from the game's own particle
  distortion shader and a ripple pattern generated in code, and it fades out at
  the edge of each particle so there is no visible quad.
- The Spot Light's strobe **Activate** switch jumped down the panel when you
  turned it on, because the controls it reveals were listed above it. It stays
  put under the page menu now.

**Changed**

- Rebuilt against current Besiege. `ExplodeOnCollideBlock.explosionEffectPrefab`
  had been renamed since 2018, which is what stopped the mod compiling at all.
- The Heatwave's slider is **Heatwave Strength** rather than Heatwave Scale, and
  sets how hard the haze bends the view. Same range; the default is unchanged.
- None of the four blocks offer a skin in the mapper any more. Each is its own
  mesh and texture and had nothing to swap to. The block-menu icons are
  unaffected.
- The spot light entity's event no longer prints its own arguments to the mod
  console every time it fires.
- One console message said "Fod Density Set."; it says "Fog" now.

The source was recovered from the shipped assembly — the original was lost. See
[docs/RECOVERY.md](docs/RECOVERY.md).

## 0.3.2

The last released Workshop version, built in 2018.
