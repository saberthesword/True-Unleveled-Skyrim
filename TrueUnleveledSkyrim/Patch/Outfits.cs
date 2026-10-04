using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

using TrueUnleveledSkyrim.Config;

namespace TrueUnleveledSkyrim.Patch
{
    class OutfitsPatcher
    {
        // Creates a weak or strong variant of an outfit, replacing leveled list entries with the respective generated variants of the list.
        // Nothing is created (and no FormKey is consumed) when the outfit has no leveled list with a generated variant.
        private static Outfit? CreateVariant(IOutfitGetter source, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ILinkCache linkCache, string postfix)
        {
            if (source.Items is null || source.EditorID is null)
                return null;

            List<(int Index, LeveledItem Replacement)>? replacements = null;
            for (int i = 0; i < source.Items.Count; ++i)
            {
                ILeveledItemGetter? resolvedItem = source.Items[i].TryResolve<ILeveledItemGetter>(linkCache);
                if (resolvedItem is null)
                    continue;

                if (!GeneratedRecords.TryGetLeveledItem(resolvedItem.EditorID, postfix, out var newItem))
                    continue;

                replacements ??= new();
                replacements.Add((i, newItem));
            }

            if (replacements is null)
                return null;

            Outfit variant = new(state.PatchMod);
            variant.DeepCopyIn(source);
            variant.EditorID = source.EditorID + postfix;
            foreach (var (index, replacement) in replacements)
                variant.Items![index] = replacement.ToLink();

            return variant;
        }

        // Main function to unlevel outfits.
        public static void PatchOutfits(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            uint processedRecords = 0;
            uint generatedRecords = 0;
            foreach (IOutfitGetter outfitGetter in state.LoadOrder.PriorityOrder.Outfit().WinningOverrides())
            {
                try
                {
                    ++processedRecords;
                    if (processedRecords % 100 == 0)
                        Console.WriteLine("Processed " + processedRecords + " outfits.");

                    foreach (string postfix in new[] { TUSConstants.WeakPostfix, TUSConstants.StrongPostfix })
                    {
                        Outfit? variant = CreateVariant(outfitGetter, state, Patcher.LinkCache, postfix);
                        if (variant is null)
                            continue;

                        state.PatchMod.Outfits.Set(variant);
                        GeneratedRecords.RegisterOutfit(variant);
                        ++generatedRecords;
                    }
                }
                catch (Exception ex)
                {
                    throw RecordException.Enrich(ex, outfitGetter);
                }
            }

            Console.WriteLine("Processed " + processedRecords + " outfits in total, generated " + generatedRecords + " weak/strong variants.\n");
        }
    }
}
