using Modding;
using Modding.Modules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpecialEffectsMod
{
    // Mod entry point: registers the four block modules, builds the Spot Light
    // entity's prefab, and adds the two console commands for the level's lighting.
    public class Mod : ModEntryPoint
    {
        // The <ID> in SpotLightEntity.xml.
        private const int SpotLightEntityId = 1;

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

            ModConsole.RegisterCommand("Night", NightModeHandler,
                "Night + true (Night) or false (Day)");
            ModConsole.RegisterCommand("Custom", CustomHandler,
                "Custom + Various ");
        }

        // The editor clones this prefab for every placed object, so the light, the
        // lens and the behaviour that drives them are built once here and every
        // entity gets its own. The behaviour is also the only per-instance hook a
        // mod has for an entity -- see SpotLightEntityBehaviour.
        public override void OnEntityPrefabCreation(int entityId, GameObject prefab)
        {
            if (entityId != SpotLightEntityId) return;
            SpotLightEntity.Beam(prefab);
            SpotLightEntity.Lens(prefab);
            Attach.Component<SpotLightEntityBehaviour>(prefab);
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
