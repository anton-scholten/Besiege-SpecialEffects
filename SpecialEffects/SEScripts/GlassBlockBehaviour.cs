using System.Collections.Generic;
using System.Linq;
using Modding;
using Modding.Modules;
using UnityEngine;

namespace SpecialEffectsMod
{
    // A translucent, colourable pane -- or sphere, or torus -- that a key can
    // switch on and off. Nothing here touches physics beyond the optional
    // collider toggle: it is the block's own mesh, restyled.
    public class GlassBlockBehaviour : BlockModuleBehaviour<GlassBlock>
    {
        private IDictionary<string, Shader> shaderDict = new Dictionary<string, Shader>();
        private IDictionary<string, Mesh> meshDict = new Dictionary<string, Mesh>();
        private MMenu shaderMenu;
        private MMenu shapeMenu;

        private MKey activate;
        private MSlider transparency;
        private MColourSlider colorSlider;
        private MToggle toggleable;
        private MToggle startState;
        private MToggle disableCollider;

        private MToggle patternToggle;
        private MText patternSequence;
        private MSlider patternSpeed;
        private MToggle timeDependentEffects;
        private MToggle patternNumbers;
        private MToggle patternAffectsTransparency;
        private MToggle patternAffectsColor;

        private bool hasStarted;
        private bool wasActive = true;
        private float localTimePattern;

        // Pattern state: the sequence being stepped through, where in it we are,
        // and the two colours the current step interpolates between.
        private string sequence;
        private int sequenceIndex;
        private bool advanceSequence = true;
        private int sequenceCounter;
        private Color currentColor;
        private Color stepFrom;
        private Color stepTo;

        // The material the block's own mesh is drawn with. Both the tint and the
        // colour property are set on it, because which one bites depends on which
        // shader the Shader menu picked.
        private Material BlockMaterial
        {
            get { return VisualController.Block.MeshRenderer.material; }
        }

        public override void SafeAwake()
        {
            shaderDict.Add("Alpha Blend", GameMaterials.Shaders.Particles.AlphaBlended);
            shaderDict.Add("Additive", GameMaterials.Shaders.Particles.Additive);
            shaderDict.Add("Overlay", GameMaterials.Shaders.Blocks.Pin);
            shaderMenu = AddMenu("PartShaderMenuKey", 0, shaderDict.Keys.ToList(), false);

            meshDict.Add("Pane", ModResource.GetMesh("Glass_mesh"));
            meshDict.Add("Sphere", ModResource.GetMesh("GlassBall_mesh"));
            meshDict.Add("Poly Sphere", ModResource.GetMesh("GlassICO_mesh"));
            meshDict.Add("Torus", ModResource.GetMesh("GlassTorus_mesh"));
            shapeMenu = AddMenu("ShapeKey", 0, meshDict.Keys.ToList(), false);

            activate = AddKey("Activate", "Activate", KeyCode.L);
            transparency = AddSliderUnclamped("Transparency", "Transparency", 0.5f, 0f, 1f);
            colorSlider = AddColourSlider("Color", "ColorKey", Color.magenta, false);
            toggleable = AddToggle("Toggle", "ToggleKey", true);
            startState = AddToggle("Inverse state", "StartStateKey", false);
            disableCollider = AddToggle("Collider", "ColliderKey", false);

            patternToggle = AddToggle("Pattern", "PatternModeKey", false);
            patternSequence = AddText("Sequence", "SequenceKey", "-123--4-9");
            patternSpeed = AddSlider("Interval", "PatternSpeedKey", 0.25f, 0f, 5f);
            timeDependentEffects = AddToggle("TimeScale", "TimeDependentEffectsKey", true);
            patternNumbers = AddToggle("Numbers \n affect", "PatternNumbersModeKey", false);
            patternAffectsTransparency = AddToggle("Transparency", "PatternAffectsTransparencyKey", true);
            patternAffectsColor = AddToggle("Color", "PatternAffectsColorKey", false);

            patternToggle.Toggled += ShowPatternControls;
            shapeMenu.ValueChanged += ShapeChanged;
        }

        // The pattern controls are only meaningful with the pattern running, so
        // they are hidden until it is switched on.
        private void ShowPatternControls(bool isActive)
        {
            patternSequence.DisplayInMapper = isActive;
            patternSpeed.DisplayInMapper = isActive;
            timeDependentEffects.DisplayInMapper = isActive;
            patternNumbers.DisplayInMapper = isActive;
            patternAffectsTransparency.DisplayInMapper = isActive;
            patternAffectsColor.DisplayInMapper = isActive;
        }

        private void ShapeChanged(int value)
        {
            VisualController.MeshFilter.mesh = meshDict[shapeMenu.Selection];
        }

