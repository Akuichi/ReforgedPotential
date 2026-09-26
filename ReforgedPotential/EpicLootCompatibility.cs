using BepInEx.Bootstrap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EpicLootAPI;
using Logger = Jotunn.Logger;
namespace ReforgedPotential.Compatibility
{
    public static class EpicLootCompatibility
    {
        private const string EpicLootGuid = "randyknapp.mods.epicloot";
        private static bool _isEpicLootLoaded;
        public static void Init()
        {
            if (Chainloader.PluginInfos.TryGetValue(EpicLootGuid, out var value) && !((Object)(object)((value != null) ? value.Instance : null) == null))
            {
                if (!EpicLoot.IsLoaded())
                {
                    Logger.LogWarning("[ReforgedPotential] EpicLoot detected but not loaded. Disabling compatibility.");
                }
                else
                {
                    _isEpicLootLoaded = true;
                    Logger.LogInfo("[ReforgedPotential] EpicLoot detected. Enabling compatibility.");
                }
            }
        }

        internal static bool TryCopyMagicItem(ItemDrop.ItemData sourceItem, ItemDrop.ItemData craftedItem)
        {
            if (!_isEpicLootLoaded)
            {
                if (ReforgedPotential.EnableDebugLogging.Value) Logger.LogInfo("EpicLoot is not installed, skipping..");
                return false;
            }
            if (sourceItem == null || craftedItem == null)
            {
                if (ReforgedPotential.EnableDebugLogging.Value) Logger.LogInfo("Source or crafted item is null, skipping..");
                return false;
            }

            if (sourceItem.IsMagicItem())
            {
                var magicItem = sourceItem.GetMagicItem();
                if (craftedItem.ApplyMagicItem(magicItem))
                {
                    if (ReforgedPotential.EnableDebugLogging.Value) Logger.LogInfo("[ReforgedPotential] Successfully copied EpicLoot magic item data.");
                    return true;
                }
            }

            if (ReforgedPotential.EnableDebugLogging.Value) Logger.LogInfo("[ReforgedPotential] No EpicLoot magic item data found to copy.");

            return false;
        }
    }
}