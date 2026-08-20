using UnityEngine;

namespace SpecialEffectsMod
{
    // Volumetric light shafts by epipolar sampling: instead of raymarching every
    // screen pixel, it raymarches a few hundred samples placed where the light
    // actually changes -- silhouettes of shadow casters -- and interpolates the
    // rest. The shaders are in the LightShafts asset bundle.
    //
    // Vendored from robcupisz/LightShafts (public domain), which is what the
    // NoLightNoLife mod compiled in too. Upstream's three partial-class files are
    // merged so the type is one file like everything else here. Other changes,
    // each marked "not upstream" where it sits:
    //
    //   * LightShaftsShadowmapMode is a bool -- an enum segfaults Besiege's
    //     compiler (AGENTS.md).
    //   * m_EpipolarLines defaults to 512, as NoLightNoLife had it.
    //   * OnDrawGizmosSelected dropped; it only ran in the Unity editor.
    //   * SetCameras, OnDestroy, m_Ignored, SetFade, and the dynamic-to-static
    //     shadowmap switch. Upstream's owner is a light placed once in a scene
    //     and never touched; these lamps are built, aimed and thrown away.
    [RequireComponent(typeof(Light))]
    public class LightShafts : MonoBehaviour
    {
        // Render the shadowmap once and keep it, rather than every frame. Cheap,
        // but nothing that moves casts a moving shadow in the beam.
        public bool m_ShadowmapStatic;
        private bool m_ShadowmapStaticOld;
        public Camera[] m_Cameras;
        public Camera m_CurrentCamera;
        private bool m_ShadowmapDirty = true;
        public Vector3 m_Size = new Vector3(10, 10, 20);
        public float m_SpotNear = 0.1f;
        public float m_SpotFar = 1.0f;
        public LayerMask m_CullingMask = ~0;
        public LayerMask m_ColorFilterMask = 0;
        public float m_Brightness = 5;
        public float m_BrightnessColored = 5;
        public float m_Extinction = 0.5f;
        public float m_MinDistFromCamera = 0.0f;

        public int m_ShadowmapRes = 1024;
        private Camera m_ShadowmapCamera;
        private RenderTexture m_Shadowmap;
        public Shader m_DepthShader;
        private RenderTexture m_ColorFilter;
        public Shader m_ColorFilterShader;
        public bool m_Colored = false;
        public float m_ColorBalance = 1.0f;

        public int m_EpipolarLines = 512;
        public int m_EpipolarSamples = 512;
        private RenderTexture m_CoordEpi;
        private RenderTexture m_DepthEpi;
        public Shader m_CoordShader;
        private Material m_CoordMaterial;

        private RenderTexture m_InterpolationEpi;
        public Shader m_DepthBreaksShader;
        private Material m_DepthBreaksMaterial;

        private RenderTexture m_RaymarchedLightEpi;
        private Material m_RaymarchMaterial;
        public Shader m_RaymarchShader;

        private RenderTexture m_InterpolateAlongRaysEpi;
        public Shader m_InterpolateAlongRaysShader;
        private Material m_InterpolateAlongRaysMaterial;

        private RenderTexture m_SamplePositions;
        public Shader m_SamplePositionsShader;
        private Material m_SamplePositionsMaterial;
        private bool m_SamplePositionsShaderCompiles = false;

        public Shader m_FinalInterpolationShader;
        private Material m_FinalInterpolationMaterial;

        public float m_DepthThreshold = 0.5f;
        public int m_InterpolationStep = 32;

        public bool m_ShowSamples = false;
        public bool m_ShowInterpolatedSamples = false;
        public float m_ShowSamplesBackgroundFade = 0.8f;

        public bool m_AttenuationCurveOn = false;
        public AnimationCurve m_AttenuationCurve;
        private Texture2D m_AttenuationCurveTex;

        private Light m_Light;
        private LightType m_LightType = LightType.Directional;
        private bool m_DX11Support = false;
        private bool m_MinRequirements = false;

        private Mesh m_SpotMesh;
        private float m_SpotMeshNear = -1;
        private float m_SpotMeshFar = -1;
        private float m_SpotMeshAngle = -1;
        private float m_SpotMeshRange = -1;

        // What SetFade last built the lookup table from.
        private float m_FadeBuilt = -1f;
        private bool m_FadeBuiltForSpot;

        // Not upstream. Renderers kept out of the shadowmap: the lamp's own
        // housing and lens sit inside the cone under a unit ahead of the light, so
        // they are the nearest thing it sees and they black the whole beam out.
        public Renderer[] m_Ignored;
        private bool[] m_IgnoredWere;

        // What the lamp looked like when a kept shadowmap was last rendered.
        private Vector3 m_KeptPosition;
        private Quaternion m_KeptRotation;
        private float m_KeptRange = -1;
        private float m_KeptAngle = -1;

