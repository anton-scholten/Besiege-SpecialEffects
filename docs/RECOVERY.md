# How the source was recovered

The C# for this mod was lost; only the shipped assembly survived —
`SpecialEffectsAssembly.dll`, 45,568 bytes, built 2018. The nine files under
`SpecialEffects/SEScripts/` were reconstructed from that assembly and then
checked against it. This is the record of how, and of how much the result can be
trusted.

## The tooling

No .NET toolchain is installed on this machine and none was added. Everything
came out of the game's own `Besiege_Data/Managed`:

- **Reading the assembly**: `Mono.Cecil.dll`, which Besiege ships. A dumper walks
  the metadata — types, base types, fields with their flags, method signatures,
  custom attributes, locals, exception handlers — and prints every method body as
  an instruction list with branch targets resolved to the *ordinal* of the target
  instruction rather than a byte offset, so two builds with different encodings
  can still be compared line by line.
- **Running the dumper, and rebuilding the mod**: Besiege's own `mcs.dll`, driven
  offline through the game's `libmono.so`. That is what `tools/build.sh` does, and
  the same host runs the dumper against an assembly.

The same dumper was pointed at `Assembly-CSharp.dll`, `UnityEngine.dll` and
`DynamicText.dll` to check every API the mod calls against what those assemblies
expose today. That is what found the one member that had actually moved, and it
is also what confirmed the rest had not — this mod leans hard on Unity's
pre-5.5 particle API (`ParticleSystem.startLifetime`, `startSpeed`, `startSize`,
`EmissionModule.rate`), all of which is still there in the Unity build Besiege
runs.

## What the assembly gave up

Everything structural survives compilation and was read directly rather than
guessed at: the ten types and their base types, every field with its type and its
`public`/`private` flags, every method signature and its accessibility, the four
`[XmlRoot]` attributes naming the module elements, the `[XmlArray("Particles")]`
/ `[XmlArrayItem]` / `[RequireToValidate]` / `[CanBeEmpty]` markers on
`ParticleEmitter.ParticleTextures`, the four `Text` auto-properties, and all six
assembly references.

What does **not** survive is what you would expect: local variable names,
parameter names of private methods, comments, and the file layout. Local names in
the reconstruction are chosen for readability.

Field names *did* survive, and they were a mix of casing conventions —
`sourceLight` and `hasStarted` beside `ShaderDict`, `Activate` and `LittleColor`.
The reconstruction kept them while it was being checked against the assembly and
normalised them to camelCase afterwards; they are private fields, so nothing
outside the assembly ever saw them. `LittleColor` became `currentColor`,
`Col_1`/`Col_2` became `stepFrom`/`stepTo`, `ActivateSequence` became
`advanceSequence`, `Counter` became `startFrames`, `FileChecker` became
`ApplyTexture`, and the various `*_ValueChanged` / `*_Toggled` handlers were
renamed for what they do.

The one name deliberately kept is `SpotLightBehaviour.sourceLight`, which is
`public`.

## The original was not built with Besiege's compiler

The 2018 assembly is a **Debug** build produced by Microsoft's C# compiler: it
carries `[Debuggable]` and every method is padded with `nop`. The rebuild is a
Release build from Besiege's `mcs`. A byte-for-byte comparison was never
available; what is available is a comparison of what the two assemblies *do*.

## How the reconstruction was checked

Both assemblies were dumped and compared per type on their semantic content:
which external members are called, which external fields are read and written,
which string literals appear, and which floating-point constants appear —
aggregated per type rather than per method, because the refactoring below moves
code between methods freely.

**Every floating-point constant in the mod matched, in every type.** That is the
check that covers the several hundred slider defaults, ranges, positions, scales
and thresholds, and it is the one that would have caught a mistyped `0.963` or a
`0.01745329` in the wrong place.

**Every string literal matched** except the ten that were removed or corrected on
purpose, listed in [AGENTS.md](../AGENTS.md#what-was-changed-and-what-is-left-alone).
Strings matter more here than they look: the mapper keys, the menu item labels,
the shader property names (`_TintColor`, `_Color`, `_BumpMap`), the child object
names the behaviours search for (`LightLens`, `ParticleHandler`, `TextHandler`,
`LightPiece`), the resource names, and the console command names are all string
literals, and a typo in any of them is silent.

**Every external member call matched**, in both directions, except:

| difference | why |
| --- | --- |
| `ExplodeOnCollideBlock::explosionEffectPrefab` → `explosionEffect` | the field was renamed in Besiege |
| `Dictionary<int,BlockPrefab>::get_Item` → `TryGetValue`, plus `Object::op_Inequality` | the null-checked walk to the heat shimmer material |
| `Mathf::Lerp` → `Color::Lerp` (Glass) | four componentwise lerps of one colour |
| `Color::op_Equality` → `op_Inequality` (Spot Light) | `!(a == b)` written as `a != b` |
| `Element::Validate` gone | dead override, see AGENTS.md |
| a `Dictionary<string,int>` appears in `Mod` | how `mcs` compiles a `switch` over strings |

Everything else in that diff is the new private helper methods the refactoring
introduced, which is what it should be.

**Every mapper control was compared individually**, in registration order: the
`Add*` call used, the display name, the key, and the default. All identical. The
`AddMenu` calls were compared again separately on their key, default index and
full item list — also identical, which is the check that matters most, since a
menu index is what a saved machine stores.

## What the check does not cover

The comparison is per type, not per method, so it would not catch a call that
moved from one method of a type to another — a setting applied on the wrong
frame, say. That risk is concentrated in the four `SimulateUpdateAlways` /
`SimulateUpdateHost` methods, and those were read against the IL line by line
rather than trusted to the diff. Two things came out of that reading and are
worth recording, because both are easy to "fix" wrongly:

- The Spot Light's startup block **falls through** into the rest of the update on
  the frames before it fires, so the lamp answers its key from frame zero rather
  than from frame three. The Particle Emitter does the same. An early `return`
  there would be a behaviour change.
- The Spot Light tests `brightness > 0 || Auto Brightness || Strobe` **twice** —
  once inside the startup block and once after it. Both survive, as `IsLit()`.
  The first one returning is what makes the second one's return correct.

And two things the check says nothing about at all:

- It says the reconstruction matches the 2018 assembly. It says nothing about
  whether the 2018 assembly was *correct* — and in the places listed in
  [AGENTS.md](../AGENTS.md#what-was-changed-and-what-is-left-alone) it was not.
- Nothing here has been run in the game. The build is checked, the IL is checked,
  the manifest is checked, and the blacklist scanner that would make Besiege
  refuse the assembly is checked. Whether the blocks behave is a question for a
  level.
