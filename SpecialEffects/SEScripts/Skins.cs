namespace SpecialEffectsMod
{
    // Takes the skin picker out of a block's mapper. Each of this mod's blocks is
    // its own mesh and its own texture with nothing to swap to, so the row is only
    // ever an empty choice.
    public static class Skins
    {
        // The key the game itself gives this control. Kept the same so a machine
        // saved before this change still finds its stored value.
        private const string SkinKey = "_CurrentSkin";

        // The obvious lever -- BlockPrefab.SkinCanBeChanged -- is the wrong one.
        // BlockPrefab.SetIcons also reads it, and only calls SetPrefabIcons() when
        // it is true, so turning it off at prefab creation leaves every one of the
        // mod's blocks showing a placeholder in the block menu.
        //
        // So the control is hidden rather than prevented. BlockMapper.RefreshLists
        // builds the block's MVisual and hands it to a GenericController, and that
        // controller skips any MapperType whose DisplayInMapper is false, exactly
        // as it does for the pages of controls the Spot Light hides.
        //
        // It has to exist before the mapper is first opened, or the game builds it
        // there and shows it once. Building it here is the same call RefreshLists
        // makes; from then on RefreshLists takes its reuse path, which refreshes
        // the items and the label but leaves DisplayInMapper alone.
        public static void Hide(BlockBehaviour block)
        {
            if (block == null) return;

            if (block.Visual == null)
            {
                BlockVisualController visuals = block.VisualController;
                if (visuals == null || visuals.Options == null || visuals.Options.Count == 0) return;
                block.Visual = new MVisual(visuals, visuals.Options.IndexOf(visuals.selectedSkin),
                    visuals.Options, SkinKey, null);
            }

            block.Visual.DisplayInMapper = false;
        }
    }
}
