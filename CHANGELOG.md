# Changelog

## Unreleased

**Added**

- The **Text block** takes a key. **Activate** shows or hides the text, **Toggle**
  picks press-to-flip or hold, and **Start Shown** decides which state a run
  begins in. Existing machines start shown and stay shown, as before.
- Every block's **Activate** key can be driven by one of Besiege's variables
  instead of a keypress, in both hold and toggle modes.

  The emulated state lives on `MKey` beside the keyboard state, but its edges are
  only true inside Besiege's own `KeyEmulationUpdate` pass, which `Machine` runs
  from `FixedUpdate` once per emulation tick. `MKey.CheckEmulation` compares
  against a snapshot it advances the first time it is called in a fixed step, so
  reading an edge from an ordinary `Update` reports the same press again on every
  frame of that step — two or three presses for one, which cancels out any
  Toggle. All four blocks now take their edges in `KeyEmulationUpdate` and hand
  them to the frame update, which consumes each once.

- **Light shafts** on both the Spot Light block and the Spot Light level editor
  object: visible beams through the air, shadowed by whatever stands in them.
  Ported from EEX-slime's *No Light No Life* (Workshop item 3374723392) and
  folded into the lamps that were already here rather than added as a second
  object.

  On the block they are a sixth options page, **Shafts**, with an **Activate**
  switch and, under it, **Moving Shadows**, **Brightness**, **Fade**, and either
  **Start** and **End** along the cone (spot) or a **Volume X/Y/Z** box
  (directional). On the entity the same settings sit under a **Shafts** toggle on
  the SETTINGS tab. A point light has no beam to draw, so neither offers it one.

  Nothing extra has to be wired up to animate them: LightShafts reads the light's
  own intensity, colour, cone angle and range every frame, so the strobe pattern
  and the three Auto sliders drive the shafts for free.

  **Moving Shadows** off renders the shadowmap once and keeps it — much cheaper,
  but the silhouettes in the beam freeze. Leave it on for a beam something moves
  through.

- The Spot Light block is **lit in the build menu**, so the colour, the strobe,
  the auto sweeps and the shafts can all be judged without starting a run. There
  is no key to hold there, so it is simply on; none of the simulation's startup
  work happens, so the joint, the mass and the collider stay as you left them; and
  the sliders are re-read every frame rather than once, so moving one shows.

  Starting a run resets all of it — the strobe to its first step, the lamp to
  unlit and waiting on its key, the shafts off — so a run behaves exactly as it
  would have without a preview.

- The three **Auto** sweeps now start at their minimum each run, rather than at
  whatever phase the wall clock happened to be at. They are the same ping-pong;
  they are just measured from the start of the run instead of from the start of
  the game.

- Five more level variables on the Spot Light object, following the same rule as
  the others — negative hands the setting back to its slider: `shafts`,
  `shaftbrightness`, `shaftfade`, `shaftstart` and `shaftend`.

  The shafts themselves are
  [robcupisz/LightShafts](https://github.com/robcupisz/LightShafts) (public
  domain), the same code the original mod compiled in, vendored as
  `SEScripts/LightShafts.cs` and driven by shaders in the two `LightShafts` asset
  bundles.

  Deviations from that mod, all deliberate:

  - Its multiplayer position sync is not ported. It broadcast a placed light's
    transform every 50 fixed frames; level objects do not move.
  - Its seven-language UI is not ported; the labels are the English ones.
  - The shaft shaders are chosen by trying the platform's bundle and falling back
    to the other, rather than assuming anything not Windows is a Mac. Besiege on
    Linux is an OpenGL build and wants the Mac bundle.
  - `LightShafts` gained an `OnDestroy`. Upstream never had one because its owner
    is a scene light that outlives the level; here a light is created and
    destroyed freely, and every render texture, material and the per-light Depth
    Camera it allocates is `HideAndDontSave`, so nothing else would collect them.
  - **Start** defaults to `0`, so a beam reaches the lamp unless told otherwise.
  - **Start** and **End** are held apart by at least `0.01`, and **Start** is
    held at least `0.005` off the lamp. Both are divisors in the shaft geometry:
    the raymarch shader unstretches its shadowmap UVs by dividing by the distance
    from the cone's apex, so a volume reaching the apex divides by zero and the
    whole beam comes out NaN. Start at `0` drew no shafts at all before this.
  - Switching **Moving Shadows** used to make the beam vanish. Upstream only
    handles the static-to-dynamic direction; the other way round left the kept
    shadowmap pointing at a pooled temporary that had already been handed back,
    and `InitRenderTexture` went on using it because its size still matched.
  - **Fade** reaches a spot light at all. `m_Extinction` only feeds the raymarch
    shader's directional branch; its spot branch has a fixed `1/(1+25d²)` falloff
    with no setting behind it, so on the default light type the slider did
    nothing. The shader's lookup-table path for a custom falloff is filled
    instead — with `exp(-fade·d)` for a directional light, which is exactly what
    its own branch computes, and with that curve on top of the fixed falloff for a
    spot light, so a Fade of `0` still looks like the falloff it replaces.
  - A lamp is left out of its own shadowmap. Its housing and lens sit inside the
    cone less than a unit in front of the light, so they were the nearest thing
    the shadowmap saw and they blacked the whole beam out: no shafts at all until
    **Start** cleared the block, at exactly `0.963 / range`, or until the lens was
    set to `Hidden`. A real lamp does not shade itself.
  - A kept shadowmap is re-rendered when the lamp itself moves, turns, or changes
    range or cone angle. Upstream's light is placed once in a scene; a block or a
    level object is aimed while you watch, and a shadowmap from where the lamp
    used to be reads as broken rather than as cheap.

  Existing machines and levels are unaffected: every key is new, so they load
  with shafts off and look exactly as they did. The **Shafts** page is appended
  to the block's options menu as index 5, leaving General, Brightness, Cone
  Angle, Color and Strobe where they were.

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

- The Glass block logged `IsDown is deprecated, please use IsHeld` every frame of
  every run in which its Toggle was off. `MKey.IsDown` forwards to `IsHeld` after
  writing that warning.
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
