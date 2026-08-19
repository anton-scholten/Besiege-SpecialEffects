using System.Collections.Generic;
using System.Linq;
using Modding;
using Modding.Modules;
using UnityEngine;

namespace SpecialEffectsMod
{
    // A Unity particle system on a block, with most of the system's modules
    // exposed through the mapper. The settings menu picks which page of controls
    // is shown; everything is applied once, on the startup frame.
    public class ParticleEmitterBehaviour : BlockModuleBehaviour<ParticleEmitter>
    {
        // Settings menu pages. A machine saves its choice as an index, so these
        // must keep their order; new pages go on the end.
        private const int PageGeneral = 0;
        private const int PageEmission = 1;
        private const int PageDampen = 2;
        private const int PageColor = 3;
        private const int PageSize = 4;
        private const int PageRotation = 5;
        private const int PageCollision = 6;

        // What drives a "Changes" setting, likewise a saved index.
        private const int OverLifetime = 0;
        private const int BySpeed = 1;
        private const int Randomly = 2;

        // Frames of simulation to wait before applying everything below: the
        // block's joint and visual controller are not settled on frame zero.
        private const int StartFrame = 3;

        // What the block's joint is worth once simulating.
        private const float JointBreakForce = 17000f;

        private GameObject particleHandler;
        private ParticleSystem particleSys;
        private Renderer particleRenderer;
        private Material heatwaveMat;

        private ParticleSystem.EmissionModule moduleEmission;
        private ParticleSystem.ShapeModule moduleShape;
        private ParticleSystem.LimitVelocityOverLifetimeModule moduleLimitVelocity;
        private ParticleSystem.ColorBySpeedModule moduleColorSpeed;
        private ParticleSystem.ColorOverLifetimeModule moduleColorLife;
        private ParticleSystem.SizeBySpeedModule moduleSizeSpeed;
        private ParticleSystem.SizeOverLifetimeModule moduleSizeLife;
        private ParticleSystem.RotationBySpeedModule moduleRotationSpeed;
        private ParticleSystem.RotationOverLifetimeModule moduleRotationLife;
        private ParticleSystem.CollisionModule moduleCollision;

        private MMenu settingsMenu;

        private MKey activate;
        private MToggle toggleable;
        private MToggle loop;
        private MSlider maxCount;
        private MSlider lifeTime;
        private MSlider speed;
        private MSlider gravity;
        private MSlider playbackSpeed;
        private MMenu physMenu;
        private MToggle disableCollider;
        private MMenu shaderMenu;
        private MMenu textureMenu;

        private MSlider emissionRate;
        private MMenu emissionShape;
        private MSlider emissionAngle;

        private MSlider dampenValue;
        private MSlider dampenLimit;

        private MToggle heatwave;
        private MSlider heatwaveScale;
        private MColourSlider colorStart;
        private MSlider opacityStart;
        private MToggle colorChanges;
        private MMenu colorMenu;
        private MColourSlider colorEnd;
        private MSlider opacityEnd;
        private MSlider colorSpeedMin;
        private MSlider colorSpeedMax;

        private MSlider sizeStart;
        private MToggle sizeChanges;
        private MMenu sizeMenu;
        private MSlider sizeEnd;
        private MSlider sizeSpeedMin;
        private MSlider sizeSpeedMax;

        private MSlider rotationStart;
        private MToggle rotationChanges;
        private MMenu rotationMenu;
        private MSlider rotationSpeedMin;
        private MSlider rotationSpeedMax;

        private MToggle collisionToggle;
        private MSlider collisionRadius;
        private MSlider collisionDampen;
        private MSlider collisionBounce;
        private MSlider collisionLifetimeLoss;
        private MMenu collisionQuality;
        private IDictionary<string, ParticleSystemCollisionQuality> collisionQualityDict =
            new Dictionary<string, ParticleSystemCollisionQuality>();

        private IDictionary<string, Shader> shaderDict = new Dictionary<string, Shader>();
        private List<string> textureNames = new List<string>();

        private bool hasStarted;
        private bool wasToggled;
        private int startFrames;
        private Gradient gradient = new Gradient();