        public bool directional { get { return m_LightType == LightType.Directional; } }
        public bool spot { get { return m_LightType == LightType.Spot; } }

        public void Start()
        {
            CheckMinRequirements();

            if (m_Cameras == null || m_Cameras.Length == 0)
                m_Cameras = new Camera[] { Camera.main };

            UpdateCameraDepthMode();
        }

        // Not upstream. Upstream latches Camera.main in Start; Besiege swaps
        // cameras, and shafts only draw for one in this list.
        public void SetCameras(Camera camera)
        {
            if (camera == null) return;
            if (m_Cameras != null && m_Cameras.Length == 1 && m_Cameras[0] == camera) return;

            m_Cameras = new Camera[] { camera };
            UpdateCameraDepthMode();
        }

        private void UpdateShadowmap()
        {
            if (m_ShadowmapStatic) KeepUpWithLamp();
            if (m_ShadowmapStatic && !m_ShadowmapDirty)
                return;

            InitShadowmap();

            if (m_ShadowmapCamera == null)
            {
                GameObject go = new GameObject("Depth Camera");
                go.AddComponent(typeof(Camera));
                m_ShadowmapCamera = go.GetComponent<Camera>();
                go.hideFlags = HideFlags.HideAndDontSave;
                m_ShadowmapCamera.enabled = false;
                m_ShadowmapCamera.clearFlags = CameraClearFlags.SolidColor;
            }
            Transform cam = m_ShadowmapCamera.transform;
            cam.position = transform.position;
            cam.rotation = transform.rotation;

            if (directional)
            {
                m_ShadowmapCamera.orthographic = true;
                m_ShadowmapCamera.nearClipPlane = 0;
                m_ShadowmapCamera.farClipPlane = m_Size.z;
                m_ShadowmapCamera.orthographicSize = m_Size.y * 0.5f;
                m_ShadowmapCamera.aspect = m_Size.x / m_Size.y;
            }
            else
            {
                m_ShadowmapCamera.orthographic = false;
                m_ShadowmapCamera.nearClipPlane = m_SpotNear * m_Light.range;
                m_ShadowmapCamera.farClipPlane = m_SpotFar * m_Light.range;
                m_ShadowmapCamera.fieldOfView = m_Light.spotAngle;
                m_ShadowmapCamera.aspect = 1.0f;
            }
            m_ShadowmapCamera.renderingPath = RenderingPath.Forward;
            m_ShadowmapCamera.targetTexture = m_Shadowmap;
            m_ShadowmapCamera.cullingMask = m_CullingMask;
            m_ShadowmapCamera.backgroundColor = Color.white;

            // Safe here and put straight back: OnRenderObject runs after the scene
            // has been drawn for this camera.
            HideIgnored();
            m_ShadowmapCamera.RenderWithShader(m_DepthShader, "RenderType");

            if (m_Colored)
            {
                m_ShadowmapCamera.targetTexture = m_ColorFilter;
                m_ShadowmapCamera.cullingMask = m_ColorFilterMask;
                m_ShadowmapCamera.backgroundColor = new Color(m_ColorBalance, m_ColorBalance, m_ColorBalance);
                m_ShadowmapCamera.RenderWithShader(m_ColorFilterShader, "");
            }
            RestoreIgnored();

            m_ShadowmapDirty = false;
        }

        // Not upstream. A kept shadowmap should freeze what stands in the beam,
        // not the beam: one from where the lamp used to be looks broken, not cheap.
        private void KeepUpWithLamp()
        {
            Transform t = transform;
            if (t.position == m_KeptPosition && t.rotation == m_KeptRotation
                && m_Light != null && m_Light.range == m_KeptRange
                && m_Light.spotAngle == m_KeptAngle)
                return;

            m_KeptPosition = t.position;
            m_KeptRotation = t.rotation;
            if (m_Light != null)
            {
                m_KeptRange = m_Light.range;
                m_KeptAngle = m_Light.spotAngle;
            }
            m_ShadowmapDirty = true;
        }

        private void HideIgnored()
        {
            if (m_Ignored == null) return;
            if (m_IgnoredWere == null || m_IgnoredWere.Length != m_Ignored.Length)
                m_IgnoredWere = new bool[m_Ignored.Length];

            for (int i = 0; i < m_Ignored.Length; i++)
            {
                if (m_Ignored[i] == null) continue;
                m_IgnoredWere[i] = m_Ignored[i].enabled;
                m_Ignored[i].enabled = false;
            }
        }

        // Back to what each one was, not simply "on": the lens and the housing are
        // switched off in their own right by the lamp's settings.
        private void RestoreIgnored()
        {
            if (m_Ignored == null || m_IgnoredWere == null) return;
            for (int i = 0; i < m_Ignored.Length; i++)
                if (m_Ignored[i] != null) m_Ignored[i].enabled = m_IgnoredWere[i];
        }

