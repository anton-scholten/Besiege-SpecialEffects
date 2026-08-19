using System.Collections.Generic;
using Modding;
using Modding.Levels;
using Modding.Modules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpecialEffectsMod
{
    // Mod entry point: registers the four block modules, the spot light entity's
    // event, and the two console commands that drive the level's lighting.
    public class Mod : ModEntryPoint
    {
        // The <Event><ID>1</ID> in Mod.xml -- the spot light entity's only event.
        private const int SpotLightEventId = 1;

        // Brightness comes in as a percentage; Unity's intensity is roughly 0-8.
        private const float BrightnessToIntensity = 12.5f;

        private Light sourceLight;
        private GameObject lightPiece;

        private bool night;
        private Color dayAmbientColor;
        private float dayAmbientIntensity;
        private Color parsedColor;

        public override void OnLoad()
        {
            CustomModules.AddBlockModule<SpotLight, SpotLightBehaviour>("SpotLight", false);
            CustomModules.AddBlockModule<ParticleEmitter, ParticleEmitterBehaviour>("ParticleEmitter", false);
            CustomModules.AddBlockModule<GlassBlock, GlassBlockBehaviour>("GlassBlock", false);
            CustomModules.AddBlockModule<TextBlock, TextBlockBehaviour>("TextBlock", false);

            ModEvents.RegisterCallback(SpotLightEventId, EntitySpotLight);

            ModConsole.RegisterCommand("Night", NightModeHandler,
                "Night + true (Night) or false (Day)");
            ModConsole.RegisterCommand("Custom", CustomHandler,
                "Custom + Various ");
        }

        // Every spot light entity gets a child object carrying the actual Light.
        // The prefab only has to have one; the event below configures it.
        public override void OnEntityPrefabCreation(int entityId, GameObject prefab)
        {
            lightPiece = new GameObject();
            lightPiece.transform.name = "LightPiece";
            lightPiece.transform.parent = prefab.gameObject.transform;
            sourceLight = GetOrAddLight(lightPiece);
            sourceLight.color = Color.Lerp(Color.red, Color.blue, 0.5f);
        }

        // The spot light entity's event, fired from a level's logic chain: reads
        // the properties the level author set in Mod.xml and applies them.
        private void EntitySpotLight(LogicChain logic, IDictionary<string, EventProperty> properties)
        {
            GameObject entity = logic.Entity.GameObject;

            lightPiece = null;
            foreach (Transform child in entity.GetComponentsInChildren<Transform>())
            {
                if (child.name == "LightPiece")
                {
                    lightPiece = child.gameObject;
                    break;
                }
            }

            if (lightPiece == null)
            {
                lightPiece = new GameObject();
                lightPiece.transform.name = "LightPiece";
                lightPiece.transform.parent = entity.transform;
            }

            sourceLight = GetOrAddLight(lightPiece);
            lightPiece.transform.localPosition = new Vector3(2f, 5f, 0f);

            switch (((EventProperty.Choice)properties["LightTypeInput"]).CurrentIndex)
            {
                case 1: sourceLight.type = LightType.Directional; break;
                case 2: sourceLight.type = LightType.Point; break;
                default: sourceLight.type = LightType.Spot; break;
            }

            sourceLight.intensity =
                ((EventProperty.NumberInput)properties["BrightnessInput"]).Value / BrightnessToIntensity;

            ColorUtility.TryParseHtmlString(
                ((EventProperty.TextInput)properties["ColorInput"]).Text, out parsedColor);
            sourceLight.color = parsedColor;

            sourceLight.spotAngle = ((EventProperty.NumberInput)properties["AngleInput"]).Value;
            sourceLight.range = ((EventProperty.NumberInput)properties["RangeInput"]).Value;

            switch (((EventProperty.Choice)properties["IlluminationTypeInput"]).CurrentIndex)
            {
                case 1: sourceLight.renderMode = LightRenderMode.ForceVertex; break;
                case 2: sourceLight.renderMode = LightRenderMode.Auto; break;
                default: sourceLight.renderMode = LightRenderMode.ForcePixel; break;
            }
        }

        private static Light GetOrAddLight(GameObject go)
        {
            Light light = go.GetComponent<Light>();
            if (light == null) light = go.AddComponent<Light>();
            return light;
        }

        // "Night true" / "Night false". The daytime ambient settings are captured
        // the first time it is used, so switching back restores the level's own.
        private void NightModeHandler(string[] values)
        {
            if (!night)
            {
                dayAmbientColor = RenderSettings.ambientLight;
                dayAmbientIntensity = RenderSettings.ambientIntensity;
            }

            if (values[0] == "true")
            {
                night = true;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0.5f;
                ModConsole.Log("Set to Night");
            }
            else
            {
                night = false;
                RenderSettings.ambientLight = dayAmbientColor;
                RenderSettings.ambientIntensity = dayAmbientIntensity;
                ModConsole.Log("Set to Day");
            }
        }

        // "Custom <setting> <value>" -- a thin pass-through to RenderSettings, so
        // a level's fog, ambient light and flares can be changed from the console.
        private void CustomHandler(string[] values)
        {
            switch (values[0])
            {
                case "ambientLight":
                    RenderSettings.ambientLight = ParseColor(values[1]);
                    ModConsole.Log("Light Color Set. ");
                    break;
                case "ambientEquatorColor":
                    RenderSettings.ambientEquatorColor = ParseColor(values[1]);
                    ModConsole.Log("Equator Color Set. ");
                    break;
                case "ambientGroundColor":
                    RenderSettings.ambientGroundColor = ParseColor(values[1]);
                    ModConsole.Log("Ground Color Set. ");
                    break;
                case "ambientSkyColor":
                    RenderSettings.ambientSkyColor = ParseColor(values[1]);
                    ModConsole.Log("Sky Color Set. ");
                    break;
                case "fogColor":
                    RenderSettings.fogColor = ParseColor(values[1]);
                    ModConsole.Log("Fog Color Set. ");
                    break;
                case "ambientIntensity":
                    RenderSettings.ambientIntensity = float.Parse(values[1]);
                    ModConsole.Log("Light Intensity Set. ");
                    break;
                case "flareFadeSpeed":
                    RenderSettings.flareFadeSpeed = float.Parse(values[1]);
                    ModConsole.Log("Flare Fade Set. ");
                    break;
                case "flareStrength":
                    RenderSettings.flareStrength = float.Parse(values[1]);
                    ModConsole.Log("Flare Strength Set. ");
                    break;
                case "fogDensity":
                    RenderSettings.fogDensity = float.Parse(values[1]);
                    ModConsole.Log("Fog Density Set. ");
                    break;
                case "fogStartDistance":
                    RenderSettings.fogStartDistance = float.Parse(values[1]);
                    ModConsole.Log("Fog Start Distance Set. ");
                    break;
                case "fogEndDistance":
                    RenderSettings.fogEndDistance = float.Parse(values[1]);
                    ModConsole.Log("Fog End Distance Set. ");
                    break;
                case "haloStrength":
                    RenderSettings.haloStrength = float.Parse(values[1]);
                    ModConsole.Log("Halo Strength Set. ");
                    break;
                case "fog":
                    RenderSettings.fog = bool.Parse(values[1]);
                    ModConsole.Log("Fog Set. ");
                    break;
                case "ambientMode":
                    if (values[1] == "Skybox") RenderSettings.ambientMode = AmbientMode.Skybox;
                    else if (values[1] == "Trilight") RenderSettings.ambientMode = AmbientMode.Trilight;
                    else if (values[1] == "Flat") RenderSettings.ambientMode = AmbientMode.Flat;
                    ModConsole.Log("Ambient Mode Set. ");
                    break;
                case "up":
                    DynamicGI.UpdateEnvironment();
                    ModConsole.Log("Update Environment. ");
                    break;
            }
        }

        private Color ParseColor(string html)
        {
            ColorUtility.TryParseHtmlString(html, out parsedColor);
            return parsedColor;
        }
    }
}