        public override void SafeAwake()
        {
            CreateParticleSystem();

            settingsMenu = AddMenu("SettingsMenuKey", 0, new List<string>
                { "General", "Emission", "Dampen", "Color", "Size", "Rotation", "Collision" }, false);

            activate = AddKey("Activate", "Activate", KeyCode.K);
            toggleable = AddToggle("Toggle", "ToggleKey", false);
            loop = AddToggle("Loop", "LoopKey", true);
            speed = AddSliderUnclamped("Speed", "ParticleSpeedKey", 5f, 0f, 10f);
            maxCount = AddSliderUnclamped("Max Particles", "MaxParticlesKey", 50f, 0f, 100f);
            lifeTime = AddSliderUnclamped("Lifetime", "LifetimeKey", 1f, 0f, 10f);
            gravity = AddSliderUnclamped("Gravity", "GravityKey", 0.05f, -1f, 1f);
            playbackSpeed = AddSliderUnclamped("Playback speed", "PlaybackKey", 0.5f, -2f, 2f);
            physMenu = AddMenu("ParticlePhysMenuKey", 0, new List<string> { "World", "Local" }, true);
            disableCollider = AddToggle("No Collider", "ColliderKey", false);

            emissionRate = AddSliderUnclamped("Emission Rate", "EmissionRate", 20f, 0f, 30f);
            emissionShape = AddMenu("EmissionShape", (int)ParticleSystemShapeType.Cone, new List<string>
            {
                "Sphere", "Sphere Shell", "Hemisphere", "Hemisphere Shell", "Cone", "Box", "Mesh",
                "Cone Shell", "Cone Volume", "Cone Volume Shell", "Circle", "Circle Edge",
                "Single Sided Edge", "Mesh Renderer", "Skinned Mesh Renderer"
            }, true);
            emissionAngle = AddSlider("Angle (Degrees)", "EmissionAngle", 30f, 0f, 180f);

            dampenValue = AddSliderUnclamped("Dampening", "DampenValue", 0f, -0.5f, 0.5f);
            dampenLimit = AddSliderUnclamped("Speed Limit", "DampenLimit", 0f, -10f, 10f);

            heatwave = AddToggle("Heatwave", "HeatwaveKey", false);
            heatwaveScale = AddSliderUnclamped("Heatwave Scale", "HeatwaveScaleKey", 0.15f, 0.01f, 1f);
            colorStart = AddColourSlider("Start", "ColorStart", Color.cyan, false);
            opacityStart = AddSlider("Opacity Start", "ColorOpacityStart", 0.75f, 0f, 1f);
            colorChanges = AddToggle("Changes", "ColorToggleChange", false);
            colorMenu = AddMenu("ColorMenu", 0, ChangeModes(), true);
            colorEnd = AddColourSlider("End", "ColorEnd", Color.red, false);
            opacityEnd = AddSlider("Opacity End", "ColorOpacityEnd", 0.25f, 0f, 1f);
            colorSpeedMin = AddSliderUnclamped("Min Speed", "ColorSpeedMin", 2.5f, 0f, 10f);
            colorSpeedMax = AddSliderUnclamped("Max Speed", "ColorSpeedMax", 7.5f, 0f, 10f);

            sizeStart = AddSliderUnclamped("Start", "SizeStart", 1f, 0f, 5f);
            sizeChanges = AddToggle("Changes", "SizeToggleChange", false);
            sizeMenu = AddMenu("SizeMenu", 0, ChangeModes(), true);
            sizeEnd = AddSliderUnclamped("End", "SizeEnd", 0.5f, 0f, 5f);
            sizeSpeedMin = AddSliderUnclamped("Min Speed", "SizeSpeedMin", 2.5f, 0f, 10f);
            sizeSpeedMax = AddSliderUnclamped("Max Speed", "SizeSpeedMax", 7.5f, 0f, 10f);

            rotationStart = AddSliderUnclamped("Start (Degrees)", "RotationStart", 0f, -180f, 180f);
            rotationChanges = AddToggle("Changes", "RotationToggleChange", false);
            rotationMenu = AddMenu("RotationMenu", 0, ChangeModes(), true);
            rotationSpeedMin = AddSliderUnclamped("Min Speed", "RotationSpeedMin", 2.5f, 0f, 10f);
            rotationSpeedMax = AddSliderUnclamped("Max Speed", "RotationSpeedMax", 7.5f, 0f, 10f);

            collisionToggle = AddToggle("Activate", "CollisionActivate", false);
            collisionRadius = AddSliderUnclamped("Collider Radius", "CollisionRadius", 0.5f, 0f, 5f);
            collisionDampen = AddSliderUnclamped("Collision Dampening", "CollisionDampen", 0.5f, -1f, 1f);
            collisionBounce = AddSliderUnclamped("Collision Bounce", "CollisionBounce", 0.75f, -1f, 1f);
            collisionLifetimeLoss = AddSliderUnclamped("Collision Life Loss", "CollisionLifetimeLoss", 0.2f, -1f, 1f);
            collisionQualityDict.Add("Low", ParticleSystemCollisionQuality.Low);
            collisionQualityDict.Add("Medium", ParticleSystemCollisionQuality.Medium);
            collisionQualityDict.Add("High", ParticleSystemCollisionQuality.High);
            collisionQuality = AddMenu("Quality", 1, collisionQualityDict.Keys.ToList(), true);

            shaderDict.Add("Alpha Blend", GameMaterials.Shaders.Particles.AlphaBlended);
            shaderDict.Add("Additive", GameMaterials.Shaders.Particles.Additive);
            shaderDict.Add("Multiply", Shader.Find("Particles/Multiply"));
            shaderDict.Add("Overlay", GameMaterials.Shaders.Blocks.Pin);
            shaderDict.Add("Vertex lit", GameMaterials.Shaders.Entities.VertexLit);
            shaderDict.Add("Loading", GameMaterials.Shaders.Misc.Loading);
            shaderDict.Add("Render Text Cut", GameMaterials.Shaders.Misc.RenderTextureCutout);
            shaderMenu = AddMenu("PartShaderMenuKey", 1, shaderDict.Keys.ToList(), false);

            // The particle textures are named in ParticleEmitter.xml rather than
            // here, so the set can be changed without rebuilding the assembly.
            foreach (object reference in Module.ParticleTextures)
            {
                ModTexture texture = (ModTexture)GetResource((Modding.Serialization.ResourceReference)reference);
                textureNames.Add(texture.Name);
            }
            textureMenu = AddMenu("ParticleTextureMenuKey", 0, textureNames, false);

            settingsMenu.ValueChanged += PageChanged;
            colorMenu.ValueChanged += ColorModeChanged;
            sizeMenu.ValueChanged += SizeModeChanged;
            rotationMenu.ValueChanged += RotationModeChanged;
            heatwave.Toggled += ShowHeatwaveControls;
            colorChanges.Toggled += ShowColorChangeControls;
            sizeChanges.Toggled += ShowSizeChangeControls;
            rotationChanges.Toggled += ShowRotationChangeControls;
            collisionToggle.Toggled += ShowCollisionControls;
        }

