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
        private const int PageShafts = 5;

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

        private MToggle shaftsToggle;
        private MToggle shaftShadows;
        private MSlider shaftBrightness;
        private MSlider shaftFade;
        private MSlider shaftStart;
        private MSlider shaftEnd;
        private MSlider shaftVolumeX;
        private MSlider shaftVolumeY;
        private MSlider shaftVolumeZ;
        private LightShafts shafts;

        private GameObject lightLens;
        private MeshRenderer meshRenderLens;
        private MeshFilter meshFilterLens;

        private bool hasStarted;
        private int startFrames;
        private float localTimeEffects;
        private float localTimePattern;

        // When the current run, or the current preview, began. The ping-pongs are
        // phase-locked to this rather than to the clock, so both start at the min.
        private float effectsStarted;
        private float effectsStartedUnscaled;
        private bool previewing;

        // What the light is set to right now, which the pattern reads back when a
        // sequence character means "leave it as it is".
        private float currentBrightness;
        private float currentConeAngle;
        private Color currentColor;

        // The two steps being blended between.
        private Strobe strobe = new Strobe();
        private string sequence;
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
            sourceLight = Attach.Component<Light>(gameObject);
            sourceLight.enabled = false;
            shafts = Shafts.Add(gameObject);

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

            // The mapper orders each kind of control by when it was added, so
            // Activate goes in before the toggles it reveals -- otherwise they
            // appear above it and push it down the panel when it is switched on.
            patternToggle = AddToggle("Activate", "PatternModeKey", false);
            patternSequence = AddText("Sequence", "SequenceKey", "-123--4-9");
            patternSpeed = AddSlider("Interval", "PatternSpeedKey", 0.25f, 0f, 5f);
            patternNumbers = AddToggle("Numbers \n affect", "PatternNumbersModeKey", false);
            patternAffectsBrightness = AddToggle("Brightness", "PatternAffectsBrightnessKey", true);
            patternAffectsConeAngle = AddToggle("Cone Angle", "PatternAffectsConeAnglesKey", false);
            patternAffectsColor = AddToggle("Color", "PatternAffectsColorKey", false);

            // Same rule as the strobe page: the switch goes in before what it
            // reveals, or it slides down the panel when turned on.
            shaftsToggle = AddToggle("Activate", "ShaftsModeKey", false);
            shaftShadows = AddToggle("Moving Shadows", "ShaftShadowsKey", true);
            shaftBrightness = AddSlider("Brightness", "ShaftBrightnessKey", 3f, 0f, 20f);
            shaftFade = AddSlider("Fade", "ShaftFadeKey", 0.5f, 0f, 20f);
            shaftStart = AddSlider("Start", "ShaftStartKey", 0f, 0f, 1f);
            shaftEnd = AddSlider("End", "ShaftEndKey", 1f, 0f, 1f);
            shaftVolumeX = AddSlider("Volume X", "ShaftVolumeXKey", 10f, 0f, 200f);
            shaftVolumeY = AddSlider("Volume Y", "ShaftVolumeYKey", 10f, 0f, 200f);
            shaftVolumeZ = AddSlider("Volume Z", "ShaftVolumeZKey", 20f, 0f, 200f);

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
                new List<string> { "General", "Brightness", "Cone Angle", "Color", "Strobe", "Shafts" }, false);
            textureMenu = AddMenu("TextureMenu", 0,
                new List<string> { "Normal", "Hidden", "Sphere", "Box" }, true);

            lightOptionsMenu.ValueChanged += PageChanged;
            textureMenu.ValueChanged += LensStyleChanged;
            brightnessToggle.Toggled += ShowBrightnessControls;
            coneAngleToggle.Toggled += ShowConeAngleControls;
            colorToggle.Toggled += ShowColorControls;
            patternToggle.Toggled += ShowPatternControls;
            shaftsToggle.Toggled += ShowShaftControls;
            lightTypes.ValueChanged += LightTypeChanged;

            colorDefault.ValueChanged += LensColorChanged;
            colorMin.ValueChanged += LensColorChanged;
            colorMax.ValueChanged += LensColorChanged;
            brightnessDefault.ValueChanged += LensBrightnessChanged;
            brightnessMin.ValueChanged += LensBrightnessChanged;
            brightnessMax.ValueChanged += LensBrightnessChanged;
        }

        // The glowing disc in front of the lamp is a child of its own so it can be
        // shaped and lit independently of the block's mesh.
        private void CreateLens()
        {
            lightLens = Attach.Child(sourceLight.transform, "LightLens");
            meshRenderLens = Attach.Component<MeshRenderer>(lightLens);
            meshRenderLens.material.shader = GameMaterials.Shaders.Particles.AlphaBlended;
            meshFilterLens = Attach.Component<MeshFilter>(lightLens);
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

            ShowShaftControls(shaftsToggle.IsActive);
        }

        // The shafts page depends on the light's type, which is set on another one.
        private void LightTypeChanged(int value)
        {
            ShowShaftControls(shaftsToggle.IsActive);
        }

        // On the page: the fixed value and its "Auto" switch. Off it: nothing.
        private static void ShowGroup(bool onPage, MToggle mode, MapperType value,
            MapperType speed, MapperType min, MapperType max)
        {
            Controls.Show(onPage, mode, value);
            if (!onPage) Controls.Show(false, speed, min, max);
        }

        // "Auto" swaps the fixed value for a min/max/speed ping-pong.
        private static void ShowAutoControls(bool isActive, MapperType value,
            MapperType speed, MapperType min, MapperType max)
        {
            Controls.Show(!isActive, value);
            Controls.Show(isActive, speed, min, max);
        }

        private void ShowGeneralControls(bool state)
        {
            Controls.Show(state, activate, lightTypes, range, toggleMode,
                illuminationType, timeDependentEffects, textureMenu);
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
            Controls.Show(isActive, patternSequence, patternSpeed, patternNumbers,
                patternAffectsBrightness, patternAffectsConeAngle, patternAffectsColor);
        }

        // A point light has no beam, so it is offered nothing and the page is
        // empty. A spot light's volume runs along its cone; a directional one's is
        // a box.
        private void ShowShaftControls(bool isActive)
        {
            LightType type = lightModesDict[lightTypes.Selection];
            bool onPage = lightOptionsMenu.Value == PageShafts;
            bool possible = type != LightType.Point;
            bool on = onPage && isActive && possible;

            shaftsToggle.DisplayInMapper = onPage && possible;
            Controls.Show(on, shaftShadows, shaftBrightness, shaftFade);
            Controls.Show(on && type != LightType.Directional, shaftStart, shaftEnd);
            Controls.Show(on && type == LightType.Directional,
                shaftVolumeX, shaftVolumeY, shaftVolumeZ);
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

            Animate(false);

            if (activate.IsPressed) sourceLight.enabled = !sourceLight.isActiveAndEnabled;

            // Hold-to-light: letting go puts it out, unless Toggle is on.
            if (activate.Value == 0f && sourceLight.isActiveAndEnabled && !toggleMode.IsActive)
                sourceLight.enabled = false;

            UpdateLens();
            UpdateShafts();
        }

        // Lit in the build menu too, so the colour, the strobe and the shafts can
        // be judged without starting a run. No key -- there is nothing to hold down
        // here -- and none of the simulation's startup work, so the joint, the mass
        // and the collider stay as the builder left them.
        public override void BuildingUpdate()
        {
            if (!previewing)
            {
                previewing = true;
                ResetAnimation();
            }

            sourceLight.enabled = IsLit();
            if (sourceLight.enabled)
            {
                sourceLight.type = lightModesDict[lightTypes.Selection];
                sourceLight.renderMode = illuminationTypeDict[illuminationType.Selection];
                sourceLight.range = range.Value;
                sourceLight.shadows = LightShadows.Soft;
                sourceLight.shadowStrength = textureMenu.Value == LensNormal ? 0f : 1f;
                sequence = patternSequence.Value;
                Animate(true);
            }

            UpdateLens();
            UpdateShafts();
        }

        // One frame of the animated settings. `live` re-reads the sliders every
        // frame, which the build menu wants and a simulation does not: there the
        // fixed values are read once, in Begin.
        private void Animate(bool live)
        {
            if (timeDependentEffects.IsActive)
            {
                localTimeEffects = Time.time - effectsStarted;
                localTimePattern = Time.timeScale;
            }
            else
            {
                localTimeEffects = Time.unscaledTime - effectsStartedUnscaled;
                localTimePattern = 1f;
            }

            if (brightnessToggle.IsActive)
                currentBrightness = PingPong(brightnessMin.Value, brightnessMax.Value, brightnessSpeed.Value);
            else if (live) currentBrightness = brightnessDefault.Value;

            if (coneAngleToggle.IsActive)
                currentConeAngle = PingPong(coneAngleMin.Value, coneAngleMax.Value, coneAngleSpeed.Value);
            else if (live) currentConeAngle = coneAngleDefault.Value;

            if (colorToggle.IsActive)
                currentColor = Color.Lerp(colorMin.Value, colorMax.Value,
                    Mathf.PingPong(localTimeEffects * colorSpeed.Value, 1f));
            else if (live) currentColor = colorDefault.Value;

            if (brightnessToggle.IsActive || live) sourceLight.intensity = currentBrightness;
            if (coneAngleToggle.IsActive || live) sourceLight.spotAngle = currentConeAngle;
            if (colorToggle.IsActive || live) sourceLight.color = currentColor;

            if (patternToggle.IsActive) StepPattern();
        }

        // Strobe back to its first step, ping-pongs back to their minimum: a run
        // starts where the pattern starts, not wherever the preview had got to.
        private void ResetAnimation()
        {
            strobe.Reset();
            effectsStarted = Time.time;
            effectsStartedUnscaled = Time.unscaledTime;
            localTimeEffects = 0f;
            localTimePattern = 1f;
        }

        // Visible beams through the air. LightShafts reads the light's own
        // intensity, colour, cone angle and range every frame, so the strobe and
        // the Auto sliders drive the shafts too with nothing wired here.
        private void UpdateShafts()
        {
            bool on = shaftsToggle.IsActive
                && sourceLight.enabled
                && sourceLight.type != LightType.Point;

            if (on)
                Shafts.Set(shafts, sourceLight.type, shaftBrightness.Value, shaftFade.Value,
                    shaftStart.Value, shaftEnd.Value,
                    new Vector3(shaftVolumeX.Value, shaftVolumeY.Value, shaftVolumeZ.Value),
                    shaftShadows.IsActive);

            Shafts.Follow(shafts, on, gameObject);
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

        // A step's values are read once, when the strobe rolls onto them.
        private void StepPattern()
        {
            bool restart;
            float blend;
            if (!strobe.Step(sequence, patternSpeed.Value * 100f / localTimePattern,
                    out restart, out blend)) return;

            if (restart)
            {
                ReadSequenceStep(strobe.From, out intensityFrom, out angleFrom, out colorFrom);
                ReadSequenceStep(strobe.To, out intensityTo, out angleTo, out colorTo);
            }

            ApplyPattern(Mathf.Lerp(intensityFrom, intensityTo, blend),
                         Mathf.Lerp(angleFrom, angleTo, blend),
                         Color.Lerp(colorFrom, colorTo, blend));
        }

        // Separate toggles, so a sequence can blink the lamp while the colour
        // slider still rules the hue, or the other way round.
        private void ApplyPattern(float intensity, float angle, Color color)
        {
            if (patternAffectsBrightness.IsActive) sourceLight.intensity = intensity;
            if (patternAffectsConeAngle.IsActive) sourceLight.spotAngle = angle;
            if (patternAffectsColor.IsActive) sourceLight.color = color;
        }

        // One character: '-' is a gap, as near off as the lamp gets without going
        // out; a digit is a brightness, cone angle and hue, but only with "Numbers
        // affect" on; anything else holds.
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

        // The lens tracks whatever the light ended up at, and is hidden outright
        // when the lamp is off or too dark to be worth drawing.
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

        // Besiege keeps the behaviour alive between runs, so without this a second
        // run picks up mid-pattern with the startup work already marked done.
        public override void OnSimulateStart()
        {
            hasStarted = false;
            startFrames = 0;
            previewing = false;
            shafts.enabled = false;
            sourceLight.enabled = false;
            ResetAnimation();
        }

        // Nothing calls SimulateUpdateAlways once a run ends, so the beams would
        // stay as the run left them. BuildingUpdate restarts the preview.
        public override void OnSimulateStop()
        {
            shafts.enabled = false;
            previewing = false;
        }
    }
}
