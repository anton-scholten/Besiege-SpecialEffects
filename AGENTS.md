# Working on this repository

Notes for anyone — human or AI — changing this mod. The README is for people who
just want to use it; nothing here needs repeating there.

How the C# was recovered from the shipped assembly, and how faithful the result
is, are in [docs/RECOVERY.md](docs/RECOVERY.md).

## Layout

The folder Besiege loads is `SpecialEffects/`, because that subfolder is the
whole of what gets uploaded to the Workshop. Everything beside it is not part of
the mod.

```
SpecialEffects/Mod.xml                       manifest: assembly, blocks, entity, resources
SpecialEffects/SpotLight.xml                 the four blocks: mesh, colliders, module
SpecialEffects/GlassBlock.xml
SpecialEffects/ParticleEmitter.xml
SpecialEffects/TextBlock.xml
SpecialEffects/SpotLightEntity.xml           the level editor object: mesh, colliders, triggers
SpecialEffects/SpecialEffectsAssembly.dll    built by tools/build.sh (checked in, the game loads it)
SpecialEffects/Resources/                    meshes, textures, the font asset bundle
SpecialEffects/SEScripts/*.cs                mod source; not read by the game
tools/build.sh                               compiles with Besiege's own compiler
tools/verify-build.sh                        the check to run after editing any .cs
tools/install.sh                             builds and installs into the game
docs/, Previous_stuff/                       notes and working files; not loaded by anything
```

`SpecialEffects/SpecialEffectsAssembly.dll` is committed on purpose. `Mod.xml`
names it as an `<Assembly>`, so a checkout has to carry a built one or the mod
does not load.

`SEScripts/` sits inside `SpecialEffects/` so the sources travel with the mod
folder, the way Clippy, Git View, Moon and Return 2 Center do it. Besiege only
reads what `Mod.xml` names, so the `.cs` files there are ignored by the game;
`tools/install.sh --copy` strips them out of the copy it makes.

One file per type, named after it. The four `<Block>Module` classes are two-line
XML markers and could share a file; they do not, so that a block's module and its
behaviour sit next to each other in a directory listing.

Four of those files are shared rather than a block's own: `Attach` (find or make a
child object, get or add a component), `Controls` (show or hide a run of mapper
controls), `Strobe` (the sequence walker below), and `Skins`.

## Hard rules

**Never change `<ID>` in `Mod.xml`.** The game generated it on first load, and
changing it breaks every saved machine that references the mod. The same goes for
`<ID>1</ID>`..`<ID>4</ID>` in the four block XMLs, `<ID>1</ID>` in
`SpotLightEntity.xml`, and for the four module names — `SpotLight`, `GlassBlock`,
`ParticleEmitter`, `TextBlock` — each of which is spelled in three places that
must agree: the `[XmlRoot]` on the module class, the `AddBlockModule` call in
`Mod.OnLoad`, and the element inside `<Modules>` in the block XML. The `modid`
attribute on those elements is the mod's GUID again.

**Do not rename a mapper key.** The second argument to every `AddKey` /
`AddSlider` / `AddToggle` / `AddText` / `AddColourSlider`, and the first argument
to every `AddMenu`, is what a saved machine stores that setting under. Renaming
one silently resets it on every existing machine. The first argument to the
others — `displayName` — is only the mapper label and is free to change.

Note the keys are not tidy and must stay untidy: `"Activate"`, `"Range"` and
`"Transparency"` are their own keys, `ColorStart` has no `Key` suffix where
`ColorKey` does, and the Spot Light's strobe page uses
`"PatternAffectsConeAnglesKey"` — plural, unlike its two neighbours.

**Variables reach a block through its keys and nowhere else.** `MKey` carries the
whole feature — `Emulating`, `EmulationPressed`, `EmulationHeld(includePressed)`,
`EmulationReleased` — while `MSlider`, `MToggle`, `MMenu` and `MapperType` have
nothing variable-related on them at all. `KeyReader` is the only place that
should touch any of it.