        private static List<string> ChangeModes()
        {
            return new List<string> { "Lifetime", "Speed", "Random" };
        }

        // The particle system lives on a child object of its own, pointing out of
        // the block's nozzle. Reused if it is already there, which it is on a
        // reload.
        private void CreateParticleSystem()
        {
            foreach (Transform child in gameObject.GetComponentsInChildren<Transform>())
            {
                if (child.name == "ParticleHandler")
                {
                    particleHandler = child.gameObject;
                    break;
                }
            }

            if (particleHandler == null)
            {
                particleHandler = new GameObject();
                particleHandler.transform.name = "ParticleHandler";
                particleHandler.transform.parent = gameObject.transform;
                particleHandler.transform.localPosition = Vector3.forward * 1.8f;
                particleHandler.transform.localRotation =
                    Quaternion.Euler(-particleHandler.transform.rotation.eulerAngles);
                particleHandler.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            }

            particleSys = particleHandler.GetComponent<ParticleSystem>();
            if (particleSys == null) particleSys = particleHandler.AddComponent<ParticleSystem>();
            particleSys.Stop();
            particleSys.playOnAwake = false;

            particleRenderer = particleHandler.GetComponent<Renderer>();
            if (particleRenderer == null) particleRenderer = particleHandler.AddComponent<Renderer>();

            heatwaveMat = FindHeatwaveMaterial();

            moduleEmission = particleSys.emission;
            moduleShape = particleSys.shape;
            moduleLimitVelocity = particleSys.limitVelocityOverLifetime;
            moduleColorSpeed = particleSys.colorBySpeed;
            moduleColorLife = particleSys.colorOverLifetime;
            moduleSizeSpeed = particleSys.sizeBySpeed;
            moduleSizeLife = particleSys.sizeOverLifetime;
            moduleRotationSpeed = particleSys.rotationBySpeed;
            moduleRotationLife = particleSys.rotationOverLifetime;
            moduleCollision = particleSys.collision;
        }

