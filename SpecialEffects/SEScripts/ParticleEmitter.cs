using System.Xml.Serialization;
using Modding.Modules;
using Modding.Serialization;

namespace SpecialEffectsMod
{
    // The <ParticleEmitter> element inside <Modules> in ParticleEmitter.xml.
    // Unlike the other three modules this one carries a setting: the list of
    // particle textures the block's Texture menu offers, named in the block XML
    // rather than in code so the set can be changed without a rebuild.
    [XmlRoot("ParticleEmitter")]
    public class ParticleEmitter : BlockModule
    {
        [XmlArray("Particles")]
        [XmlArrayItem("Texture", typeof(ResourceReference))]
        [RequireToValidate]
        [CanBeEmpty]
        public object[] ParticleTextures;
    }
}
