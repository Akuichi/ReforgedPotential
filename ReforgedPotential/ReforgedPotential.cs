using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using UnityEngine.Windows;

namespace ReforgedPotential
{
    [BepInPlugin(modGUID, modName, modVersion)]
    [BepInProcess("valheim.exe")]
    public class ReforgedPotential : BaseUnityPlugin
    {
        private const string modGUID = "akuichi.ReforgedPotential";
        private const string modName = "Reforged Potential";
        private const string modVersion = "1.0.1";

        private readonly Harmony harmony = new Harmony(modGUID);


        internal static ConfigEntry<string> Recipe_Upgrader0Armor;
        internal static ConfigEntry<string> Recipe_Upgrader0Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader1Armor;
        internal static ConfigEntry<string> Recipe_Upgrader1Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader2Armor;
        internal static ConfigEntry<string> Recipe_Upgrader2Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader3Armor;
        internal static ConfigEntry<string> Recipe_Upgrader3Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader4Armor;
        internal static ConfigEntry<string> Recipe_Upgrader4Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader5Armor;
        internal static ConfigEntry<string> Recipe_Upgrader5Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader6Armor;
        internal static ConfigEntry<string> Recipe_Upgrader6Weapon;
        internal static ConfigEntry<string> Recipe_Upgrader7Armor;
        internal static ConfigEntry<string> Recipe_Upgrader7Weapon;

        internal static ConfigEntry<string> Station_Global;

        internal static ConfigEntry<bool> UpgradeCantFail;
        internal static ConfigEntry<int> CostStart;
        internal static ConfigEntry<int> CostIncreaseInterval;
        internal static ConfigEntry<int> CostIncreasePerInterval;
        internal static ConfigEntry<int> CostScalingLevelStart;


        internal static ConfigEntry<float> UpgradeBaseDuration;
        internal static ConfigEntry<float> UpgradeDurationIncreasePerLevel;

