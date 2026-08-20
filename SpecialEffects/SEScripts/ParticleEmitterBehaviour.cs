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

        // How much wider than its particle a heat shimmer is drawn, and the
        // spread of turn rates across shimmers, in radians per second.
        private const float HeatwaveSize = 1.5f;
        private const float HeatwaveSpin = 1.5f;

        private GameObject particleHandler;
        private ParticleSystem particleSys;
        private ParticleSystemRenderer particleRenderer;

        private GameObject heatwaveHandler;
        private ParticleSystem heatwaveSys;
        private ParticleSystemRenderer heatwaveRenderer;

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
            Skins.Hide(BlockBehaviour);
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
            heatwaveScale = AddSliderUnclamped("Heatwave Strength", "HeatwaveScaleKey", 0.15f, 0.01f, 1f);
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

            // Named in ParticleEmitter.xml, so the set can change without a rebuild.
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
        // the block's nozzle.
        private void CreateParticleSystem()
        {
            bool created;
            particleHandler = Attach.Child(gameObject.transform, "ParticleHandler", out created);
            if (created)
            {
                particleHandler.transform.localPosition = Vector3.forward * 1.8f;
                particleHandler.transform.localRotation =
                    Quaternion.Euler(-particleHandler.transform.rotation.eulerAngles);
                particleHandler.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            }

            particleSys = Attach.Component<ParticleSystem>(particleHandler);
            particleSys.Stop();
            particleSys.playOnAwake = false;
            particleRenderer = Attach.Component<ParticleSystemRenderer>(particleHandler);

            CreateHeatwaveSystem();

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

        // The shimmer sits exactly on the emitter as a child of it, which is also
        // what makes Play and Stop reach it: both recurse into child systems. It is
        // found by name rather than cloned, so re-running the simulation reuses
        // this one instead of stacking another shimmer on top every run.
        private void CreateHeatwaveSystem()
        {
            heatwaveHandler = Attach.Child(particleHandler.transform, "HeatwaveHandler");
            heatwaveHandler.transform.localPosition = Vector3.zero;
            heatwaveHandler.transform.localRotation = Quaternion.identity;
            heatwaveHandler.transform.localScale = Vector3.one;

            heatwaveSys = Attach.Component<ParticleSystem>(heatwaveHandler);
            heatwaveSys.Stop();
            heatwaveSys.playOnAwake = false;

            heatwaveRenderer = Attach.Component<ParticleSystemRenderer>(heatwaveHandler);
            if (Heatwave.Available) heatwaveRenderer.material = Heatwave.CreateMaterial();
            heatwaveHandler.SetActive(false);
        }

        // Only one page of the settings menu is shown at a time; everything else
        // is hidden. The per-page toggles then decide what within a page shows.
        private void PageChanged(int value)
        {
            ShowGeneralControls(value == PageGeneral);

            Controls.Show(value == PageEmission, emissionRate, emissionShape, emissionAngle);
            Controls.Show(value == PageDampen, dampenValue, dampenLimit);

            if (value == PageColor)
            {
                Controls.Show(true, heatwave, colorStart, opacityStart, colorChanges);
                heatwaveScale.DisplayInMapper = heatwave.IsActive;
                ShowColorChangeControls(colorChanges.IsActive);
            }
            else
            {
                Controls.Show(false, heatwave, heatwaveScale, colorStart, opacityStart, colorChanges,
                    colorMenu, colorEnd, opacityEnd, colorSpeedMin, colorSpeedMax);
            }

            if (value == PageSize)
            {
                Controls.Show(true, sizeStart, sizeChanges);
                ShowSizeChangeControls(sizeChanges.IsActive);
            }
            else
            {
                Controls.Show(false, sizeStart, sizeChanges, sizeMenu, sizeEnd, sizeSpeedMin, sizeSpeedMax);
            }

            if (value == PageRotation)
            {
                Controls.Show(true, rotationStart, rotationChanges);
                ShowRotationChangeControls(rotationChanges.IsActive);
            }
            else
            {
                Controls.Show(false, rotationStart, rotationChanges, rotationMenu,
                    rotationSpeedMin, rotationSpeedMax);
            }

            collisionToggle.DisplayInMapper = value == PageCollision;
            ShowCollisionControls(value == PageCollision && collisionToggle.IsActive);
        }

        private void ShowGeneralControls(bool state)
        {
            Controls.Show(state, activate, toggleable, loop, speed, maxCount, lifeTime, gravity,
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
            Controls.Show(state, colorMenu, colorEnd, opacityEnd);
            Controls.Show(state && colorMenu.Value == BySpeed, colorSpeedMin, colorSpeedMax);
        }

        private void ShowSizeChangeControls(bool state)
        {
            Controls.Show(state, sizeMenu, sizeEnd);
            Controls.Show(state && sizeMenu.Value == BySpeed, sizeSpeedMin, sizeSpeedMax);
        }

        private void ShowRotationChangeControls(bool state)
        {
            Controls.Show(state, rotationMenu);
            Controls.Show(state && rotationMenu.Value == BySpeed, rotationSpeedMin, rotationSpeedMax);
        }

        private void ShowCollisionControls(bool state)
        {
            Controls.Show(state, collisionRadius, collisionDampen, collisionBounce,
                collisionLifetimeLoss, collisionQuality);
        }

        private void ColorModeChanged(int value)
        {
            Controls.Show(value == BySpeed, colorSpeedMin, colorSpeedMax);
        }

        private void SizeModeChanged(int value)
        {
            Controls.Show(value == BySpeed, sizeSpeedMin, sizeSpeedMax);
        }

        private void RotationModeChanged(int value)
        {
            Controls.Show(value == BySpeed, rotationSpeedMin, rotationSpeedMax);
        }

        public override void SimulateUpdateAlways()
        {
            if (!hasStarted)
            {
                Rigidbody.detectCollisions = false;
                if (startFrames == StartFrame) Begin();
                else startFrames++;
            }

            // The "Random" modes re-roll every frame, unlike everything else.
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

        // The startup frame: the whole mapper goes into the particle system at
        // once, so a slider moved mid-run does nothing until the next run.
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
                // Random spin still needs the over-lifetime module to turn it.
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

            ConfigureHeatwave();

            // The emitter is heavier than its mounting suggests; without this it
            // snaps off the moment the machine moves.
            ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
            joint.breakForce = JointBreakForce;
            joint.breakTorque = JointBreakForce;
        }

        // The shimmer mirrors how the emitter throws its particles -- rate, shape,
        // speed, lifetime -- but nothing about how they are coloured, since it is
        // not drawing a texture. Read off the emitter so the two cannot drift.
        private void ConfigureHeatwave()
        {
            bool on = heatwave.IsActive && Heatwave.Available;
            heatwaveHandler.SetActive(on);
            if (!on) return;

            heatwaveSys.maxParticles = particleSys.maxParticles;
            heatwaveSys.startLifetime = particleSys.startLifetime;
            heatwaveSys.startSpeed = particleSys.startSpeed;
            heatwaveSys.gravityModifier = particleSys.gravityModifier;
            heatwaveSys.playbackSpeed = particleSys.playbackSpeed;
            heatwaveSys.loop = particleSys.loop;
            heatwaveSys.simulationSpace = particleSys.simulationSpace;
            heatwaveSys.scalingMode = particleSys.scalingMode;

            // Air bends around something hot rather than only in front of it, so
            // the shimmer is drawn a little wider than the particle it follows.
            heatwaveSys.startSize = sizeStart.Value * HeatwaveSize;

            ParticleSystem.EmissionModule emission = heatwaveSys.emission;
            emission.rate = emissionRate.Value;
            emission.enabled = true;

            ParticleSystem.ShapeModule shape = heatwaveSys.shape;
            shape.shapeType = (ParticleSystemShapeType)emissionShape.Value;
            shape.angle = emissionAngle.Value;
            shape.enabled = true;

            // Each shimmer turns at its own rate, so the ripple rings never line up
            // between particles and the haze reads as moving air.
            ParticleSystem.RotationOverLifetimeModule spin = heatwaveSys.rotationOverLifetime;
            spin.z = new ParticleSystem.MinMaxCurve(-HeatwaveSpin, HeatwaveSpin);
            spin.enabled = true;

            Heatwave.SetStrength(heatwaveRenderer.material, heatwaveScale.Value);

            // The emitter can be switched on in the frames before this runs, and
            // Play does not reach a child that was still inactive at the time.
            if (particleSys.isPlaying) heatwaveSys.Play();
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
        // run starts with the startup work already marked done.
        public override void OnSimulateStart()
        {
            hasStarted = false;
            startFrames = 0;
            wasToggled = false;
        }
    }
}
