using System.Collections.Generic;
using System.Linq;
using Modding;
using Modding.Modules;
using UnityEngine;

namespace SpecialEffectsMod
{
    // A real Unity Light on a block, with the mapper wired to everything about
    // it: type, colour, cone angle, range, and three ways of animating those --
    // a ping-pong between two values, or a strobe pattern typed as text.
    public class SpotLightBehaviour : BlockModuleBehaviour<SpotLight>
    {
        // Options menu pages. A machine saves its choice as an index, so these
        // must keep their order; new pages go on the end.
        private const int PageGeneral = 0;
        private const int PageBrightness = 1;
        private const int PageConeAngle = 2;
        private const int PageColor = 3;
        private const int PageStrobe = 4;

        // Lens appearance, likewise saved as an index into the Texture menu.
        private const int LensNormal = 0;
        private const int LensHidden = 1;
        private const int LensSphere = 2;
        private const int LensBox = 3;

        // Frames of simulation to wait before the startup work below: the block's
        // joint and visual controller are not settled on frame zero.
        private const int StartFrame = 3;

        // What the block's joint is worth once simulating. The block is light and
        // would otherwise snap off under its own lamp housing.
        private const float JointBreakForce = 16500f;

        // The lens glows at the light's own colour, scaled down: intensity runs
        // 0-10 where the tint's alpha runs 0-1.
        private const float IntensityToAlpha = 10f;

        // Below this the lamp is dark enough that the lens is switched off rather
        // than drawn as a black disc.
        private const float LensVisibleIntensity = 0.02f;

        public Light sourceLight;

        private MKey activate;
        private MMenu lightOptionsMenu;
        private MMenu lightTypes;
        private MMenu illuminationType;
        private MMenu textureMenu;
        private MSlider range;
        private MToggle toggleMode;
        private MToggle timeDependentEffects;

        private IDictionary<string, LightType> lightModesDict = new Dictionary<string, LightType>();
        private IDictionary<string, LightRenderMode> illuminationTypeDict = new Dictionary<string, LightRenderMode>();

        private MToggle brightnessToggle;
        private MSlider brightnessDefault;
        private MSlider brightnessSpeed;
        private MSlider brightnessMin;
        private MSlider brightnessMax;

        private MToggle coneAngleToggle;
        private MSlider coneAngleDefault;
        private MSlider coneAngleSpeed;
        private MSlider coneAngleMin;
        private MSlider coneAngleMax;

        private MToggle colorToggle;
        private MColourSlider colorDefault;
        private MSlider colorSpeed;
        private MColourSlider colorMin;
        private MColourSlider colorMax;

        private MToggle patternToggle;
        private MText patternSequence;
        private MSlider patternSpeed;
        private MToggle patternNumbers;
        private MToggle patternAffectsBrightness;
        private MToggle patternAffectsConeAngle;
        private MToggle patternAffectsColor;

        private GameObject lightLens;
        private MeshRenderer meshRenderLens;
        private MeshFilter meshFilterLens;

        private bool hasStarted;
        private int startFrames;
        private float localTimeEffects;
        private float localTimePattern;

        // What the light is set to right now, which the pattern reads back when a
        // sequence character means "leave it as it is".
        private float currentBrightness;
        private float currentConeAngle;
        private Color currentColor;

        // Pattern state: where in the sequence we are, and the two steps being
        // interpolated between.
        private string sequence;
        private int sequenceIndex;
        private bool advanceSequence = true;
        private int sequenceCounter;
        private float intensityFrom, intensityTo;
        private float angleFrom, angleTo;
        private Color colorFrom, colorTo;

        private Material BlockMaterial
        {
            get { return VisualController.Block.MeshRenderer.material; }
        }