        // The heat shimmer is the game's own, lifted off the bomb's explosion
        // effect. Nothing here owns that hierarchy, so every step is checked
        // rather than assumed: a miss costs the Heatwave toggle, not the block.
        private Material FindHeatwaveMaterial()
        {
            BlockPrefab bomb;
            if (!PrefabMaster.BlockPrefabs.TryGetValue((int)BlockType.Bomb, out bomb)) return null;

            ExplodeOnCollideBlock explode = bomb.gameObject.GetComponent<ExplodeOnCollideBlock>();
            if (explode == null || explode.explosionEffect == null) return null;

            Transform puff = explode.explosionEffect.Find("PyroclasticPuff");
            if (puff == null) return null;
            Transform ripple = puff.Find("Ripple");
            if (ripple == null) return null;

            MeshRenderer renderer = ripple.GetComponent<MeshRenderer>();
            if (renderer == null) return null;
            return Instantiate(renderer.material);
        }

        // Only one page of the settings menu is shown at a time; everything else
        // is hidden. The per-page toggles then decide what within a page shows.
        private void PageChanged(int value)
        {
            ShowGeneralControls(value == PageGeneral);

            Show(value == PageEmission, emissionRate, emissionShape, emissionAngle);
            Show(value == PageDampen, dampenValue, dampenLimit);

            if (value == PageColor)
            {
                Show(true, heatwave, colorStart, opacityStart, colorChanges);
                heatwaveScale.DisplayInMapper = heatwave.IsActive;
                ShowColorChangeControls(colorChanges.IsActive);
            }
            else
            {
                Show(false, heatwave, heatwaveScale, colorStart, opacityStart, colorChanges,
                    colorMenu, colorEnd, opacityEnd, colorSpeedMin, colorSpeedMax);
            }

            if (value == PageSize)
            {
                Show(true, sizeStart, sizeChanges);
                ShowSizeChangeControls(sizeChanges.IsActive);
            }
            else
            {
                Show(false, sizeStart, sizeChanges, sizeMenu, sizeEnd, sizeSpeedMin, sizeSpeedMax);
            }

            if (value == PageRotation)
            {
                Show(true, rotationStart, rotationChanges);
                ShowRotationChangeControls(rotationChanges.IsActive);
            }
            else
            {
                Show(false, rotationStart, rotationChanges, rotationMenu,
                    rotationSpeedMin, rotationSpeedMax);
            }

            collisionToggle.DisplayInMapper = value == PageCollision;
            ShowCollisionControls(value == PageCollision && collisionToggle.IsActive);
        }

        private static void Show(bool state, params MapperType[] controls)
        {
            foreach (MapperType control in controls) control.DisplayInMapper = state;
        }

        private void ShowGeneralControls(bool state)
        {
            Show(state, activate, toggleable, loop, speed, maxCount, lifeTime, gravity,
                playbackSpeed, physMenu, disableCollider, shaderMenu, textureMenu);
        }

        private void ShowHeatwaveControls(bool state)
        {
            heatwaveScale.DisplayInMapper = state;
        }

        // "Changes" opens up the end value and how the change is driven; the
        // min/max speed pair only applies to the by-speed mode.
        private void ShowColorChangeControls(bool state)
        {
            Show(state, colorMenu, colorEnd, opacityEnd);
            Show(state && colorMenu.Value == BySpeed, colorSpeedMin, colorSpeedMax);
        }