        public override void BuildingUpdate()
        {
            Color col = colorSlider.Value;
            Color tinted = new Color(col.r, col.g, col.b, transparency.Value);
            BlockMaterial.shader = shaderDict[shaderMenu.Selection];
            BlockMaterial.SetColor("_Color", tinted);
            BlockMaterial.SetColor("_TintColor", tinted);
        }

        public override void SimulateUpdateAlways()
        {
            if (!hasStarted) Begin();

            // Fully transparent glass is left alone entirely: no pattern stepping,
            // no visibility switching, no material writes.
            if (transparency.Value <= 0f) return;

            localTimePattern = timeDependentEffects.IsActive ? Time.timeScale : 1f;
            if (patternToggle.IsActive) StepPattern();

            if (activate.IsPressed) ToggleVisibility();
            HoldVisibility();

            BlockMaterial.SetColor("_TintColor", currentColor);
            BlockMaterial.SetColor("_Color", currentColor);
        }

        private void Begin()
        {
            hasStarted = true;
            BlockMaterial.shader = shaderDict[shaderMenu.Selection];
            sequence = patternSequence.Value;

            Color col = colorSlider.Value;
            currentColor = new Color(col.r, col.g, col.b, transparency.Value);

            if (startState.IsActive)
            {
                VisualController.SetInvisible();
                wasActive = false;
            }

            // A collider-less pane is meant to be decoration, so it also loses its
            // mass rather than hanging weight off the machine.
            if (disableCollider.IsActive)
            {
                Rigidbody.detectCollisions = false;
                Rigidbody.mass = 0f;
            }
        }

        // Walks the sequence one character every `Interval` seconds, interpolating
        // between the current character's colour and the next one's in between.
        private void StepPattern()
        {
            float framesPerStep = patternSpeed.Value * 100f / localTimePattern;

            if (advanceSequence)
            {
                advanceSequence = false;
                if (sequenceIndex >= sequence.Length) sequenceIndex = 0;
                stepFrom = ReadSequenceColor(sequence[sequenceIndex]);

                int next = sequenceIndex + 1;
                if (next == sequence.Length) next = 0;
                stepTo = ReadSequenceColor(sequence[next]);

                ApplyPatternColor(stepFrom);
                sequenceIndex++;
            }
            else if (sequenceCounter >= framesPerStep)
            {
                sequenceCounter = 0;
                advanceSequence = true;
            }
            else
            {
                sequenceCounter++;
                float t = sequenceCounter / framesPerStep;
                ApplyPatternColor(Color.Lerp(stepFrom, stepTo, t));
            }
        }

        // Which channels the pattern is allowed to drive are two separate toggles,
        // so a pattern can blink the opacity while the colour slider still rules
        // the hue, or the other way round.
        private void ApplyPatternColor(Color col)
        {
            if (patternAffectsTransparency.IsActive) currentColor.a = col.a;
            if (patternAffectsColor.IsActive)
            {
                currentColor.r = col.r;
                currentColor.g = col.g;
                currentColor.b = col.b;
            }
        }

        // One character of the sequence. '-' is a gap; a digit is a hue and an
        // opacity, but only with "Numbers affect" on. Anything else -- and every
        // digit without that toggle -- is the block's own colour and transparency.
        private Color ReadSequenceColor(char c)
        {
            if (c == '-') return new Color(0f, 0f, 0f, 0f);

            if (char.IsDigit(c) && patternNumbers.IsActive)
            {
                float digit = (float)char.GetNumericValue(c);
                Color hue = Color.HSVToRGB((int)digit * 0.1f, 1f, 1f);
                return new Color(hue.r, hue.g, hue.b, digit / 10f);
            }

            Color col = colorSlider.Value;
            return new Color(col.r, col.g, col.b, transparency.Value);
        }

        private void ToggleVisibility()
        {
            if (toggleable.IsActive)
            {
                wasActive = !wasActive;
                SetVisible(wasActive);
            }
            else
            {
                SetVisible(!startState.IsActive);
            }
        }

        // Held-down mode has to reassert the state every frame, because letting go
        // of the key is what puts the pane back the way it started.
        private void HoldVisibility()
        {
            if (toggleable.IsActive)
            {
                if (wasActive) VisualController.SetVisible();
            }
            else if (!activate.IsDown)
            {
                SetVisible(startState.IsActive);
            }
            else if (patternToggle.IsActive)
            {
                SetVisible(!startState.IsActive);
            }
        }

        private void SetVisible(bool visible)
        {
            if (visible) VisualController.SetVisible();
            else VisualController.SetInvisible();
        }

        // Besiege keeps the behaviour alive between runs, so without this the
        // second run would start from wherever the first one left off -- mid
        // pattern, and with the startup work above already marked done.
        public override void OnSimulateStart()
        {
            hasStarted = false;
            wasActive = true;
            sequenceIndex = 0;
            sequenceCounter = 0;
            advanceSequence = true;
        }
    }
}