        public override void SafeAwake()
        {
            Skins.Hide(BlockBehaviour);
            sourceLight = gameObject.GetComponent<Light>();
            if (sourceLight == null) sourceLight = gameObject.AddComponent<Light>();
            sourceLight.enabled = false;

            CreateLens();

            brightnessDefault = AddSlider("Value", "BrightnessNormalKey", 3f, 0f, 10f);
            brightnessSpeed = AddSlider("Speed", "BrightnessSpeedKey", 1f, 0f, 5f);
            brightnessMin = AddSlider("Min", "BrightnessMinKey", 2f, 0f, 10f);
            brightnessMax = AddSlider("Max", "BrightnessMaxKey", 6f, 0f, 10f);
            brightnessToggle = AddToggle("Auto Brightness", "BrightnessModeKey", false);

            coneAngleDefault = AddSlider("Angle", "ConeAngleNormalKey", 65f, 0f, 180f);
            coneAngleSpeed = AddSlider("Speed", "ConeAngleSpeedKey", 0.25f, 0f, 5f);
            coneAngleMin = AddSlider("Min", "ConeAngleMinKey", 25f, 0f, 180f);
            coneAngleMax = AddSlider("Max", "ConeAngleMaxKey", 95f, 0f, 180f);
            coneAngleToggle = AddToggle("Auto Cone Angle", "ConeAngleModeKey", false);

            colorDefault = AddColourSlider("Color", "ColorKey", Color.cyan, false);
            colorSpeed = AddSlider("Speed", "ColorSpeedKey", 0.25f, 0f, 5f);
            colorMin = AddColourSlider("Min", "ColorMinKey", Color.green, false);
            colorMax = AddColourSlider("Max", "ColorMaxKey", Color.magenta, false);
            colorToggle = AddToggle("Auto Color", "ColorModeKey", false);

            // The mapper groups controls by kind and orders each group by the
            // order they were added in, so Activate is registered before the
            // toggles it reveals: that keeps it at the top of the toggles --
            // directly under the page menu -- instead of the strobe controls
            // appearing above it and pushing it down the moment it is switched on.
            patternToggle = AddToggle("Activate", "PatternModeKey", false);
            patternSequence = AddText("Sequence", "SequenceKey", "-123--4-9");
            patternSpeed = AddSlider("Interval", "PatternSpeedKey", 0.25f, 0f, 5f);
            patternNumbers = AddToggle("Numbers \n affect", "PatternNumbersModeKey", false);
            patternAffectsBrightness = AddToggle("Brightness", "PatternAffectsBrightnessKey", true);
            patternAffectsConeAngle = AddToggle("Cone Angle", "PatternAffectsConeAnglesKey", false);
            patternAffectsColor = AddToggle("Color", "PatternAffectsColorKey", false);

            lightModesDict.Add("Spot", LightType.Spot);
            lightModesDict.Add("Directional", LightType.Directional);
            lightModesDict.Add("Point", LightType.Point);
            lightTypes = AddMenu("lightModesKey", 0, lightModesDict.Keys.ToList(), true);

            illuminationTypeDict.Add("Pixel", LightRenderMode.ForcePixel);
            illuminationTypeDict.Add("Vertex", LightRenderMode.ForceVertex);
            illuminationTypeDict.Add("Auto", LightRenderMode.Auto);
            illuminationType = AddMenu("IlluminationTypeKey", 0, illuminationTypeDict.Keys.ToList(), true);

            activate = AddKey("Activate", "Activate", KeyCode.P);
            range = AddSlider("Range", "Range", 30f, 0f, 1000f);
            toggleMode = AddToggle("Toggle", "ToggleKey", false);
            timeDependentEffects = AddToggle("TimeScale", "TimeDependentEffectsKey", true);

            lightOptionsMenu = AddMenu("LightOptionsKey", 0,
                new List<string> { "General", "Brightness", "Cone Angle", "Color", "Strobe" }, false);
            textureMenu = AddMenu("TextureMenu", 0,
                new List<string> { "Normal", "Hidden", "Sphere", "Box" }, true);

            lightOptionsMenu.ValueChanged += PageChanged;
            textureMenu.ValueChanged += LensStyleChanged;
            brightnessToggle.Toggled += ShowBrightnessControls;
            coneAngleToggle.Toggled += ShowConeAngleControls;
            colorToggle.Toggled += ShowColorControls;
            patternToggle.Toggled += ShowPatternControls;

            colorDefault.ValueChanged += LensColorChanged;
            colorMin.ValueChanged += LensColorChanged;
            colorMax.ValueChanged += LensColorChanged;
            brightnessDefault.ValueChanged += LensBrightnessChanged;
            brightnessMin.ValueChanged += LensBrightnessChanged;
            brightnessMax.ValueChanged += LensBrightnessChanged;
        }

