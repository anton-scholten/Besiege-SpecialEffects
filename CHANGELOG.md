# Changelog

## 0.4.0

Everything below is on top of 0.3.2, the last released version. Machines built
with that version load and behave as they did, apart from the fixes.

**Added**

- The **Spot Light** is now a finished level editor object, under Virtual. It
  lights as soon as it is placed, and its settings — type, brightness, colour,
  cone angle, range, illumination and the lens — are proper Besiege sliders and
  colour sliders on the object's SETTINGS tab. It carries the block's glowing
  lens, tinted to match. The `SpotLight` event still overrides all of it from a
  level's logic. Previously it was a stub: an unconfigured purple light floating
  beside the housing, shining out of its side, with no way to change it.
- The object and its event use the Spot Light block's icon rather than the mod's
  promo art.
- A level can drive a placed Spot Light with the game's own Modify Variable
  event, which has an entity picker, so one lamp can be changed without touching
  the others: `brightness`, `angle`, `range`, `red`, `green`, `blue`, `type`,
  `illumination`, `lens` and `housing`. Setting one negative gives that setting
  back to the object's own slider.

- The object's settings no longer reset to their defaults when a level starts.

- The README documents the level variables, with pictures.

**Removed**

- The mod's own `SpotLight` event. A modded event gets no slider and no colour
  picker from the game — brightness was a typed number and colour a typed hex
  string — and it could only reach the entity whose logic chain it sat in. The
  SETTINGS tab and the variables above do all of it with the game's own sliders
  and its own entity picker.

**Fixed**

- The entity's **Hide Visuals** event option did nothing. It hides the housing
  and leaves the beam now.
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
