using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpecialEffectsMod
{
    // The Spot Light level editor object: the light, the lens, and the controls
    // for both on the entity's SETTINGS tab.
    //
    // Mod.OnEntityPrefabCreation puts this on the entity prefab, and the editor
    // clones that prefab for every placed object. That is the only per-instance
    // hook a mod has for an entity: ModEntryPoint offers prefab callbacks and
    // nothing else, and stock entities add their controls from a GenericEntity.
    // Init override a mod cannot supply.
    public class SpotLightEntityBehaviour : MonoBehaviour
    {
        // Below this the lamp is too dark to be worth drawing a lens for.
        private const float LensVisibleIntensity = 0.02f;

        // The level variables this object answers to, set with the game's own
        // Modify Variable event -- which has an entity picker, so it reaches one
        // named lamp. Variables are floats throughout Besiege, hence three for a
        // colour and an index for each menu.
        //
        // Negative means "no opinion" and hands that setting back to the SETTINGS
        // tab. Nothing can delete a variable once set, so without the sentinel a
        // level could take a lamp over and never give it back.
        private const string VarBrightness = "brightness";
        private const string VarRed = "red";
        private const string VarGreen = "green";
        private const string VarBlue = "blue";
        private const string VarAngle = "angle";
        private const string VarRange = "range";
        private const string VarType = "type";
        private const string VarIllumination = "illumination";
        private const string VarLens = "lens";
        private const string VarHousing = "housing";

        private Light beam;
        private GameObject lens;
        private MeshRenderer lensRenderer;

        private MSlider brightness;
        private MColourSlider color;
        private MSlider coneAngle;
        private MSlider range;
        private MMenu lightTypes;
        private MMenu illumination;
        private MToggle showLens;
        private MToggle showHousing;

        private readonly IDictionary<string, LightType> lightTypeDict =
            new Dictionary<string, LightType>();
        private readonly IDictionary<string, LightRenderMode> illuminationDict =
            new Dictionary<string, LightRenderMode>();

        private GenericEntity holder;
        private bool built;

        // Awake, not Start: LevelXMLLoader instantiates the entity and then calls
        // LoadEntityData in the same breath, while Start does not run until the
        // end of the frame. Controls added that late are not there for the saved
        // values to land in, which is what reset every setting on level start.
        private void Awake()
        {
            beam = SpotLightEntity.Beam(gameObject);
            lens = SpotLightEntity.Lens(gameObject);
            lensRenderer = Attach.Component<MeshRenderer>(lens);
            AddControls();
        }

        // Again in case LevelEntity had not wired up its GenericEntity yet.
        private void Start()
        {
            AddControls();
        }

        // LevelEntity.EntityBehaviour is a GenericEntity, which is a
        // SaveableDataHolder like a block is -- so the SETTINGS tab takes the same
        // sliders and colour sliders the blocks use.
        private void AddControls()
        {
            if (built) return;
            LevelEntity entity = GetComponent<LevelEntity>();
            if (entity == null || entity.EntityBehaviour == null) return;
            holder = entity.EntityBehaviour;
            built = true;

            lightTypeDict.Add("Spot", LightType.Spot);
            lightTypeDict.Add("Directional", LightType.Directional);
            lightTypeDict.Add("Point", LightType.Point);
            lightTypes = holder.AddMenu("EntityLightTypeKey", 0, lightTypeDict.Keys.ToList(), true);

            illuminationDict.Add("Pixel", LightRenderMode.ForcePixel);
            illuminationDict.Add("Vertex", LightRenderMode.ForceVertex);
            illuminationDict.Add("Auto", LightRenderMode.Auto);
            illumination = holder.AddMenu("EntityIlluminationKey", 0, illuminationDict.Keys.ToList(), true);

            brightness = holder.AddSlider("Brightness", "EntityBrightnessKey", 4f, 0f, 10f);
            color = holder.AddColourSlider("Color", "EntityColorKey", Color.white, false);
            coneAngle = holder.AddSlider("Angle", "EntityConeAngleKey", 65f, 0f, 180f);
            range = holder.AddSlider("Range", "EntityRangeKey", 30f, 0f, 1000f);
            showLens = holder.AddToggle("Lens", "EntityLensKey", true);
            showHousing = holder.AddToggle("Housing", "EntityHousingKey", true);
        }

        // Aimed and applied every frame: the housing mesh arrives asynchronously,
        // the object can be turned in the editor, and a variable can change at any
        // time. That also makes change subscriptions unnecessary.
        private void Update()
        {
            SpotLightEntity.AimAtBarrel(gameObject);
            Apply();
        }

        // The SETTINGS tab sets everything; a variable overrides whichever of them
        // it names, until it is set negative again.
        private void Apply()
        {
            if (beam == null || !built) return;

            float intensity = brightness.Value;
            float angle = coneAngle.Value;
            float reach = range.Value;
            Variable(VarBrightness, ref intensity);
            Variable(VarAngle, ref angle);
            Variable(VarRange, ref reach);
            Color tint = Tint();

            beam.type = lightTypeDict[Choice(lightTypes, VarType)];
            beam.renderMode = illuminationDict[Choice(illumination, VarIllumination)];
            beam.intensity = intensity;
            beam.color = tint;
            beam.spotAngle = angle;
            beam.range = reach;

            SpotLightEntity.TintLens(lensRenderer, tint, intensity);
            lens.SetActive(Switch(showLens, VarLens)
                && intensity >= LensVisibleIntensity
                && tint != Color.black);
            ShowHousing(Switch(showHousing, VarHousing));
        }

        private bool Variable(string key, ref float value)
        {
            float set;
            if (holder == null || !holder.GetVariableValue(key, out set) || set < 0f) return false;
            value = set;
            return true;
        }

        // A menu's selection, or the variable's index into the same list.
        private string Choice(MMenu menu, string key)
        {
            float index = -1f;
            if (!Variable(key, ref index)) return menu.Selection;
            return menu.Items[Mathf.Clamp(Mathf.RoundToInt(index), 0, menu.Items.Count - 1)];
        }

        private bool Switch(MToggle toggle, string key)
        {
            float on = -1f;
            return Variable(key, ref on) ? on > 0f : toggle.IsActive;
        }

        // Each channel overrides on its own, so a level can change just the red.
        // Written 0-1, or 0-255 if any of the three is above 1 -- both readings
        // are in use, and a value over 1 cannot be meant as 0-1.
        private Color Tint()
        {
            Color slider = color.Value;
            float r = slider.r, g = slider.g, b = slider.b;
            bool any = Variable(VarRed, ref r);
            any |= Variable(VarGreen, ref g);
            any |= Variable(VarBlue, ref b);
            if (!any) return slider;

            if (r > 1f || g > 1f || b > 1f)
            {
                r /= 255f;
                g /= 255f;
                b /= 255f;
            }
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), slider.a);
        }

        // The housing only: the lens is switched separately, and a Light is not a
        // Renderer so it is not caught by this.
        private void ShowHousing(bool show)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject.name != SpotLightEntity.LensName) renderer.enabled = show;
        }
    }
}