        private void ShowSizeChangeControls(bool state)
        {
            Show(state, sizeMenu, sizeEnd);
            Show(state && sizeMenu.Value == BySpeed, sizeSpeedMin, sizeSpeedMax);
        }

        private void ShowRotationChangeControls(bool state)
        {
            Show(state, rotationMenu);
            Show(state && rotationMenu.Value == BySpeed, rotationSpeedMin, rotationSpeedMax);
        }

        private void ShowCollisionControls(bool state)
        {
            Show(state, collisionRadius, collisionDampen, collisionBounce,
                collisionLifetimeLoss, collisionQuality);
        }

        private void ColorModeChanged(int value)
        {
            Show(value == BySpeed, colorSpeedMin, colorSpeedMax);
        }

        private void SizeModeChanged(int value)
        {
            Show(value == BySpeed, sizeSpeedMin, sizeSpeedMax);
        }

        private void RotationModeChanged(int value)
        {
            Show(value == BySpeed, rotationSpeedMin, rotationSpeedMax);
        }

        public override void SimulateUpdateAlways()
        {
            if (!hasStarted)
            {
                Rigidbody.detectCollisions = false;
                if (startFrames == StartFrame) Begin();
                else startFrames++;
            }

            // The "Random" modes re-roll every frame, so unlike everything else
            // they cannot be applied once on the startup frame.
            if (colorChanges.IsActive && colorMenu.Value == Randomly)
                particleSys.startColor = gradient.Evaluate(Random.Range(0f, 1f));
            if (sizeChanges.IsActive && sizeMenu.Value == Randomly)
                particleSys.startSize = Random.Range(sizeStart.Value, sizeEnd.Value);
            if (rotationChanges.IsActive && rotationMenu.Value == Randomly)
                particleSys.startRotation = Random.Range(rotationStart.Value * Mathf.Deg2Rad, Mathf.PI);

            if (toggleable.IsActive)
            {
                if (activate.IsPressed)
                {
                    wasToggled = !wasToggled;
                    if (wasToggled) particleSys.Play();
                    else particleSys.Stop();
                }
            }
            else if (activate.IsPressed) particleSys.Play();
            else if (activate.IsReleased) particleSys.Stop();
        }

        // The startup frame: the whole mapper is pushed into the particle system
        // at once. Changing a slider mid-run does nothing until the next run.
        private void Begin()
        {
            hasStarted = true;

            // A collider-less emitter is meant to be decoration, so it also loses
            // its mass rather than hanging weight off the machine.
            if (disableCollider.IsActive)
            {
                VisualController.SetInvisible();
                Rigidbody.detectCollisions = false;
                Rigidbody.mass = 0f;
            }
            else
            {
                Rigidbody.detectCollisions = true;
            }

            particleSys.scalingMode = ParticleSystemScalingMode.Shape;
            particleRenderer.material.shader = shaderDict[shaderMenu.Selection];
            particleSys.maxParticles = Mathf.RoundToInt(maxCount.Value);
            particleSys.startLifetime = lifeTime.Value;
            particleSys.startSpeed = speed.Value;
            particleSys.gravityModifier = gravity.Value;
            particleSys.playbackSpeed = playbackSpeed.Value;
            particleSys.loop = loop.IsActive;
            particleSys.simulationSpace = physMenu.Value == 0
                ? ParticleSystemSimulationSpace.World
                : ParticleSystemSimulationSpace.Local;
            ApplyTexture(textureMenu.Selection);

            moduleEmission.rate = emissionRate.Value;
            moduleEmission.enabled = true;

            moduleShape.shapeType = (ParticleSystemShapeType)emissionShape.Value;
            moduleShape.angle = emissionAngle.Value;
            moduleShape.enabled = true;

            // Zero dampening is not "no limit" to Unity -- it pins every particle
            // in place -- so the module stays off entirely at that setting.
            if (dampenValue.Value != 0f)
            {
                moduleLimitVelocity.dampen = dampenValue.Value;
                moduleLimitVelocity.limit = dampenLimit.Value;
                moduleLimitVelocity.enabled = true;
            }

            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(colorStart.Value, 0f),
                    new GradientColorKey(colorEnd.Value, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(opacityStart.Value, 0f),
                    new GradientAlphaKey(opacityEnd.Value, 1f)
                });