**An emulated edge is only true inside `KeyEmulationUpdate`.**
`MKey.CheckEmulation` compares `Emulating` against a snapshot it advances the
first time it is called in a given `Time.fixedTime`, so a second call inside the
same fixed step reports the same rising edge again. Read one from an ordinary
`Update` and every variable press lands as two or three, which silently breaks
anything toggling. `Machine.FixedUpdate` runs `EmulationUpdateBlock` — and
through it `ModBlockBehaviour.KeyEmulationUpdate` — once per emulation tick, and
`InternalModding.Blocks.BlockPrefabCreator.SetupBehaviour` sets
`BlockPrefab.RegisterEmulationUpdate = true` for every modded block, so the hook
is already live and needs no opting in. Take the edges there, latch them, and let
the frame update consume each once: that is what `SpinningModuleBehaviour` and
`SpewingModuleBehaviour` do. `ShootingModuleBehaviour` does *not* — it reads
`IsPressed || EmulationPressed()` straight from `SimulateUpdateAlways` — and has
the double-fire bug to go with it, so it is not the one to copy.

Note the emulation pass only runs at all when the machine holds a block whose
`Prefab.EmulatesAnyKeys` is set (`Machine.HasEmulationBlocks`). With nothing
emulating there are no edges to miss, so keyboard-only play is unaffected.

**`MKey.IsDown` is deprecated and says so, loudly.** It forwards to `IsHeld` after
a `Debug.LogWarning`, so a per-frame caller floods the console. Use `IsHeld`.

**Light shafts are expensive and uncapped.** One enabled `LightShafts` costs a
full extra scene render per frame -- `Camera.RenderWithShader` into a 1024²
shadowmap -- plus six 512² render textures and about five fullscreen passes. That
is by design: a machine with a dozen shaft blocks will crawl, and there is
deliberately nothing stopping it. `Moving Shadows` off is the cheap mode.

**Do not reorder a menu.** A machine saves its choice as an *index*, so inserting
anything but at the end repoints every saved block at a different option. That
covers the Spot Light's options pages and lens styles, the Particle Emitter's
settings pages and its three `Lifetime/Speed/Random` menus, the Glass block's
shader and shape menus, and the Text block's font and style menus. The named
constants at the top of each behaviour exist to make the index meanings visible.

The Particle Emitter's emission-shape menu is worse than that: its indices are
`ParticleSystemShapeType` values, cast straight across. The list has to stay in
the enum's order, and its default is written as `(int)ParticleSystemShapeType.Cone`
so that stays visible.

**The particle textures live in `ParticleEmitter.xml`, not in code.** The
`<Particles>` list under the module element is what fills the block's Texture
menu, in that order — so it is an index like any other menu, and entries only go
on the end. Each `<Texture name="...">` there must match a `<Texture>` in
`Mod.xml`'s `<Resources>`.

**Run `./tools/verify-build.sh` after editing any `.cs`.** Besiege's compiler is
ancient — write C# 4: no interpolated strings, no `?.`, no `nameof`, no
expression-bodied members, and no `enum` declarations (they segfault it). That
last one is why the menu-index constants are `const int`.

**Resource paths in `Mod.xml` are case-sensitive in practice.** They are written
Windows-style (`Blocks\SpotLight_mesh.obj`); `InternalModding.Misc.ModPaths.
GetFilePath` replaces the separator, but nothing fixes the case, so a path that
works on Windows can silently fail on Linux. `tools/install.sh` checks every path
in the manifest before it installs anything.

**The five adding points in each block XML are the house standard; keep them.**
Top at `(0,0,1.0)`, and the four sides at `z=0.5` with `±0.5` offsets and their
matching `±90` rotations. They are what makes a modded block snap onto the same
grid as a base-game one. The Glass block deliberately uses `±0.05` on its left
and right points instead: the pane is 0.09 thick, so the base-game offset would
put those points well clear of it. The Particle Emitter has none — it is a nozzle
and nothing is meant to attach to its sides.

