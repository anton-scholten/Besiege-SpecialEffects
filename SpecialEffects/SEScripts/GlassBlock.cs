using System.Xml.Serialization;
using Modding.Modules;

namespace SpecialEffectsMod
{
    // The <GlassBlock> element inside <Modules> in GlassBlock.xml. It carries no
    // settings of its own; the block's controls all live in the mapper.
    [XmlRoot("GlassBlock")]
    public class GlassBlock : BlockModule
    {
    }
}
