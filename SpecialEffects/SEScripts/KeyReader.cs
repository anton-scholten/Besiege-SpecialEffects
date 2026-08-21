using UnityEngine;

namespace SpecialEffectsMod
{
    // A mapper key read from the keyboard and from Besiege's variable system at
    // once, so automation drives a block exactly as a keypress does.
    //
    // MKey carries the whole emulation feature -- MSlider, MToggle and MMenu have
    // nothing variable-related on them -- but its edges cannot be read from an
    // ordinary Update. MKey.CheckEmulation compares against a snapshot it advances
    // the first time it is called in a fixed step, so a second call inside the same
    // step reports the same rising edge again. Poll it once per frame and a variable
    // press lands two or three times, which is what stopped every Toggle working
    // under automation.
    //
    // Besiege has a pass for exactly this: KeyEmulationUpdate, which Machine runs
    // from FixedUpdate once per emulation tick, and which BlockPrefabCreator turns
    // on for every modded block. So the edges are taken there and handed to the
    // frame update, which consumes each one once. It is the same split
    // SpinningModuleBehaviour and SpewingModuleBehaviour use.
    public class KeyReader
    {
        private readonly MKey key;

        private bool emulatedPress;
        private bool emulatedRelease;
        private bool emulatedHold;

        // What the key is doing, as of the last Poll.
        public bool Pressed;
        public bool Held;
        public bool Released;

        public KeyReader(MKey key)
        {
            this.key = key;
        }

        // The control itself, for the pages that show and hide it.
        public MKey Mapper { get { return key; } }

        // From KeyEmulationUpdate and nowhere else. The edges are latched rather
        // than used: a frame may not have happened since the last tick, and one
        // that has not been handed out yet must not be lost.
        public void ReadEmulation()
        {
            emulatedPress |= key.EmulationPressed();
            emulatedRelease |= key.EmulationReleased();
            emulatedHold = key.EmulationHeld(true);
        }

        // From the per-frame update. Each emulated edge is handed out once.
        public void Poll()
        {
            Pressed = key.IsPressed || emulatedPress;
            Released = key.IsReleased || emulatedRelease;
            Held = key.IsHeld || emulatedHold;
            emulatedPress = false;
            emulatedRelease = false;
        }

        // Besiege keeps a behaviour alive between runs, so a latched edge would
        // otherwise fire on the first frame of the next one.
        public void Reset()
        {
            emulatedPress = false;
            emulatedRelease = false;
            emulatedHold = false;
            Pressed = false;
            Held = false;
            Released = false;
        }
    }
}
