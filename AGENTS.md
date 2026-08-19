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
SpecialEffects/Mod.xml                       manifest: assembly, blocks, entity, event, resources
SpecialEffects/SpotLight.xml                 the four blocks: mesh, colliders, module
SpecialEffects/GlassBlock.xml
SpecialEffects/ParticleEmitter.xml
SpecialEffects/TextBlock.xml
SpecialEffects/SpotLightEntity.xml           the level entity (a stub; see below)
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

**The strobe patterns in the Spot Light and the Glass block are the same
machine.** A string of characters is walked one per `Interval` seconds, with the
frame in between interpolating towards the next character's values. `-` is a gap;
a digit is a value, but only with *Numbers affect* on; anything else holds what
is already set. They are deliberately *not* shared code — the two read different
things out of a character (a light has a brightness and a cone angle; a pane has
an alpha) and interpolate different sets of fields.

**The heat shimmer is the game's own.** `FindHeatwaveMaterial` walks
`PrefabMaster.BlockPrefabs[Bomb]` → `ExplodeOnCollideBlock.explosionEffect` →
`PyroclasticPuff` → `Ripple` and instantiates that renderer's material. Nothing
in this mod owns any of that hierarchy, so every step is null-checked: if Besiege
moves it again, the *Heatwave* toggle stops working and nothing else does.

## What was changed, and what is left alone

The 2018 assembly was recovered faithfully first, and then changed in these ways.
Read this before "fixing" any of it back.

**`ExplodeOnCollideBlock.explosionEffectPrefab` is now `explosionEffect`.** The
field was renamed at some point after 2018, which is what stopped the mod
compiling at all.

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

Left alone deliberately:

- **The spot light entity is still a stub.** `Mod.xml`'s own description says so.
  Its event reads a `HideVisuals` toggle and does nothing with it — that much
  dead code is gone — and `OnEntityPrefabCreation` gives every instance a light
  lerped halfway between red and blue, which the event then overwrites. Finishing
  it is a feature, not a tidy-up.
- **The commented-out `<Triggers>` block in `Mod.xml`.** It is the other half of
  the same unfinished entity work.
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