        // The glowing disc in front of the lamp is a child object of its own, so
        // it can be shaped and lit independently of the block's mesh. Reused if it
        // is already there, which it is on a reload.
        private void CreateLens()
        {
            foreach (Transform child in sourceLight.GetComponentsInChildren<Transform>())
            {
                if (child.name == "LightLens")
                {
                    lightLens = child.gameObject;
                    break;
                }
            }

            if (lightLens == null)
            {
                lightLens = new GameObject();
                lightLens.transform.name = "LightLens";
                lightLens.transform.parent = sourceLight.transform;
            }

            meshRenderLens = lightLens.GetComponent<MeshRenderer>();
            if (meshRenderLens == null) meshRenderLens = lightLens.AddComponent<MeshRenderer>();
            meshRenderLens.material.shader = GameMaterials.Shaders.Particles.AlphaBlended;

            meshFilterLens = lightLens.GetComponent<MeshFilter>();
            if (meshFilterLens == null) meshFilterLens = lightLens.AddComponent<MeshFilter>();
        }

        // Only one page of the options menu is shown at a time; everything else is
        // hidden. The per-page "Auto" toggles then decide what within a page shows.
        private void PageChanged(int value)
        {
            ShowGeneralControls(value == PageGeneral);

            ShowGroup(value == PageBrightness, brightnessToggle, brightnessDefault,
                brightnessSpeed, brightnessMin, brightnessMax);
            if (value == PageBrightness) ShowBrightnessControls(brightnessToggle.IsActive);

            ShowGroup(value == PageConeAngle, coneAngleToggle, coneAngleDefault,
                coneAngleSpeed, coneAngleMin, coneAngleMax);
            if (value == PageConeAngle) ShowConeAngleControls(coneAngleToggle.IsActive);

            ShowGroup(value == PageColor, colorToggle, colorDefault,
                colorSpeed, colorMin, colorMax);
            if (value == PageColor) ShowColorControls(colorToggle.IsActive);

            patternToggle.DisplayInMapper = value == PageStrobe;
            if (value == PageStrobe) ShowPatternControls(patternToggle.IsActive);
            else ShowPatternControls(false);
        }

        // On the page: the fixed value and its "Auto" switch. Off it: nothing.
        private static void ShowGroup(bool onPage, MToggle mode, MapperType value,
            MapperType speed, MapperType min, MapperType max)
        {
            mode.DisplayInMapper = onPage;
            value.DisplayInMapper = onPage;
            if (onPage) return;
            speed.DisplayInMapper = false;
            min.DisplayInMapper = false;
            max.DisplayInMapper = false;
        }

        // "Auto" swaps the fixed value for a min/max/speed ping-pong.
        private static void ShowAutoControls(bool isActive, MapperType value,
            MapperType speed, MapperType min, MapperType max)
        {
            value.DisplayInMapper = !isActive;
            speed.DisplayInMapper = isActive;
            min.DisplayInMapper = isActive;
            max.DisplayInMapper = isActive;
        }

        private void ShowGeneralControls(bool state)
        {
            activate.DisplayInMapper = state;
            lightTypes.DisplayInMapper = state;
            range.DisplayInMapper = state;
            toggleMode.DisplayInMapper = state;
            illuminationType.DisplayInMapper = state;
            timeDependentEffects.DisplayInMapper = state;
            textureMenu.DisplayInMapper = state;
        }

        private void ShowBrightnessControls(bool isActive)
        {
            ShowAutoControls(isActive, brightnessDefault, brightnessSpeed, brightnessMin, brightnessMax);
        }

        private void ShowConeAngleControls(bool isActive)
        {
            ShowAutoControls(isActive, coneAngleDefault, coneAngleSpeed, coneAngleMin, coneAngleMax);
        }

        private void ShowColorControls(bool isActive)
        {
            ShowAutoControls(isActive, colorDefault, colorSpeed, colorMin, colorMax);
        }

        private void ShowPatternControls(bool isActive)
        {
            patternSequence.DisplayInMapper = isActive;
            patternSpeed.DisplayInMapper = isActive;
            patternNumbers.DisplayInMapper = isActive;
            patternAffectsBrightness.DisplayInMapper = isActive;
            patternAffectsConeAngle.DisplayInMapper = isActive;
            patternAffectsColor.DisplayInMapper = isActive;
        }

        // In the build menu the lens is restyled as soon as the menu changes.
        private void LensStyleChanged(int value)
        {
            if (value == LensSphere || value == LensBox)
            {
                VisualController.SetInvisible();
            }
            else
            {
                VisualController.SetVisible();
                BlockMaterial.shader = value == LensHidden
                    ? GameMaterials.Shaders.Misc.Loading
                    : GameMaterials.Shaders.Blocks.Main;
            }

            ShapeLens(value);
            TintLensFromSliders();
        }