        private void Awake()
        {
            InitConfig();
            harmony.PatchAll();
        }
        [HarmonyPatch(typeof(InventoryGui), "SetupCrafting")]
        private class InventoryGuiCraftSpeedPatches
        {
            [HarmonyPrefix]
            static void SetCraftSpeed(ref float ___m_upgraderDuration, ref float ___m_upgraderDurationPerLevel)
            {
                ___m_upgraderDuration = UpgradeBaseDuration.Value;
                ___m_upgraderDurationPerLevel = UpgradeDurationIncreasePerLevel.Value;
            }
        }



        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private class UpgradePatch
        {
            private static bool Prefix(object __instance, object player, out object __state)
            {
                if (!UpgradeCantFail.Value) { __state = null; return true; } // not enabled; skip
                __state = null;
                try
                {
                    if (__instance == null || player == null) return true;

                    // get current crafting station from player
                    MethodInfo getStation = player.GetType().GetMethod("GetCurrentCraftingStation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var station = getStation?.Invoke(player, null);

                    // compute the same 'flag' used in DoCrafting: station != null && station.m_upgrader
                    bool flag = false;
                    if (station != null)
                    {
                        var upgraderField = station.GetType().GetField("m_upgrader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (upgraderField != null && upgraderField.FieldType == typeof(bool))
                        {
                            flag = (bool)upgraderField.GetValue(station);
                        }
                    }

                    if (!flag) return true; // not an upgrader call; nothing to do

                    // get m_craftRecipe from the InventoryGui instance
                    var igType = __instance.GetType();
                    var recipeField = igType.GetField("m_craftRecipe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var recipeObj = recipeField?.GetValue(__instance);
                    if (recipeObj == null) return true;

                    // get resources array from the recipe
                    var resourcesField = recipeObj.GetType().GetField("m_resources", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var resources = resourcesField?.GetValue(recipeObj) as Array;
                    if (resources == null || resources.Length == 0) return true;

                    var modified = new List<KeyValuePair<object, float>>();

                    // find the upgrader requirement and modify its shared.m_upgradeChance
                    foreach (var req in resources)
                    {
                        if (req == null) continue;
                        var reqType = req.GetType();

                        var upgraderResField = reqType.GetField("m_upgraderResource", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (upgraderResField == null) continue;

                        bool isUpgraderResource = false;
                        try { isUpgraderResource = (bool)upgraderResField.GetValue(req); } catch { isUpgraderResource = false; }
                        if (!isUpgraderResource) continue;

                        // req.m_resItem.m_itemData.m_shared
                        var resItemObj = reqType.GetField("m_resItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(req);
                        if (resItemObj == null) continue;
                        var itemDataObj = resItemObj.GetType().GetField("m_itemData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(resItemObj);
                        if (itemDataObj == null) continue;
                        var sharedObj = itemDataObj.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemDataObj);
                        if (sharedObj == null) continue;

                        var sharedType = sharedObj.GetType();
                        var upgradeChanceField = sharedType.GetField("m_upgradeChance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (upgradeChanceField == null) continue;

                        try
                        {
                            float original = Convert.ToSingle(upgradeChanceField.GetValue(sharedObj));
                            modified.Add(new KeyValuePair<object, float>(sharedObj, original));
                            // set to 100%
                            upgradeChanceField.SetValue(sharedObj, 1f);
                        }
                        catch { /* ignore individual failures and continue */ }
                    }

                    if (modified.Count > 0)
                    {
                        __state = modified; // pass to Postfix for restore
                    }
                }
                catch (Exception)
                {
                    // swallow or log as desired
                }

                // continue to original DoCrafting
                return true;
            }
        }

        [HarmonyPatch(typeof(Piece.Requirement), "GetAmount")]
        private class Requirement_GetAmount_Patch
        {
            private const string LogPrefix = "[ReforgedOfPotential] GetAmount: ";
            static void Postfix(Piece.Requirement __instance, int qualityLevel, ref int __result)
            {
                if (__instance == null) return;

                if (!__instance.m_upgraderResource) return;
                int level = qualityLevel - 1;
                int costStartLevel = Math.Max(1, CostScalingLevelStart.Value);
                int cost = CostStart.Value;

                if (CostIncreaseInterval.Value > 0 && level >= costStartLevel)
                {
                    cost += ((level - costStartLevel)
                        / CostIncreaseInterval.Value + 1)
                        * CostIncreasePerInterval.Value;
                }

                __result = cost;
            }
        }

        private void InitConfig()
        {
            UpgradeCantFail = Config.Bind("Upgrade Settings", "UpgradeCantFail", true,
                "If true, upgrades cannot fail.");

            CostStart = Config.Bind("Upgrade Settings", "CostStart", 1, "Base idol cost for upgrades. (Game Default is 1)");
            CostIncreasePerInterval = Config.Bind("Upgrade Settings", "CostIncreasePerInterval", 1,
                "Additional ingredient cost each time the cost scaling interval is reached starting at CostScalingLevelStart. (Starting at level X, the upgrade cost increases by this amount every Y levels. For example, with an increase of 1 every 2 levels starting at level 6: levels 1–5 cost 1, levels 6–7 cost 2, levels 8–9 cost 3, and so on.)");
            CostIncreaseInterval = Config.Bind("Upgrade Settings", "CostIncreaseInterval", 2,
                "Number of levels between each cost increase. Set to 0 to disable cost scaling.");
            CostScalingLevelStart = Config.Bind("Upgrade Settings", "CostScalingLevelStart", 6,
                "Level at which cost scaling starts. Anything below 1 is set to 1.");

            UpgradeBaseDuration = Config.Bind("Upgrade Settings", "UpgradeBaseDuration", 2f,
                "Base crafting duration for upgrading. (Game Default is 8)");
            UpgradeDurationIncreasePerLevel = Config.Bind("Upgrade Settings", "UpgradeDurationIncreasePerLevel", 1f,
                "Additional crafting duration per item level. (Game Default is 1)");

            Station_Global = Config.Bind("Crafting Station", "GlobalStation", "$piece_artisanstation",
                "Global crafting station for all configurable upgrader recipes. Use station m_name (e.g. '$piece_workbench') or prefab name. Empty = craftable by hand.");

            Recipe_Upgrader0Armor = Config.Bind("Recipes", "Wooden Protection Idol",
                "FineWood:20,Tin:10,GreydwarfEye:10",
                "Ingredients for Wooden Protection Idol: comma-separated entries 'PrefabName:Amount'.");
            Recipe_Upgrader0Weapon = Config.Bind("Recipes", "Wooden Battle Idol",
                "FineWood:20,Tin:10,GreydwarfEye:10",
                "Ingredients for Wooden Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader1Armor = Config.Bind("Recipes", "Bronze Protection Idol",
                "Bronze:10,SurtlingCore:1",
                "Ingredients for Bronze Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader1Weapon = Config.Bind("Recipes", "Bronze Battle Idol",
                "Bronze:10,SurtlingCore:1",
                "Ingredients for Bronze Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader2Armor = Config.Bind("Recipes", "Iron Protection Idol",
                "Iron:10,ElderBark:5",
                "Ingredients for Iron Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader2Weapon = Config.Bind("Recipes", "Iron Battle Idol",
                "Iron:10,ElderBark:5",
                "Ingredients for Iron Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader3Armor = Config.Bind("Recipes", "Silver Protection Idol",
                "Silver:10,FreezeGland:5,Obsidian:5",
                "Ingredients for Silver Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader3Weapon = Config.Bind("Recipes", "Silver Battle Idol",
                "Silver:10,FreezeGland:5,Obsidian:5",
                "Ingredients for Silver Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader4Armor = Config.Bind("Recipes", "Black Metal Protection Idol",
                "BlackMetal:10,Needle:2,Tar:5",
                "Ingredients for Black Metal Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader4Weapon = Config.Bind("Recipes", "Black Metal Battle Idol",
                "BlackMetal:10,Needle:2,Tar:5",
                "Ingredients for Black Metal Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader5Armor = Config.Bind("Recipes", "Black Marble Protection Idol",
                "BlackMarble:20,BugMeat:10,Carapace:10",
                "Ingredients for Black Marble Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader5Weapon = Config.Bind("Recipes", "Black Marble Battle Idol",
                "BlackMarble:20,BugMeat:10,Carapace:10",
                "Ingredients for Black Marble Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader6Armor = Config.Bind("Recipes", "Flametal Protection Idol",
                "FlametalNew:10,CharredBone:15",
                "Ingredients for Flametal Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader6Weapon = Config.Bind("Recipes", "Flametal Battle Idol",
                "FlametalNew:10,CharredBone:15",
                "Ingredients for Flametal Battle Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader7Armor = Config.Bind("Recipes", "Bloodgold Protection Idol",
                "Gold:10,Coins:40",
                "Ingredients for Bloodgold Protection Idol: comma-separated 'PrefabName:Amount'.");

            Recipe_Upgrader7Weapon = Config.Bind("Recipes", "Bloodgold Battle Idol",
                "Gold:10,Coins:40",
                "Ingredients for Bloodgold Battle Idol: comma-separated 'PrefabName:Amount'.");

        }
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private class ObjectDB_AddConfigRecipesMulti
        {
            private const string LogPrefix = "[ReforgedOfPotential] ConfigRecipes: ";

            static void Postfix(ObjectDB __instance)
            {
                try
                {
                    if (__instance == null)
                    {
                        Debug.LogWarning($"{LogPrefix}ObjectDB instance is null.");
                        return;
                    }

                    // Map target prefab name -> config entry (ingredients)
                    var targets = new[]
                    {
                        new { Name = "Upgrader0Armor", RecipeCfg = Recipe_Upgrader0Armor },
                        new { Name = "Upgrader0Weapon", RecipeCfg = Recipe_Upgrader0Weapon },
                        new { Name = "Upgrader1Armor", RecipeCfg = Recipe_Upgrader1Armor },
                        new { Name = "Upgrader1Weapon", RecipeCfg = Recipe_Upgrader1Weapon },
                        new { Name = "Upgrader2Armor", RecipeCfg = Recipe_Upgrader2Armor },
                        new { Name = "Upgrader2Weapon", RecipeCfg = Recipe_Upgrader2Weapon },
                        new { Name = "Upgrader3Armor", RecipeCfg = Recipe_Upgrader3Armor },
                        new { Name = "Upgrader3Weapon", RecipeCfg = Recipe_Upgrader3Weapon },
                        new { Name = "Upgrader4Armor", RecipeCfg = Recipe_Upgrader4Armor },
                        new { Name = "Upgrader4Weapon", RecipeCfg = Recipe_Upgrader4Weapon },
                        new { Name = "Upgrader5Armor", RecipeCfg = Recipe_Upgrader5Armor },
                        new { Name = "Upgrader5Weapon", RecipeCfg = Recipe_Upgrader5Weapon },
                        new { Name = "Upgrader6Armor", RecipeCfg = Recipe_Upgrader6Armor },
                        new { Name = "Upgrader6Weapon", RecipeCfg = Recipe_Upgrader6Weapon },
                        new { Name = "Upgrader7Armor", RecipeCfg = Recipe_Upgrader7Armor },
                        new { Name = "Upgrader7Weapon", RecipeCfg = Recipe_Upgrader7Weapon }
                    };
                    // Resolve global station once
                    CraftingStation globalStation = ResolveCraftingStation(__instance, Station_Global?.Value?.Trim());
                    if (globalStation != null)
                    {
                        Debug.Log($"{LogPrefix}Resolved global crafting station: '{globalStation.m_name ?? globalStation.gameObject.name}'");
                    }
                    else if (!string.IsNullOrEmpty(Station_Global?.Value))
                    {
                        Debug.LogWarning($"{LogPrefix}Global station '{Station_Global.Value}' was not found; recipes will be craftable by hand.");
                    }

                    int added = 0;
                    foreach (var t in targets)
                    {
                        string prefabName = t.Name;
                        var cfg = t.RecipeCfg;
                        if (cfg == null)
                        {
                            Debug.LogWarning($"{LogPrefix}No config for {prefabName}; skipping.");
                            continue;
                        }

                        string cfgValue = cfg.Value?.Trim();
                        if (string.IsNullOrEmpty(cfgValue))
                        {
                            Debug.Log($"{LogPrefix}Empty ingredient list for {prefabName}; skipping.");
                            continue;
                        }

                        // get target prefab by name via ObjectDB helper
                        GameObject targetGO = __instance.GetItemPrefab(prefabName);
                        if (targetGO == null)
                        {
                            Debug.LogWarning($"{LogPrefix}Target prefab '{prefabName}' not found in ObjectDB; skipping.");
                            continue;
                        }

                        ItemDrop targetItem = targetGO.GetComponent<ItemDrop>();
                        if (targetItem == null)
                        {
                            Debug.LogWarning($"{LogPrefix}Target prefab '{prefabName}' has no ItemDrop; skipping.");
                            continue;
                        }

                        // skip if recipe already exists
                        bool exists = __instance.m_recipes.Exists(r => r != null && r.m_item != null &&
                                                                     (r.m_item == targetItem || string.Equals(r.m_item.name, prefabName, StringComparison.Ordinal)));
                        if (exists)
                        {
                            Debug.Log($"{LogPrefix}Recipe for '{prefabName}' already exists; skipping.");
                            continue;
                        }

                        // parse config into requirements
                        var requirements = ParseRequirements(cfgValue, __instance).ToArray();
                        if (requirements == null || requirements.Length == 0)
                        {
                            Debug.LogWarning($"{LogPrefix}No valid ingredients parsed for '{prefabName}' from '{cfgValue}'; skipping.");
                            continue;
                        }

                        // create recipe using ScriptableObject pattern (matching other mods)
                        Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
                        recipe.name = prefabName + "_ConfigRecipe";
                        recipe.m_amount = 1;
                        recipe.m_item = targetItem;
                        recipe.m_enabled = true;
                        recipe.m_craftingStation = globalStation; // apply the single global station to all
                        recipe.m_minStationLevel = 0;
                        recipe.m_repairStation = null;
                        recipe.m_resources = requirements;

                        __instance.m_recipes.Add(recipe);
                        added++;
                        Debug.Log($"{LogPrefix}Added recipe for '{prefabName}' requiring {string.Join(", ", requirements.Select(req => $"{GetReqName(req)} x{req.m_amount}"))}.");
                    }

                    if (added > 0)
                    {
                        Debug.Log($"{LogPrefix}Finished adding {added} recipe(s). Total recipes now: {__instance.m_recipes.Count}");
                        // try to refresh crafting UI if open
                        try
                        {
                            var invGuiType = typeof(InventoryGui);
                            var instanceProp = invGuiType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
                            var invGui = instanceProp?.GetValue(null);
                            if (invGui != null)
                            {
                                var updateMethod = invGuiType.GetMethod("UpdateCraftingPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                updateMethod?.Invoke(invGui, null);
                                Debug.Log($"{LogPrefix}Requested InventoryGui.UpdateCraftingPanel()");
                            }
                        }
                        catch { /* non-critical */ }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LogPrefix}Exception: {ex}");
                }
            }

            // parse "PrefabA:10,PrefabB:2" --> List<Piece.Requirement>
            private static List<Piece.Requirement> ParseRequirements(string cfgValue, ObjectDB odb)
            {
                var list = new List<Piece.Requirement>();
                if (string.IsNullOrEmpty(cfgValue)) return list;

                // split by comma, each token "Name:Amount"
                var tokens = cfgValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in tokens)
                {
                    var token = raw.Trim();
                    if (string.IsNullOrEmpty(token)) continue;
                    var parts = token.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                    {
                        Debug.LogWarning($"[ReforgedOfPotential] ConfigRecipes: Invalid ingredient token '{token}'. Use 'PrefabName:Amount'.");
                        continue;
                    }

                    string name = parts[0].Trim();
                    if (!int.TryParse(parts[1].Trim(), out int amount) || amount <= 0)
                    {
                        Debug.LogWarning($"[ReforgedOfPotential] ConfigRecipes: Invalid amount in token '{token}'. Must be positive integer.");
                        continue;
                    }

                    // find prefab (prefer ObjectDB lookup)
                    GameObject prefab = odb.GetItemPrefab(name) ?? GetPrefabFromZNet(name);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[ReforgedOfPotential] ConfigRecipes: Ingredient prefab '{name}' not found in ObjectDB or ZNetScene.");
                        continue;
                    }

                    // attempt to use ItemDrop on requirement (most mods expect ItemDrop)
                    ItemDrop drop = prefab.GetComponent<ItemDrop>();
                    if (drop == null)
                    {
                        Debug.LogWarning($"[ReforgedOfPotential] ConfigRecipes: Ingredient prefab '{name}' missing ItemDrop component; skipping.");
                        continue;
                    }

                    var req = new Piece.Requirement
                    {
                        m_amount = amount,
                        m_resItem = drop,
                        m_amountPerLevel = 0,
                        m_upgraderResource = false,
                        m_recover = false
                    };
                    list.Add(req);
                }

                return list;
            }

            private static GameObject GetPrefabFromZNet(string name)
            {
                try
                {
                    var znetType = typeof(ZNetScene);
                    var instanceProp = znetType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
                    var znet = instanceProp?.GetValue(null);
                    var prefabsField = znetType.GetField("m_prefabs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var prefabs = prefabsField?.GetValue(znet) as List<GameObject>;
                    if (prefabs != null)
                    {
                        return prefabs.Find(g => string.Equals(g?.name, name, StringComparison.Ordinal));
                    }
                }
                catch { }
                return null;
            }

            // Resolve station by searching recipes' m_craftingStation name or ItemDB prefabs with CraftingStation component.
            private static CraftingStation ResolveCraftingStation(ObjectDB odb, string stationName)
            {
                if (string.IsNullOrEmpty(stationName)) return null;

                try
                {
                    // search existing recipes for a matching station
                    foreach (var rec in odb.m_recipes)
                    {
                        if (rec?.m_craftingStation == null) continue;
                        var cs = rec.m_craftingStation;
                        if (string.Equals(cs.m_name, stationName, StringComparison.Ordinal) ||
                            string.Equals(cs.gameObject.name, stationName, StringComparison.Ordinal))
                        {
                            return cs;
                        }
                    }

                    // search ObjectDB.m_items for a prefab with CraftingStation component
                    var itemsField = typeof(ObjectDB).GetField("m_items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var items = itemsField?.GetValue(odb) as List<GameObject>;
                    if (items != null)
                    {
                        foreach (var go in items)
                        {
                            if (go == null) continue;
                            var csComp = go.GetComponent<CraftingStation>();
                            if (csComp == null) continue;
                            if (string.Equals(csComp.m_name, stationName, StringComparison.Ordinal) ||
                                string.Equals(go.name, stationName, StringComparison.Ordinal))
                            {
                                return csComp;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ReforgedOfPotential] ConfigRecipes: ResolveCraftingStation exception: {ex}");
                }

                return null;
            }

            private static string GetReqName(Piece.Requirement req)
            {
                if (req == null) return "<null>";
                try
                {
                    if (req.m_resItem is ItemDrop id) return id.gameObject.name;
                }
                catch { }
                return "<unknown>";
            }
        }
    }
}