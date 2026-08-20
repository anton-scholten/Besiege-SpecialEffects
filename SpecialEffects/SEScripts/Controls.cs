using Modding;

namespace SpecialEffectsMod
{
    // The mapper shows one page of a block's settings at a time by hiding the rest,
    // which is a lot of DisplayInMapper assignments.
    public static class Controls
    {
        public static void Show(bool state, params MapperType[] controls)
        {
            foreach (MapperType control in controls) control.DisplayInMapper = state;
        }
    }
}