        // Where the lens sits, how big it is, and which mesh it uses. "Hidden"
        // keeps the flat lens: it is the block behind it that goes see-through.
        private void ShapeLens(int style)
        {
            Transform lens = lightLens.transform;
            switch (style)
            {
                case LensSphere:
                    lens.localPosition = Vector3.forward * 0.5f;
                    lens.localScale = new Vector3(1f, 1f, 1f);
                    meshFilterLens.mesh = ModResource.GetMesh("GlassBall_mesh");
                    break;
                case LensBox:
                    lens.localPosition = Vector3.forward * 0.5f;
                    lens.localScale = new Vector3(10f, 1f, 1f);
                    meshFilterLens.mesh = ModResource.GetMesh("Glass_mesh");
                    break;
                default:
                    lens.localPosition = Vector3.forward * 0.963f;
                    lens.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    lens.localScale = new Vector3(0.95f, 0.01f, 0.95f);
                    meshFilterLens.mesh = ModResource.GetMesh("Lens");
                    break;
            }
        }

        private void TintLensFromSliders()
        {
            Color col = colorDefault.Value;
            meshRenderLens.material.SetColor("_TintColor",
                new Color(col.r, col.g, col.b, brightnessDefault.Value / IntensityToAlpha));
        }

        private void LensColorChanged(Color color)
        {
            float alpha = meshRenderLens.material.GetColor("_TintColor").a;
            meshRenderLens.material.SetColor("_TintColor",
                new Color(color.r, color.g, color.b, alpha));
        }

        private void LensBrightnessChanged(float bright)
        {
            Color col = meshRenderLens.material.GetColor("_TintColor");
            meshRenderLens.material.SetColor("_TintColor",
                new Color(col.r, col.g, col.b, bright / IntensityToAlpha));
        }

        public override void SimulateUpdateAlways()
        {
            if (!hasStarted)
            {
                Rigidbody.detectCollisions = false;
                if (startFrames == StartFrame) Begin();
                else startFrames++;
            }

            // A lamp set to zero brightness with nothing animating it is off, and
            // is left alone entirely rather than written every frame.
            if (!IsLit()) return;

            if (timeDependentEffects.IsActive)
            {
                localTimeEffects = Time.time;
                localTimePattern = Time.timeScale;
            }
            else
            {
                localTimeEffects = Time.unscaledTime;
                localTimePattern = 1f;
            }

            if (brightnessToggle.IsActive)
            {
                currentBrightness = PingPong(brightnessMin.Value, brightnessMax.Value, brightnessSpeed.Value);
                sourceLight.intensity = currentBrightness;
            }
            if (coneAngleToggle.IsActive)
            {
                currentConeAngle = PingPong(coneAngleMin.Value, coneAngleMax.Value, coneAngleSpeed.Value);
                sourceLight.spotAngle = currentConeAngle;
            }
            if (colorToggle.IsActive)
            {
                currentColor = Color.Lerp(colorMin.Value, colorMax.Value,
                    Mathf.PingPong(localTimeEffects * colorSpeed.Value, 1f));
                sourceLight.color = currentColor;
            }

            if (patternToggle.IsActive) StepPattern();

            if (activate.IsPressed) sourceLight.enabled = !sourceLight.isActiveAndEnabled;

            // Hold-to-light: letting go puts it out, unless Toggle is on.
            if (activate.Value == 0f && sourceLight.isActiveAndEnabled && !toggleMode.IsActive)
                sourceLight.enabled = false;

            UpdateLens();
        }

        // The startup frame: everything that needs the block to be simulating.
        private void Begin()
        {
            hasStarted = true;

            switch (textureMenu.Value)
            {
                case LensNormal:
                    Rigidbody.detectCollisions = true;
                    VisualController.SetVisible();
                    BlockMaterial.shader = GameMaterials.Shaders.Blocks.Main;
                    ShapeLens(LensNormal);
                    sourceLight.shadowStrength = 0f;
                    break;
                case LensHidden:
                    MakeWeightless();
                    VisualController.SetInvisible();
                    lightLens.SetActive(false);
                    sourceLight.shadowStrength = 1f;
                    break;
                default:
                    MakeWeightless();
                    VisualController.SetInvisible();
                    ShapeLens(textureMenu.Value);
                    sourceLight.shadowStrength = 1f;
                    break;
            }

            TintLensFromSliders();

            sourceLight.type = lightModesDict[lightTypes.Selection];
            sourceLight.renderMode = illuminationTypeDict[illuminationType.Selection];

            if (!IsLit()) return;

            currentBrightness = brightnessDefault.Value;
            sourceLight.intensity = currentBrightness;
            currentColor = colorDefault.Value;
            sourceLight.color = currentColor;
            currentConeAngle = coneAngleDefault.Value;
            sourceLight.spotAngle = currentConeAngle;

            sourceLight.shadowResolution = LightShadowResolution.FromQualitySettings;
            sourceLight.shadows = LightShadows.Soft;
            sourceLight.range = range.Value;

            sequence = patternSequence.Value;

            // The lamp is heavier than its mounting suggests; without this it
            // snaps off the moment the machine moves.
            ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
            joint.breakForce = JointBreakForce;
            joint.breakTorque = JointBreakForce;
        }