        private void RenderCoords(int width, int height, Vector4 lightPos)
        {
            SetFrustumRays(m_CoordMaterial);

            RenderBuffer[] buffers = { m_CoordEpi.colorBuffer, m_DepthEpi.colorBuffer };
            Graphics.SetRenderTarget(buffers, m_DepthEpi.depthBuffer);
            m_CoordMaterial.SetVector("_LightPos", lightPos);
            m_CoordMaterial.SetVector("_CoordTexDim", new Vector4(m_CoordEpi.width, m_CoordEpi.height, 1.0f / m_CoordEpi.width, 1.0f / m_CoordEpi.height));
            m_CoordMaterial.SetVector("_ScreenTexDim", new Vector4(width, height, 1.0f / width, 1.0f / height));
            m_CoordMaterial.SetPass(0);
            RenderQuad();
        }

        private void RenderInterpolationTexture(Vector4 lightPos)
        {
            Graphics.SetRenderTarget(m_InterpolationEpi.colorBuffer, m_RaymarchedLightEpi.depthBuffer);
            if (!m_DX11Support && (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer))
            {
                // Looks like in dx9 stencil is not cleared properly with GL.Clear()
                // Edit: fixed in 4.5, so this hack can be removed
                m_DepthBreaksMaterial.SetPass(1);
                RenderQuad();
            }
            else
            {
                GL.Clear(true, true, new Color(0, 0, 0, 1));
            }
            m_DepthBreaksMaterial.SetFloat("_InterpolationStep", m_InterpolationStep);
            m_DepthBreaksMaterial.SetFloat("_DepthThreshold", GetDepthThresholdAdjusted());
            m_DepthBreaksMaterial.SetTexture("_DepthEpi", m_DepthEpi);
            m_DepthBreaksMaterial.SetVector("_DepthEpiTexDim", new Vector4(m_DepthEpi.width, m_DepthEpi.height, 1.0f / m_DepthEpi.width, 1.0f / m_DepthEpi.height));
            m_DepthBreaksMaterial.SetPass(0);
            RenderQuadSections(lightPos);
        }

        private void InterpolateAlongRays(Vector4 lightPos)
        {
            Graphics.SetRenderTarget(m_InterpolateAlongRaysEpi);
            m_InterpolateAlongRaysMaterial.SetFloat("_InterpolationStep", m_InterpolationStep);
            m_InterpolateAlongRaysMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);
            m_InterpolateAlongRaysMaterial.SetTexture("_RaymarchedLightEpi", m_RaymarchedLightEpi);
            m_InterpolateAlongRaysMaterial.SetVector("_RaymarchedLightEpiTexDim", new Vector4(m_RaymarchedLightEpi.width, m_RaymarchedLightEpi.height, 1.0f / m_RaymarchedLightEpi.width, 1.0f / m_RaymarchedLightEpi.height));
            m_InterpolateAlongRaysMaterial.SetPass(0);
            RenderQuadSections(lightPos);
        }

        private void RenderSamplePositions(int width, int height, Vector4 lightPos)
        {
            InitRenderTexture(ref m_SamplePositions, width, height, 0, RenderTextureFormat.ARGB32, false);
            // Unfortunately can't be a temporary RT if we want random write
            m_SamplePositions.enableRandomWrite = true;
            m_SamplePositions.filterMode = FilterMode.Point;

            Graphics.SetRenderTarget(m_SamplePositions);
            GL.Clear(false, true, new Color(0, 0, 0, 1));

            Graphics.ClearRandomWriteTargets();
            Graphics.SetRandomWriteTarget(1, m_SamplePositions);

            // We need a render target with m_Coord dimensions, but reading and writing
            // to the same target produces wrong read results, so using a dummy.
            Graphics.SetRenderTarget(m_RaymarchedLightEpi);

            m_SamplePositionsMaterial.SetVector("_OutputTexDim", new Vector4(width - 1, height - 1, 0, 0));
            m_SamplePositionsMaterial.SetVector("_CoordTexDim", new Vector4(m_CoordEpi.width, m_CoordEpi.height, 0, 0));
            m_SamplePositionsMaterial.SetTexture("_Coord", m_CoordEpi);
            m_SamplePositionsMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);

            if (m_ShowInterpolatedSamples)
            {
                m_SamplePositionsMaterial.SetFloat("_SampleType", 1);
                m_SamplePositionsMaterial.SetVector("_Color", new Vector4(0.4f, 0.4f, 0, 0));
                m_SamplePositionsMaterial.SetPass(0);
                RenderQuad();
            }

            m_SamplePositionsMaterial.SetFloat("_SampleType", 0);
            m_SamplePositionsMaterial.SetVector("_Color", new Vector4(1, 0, 0, 0));
            m_SamplePositionsMaterial.SetPass(0);
            RenderQuadSections(lightPos);

