using System.Xml.Serialization;
using Modding.Modules;

namespace SpecialEffectsMod
{
    // The <SpotLight> element inside <Modules> in SpotLight.xml. It carries no
    // settings of its own; the block's controls all live in the mapper.
    [XmlRoot("SpotLight")]
    public class SpotLight : BlockModule
    {
    }
}