        private void MakeWeightless()
        {
            Rigidbody.detectCollisions = false;
            Rigidbody.mass = 0f;
        }

        private bool IsLit()
        {
            return brightnessDefault.Value > 0f || brightnessToggle.IsActive || patternToggle.IsActive;
        }

        private float PingPong(float min, float max, float speed)
        {
            return Mathf.Lerp(min, max, Mathf.PingPong(localTimeEffects * speed, 1f));
        }

        // Walks the sequence one character every `Interval` seconds, interpolating
        // between the current character's values and the next one's in between.
        private void StepPattern()
        {
            float framesPerStep = patternSpeed.Value * 100f / localTimePattern;

            if (advanceSequence)
            {
                advanceSequence = false;
                if (sequenceIndex >= sequence.Length) sequenceIndex = 0;
                ReadSequenceStep(sequence[sequenceIndex], out intensityFrom, out angleFrom, out colorFrom);

                int next = sequenceIndex + 1;
                if (next == sequence.Length) next = 0;
                ReadSequenceStep(sequence[next], out intensityTo, out angleTo, out colorTo);

                ApplyPattern(intensityFrom, angleFrom, colorFrom);
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
                ApplyPattern(Mathf.Lerp(intensityFrom, intensityTo, t),
                             Mathf.Lerp(angleFrom, angleTo, t),
                             Color.Lerp(colorFrom, colorTo, t));
            }
        }

        // Which of the three the pattern is allowed to drive are separate toggles,
        // so a sequence can blink the lamp while the colour slider still rules
        // the hue, or the other way round.
        private void ApplyPattern(float intensity, float angle, Color color)
        {
            if (patternAffectsBrightness.IsActive) sourceLight.intensity = intensity;
            if (patternAffectsConeAngle.IsActive) sourceLight.spotAngle = angle;
            if (patternAffectsColor.IsActive) sourceLight.color = color;
        }

        // One character of the sequence. '-' is a gap -- as near off as the light
        // gets without switching it off. A digit is a brightness, a cone angle and
        // a hue, but only with "Numbers affect" on. Anything else holds.
        private void ReadSequenceStep(char c, out float intensity, out float angle, out Color color)
        {
            if (c == '-')
            {
                intensity = 0.01f;
                angle = 0.01f;
                color = Color.black;
                return;
            }

            if (char.IsDigit(c) && patternNumbers.IsActive)
            {
                int digit = (int)char.GetNumericValue(c);
                intensity = digit;
                angle = digit * 20;
                color = Color.HSVToRGB(digit * 0.1f, 1f, 1f);
                return;
            }

            intensity = currentBrightness;
            angle = currentConeAngle;
            color = currentColor;
        }

        // The lens tracks whatever the light ended up at this frame, and is hidden
        // outright when the lamp is off or too dark to be worth drawing.
        private void UpdateLens()
        {
            if (textureMenu.Value == LensHidden)
            {
                lightLens.SetActive(false);
                return;
            }

            Color col = sourceLight.color;
            meshRenderLens.material.SetColor("_TintColor",
                new Color(col.r, col.g, col.b, sourceLight.intensity / IntensityToAlpha));
            lightLens.SetActive(sourceLight.intensity >= LensVisibleIntensity
                && sourceLight.color != Color.black
                && sourceLight.enabled);
        }

        // Besiege keeps the behaviour alive between runs, so without this the
        // second run would start from wherever the first one left off -- mid
        // pattern, and with the startup work above already marked done.
        public override void OnSimulateStart()
        {
            hasStarted = false;
            startFrames = 0;
            sequenceIndex = 0;
            sequenceCounter = 0;
            advanceSequence = true;
        }
    }
}
