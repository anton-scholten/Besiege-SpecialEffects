using Modding;
using UnityEngine;

namespace SpecialEffectsMod
{
    // Puts LightShafts on a light and feeds it its shaders. Shared by the Spot
    // Light block and the Spot Light entity, which are otherwise unrelated.
    //
    // The shafts component has to sit on the same object as the Light, and that
    // object's +Z has to be the beam: the block's own GameObject is both, and so
    // is the entity's LightPiece child.
    public static class Shafts
    {
        // The shaders are built per graphics API, so there are two bundles.
        // Besiege on Linux is an OpenGL build like the Mac one, which is why this
        // is "Windows or not" rather than a three-way test.
        private const string WindowsBundle = "LightShaftsShadersWin";
        private const string OpenGLBundle = "LightShaftsShadersMac";
        private const string ShaderPath = "Assets/LightShafts-master/";

        // Start cannot be 0: the raymarch shader unstretches its shadowmap UVs by
        // dividing by the distance from the cone's apex, so a volume reaching the
        // apex divides by zero and the beam comes out NaN. A cone has no width at
        // its tip, so nothing is lost. End must clear Start for the same reason.
        private const float MinNear = 0.005f;
        private const float MinDepth = 0.01f;

        private static ModAssetBundle shaders;
        private static bool searched;

        // Added disabled: until switched on there is no reason to pay for a
        // shadowmap and six render textures.
        public static LightShafts Add(GameObject host)
        {
            LightShafts shafts = Attach.Component<LightShafts>(host);
            ModAssetBundle bundle = Shaders();
            if (bundle != null)
            {
                shafts.m_DepthShader = LoadShader(bundle, "Depth");
                shafts.m_ColorFilterShader = LoadShader(bundle, "ColorFilter");
                shafts.m_CoordShader = LoadShader(bundle, "Coord");
                shafts.m_DepthBreaksShader = LoadShader(bundle, "DepthBreaks");
                shafts.m_RaymarchShader = LoadShader(bundle, "Raymarch");
                shafts.m_InterpolateAlongRaysShader = LoadShader(bundle, "InterpolateAlongRays");
                shafts.m_FinalInterpolationShader = LoadShader(bundle, "FinalInterpolation");
                shafts.m_SamplePositionsShader = LoadShader(bundle, "SamplePositions");
            }
            shafts.enabled = false;
            return shafts;
        }

        // Everything a lamp's shaft settings amount to, in one place so the block
        // and the entity cannot drift apart. `volume` is the box a directional
        // light fills; `start` and `end` are along a spot light's own cone.
        public static void Set(LightShafts shafts, LightType type, float brightness,
            float fade, float start, float end, Vector3 volume, bool movingShadows)
        {
            shafts.m_Brightness = brightness;
            shafts.m_Size = volume;

            float near = Mathf.Max(start, MinNear);
            shafts.m_SpotNear = near;
            shafts.m_SpotFar = Mathf.Max(end, near + MinDepth);

            // A spot light's own falloff is fixed in the shader, so fade rides on
            // top of it; for a directional light it is the whole falloff.
            shafts.m_Extinction = fade;
            shafts.SetFade(fade, type != LightType.Directional);

            // Moving shadows off keeps one rendering of the shadowmap instead of
            // making a new one each frame. The switch must invalidate it.
            bool keep = !movingShadows;
            if (shafts.m_ShadowmapStatic != keep)
            {
                shafts.m_ShadowmapStatic = keep;
                shafts.SetShadowmapDirty();
            }
        }

        // Switches the shafts on or off and keeps them pointed at the camera.
        // Besiege swaps cameras between the build area, the level editor and a
        // running level, and shafts only draw for one they have been handed.
        public static void Follow(LightShafts shafts, bool on, GameObject lamp)
        {
            if (shafts.enabled != on)
            {
                shafts.enabled = on;
                // Re-read on the way up, not once: the entity's housing arrives a
                // frame or two late and a block's lens is restyled between runs.
                if (on) shafts.m_Ignored = lamp.GetComponentsInChildren<Renderer>(true);
            }
            if (on) shafts.SetCameras(Camera.main);
        }

        private static Shader LoadShader(ModAssetBundle bundle, string name)
        {
            return bundle.LoadAsset<Shader>(ShaderPath + name + ".shader");
        }

        // The platform's bundle, falling back to the other if its shaders do not
        // compile here -- with none that work the effect silently does nothing.
        private static ModAssetBundle Shaders()
        {
            if (searched) return shaders;
            searched = true;

            bool windows = Application.platform == RuntimePlatform.WindowsPlayer
                        || Application.platform == RuntimePlatform.WindowsEditor;
            shaders = Usable(windows ? WindowsBundle : OpenGLBundle);
            if (shaders == null) shaders = Usable(windows ? OpenGLBundle : WindowsBundle);
            if (shaders == null)
                Debug.LogError("SpecialEffects: neither LightShafts shader bundle compiles here; " +
                    "light shafts will not draw.");
            return shaders;
        }

        private static ModAssetBundle Usable(string name)
        {
            ModAssetBundle bundle = ModResource.GetAssetBundle(name);
            if (bundle == null || bundle.HasError) return null;

            Shader probe = LoadShader(bundle, "Raymarch");
            return probe != null && probe.isSupported ? bundle : null;
        }
    }
}