## Third-party code and art

`SEScripts/LightShafts.cs` is vendored from
[robcupisz/LightShafts](https://github.com/robcupisz/LightShafts), which is public
domain. Upstream splits it across three partial-class files; it is merged into one
here, and the header comment lists every change made to it. The shaders it needs
are prebuilt Unity 5.4 asset bundles in `Resources/LightShafts/`, taken from
EEX-slime's *No Light No Life* (Workshop 3374723392) because there is no Unity
editor on this machine to rebuild them from the upstream `.shader` files.

No art was carried over: the shafts hang off the lamps this mod already had, so
the only third-party files are the code above and the shader bundles.

## Why it is built the way it is

**`System.Xml` is on the mod loader's blacklist and this mod references it
anyway.** That is not an oversight. `InternalModding.Assemblies.AssemblyScanner`
walks field types, method locals and IL operands; it never enumerates custom
attributes. The `[XmlRoot]` / `[XmlArray]` markers on the four modules are
metadata, so they pass, and they are the only way to name the elements a block
module deserialises. `tools/build.sh` runs a blacklist check over every build
rather than trusting that reasoning.

**`DynamicText` is a fifth assembly reference.** The Text block's mesh is drawn
by `DynamicText`, which Besiege ships in `Besiege_Data/Managed/` and uses for its
own in-level signs. Nothing else here needs it, and it is not on the blacklist.

**Everything is applied on the third simulated frame, not the first.** All four
behaviours count to `StartFrame` before doing their setup. `SafeAwake` builds the
mapper controls, but the block's joint, rigidbody and visual controller are not
settled until the machine has been simulating for a few frames — the Spot Light
and the Particle Emitter both raise their joint's break force there, and all four
touch the visual controller.

**Most settings are read once, at that frame.** Changing a slider mid-run does
nothing until the next run; that is how the mod has always worked and the mapper
is laid out on that assumption. The exceptions are the ones that cannot work that
way: the Glass block's colour and the Spot Light's whole animation section, which
are per-frame by nature, and the Particle Emitter's three `Random` modes, which
re-roll every frame.

**Two blocks give up their mass to lose their collider.** A Glass pane with
*Collider* off, a Text block with *Collider* off, a Particle Emitter with *No
Collider* on, and a Spot Light in any lens style but *Normal* all set
`Rigidbody.mass = 0` as well as `detectCollisions = false`. They are decoration
at that point, and leaving the mass on would hang weight off the machine that
nothing can hold up.

**The Spot Light and the Glass block run the same strobe, and share `Strobe` for
it.** A string of characters is walked one per `Interval` seconds, with the frames
in between blending towards the next character's values. `-` is a gap; a digit is
a value, but only with *Numbers affect* on; anything else holds what is already
set.

`Strobe` owns the walking only. What a character *means* is not shared and should
not be: a light reads a brightness, a cone angle and a hue out of one; a pane
reads a colour and an alpha. `Strobe.Step` returns false on the frame the sequence
rolls over — nothing is drawn that frame, which is the original behaviour — and
sets `restart` on the frame the pair changes, which is when each block re-reads
its own values. Reading them on that frame only is deliberate: a slider moved
mid-run takes effect at the next character rather than part-way through a blend.

**The heat shimmer draws nothing of its own — it bends what is behind it.**
`Heatwave.cs` builds a material from the game's `Particles/Distort` shader, which
grabs the screen once a frame and offsets it per pixel by a normal map. This mod
ships no shader and no texture for that: the normal map (concentric ripples) and
its mask are generated in code, once, and shared by every emitter.

The ripples fade to flat before they reach the edge of the texture, and the mask
fades to black there too. Both are deliberate and either alone would do: a
distortion that is still non-zero at the edge of a particle's quad makes the
quad's own outline visible, which is a hard-edged rectangle sitting in mid-air.
Keep the falloff if you retune `Ripples` or `Slope`.

`_BumpMap` is unpacked DXT5nm-style by that shader — X out of alpha, Y out of
green — so `RippleNormals` writes X into red *and* alpha. That way it decodes
correctly whether the shader was compiled for the DXT5nm path or the plain RGB
one, and the texture is built linear rather than sRGB because both textures are
numbers, not colour.

**The shimmer is a second particle system, a child of the first.** Because
`ParticleSystem.Play` and `Stop` both recurse into child systems, it starts and
stops with the emitter without being driven separately — the one case that needs
a hand is the emitter being switched on during the frames before `Begin` runs,
which `ConfigureHeatwave` covers. It is found by name and reused, not cloned:
`Begin` runs again on every simulation start, so cloning would stack up another
shimmer each run.

**All four blocks hide the mapper's skin picker, through `Skins.Hide`.** Each is
its own mesh and its own texture with nothing to swap to, so the row is only ever
an empty choice.

**Do not do this with `BlockPrefab.SkinCanBeChanged`.** It looks like the switch
for it — `CanGetNewVisuals` reads it and `BlockMapper.RefreshLists` skips the
skin section when it is false — and it works, but `BlockPrefab.SetIcons` reads it
too, and only calls `VisualController.SetPrefabIcons()` when it is true. Turn it
off at prefab creation and every one of the mod's blocks shows a placeholder
texture in the block menu instead of its icon. That was tried; the placeholder is
what it looks like.

What `Skins.Hide` does instead is hide the control. `RefreshLists` builds the
block's `MVisual` and hands it to a `GenericController`, and
`GenericController.CreateContainers` skips any `MapperType` whose
`DisplayInMapper` is false — the same mechanism the Spot Light's option pages
use. The `MVisual` has to exist before the mapper is first opened, or the game
builds it there and shows it that once, so `Skins.Hide` builds it with the same
call `RefreshLists` would. After that `RefreshLists` takes its reuse path, which
refreshes the items and the label and leaves `DisplayInMapper` alone.

Neither approach removes the *collapsed* skin button. `RefreshLists` registers
that one — under `StatMaster.collapseSkinMapper`, which the player flips by
collapsing the skin panel on any block and which sticks for the session — before
it looks at the block at all. Only Besiege's own Skins setting
(`OptionsMaster.skinsEnabled`) turns that off.

**If you ever do need a block's prefab from `OnBlockPrefabCreation`, take it off
the `GameObject` you are handed.** The `blockId` that callback gets is the
mod-local id — 1 to 4, the `<ID>` in the block XMLs — so
`PrefabMaster.BlockPrefabs[blockId]` answers with a base-game block of that
number, and the prefab is not registered there yet in any case:
`BlockLoader.CreatePrefab` fires the callback and `RegisterPrefab` is a later
step. `BlockPrefabCreator.SetupBehaviour` has already filled in
`BlockBehaviour.Prefab` by then.

**There are no hover tips on mapper controls, and that was tried properly.** The
variable reference lives in the README instead. `SaveableDataHolder`'s
`AddToggle(name, key, tooltipText, default)` discards the tooltip argument, the
game's `Tooltip` component is prefab-driven behind a `protected Init`, and a
hand-built one has to find each control's label, project it to screen space
through the right camera, and draw its own panel — several days of screenshot
round-trips for a line of text that a README carries better.

**There is no modded event any more, and adding one back is a step backwards.**
The mod used to ship a `SpotLight` event, and it was removed because everything it
could do is done better elsewhere. Two facts drove that, and both are worth
knowing before anyone reaches for `ModEvents.RegisterCallback` again:

- **A modded event cannot have a slider or a colour picker.** Its `<Properties>`
  array accepts exactly `Choice`, `Icon`, `NumberInput`, `Picker`, `Row`,
  `TeamButton`, `Text`, `TextInput` and `Toggle` — and `Picker` picks *entities*,
  not colours. So a brightness was a typed number and a colour was a typed hex
  string. The game has no better widget to give.
- **A modded event only reaches the entity whose logic chain it sits in.** The
  game's own Modify Variable event has an entity picker, so it can reach one named
  lamp from a trigger anywhere in the level.

The SETTINGS tab plus level variables cover the same ground with the game's own
sliders and the game's own picker.

**The entity's SETTINGS tab is a mapper like a block's.**
`LevelEntity.EntityBehaviour` is a `GenericEntity`, which is a
`SaveableDataHolder` — the same base a `BlockBehaviour` has — so it takes
`AddSlider`, `AddColourSlider`, `AddMenu` and `AddToggle`. Its keys are saved into
the level, so the *Do not rename a mapper key* rule covers them too.

Getting at it needs a trick: `ModEntryPoint` has prefab callbacks and nothing
else, and stock entities add their controls from a `GenericEntity.Init` override
a mod cannot supply. So `Mod.OnEntityPrefabCreation` attaches
`SpotLightEntityBehaviour` to the *prefab*, and because
`LevelEditor.InstantiatePrefab` clones that GameObject for every placed object,
each entity ends up with its own copy — whose `Start` is the per-instance hook.
The lens and the light are built on the prefab for the same reason.

**`Mod.OnEntityPrefabCreation` builds the light, the lens and the behaviour on
the prefab**, so dropping the object into a level lights something with no wiring
at all. It has the same id trap as its block counterpart: the `entityId` is the
mod-local one, the `<ID>` in `SpotLightEntity.xml`.

**Do not write the beam's rotation down as a constant.** It was tried twice, from
the `<Mesh><Rotation>` in `SpotLightEntity.xml`, and was wrong both times: the
angle that reaches the housing is not that number alone. `SpotLightEntity.
AimAtBarrel` copies the rotation off the `Vis` transform instead — whatever the
XML, the mod loader and the editor between them did to the housing ends up there
— and does it every frame, because the mesh loads asynchronously and the object
can be turned after it lands. The beam and the lens both hang off one `LightHead`
child so they cannot drift apart from each other either.

**The entity answers to ten level variables**: `brightness`, `angle`, `range`,
`red`, `green`, `blue`, `type`, `illumination`, `lens` and `housing`. A level sets
them with the game's own Modify Variable event. Variables are `float` throughout
Besiege (`Dictionary<string, float>`, `SetVariable(..., Single)`) — there are no
string variables, which is why a colour takes three and the two menus are driven
by their index. The colour channels read 0-1, or 0-255 if any of the three is
above 1.

The entity's controls are added in **`Awake`, not `Start`**: `LevelXMLLoader`
instantiates the object and calls `LoadEntityData` in the same breath, while
`Start` does not run until the end of the frame — controls added that late are
not there for the saved values to land in, and every setting reads back as its
default on level start.

**A negative value means "no opinion"** and hands that one setting back to the
SETTINGS tab. That is not a nicety: nothing in Besiege can delete a variable once
it is set, so without the sentinel a level could take a lamp over and never give
it back.

**The mapper groups controls by kind, not by the order they were added.**
`BlockMapper.ShowMapper` lays out one controller per control type, top to bottom:
non-footer menus, keys, toggles, values, sliders, colour sliders, emulated keys,
limits, visuals, text, teams, custom, then footer menus. Registration order only
decides the order *within* a group, and hidden controls are skipped entirely.

That is why a toggle which reveals other toggles has to be registered before
them: otherwise switching it on inserts the revealed ones above it and the thing
you just clicked jumps down the panel. The Spot Light's strobe *Activate* is
registered first for exactly this reason. It is also why the page menus appear at
the top however late they are added, and why the Spot Light's light type,
illumination and texture menus sit at the bottom — those three pass
`footerMenu: true`.

## What was changed, and what is left alone

The 2018 assembly was recovered faithfully first, and then changed in these ways.
Read this before "fixing" any of it back.

**The heatwave was rebuilt from scratch.** The 2018 version cloned the whole
emitter with `Instantiate` and painted the clone with a material scavenged off
the bomb's explosion — `PrefabMaster.BlockPrefabs[Bomb]` →
`ExplodeOnCollideBlock.explosionEffectPrefab` → `PyroclasticPuff` → `Ripple`.
That field had been renamed to `explosionEffect`, which is what stopped the mod
compiling at all; fixing the name made it compile and did not make it work.

The material behind that name is `FX/Glass/Stained BumpDistort`, an *opaque*
stained-glass shader meant for a ring-shaped mesh. On a particle billboard it
fills the quad, and its tint texture — a smoke ramp, sampled over UVs that mean
nothing on a quad — comes out as flat grey. What you saw in the level was a drift
of hard-edged grey shapes, not haze. The clone made it worse: parented under the
emitter it inherited that transform's 0.1 scale twice over, and it went through
`ParticleSystemRenderer` settings it had no business inheriting.

It is `Heatwave.cs` and `ConfigureHeatwave` now — the game's own
`Particles/Distort` shader on a purpose-made child system. See the section above.

**Four texture paths in `Mod.xml` had the wrong case** — `Hex.png`, `Light1.png`,
`Light2.png`, `Fire.png` against files named `hex.png`, `light1.png`,
`light2.png`, `fire.png`. They loaded on Windows and not on Linux. Only the
`path` attributes changed; the resource `name`s, which is what
`ParticleEmitter.xml` and saved machines refer to, are untouched.

**The Particle Emitter's texture-name list was `static`.** Every block placed
appended its 32 texture names to the same shared `List<string>`, and the same
list object was handed to `AddMenu`, so a machine with two emitters offered 64
entries, three offered 96, and the menu index a machine had saved pointed at the
wrong one. It is an instance field now.

**Nothing was reset between simulation runs.** Besiege keeps the machine, and so
these behaviours, alive when you stop simulating, and every `hasStarted` was set
once and never cleared. From the second run on: the Text block never hid its
mesh, the Glass pane resumed mid-pattern with its visibility latched wherever it
had stopped, the Spot Light kept the first run's lens style and pattern position,
and the Particle Emitter ignored every changed setting. All four now override
`OnSimulateStart`.

**The `Text` property is gone.** All four modules carried a public
`string Text { get; set; }` that nothing read and no block XML set — an artefact
of whatever the original fork was taken from.

**`ParticleEmitter.Validate` is gone.** It overrode `Element.Validate` to call
`base.Validate` and return the result, which is what not overriding it does.

**The spot light entity's event no longer logs its own arguments.** Every
`EntitySpotLight` call printed the light type and the illumination mode to the
mod console — six `ModConsole.Log` calls of the kind that get left in after
debugging. The block-level `"Texture not found!"` and `"Failed to load texture!"`
messages are real diagnostics and stayed.

**One console message said `"Fod Density Set. "`.** It says `"Fog"` now.

**The Particle Emitter's "Heatwave Scale" slider is "Heatwave Strength".** It
used to set the bump map's tiling; it sets the distortion's `_BumpAmt` now, which
is the thing a player was reaching for. Same key, same range, and the default
still lands on the shader's own default strength.

Left alone deliberately:

- **`Cone.obj`, `Lens.obj` and `SquareCookie.png` are declared under an
  `<!-- Unused -->` comment in `Mod.xml`.** `Lens` is not unused — it is the spot
  light's lens mesh — and the comment is wrong about it. The other two really are
  unreferenced, and are left declared because removing a resource is the kind of
  change that only pays off if something else needs doing to that file anyway.
- **The Glass block's held-down handling reasserts its visibility every frame,
  including a branch that only fires with the key held *and* the pattern
  running.** It reads like a leftover. It is also exactly what the 2018 assembly
  did, and the pattern's own colour writes make it observable, so it stays until
  someone can watch it in a level.
- **Neither the blocks nor the entity read emulated keys**, so nothing but a
  player can switch a light or an emitter on. Worth adding — `MKey.EmulationValue`
  is how the game's own blocks do it — and worth doing deliberately, with the
  game open, because each of the four blocks latches its key differently.
