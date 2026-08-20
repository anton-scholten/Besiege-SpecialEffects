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

        // Not via BlockPrefab.SkinCanBeChanged: BlockPrefab.SetIcons reads that too
        // and skips SetPrefabIcons() when it is false, which leaves every block
        // showing a placeholder in the block menu.
        //
        // The control is hidden instead -- GenericController.CreateContainers skips
        // any MapperType with DisplayInMapper false. It has to exist before the
        // mapper first opens or the game builds it there and shows it once, so this
        // makes the same call RefreshLists would; RefreshLists then takes its reuse
        // path, which leaves DisplayInMapper alone.
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