            if (colorChanges.IsActive)
            {
                moduleColorSpeed.range = new Vector2(colorSpeedMin.Value, colorSpeedMax.Value);
                moduleColorSpeed.color = gradient;
                moduleColorLife.color = gradient;
                moduleColorLife.enabled = colorMenu.Value == OverLifetime;
                moduleColorSpeed.enabled = colorMenu.Value == BySpeed;
            }
            else
            {
                Color col = colorStart.Value;
                particleSys.startColor = new Color(col.r, col.g, col.b, opacityStart.Value);
            }

            particleSys.startSize = sizeStart.Value;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, sizeStart.Value);
            sizeCurve.AddKey(1f, sizeEnd.Value);
            moduleSizeSpeed.range = new Vector2(sizeSpeedMin.Value, sizeSpeedMax.Value);
            moduleSizeSpeed.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
            moduleSizeLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
            if (sizeChanges.IsActive)
            {
                moduleSizeLife.enabled = sizeMenu.Value == OverLifetime;
                moduleSizeSpeed.enabled = sizeMenu.Value == BySpeed;
            }

            particleSys.startRotation = rotationStart.Value * Mathf.Deg2Rad;
            moduleRotationSpeed.range = new Vector2(rotationSpeedMin.Value, rotationSpeedMax.Value);
            if (rotationChanges.IsActive)
            {
                // Random spin is re-rolled per frame above, but it still needs the
                // over-lifetime module running to actually turn the particle.
                moduleRotationLife.enabled =
                    rotationMenu.Value == OverLifetime || rotationMenu.Value == Randomly;
                moduleRotationSpeed.enabled = rotationMenu.Value == BySpeed;
            }

            moduleCollision.radiusScale = collisionRadius.Value;
            moduleCollision.dampen = collisionDampen.Value;
            moduleCollision.bounce = collisionBounce.Value;
            moduleCollision.lifetimeLoss = collisionLifetimeLoss.Value;
            moduleCollision.quality = collisionQualityDict[collisionQuality.Selection];
            moduleCollision.enableDynamicColliders = true;
            moduleCollision.enableInteriorCollisions = false;
            moduleCollision.mode = ParticleSystemCollisionMode.Collision3D;
            moduleCollision.type = ParticleSystemCollisionType.World;
            moduleCollision.enabled = collisionToggle.IsActive;

            if (heatwave.IsActive && heatwaveMat != null) AddHeatwave();

            // The emitter is heavier than its mounting suggests; without this it
            // snaps off the moment the machine moves.
            ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
            joint.breakForce = JointBreakForce;
            joint.breakTorque = JointBreakForce;
        }

        // The shimmer is a second copy of the whole system drawn with the bomb's
        // distortion material, so it follows the particles exactly.
        private void AddHeatwave()
        {
            ParticleSystem shimmer = (ParticleSystem)Instantiate(particleSys, particleSys.transform);
            ParticleSystemRenderer shimmerRenderer = shimmer.GetComponent<ParticleSystemRenderer>();
            shimmerRenderer.material = heatwaveMat;
            shimmerRenderer.material.SetTextureScale("_BumpMap",
                heatwaveScale.Value * new Vector2(1f, 1f));
        }

        private void ApplyTexture(string name)
        {
            if (!textureNames.Contains(name))
            {
                ModConsole.Log("Texture not found!");
                return;
            }

            try
            {
                particleRenderer.material.mainTexture = ModResource.GetTexture(name);
            }
            catch (System.Exception e)
            {
                ModConsole.Log("Failed to load texture!");
                ModConsole.Log(e.ToString());
            }
        }

        // Besiege keeps the behaviour alive between runs, so without this a second
        // run would start with the startup work above already marked done, and
        // with the emitter latched wherever the first run left it.
        public override void OnSimulateStart()
        {
            hasStarted = false;
            startFrames = 0;
            wasToggled = false;
        }
    }
}
