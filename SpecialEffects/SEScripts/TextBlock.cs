using System.Xml.Serialization;
using Modding.Modules;

namespace SpecialEffectsMod
{
    // The <TextBlock> element inside <Modules> in TextBlock.xml. It carries no
    // settings of its own; the block's controls all live in the mapper.
    [XmlRoot("TextBlock")]
    public class TextBlock : BlockModule
    {
    }
}