            Graphics.ClearRandomWriteTargets();
        }

        private void ShowSamples(int width, int height, Vector4 lightPos)
        {
            bool showSamples = m_ShowSamples && m_DX11Support && m_SamplePositionsShaderCompiles;
            SetKeyword(showSamples, "SHOW_SAMPLES_ON", "SHOW_SAMPLES_OFF");
            if (showSamples)
                RenderSamplePositions(width, height, lightPos);

            m_FinalInterpolationMaterial.SetFloat("_ShowSamplesBackgroundFade", m_ShowSamplesBackgroundFade);
        }

        private void Raymarch(int width, int height, Vector4 lightPos)
        {
            SetFrustumRays(m_RaymarchMaterial);

            int shadowmapWidth = m_Shadowmap.width;
            int shadowmapHeight = m_Shadowmap.height;

            Graphics.SetRenderTarget(m_RaymarchedLightEpi.colorBuffer, m_RaymarchedLightEpi.depthBuffer);
            GL.Clear(false, true, new Color(0, 0, 0, 1));
            m_RaymarchMaterial.SetTexture("_Coord", m_CoordEpi);
            m_RaymarchMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);
            m_RaymarchMaterial.SetTexture("_Shadowmap", m_Shadowmap);
            float brightness = m_Colored ? m_BrightnessColored / m_ColorBalance : m_Brightness;
            brightness *= m_Light.intensity;
            m_RaymarchMaterial.SetFloat("_Brightness", brightness);
            m_RaymarchMaterial.SetFloat("_Extinction", -m_Extinction);
            m_RaymarchMaterial.SetVector("_ShadowmapDim", new Vector4(shadowmapWidth, shadowmapHeight, 1.0f / shadowmapWidth, 1.0f / shadowmapHeight));
            m_RaymarchMaterial.SetVector("_ScreenTexDim", new Vector4(width, height, 1.0f / width, 1.0f / height));
            m_RaymarchMaterial.SetVector("_LightColor", m_Light.color.linear);
            m_RaymarchMaterial.SetFloat("_MinDistFromCamera", m_MinDistFromCamera);
            SetKeyword(m_Colored, "COLORED_ON", "COLORED_OFF");
            m_RaymarchMaterial.SetTexture("_ColorFilter", m_ColorFilter);
            SetKeyword(m_AttenuationCurveOn, "ATTENUATION_CURVE_ON", "ATTENUATION_CURVE_OFF");
            m_RaymarchMaterial.SetTexture("_AttenuationCurveTex", m_AttenuationCurveTex);
            Texture cookie = m_Light.cookie;
            SetKeyword(cookie != null, "COOKIE_TEX_ON", "COOKIE_TEX_OFF");
            if (cookie != null)
                m_RaymarchMaterial.SetTexture("_Cookie", cookie);
            m_RaymarchMaterial.SetPass(0);

            RenderQuadSections(lightPos);
        }

        public void OnRenderObject()
        {
            m_CurrentCamera = Camera.current;
            if (!m_MinRequirements || !CheckCamera() || !IsVisible())
                return;

            // Prepare
            RenderBuffer depthBuffer = Graphics.activeDepthBuffer;
            RenderBuffer colorBuffer = Graphics.activeColorBuffer;
            InitResources();
            Vector4 lightPos = GetLightViewportPos();
            bool lightOnScreen = lightPos.x >= -1 && lightPos.x <= 1 && lightPos.y >= -1 && lightPos.y <= 1;
            SetKeyword(lightOnScreen, "LIGHT_ON_SCREEN", "LIGHT_OFF_SCREEN");
            int width = Screen.width;
            int height = Screen.height;

            // Render the buffers, raymarch, interpolate along rays
            UpdateShadowmap();
            SetKeyword(directional, "DIRECTIONAL_SHAFTS", "SPOT_SHAFTS");
            RenderCoords(width, height, lightPos);
            RenderInterpolationTexture(lightPos);
            Raymarch(width, height, lightPos);
            InterpolateAlongRays(lightPos);

            ShowSamples(width, height, lightPos);

            // Final interpolation and blending onto the screen
            SetFrustumRays(m_FinalInterpolationMaterial);
            m_FinalInterpolationMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);
            m_FinalInterpolationMaterial.SetTexture("_DepthEpi", m_DepthEpi);
            m_FinalInterpolationMaterial.SetTexture("_Shadowmap", m_Shadowmap);
            m_FinalInterpolationMaterial.SetTexture("_Coord", m_CoordEpi);
            m_FinalInterpolationMaterial.SetTexture("_SamplePositions", m_SamplePositions);
            m_FinalInterpolationMaterial.SetTexture("_RaymarchedLight", m_InterpolateAlongRaysEpi);
            m_FinalInterpolationMaterial.SetVector("_CoordTexDim", new Vector4(m_CoordEpi.width, m_CoordEpi.height, 1.0f / m_CoordEpi.width, 1.0f / m_CoordEpi.height));
            m_FinalInterpolationMaterial.SetVector("_ScreenTexDim", new Vector4(width, height, 1.0f / width, 1.0f / height));
            m_FinalInterpolationMaterial.SetVector("_LightPos", lightPos);
            m_FinalInterpolationMaterial.SetFloat("_DepthThreshold", GetDepthThresholdAdjusted());
            bool renderAsQuad = directional || IntersectsNearPlane();
            m_FinalInterpolationMaterial.SetFloat("_ZTest", (float)(renderAsQuad ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Less));
            SetKeyword(renderAsQuad, "QUAD_SHAFTS", "FRUSTUM_SHAFTS");

            Graphics.SetRenderTarget(colorBuffer, depthBuffer);
            m_FinalInterpolationMaterial.SetPass(0);
            if (renderAsQuad)
                RenderQuad();
            else
                RenderSpotFrustum();

            ReleaseResources();
        }

        private void InitLUTs()
        {
            if (m_AttenuationCurveTex)
                return;

            m_AttenuationCurveTex = new Texture2D(256, 1, TextureFormat.ARGB32, false, true);
            m_AttenuationCurveTex.wrapMode = TextureWrapMode.Clamp;
            m_AttenuationCurveTex.hideFlags = HideFlags.HideAndDontSave;

            if (m_AttenuationCurve == null || m_AttenuationCurve.length == 0)
                m_AttenuationCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));

            if (m_AttenuationCurveTex)
                UpdateLUTs();
        }

        public void UpdateLUTs()
        {
            InitLUTs();

            if (m_AttenuationCurve == null)
                return;

            for (int i = 0; i < 256; ++i)
            {
                float v = Mathf.Clamp(m_AttenuationCurve.Evaluate(i / 255.0f), 0.0f, 1.0f);
                m_AttenuationCurveTex.SetPixel(i, 0, new Color(v, v, v, v));
            }
            m_AttenuationCurveTex.Apply();
        }

        // How fast the beam thins along its length.
        //
        // Not upstream, and the only way the setting reaches a spot light at all:
        // m_Extinction feeds the raymarch shader's directional branch only, and its
        // spot branch has a fixed 1/(1+25d^2) falloff with nothing behind it. The
        // shader's lookup-table path takes a custom falloff, so fill that instead --
        // exp(-fade*d), which is what the directional branch computes, times the
        // fixed falloff for a spot so that fade 0 still looks like it did.
        //
        // Rebuilt only when the value moves: both callers hand it over every frame
        // and rebuilding is 256 SetPixel calls.
        public void SetFade(float fade, bool forSpot)
        {
            if (fade == m_FadeBuilt && forSpot == m_FadeBuiltForSpot) return;
            m_FadeBuilt = fade;
            m_FadeBuiltForSpot = forSpot;

            m_AttenuationCurveOn = true;
            InitLUTs();
            if (m_AttenuationCurveTex == null) return;

            for (int i = 0; i < 256; ++i)
            {
                float d = i / 255.0f;
                float v = Mathf.Exp(-fade * d);
                if (forSpot) v *= 1.0f / (1.0f + 25.0f * d * d);
                m_AttenuationCurveTex.SetPixel(i, 0, new Color(v, v, v, v));
            }
            m_AttenuationCurveTex.Apply();
        }

        private void InitRenderTexture(ref RenderTexture rt, int width, int height, int depth, RenderTextureFormat format, bool temp = true)
        {
            if (temp)
            {
                rt = RenderTexture.GetTemporary(width, height, depth, format);
            }
            else
            {
                if (rt != null)
                {
                    if (rt.width == width && rt.height == height && rt.depth == depth && rt.format == format)
                        return;

                    rt.Release();
                    DestroyImmediate(rt);
                }

                rt = new RenderTexture(width, height, depth, format);
                rt.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void InitShadowmap()
        {
            bool dynamic = !m_ShadowmapStatic;
            if (m_ShadowmapStatic != m_ShadowmapStaticOld)
            {
                if (dynamic)
                {
                    // Kept textures are ours to destroy; the pool provides them
                    // from here. Upstream released without destroying.
                    Free(ref m_Shadowmap);
                    Free(ref m_ColorFilter);
                }
                else
                {
                    // Pooled temporaries, already handed back. InitRenderTexture
                    // would keep using one because the size still matches, which is
                    // what made a Moving Shadows switch blank the beam.
                    m_Shadowmap = null;
                    m_ColorFilter = null;
                }
            }
            InitRenderTexture(ref m_Shadowmap, m_ShadowmapRes, m_ShadowmapRes, 24, RenderTextureFormat.RFloat, dynamic);
            m_Shadowmap.filterMode = FilterMode.Point;
            m_Shadowmap.wrapMode = TextureWrapMode.Clamp;

            if (m_Colored)
                InitRenderTexture(ref m_ColorFilter, m_ShadowmapRes, m_ShadowmapRes, 0, RenderTextureFormat.ARGB32, dynamic);

            m_ShadowmapStaticOld = m_ShadowmapStatic;
        }

        private void ReleaseShadowmap()
        {
            if (m_ShadowmapStatic)
                return;

            RenderTexture.ReleaseTemporary(m_Shadowmap);
            RenderTexture.ReleaseTemporary(m_ColorFilter);
        }

        private void InitEpipolarTextures()
        {
            m_EpipolarLines = m_EpipolarLines < 8 ? 8 : m_EpipolarLines;
            m_EpipolarSamples = m_EpipolarSamples < 4 ? 4 : m_EpipolarSamples;

            InitRenderTexture(ref m_CoordEpi, m_EpipolarSamples, m_EpipolarLines, 0, RenderTextureFormat.RGFloat);
            m_CoordEpi.filterMode = FilterMode.Point;
            InitRenderTexture(ref m_DepthEpi, m_EpipolarSamples, m_EpipolarLines, 0, RenderTextureFormat.RFloat);
            m_DepthEpi.filterMode = FilterMode.Point;
            InitRenderTexture(ref m_InterpolationEpi, m_EpipolarSamples, m_EpipolarLines, 0, m_DX11Support ? RenderTextureFormat.RGInt : RenderTextureFormat.RGFloat);
            m_InterpolationEpi.filterMode = FilterMode.Point;

            InitRenderTexture(ref m_RaymarchedLightEpi, m_EpipolarSamples, m_EpipolarLines, 24, RenderTextureFormat.ARGBFloat);
            m_RaymarchedLightEpi.filterMode = FilterMode.Point;
            InitRenderTexture(ref m_InterpolateAlongRaysEpi, m_EpipolarSamples, m_EpipolarLines, 0, RenderTextureFormat.ARGBFloat);
            m_InterpolateAlongRaysEpi.filterMode = FilterMode.Point;
        }

        private void InitMaterial(ref Material material, Shader shader)
        {
            if (material || !shader)
                return;
            material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
        }

        private void InitMaterials()
        {
            InitMaterial(ref m_FinalInterpolationMaterial, m_FinalInterpolationShader);
            InitMaterial(ref m_CoordMaterial, m_CoordShader);
            InitMaterial(ref m_SamplePositionsMaterial, m_SamplePositionsShader);
            InitMaterial(ref m_RaymarchMaterial, m_RaymarchShader);
            InitMaterial(ref m_DepthBreaksMaterial, m_DepthBreaksShader);
            InitMaterial(ref m_InterpolateAlongRaysMaterial, m_InterpolateAlongRaysShader);
        }

        private void InitSpotFrustumMesh()
        {
            if (!m_SpotMesh)
            {
                m_SpotMesh = new Mesh();
                m_SpotMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            Light l = m_Light;
            if (m_SpotMeshNear != m_SpotNear || m_SpotMeshFar != m_SpotFar || m_SpotMeshAngle != l.spotAngle || m_SpotMeshRange != l.range)
            {
                float far = l.range * m_SpotFar;
                float near = l.range * m_SpotNear;
                float tan = Mathf.Tan(l.spotAngle * Mathf.Deg2Rad * 0.5f);
                float halfwidthfar = far * tan;
                float halfwidthnear = near * tan;

                Vector3[] vertices = (m_SpotMesh.vertices != null && m_SpotMesh.vertices.Length == 8) ? m_SpotMesh.vertices : new Vector3[8];
                vertices[0] = new Vector3(-halfwidthfar, -halfwidthfar, far);
                vertices[1] = new Vector3(halfwidthfar, -halfwidthfar, far);
                vertices[2] = new Vector3(halfwidthfar, halfwidthfar, far);
                vertices[3] = new Vector3(-halfwidthfar, halfwidthfar, far);
                vertices[4] = new Vector3(-halfwidthnear, -halfwidthnear, near);
                vertices[5] = new Vector3(halfwidthnear, -halfwidthnear, near);
                vertices[6] = new Vector3(halfwidthnear, halfwidthnear, near);
                vertices[7] = new Vector3(-halfwidthnear, halfwidthnear, near);
                m_SpotMesh.vertices = vertices;

                if (m_SpotMesh.GetTopology(0) != MeshTopology.Triangles || m_SpotMesh.triangles == null || m_SpotMesh.triangles.Length != 36)
                {
                    //                          far           near          top           right         left          bottom
                    int[] triangles = new int[] { 0, 1, 2, 0, 2, 3, 6, 5, 4, 7, 6, 4, 3, 2, 6, 3, 6, 7, 2, 1, 5, 2, 5, 6, 0, 3, 7, 0, 7, 4, 5, 1, 0, 5, 0, 4 };
                    m_SpotMesh.triangles = triangles;
                }

                m_SpotMeshNear = m_SpotNear;
                m_SpotMeshFar = m_SpotFar;
                m_SpotMeshAngle = l.spotAngle;
                m_SpotMeshRange = l.range;
            }
        }

        public void UpdateLightType()
        {
            if (m_Light == null)
                m_Light = GetComponent<Light>();

            m_LightType = m_Light.type;
        }

        private bool ShaderCompiles(Shader shader)
        {
            if (shader == null)
            {
                Debug.LogError("LightShafts is missing one of its shaders.");
                return false;
            }

            if (!shader.isSupported)
            {
                Debug.LogError("LightShafts' " + shader.name + " didn't compile on this platform.");
                return false;
            }

            return true;
        }

        public bool CheckMinRequirements()
        {
            m_DX11Support = SystemInfo.graphicsShaderLevel >= 50;

            m_MinRequirements = SystemInfo.graphicsShaderLevel >= 30;
            m_MinRequirements &= SystemInfo.supportsRenderTextures;
            m_MinRequirements &= SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat);
            m_MinRequirements &= SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);

            if (!m_MinRequirements)
                Debug.LogError("LightShafts require Shader Model 3.0 and render textures (including the RGFloat and RFloat) formats. Disabling.");

            bool shadersCompile = ShaderCompiles(m_DepthShader) &&
                                  ShaderCompiles(m_ColorFilterShader) &&
                                  ShaderCompiles(m_CoordShader) &&
                                  ShaderCompiles(m_DepthBreaksShader) &&
                                  ShaderCompiles(m_RaymarchShader) &&
                                  ShaderCompiles(m_InterpolateAlongRaysShader) &&
                                  ShaderCompiles(m_FinalInterpolationShader);

            if (!shadersCompile)
                Debug.LogError("LightShafts require above shaders. Disabling.");

            m_MinRequirements &= shadersCompile;

            m_SamplePositionsShaderCompiles = m_SamplePositionsShader != null && m_SamplePositionsShader.isSupported;

            return m_MinRequirements;
        }

        private void InitResources()
        {
            UpdateLightType();

            InitMaterials();
            InitEpipolarTextures();
            InitLUTs();
            InitSpotFrustumMesh();
        }

        // Not upstream. Everything below is HideAndDontSave or a render texture, so
        // nothing else would collect it -- and a Depth Camera per lamp adds up. The
        // epipolar textures are deliberately absent: they are temporaries already
        // handed back, and releasing one twice is an error.
        private void OnDestroy()
        {
            if (m_ShadowmapCamera != null) Destroy(m_ShadowmapCamera.gameObject);

            // Only kept between frames in static mode; otherwise these are
            // temporaries too and the fields are stale references by now.
            if (m_ShadowmapStatic)
            {
                Free(ref m_Shadowmap);
                Free(ref m_ColorFilter);
            }
            Free(ref m_SamplePositions);

            Discard(m_AttenuationCurveTex);
            Discard(m_SpotMesh);
            Discard(m_CoordMaterial);
            Discard(m_DepthBreaksMaterial);
            Discard(m_RaymarchMaterial);
            Discard(m_InterpolateAlongRaysMaterial);
            Discard(m_SamplePositionsMaterial);
            Discard(m_FinalInterpolationMaterial);
        }

        private void Free(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        private void Discard(Object asset)
        {
            if (asset != null) Destroy(asset);
        }

        private void ReleaseResources()
        {
            ReleaseShadowmap();
            RenderTexture.ReleaseTemporary(m_CoordEpi);
            RenderTexture.ReleaseTemporary(m_DepthEpi);
            RenderTexture.ReleaseTemporary(m_InterpolationEpi);
            RenderTexture.ReleaseTemporary(m_RaymarchedLightEpi);
            RenderTexture.ReleaseTemporary(m_InterpolateAlongRaysEpi);
        }

        private Bounds GetBoundsLocal()
        {
            if (directional)
                return new Bounds(new Vector3(0, 0, m_Size.z * 0.5f), m_Size);

            Light l = m_Light;
            Vector3 offset = new Vector3(0, 0, l.range * (m_SpotFar + m_SpotNear) * 0.5f);
            float height = (m_SpotFar - m_SpotNear) * l.range;
            float baseSize = Mathf.Tan(l.spotAngle * Mathf.Deg2Rad * 0.5f) * m_SpotFar * l.range * 2.0f;
            return new Bounds(offset, new Vector3(baseSize, baseSize, height));
        }

        private Matrix4x4 GetBoundsMatrix()
        {
            Bounds bounds = GetBoundsLocal();
            Transform t = transform;
            return Matrix4x4.TRS(t.position + t.forward * bounds.center.z, t.rotation, bounds.size);
        }

        private float GetFrustumApex()
        {
            // Assuming the frustum is inscribed in a unit cube centered at 0
            return -m_SpotNear / (m_SpotFar - m_SpotNear) - 0.5f;
        }

        private void RenderQuadSections(Vector4 lightPos)
        {
            for (int i = 0; i < 4; i++)
            {
                // Skip one or two quarters, if the light is off screen
                if (i == 0 && lightPos.y > 1 ||
                    i == 1 && lightPos.x > 1 ||
                    i == 2 && lightPos.y < -1 ||
                    i == 3 && lightPos.x < -1)
                    continue;

                // index denotes which quarter of the screen to take up,
                // so start at -1, -0.5, 0 or 0.5
                float top = i / 2.0f - 1.0f;
                float bottom = top + 0.5f;
                GL.Begin(GL.QUADS);
                GL.Vertex3(-1, top, 0);
                GL.Vertex3(1, top, 0);
                GL.Vertex3(1, bottom, 0);
                GL.Vertex3(-1, bottom, 0);
                GL.End();
            }
        }

        private void RenderQuad()
        {
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0, 0);
            GL.Vertex3(-1, -1, 0);
            GL.TexCoord2(0, 1);
            GL.Vertex3(-1, 1, 0);
            GL.TexCoord2(1, 1);
            GL.Vertex3(1, 1, 0);
            GL.TexCoord2(1, 0);
            GL.Vertex3(1, -1, 0);
            GL.End();
        }

        private void RenderSpotFrustum()
        {
            Graphics.DrawMeshNow(m_SpotMesh, transform.position, transform.rotation);
        }

        private Vector4 GetLightViewportPos()
        {
            Vector3 lightPos = transform.position;
            if (directional)
                lightPos = m_CurrentCamera.transform.position + transform.forward;

            Vector3 lightViewportPos3 = m_CurrentCamera.WorldToViewportPoint(lightPos);
            return new Vector4(lightViewportPos3.x * 2.0f - 1.0f, lightViewportPos3.y * 2.0f - 1.0f, 0, 0);
        }

        private bool IsVisible()
        {
            // Intersect against spot light's OBB (or light frustum's OBB), so AABB in it's space
            Matrix4x4 lightToCameraProjection = m_CurrentCamera.projectionMatrix * m_CurrentCamera.worldToCameraMatrix * transform.localToWorldMatrix;
            return GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(lightToCameraProjection), GetBoundsLocal());
        }

        private bool IntersectsNearPlane()
        {
            // Lazy for now:
            // Just check if any vertex is behind the near plane.
            // TODO: same for directional
            Vector3[] vertices = m_SpotMesh.vertices;
            float nearPlaneFudged = m_CurrentCamera.nearClipPlane - 0.001f;
            Transform t = transform;
            for (int i = 0; i < vertices.Length; i++)
            {
                float z = m_CurrentCamera.WorldToViewportPoint(t.TransformPoint(vertices[i])).z;
                if (z < nearPlaneFudged)
                    return true;
            }
            return false;
        }

        private void SetKeyword(bool firstOn, string firstKeyword, string secondKeyword)
        {
            Shader.EnableKeyword(firstOn ? firstKeyword : secondKeyword);
            Shader.DisableKeyword(firstOn ? secondKeyword : firstKeyword);
        }

        public void SetShadowmapDirty()
        {
            m_ShadowmapDirty = true;
        }

        private void GetFrustumRays(out Matrix4x4 frustumRays, out Vector3 cameraPosLocal)
        {
            float far = m_CurrentCamera.farClipPlane;
            Vector3 cameraPos = m_CurrentCamera.transform.position;
            Matrix4x4 m = GetBoundsMatrix().inverse;
            Vector2[] uvs = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            frustumRays = new Matrix4x4();

            for (int i = 0; i < 4; i++)
            {
                Vector3 ray = m_CurrentCamera.ViewportToWorldPoint(new Vector3(uvs[i].x, uvs[i].y, far)) - cameraPos;
                ray = m.MultiplyVector(ray);
                frustumRays.SetRow(i, ray);
            }

            cameraPosLocal = m.MultiplyPoint3x4(cameraPos);
        }

        private void SetFrustumRays(Material material)
        {
            Matrix4x4 frustumRays;
            Vector3 cameraPosLocal;
            GetFrustumRays(out frustumRays, out cameraPosLocal);
            material.SetVector("_CameraPosLocal", cameraPosLocal);
            material.SetMatrix("_FrustumRays", frustumRays);
            material.SetFloat("_FrustumApex", GetFrustumApex());
        }

        private float GetDepthThresholdAdjusted()
        {
            return m_DepthThreshold / m_CurrentCamera.farClipPlane;
        }

        private bool CheckCamera()
        {
            if (m_Cameras == null)
                return false;

            foreach (Camera cam in m_Cameras)
                if (cam == m_CurrentCamera)
                    return true;

            return false;
        }

        public void UpdateCameraDepthMode()
        {
            if (m_Cameras == null)
                return;

            foreach (Camera cam in m_Cameras)
                if (cam)
                    cam.depthTextureMode |= DepthTextureMode.Depth;
        }
    }
}
