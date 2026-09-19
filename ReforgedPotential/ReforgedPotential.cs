using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Remoting.Messaging;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.Windows;
using static System.Net.Mime.MediaTypeNames;
using static Version;
using Logger = Jotunn.Logger;
namespace ReforgedPotential
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    public partial class ReforgedPotential : BaseUnityPlugin
    {
        public const string PluginGUID = "akuichi.ReforgedPotential";
        public const string PluginName = "Reforged Potential";
        public const string PluginVersion = "2.0.0";

        
        private readonly Harmony harmony = new Harmony(PluginGUID);

        const string Boss1Key = "GP_Eikthyr";
        const string Boss2Key = "GP_TheElder";
        const string Boss3Key = "GP_Bonemass";
        const string Boss4Key = "GP_Moder";
        const string Boss5Key = "GP_Yagluth";
        const string Boss6Key = "GP_Queen";
        const string Boss7Key = "GP_Fader";


        internal static ConfigEntry<bool> EnableServerSync;

        internal static ConfigEntry<bool> EnableBossProgression;

        internal static ConfigEntry<int> BaseUpgradeLimit;

        internal static ConfigEntry<int> Boss1MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss2MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss3MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss4MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss5MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss6MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss7MaxUpgradeLevel;
        internal static ConfigEntry<int> Boss8MaxUpgradeLevel;

        internal static Dictionary<int, int> bossUpgradeValues;

        internal static Dictionary<int, string> bossNames = new Dictionary<int, string>()
        {
            { -1, "No More Further Boss" },
            { 0, "No Boss Defeated" },
            { 1, "Eikthyr" },
            { 2, "Elder" },
            { 3, "Bonemass" },
            { 4, "Moder" },
            { 5, "Yagluth" },
            { 6, "Queen" },
            { 7, "Fader" },
            { 8, "Kall Fimbulbringer" }
        };

        internal static ConfigEntry<bool> EnableIdolProgression;

        internal static ConfigEntry<bool> EnableRecipes;

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

        internal static ConfigEntry<float> UpgradeChance;
        internal static ConfigEntry<float> BreakChance;
        internal static ConfigEntry<int> CostStart;
        internal static ConfigEntry<int> CostIncreaseInterval;
        internal static ConfigEntry<int> CostIncreasePerInterval;
        internal static ConfigEntry<int> CostScalingLevelStart;


        internal static ConfigEntry<float> UpgradeBaseDuration;
        internal static ConfigEntry<float> UpgradeDurationIncreasePerLevel;
        public static CustomRPC RPC_Reforged;
        public static ConfigEntry<bool> EnableGlobalUpgradeNotifications;
        public static ConfigEntry<string> SuccessMessage;
        public static ConfigEntry<string> FailedMessage;

        private void Awake()
        {
            RPC_Reforged = NetworkManager.Instance.AddRPC("RPC_Reforged", RPC_ReforgedServerReceive, RPC_ReforgedClientReceive);
            InitConfig();
            CreateConfigWatcher();
            harmony.PatchAll();
        }

        #region RPC Handlers
        private IEnumerator RPC_ReforgedServerReceive(long sender, ZPackage package)
        {
            Jotunn.Logger.LogMessage($"Received blob, processing");

            Jotunn.Logger.LogMessage($"Broadcasting to all clients");
            RPC_Reforged.SendPackage(ZNet.instance.m_peers, new ZPackage(package.GetArray()));
            string message = package.ReadString();
            if (message != "")
            { // Make sure it isn't empty
                Jotunn.Logger.LogDebug($"[SERVER] Adding message to chat: {message}");
                ChatHelpers.ResetChatHideTimer();
                Chat.instance.AddString("Forge of Potential", message, Talker.Type.Shout);
            }
            yield return null;
        }

        private IEnumerator RPC_ReforgedClientReceive(long sender, ZPackage package)
        {
            Jotunn.Logger.LogMessage($"Received blob, processing");
            string message = package.ReadString();
            if (message != "")
            { // Make sure it isn't empty
                ChatHelpers.ResetChatHideTimer();
                Chat.instance.AddString("Forge of Potential", message, Talker.Type.Shout);
            }
            yield return null;
        }

        public static void BroadcastUpgradeResult(string playerName, string itemName, int level, bool success)
        {
            if (!EnableGlobalUpgradeNotifications.Value) return;

            string template = success
                ? SuccessMessage.Value
                : FailedMessage.Value;

            string message = template
                .Replace("{PlayerName}", playerName)
                .Replace("{ItemName}", itemName)
                .Replace("{Level}", level.ToString());
            ZPackage package = new ZPackage();
            package.Write(message);
            Jotunn.Logger.LogDebug($"Broadcasting upgrade result: {message}");
            RPC_Reforged.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
        }

        // Reflection helper to set Chat.instance.m_hideTimer = 0
        public static class ChatHelpers
        {
            public static void ResetChatHideTimer()
            {
                try
                {
                    var chat = Chat.instance;
                    if (chat == null) return;
                    var field = typeof(Chat).GetField("m_hideTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        field.SetValue(chat, 0f);
                    }
                }
                catch { /* non-critical */ }
            }
        }

        #endregion
        private void CreateConfigWatcher()
        {
            // Create config file watcher
            ConfigFileWatcher configFileWatcher = new(Config, reloadDelay: 1000);  // set delay before a subsequent reload can trigger in ms

            // Subscribe to the event that fires whenever the config is reloaded.
            configFileWatcher.OnConfigFileReloaded += () =>
            {
                Jotunn.Logger.LogInfo("Config file reloaded, reinitializing config values.");
                InitConfig();
            };
        }
        private void InitConfig()
        {
            Config.SaveOnConfigSet = true;
            ConfigurationManagerAttributes isAdminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };
            EnableServerSync = Config.Bind("Server Only", "01. Enable Server Sync", true, new ConfigDescription("If true, config values are synchronized from server to clients.", null, isAdminOnly));
            isAdminOnly = new ConfigurationManagerAttributes { IsAdminOnly = EnableServerSync.Value };
            //-------------
            #region Global Notifications
            EnableGlobalUpgradeNotifications = Config.Bind("Global Notifications","01. Enable Global Upgrade Notifications",false,
                new ConfigDescription("Broadcast a message to all online players when someone attempts an upgrade.", null, isAdminOnly));

            SuccessMessage = Config.Bind("Global Notifications","02. Success Message","'{PlayerName}' successfully upgraded '{ItemName}' to level '{Level}'.",
                new ConfigDescription("Message shown on successful upgrade. Supports {PlayerName}, {ItemName}, {Level}.", null, isAdminOnly));

            FailedMessage = Config.Bind("Global Notifications","03. Failed Message","'{PlayerName}' tried to upgrade '{ItemName}' to level '{Level}', but failed.",
                new ConfigDescription("Message shown on failed upgrade. Supports {PlayerName}, {ItemName}, {Level}.", null, isAdminOnly));
            #endregion
            //----------------
            #region Upgrade Settings
            AcceptableValueRange<float> floatRange = new AcceptableValueRange<float>(0, 1);
            UpgradeChance = Config.Bind("Upgrade Settings", "01. Upgrade Chance", 1f,
                new ConfigDescription("Chance for an upgrade to succeed.", floatRange, isAdminOnly));
            BreakChance = Config.Bind("Upgrade Settings", "02. BreakChance", 0f,
                new ConfigDescription("Chance for an upgrade to fail and break the item when failing the upgrade check, failing this check results in losing 1 level instead", floatRange, isAdminOnly));

            AcceptableValueRange<int> intRange = new AcceptableValueRange<int>(0, 1000);

            CostStart = Config.Bind("Upgrade Settings", "03. Cost Start", 1, new ConfigDescription("Base idol cost for upgrades. (Game Default is 1)", intRange, isAdminOnly));
            CostIncreasePerInterval = Config.Bind("Upgrade Settings", "04. Cost Increase Per Interval", 1,
                new ConfigDescription("Additional ingredient cost each time the cost scaling interval is reached starting at CostScalingLevelStart. (Starting at level X, the upgrade cost increases by this amount every Y levels. For example, with an increase of 1 every 2 levels starting at level 6: levels 1–5 cost 1, levels 6–7 cost 2, levels 8–9 cost 3, and so on.)", null, isAdminOnly));
            CostIncreaseInterval = Config.Bind("Upgrade Settings", "05. Cost Increase Interval", 1,
                new ConfigDescription("Number of levels between each cost increase. Set to 0 to disable cost scaling.", intRange, isAdminOnly));
            CostScalingLevelStart = Config.Bind("Upgrade Settings", "06. Cost Scaling Level Start", 1,
                new ConfigDescription("Level after at which cost scaling starts.", intRange, isAdminOnly));
            
            UpgradeBaseDuration = Config.Bind("Upgrade Settings", "07. Upgrade Base Duration", 2f,
                new ConfigDescription("Base crafting duration for upgrading. (Game Default is 8)", null, isAdminOnly));
            UpgradeDurationIncreasePerLevel = Config.Bind("Upgrade Settings", "08. Upgrade Duration Increase Per Level", 1f,
                new ConfigDescription("Additional crafting duration per item level. (Game Default is 1)", null, isAdminOnly));
            #endregion
            //--------------
            #region Boss Progression
            EnableBossProgression = Config.Bind("Boss Progression", "01. Enable Boss Progression", true,
                new ConfigDescription("If true, upgrades are limited by boss progression. Defeat bosses to unlock higher upgrade levels.", null, isAdminOnly));
            BaseUpgradeLimit = Config.Bind("Boss Progression", "02. Base Upgrade Limit", 5,
                new ConfigDescription("Base upgrade limit for all items before any additional calculations are made", null, isAdminOnly));
            Boss1MaxUpgradeLevel = Config.Bind("Boss Progression", "03. Boss 1 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for wooden tier after defeating Eikthyr.", null, isAdminOnly));
            Boss2MaxUpgradeLevel = Config.Bind("Boss Progression", "04. Boss 2 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for bronze tier and below after defeating Elder.", null, isAdminOnly));
            Boss3MaxUpgradeLevel = Config.Bind("Boss Progression", "05. Boss 3 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for iron tier and below after defeating Bonemass.", null, isAdminOnly));
            Boss4MaxUpgradeLevel = Config.Bind("Boss Progression", "06. Boss 4 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for silver tier and below after defeating Moder.", null, isAdminOnly));
            Boss5MaxUpgradeLevel = Config.Bind("Boss Progression", "07. Boss 5 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for black metal tier and below after defeating Yagluth.", null, isAdminOnly));
            Boss6MaxUpgradeLevel = Config.Bind("Boss Progression", "08. Boss 6 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for black marble tier and below after defeating Queen.", null, isAdminOnly));
            Boss7MaxUpgradeLevel = Config.Bind("Boss Progression", "09. Boss 7 Max Upgrade Level", 4, 
                new ConfigDescription("Additional max upgrade level unlocked for flametal tier and below after defeating Fader.", null, isAdminOnly));
            Boss8MaxUpgradeLevel = Config.Bind("Boss Progression", "10. Boss 8 Max Upgrade Level", 100, 
                new ConfigDescription("Additional max upgrade level unlocked for bloodgold tier and below after defeating Kall Fimbulbringer.", null, isAdminOnly));
            bossUpgradeValues = new Dictionary<int, int>()
            {
                { 0, 0 }, // No boss defeated
                { 1, Boss1MaxUpgradeLevel.Value },
                { 2, Boss2MaxUpgradeLevel.Value },
                { 3, Boss3MaxUpgradeLevel.Value },
                { 4, Boss4MaxUpgradeLevel.Value },
                { 5, Boss5MaxUpgradeLevel.Value },
                { 6, Boss6MaxUpgradeLevel.Value },
                { 7, Boss7MaxUpgradeLevel.Value },
                { 8, Boss8MaxUpgradeLevel.Value }
            };
            #endregion
            //--------------
            #region Idol Progression
            EnableIdolProgression = Config.Bind("Idol Progression", "01. Enable Idol Progression", true,
                new ConfigDescription("If true, the required idol to upgrade an equipment will change to match it's current equivalent item tier and level (If boss progression is off, tiers will change every 4 levels)", null, isAdminOnly));
            #endregion
            //--------------
            Station_Global = Config.Bind("Crafting Station", "01. Global Station", "piece_artisanstation",
                new ConfigDescription("Global crafting station for all configurable upgrader recipes. Use station m_name (e.g. 'piece_workbench'). Empty = craftable by hand.", null, isAdminOnly));
            //--------------
            #region Recipes
            EnableRecipes = Config.Bind("Recipes", "01. Enable Recipes", true,
                new ConfigDescription("Enable or disable all idol crafting recipes.", null, isAdminOnly));
            Recipe_Upgrader0Armor = Config.Bind("Recipes", "02. Wooden Protection Idol",
                "Wood:50,GreydwarfEye:10",
                new ConfigDescription("Ingredients for Wooden Protection Idol: comma-separated entries 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader0Weapon = Config.Bind("Recipes", "03. Wooden Battle Idol",
                "Wood:50,GreydwarfEye:10",
                new ConfigDescription("Ingredients for Wooden Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader1Armor = Config.Bind("Recipes", "04. Bronze Protection Idol",
                "Bronze:10,SurtlingCore:1",
                new ConfigDescription("Ingredients for Bronze Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader1Weapon = Config.Bind("Recipes", "05. Bronze Battle Idol",
                "Bronze:10,SurtlingCore:1",
                new ConfigDescription("Ingredients for Bronze Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader2Armor = Config.Bind("Recipes", "06. Iron Protection Idol",
                "Iron:10,ElderBark:5",
                new ConfigDescription("Ingredients for Iron Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader2Weapon = Config.Bind("Recipes", "07. Iron Battle Idol",
                "Iron:10,ElderBark:5",
                new ConfigDescription("Ingredients for Iron Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader3Armor = Config.Bind("Recipes", "08. Silver Protection Idol",
                "Silver:10,FreezeGland:5,Obsidian:5",
                new ConfigDescription("Ingredients for Silver Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader3Weapon = Config.Bind("Recipes", "09. Silver Battle Idol",
                "Silver:10,FreezeGland:5,Obsidian:5",
                new ConfigDescription("Ingredients for Silver Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader4Armor = Config.Bind("Recipes", "10. Black Metal Protection Idol",
                "BlackMetal:10,Needle:2,Tar:5",
                new ConfigDescription("Ingredients for Black Metal Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader4Weapon = Config.Bind("Recipes", "11. Black Metal Battle Idol",
                "BlackMetal:10,Needle:2,Tar:5",
                new ConfigDescription("Ingredients for Black Metal Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader5Armor = Config.Bind("Recipes", "12. Black Marble Protection Idol",
                "BlackMarble:20,BugMeat:10,Carapace:10",
                new ConfigDescription("Ingredients for Black Marble Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader5Weapon = Config.Bind("Recipes", "13. Black Marble Battle Idol",
                "BlackMarble:20,BugMeat:10,Carapace:10",
                new ConfigDescription("Ingredients for Black Marble Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader6Armor = Config.Bind("Recipes", "14. Flametal Protection Idol",
                "FlametalNew:10,CharredBone:15",
                new ConfigDescription("Ingredients for Flametal Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader6Weapon = Config.Bind("Recipes", "15. Flametal Battle Idol",
                "FlametalNew:10,CharredBone:15",
                new ConfigDescription("Ingredients for Flametal Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));

            Recipe_Upgrader7Armor = Config.Bind("Recipes", "16. Bloodgold Protection Idol",
                "Gold:10,Coins:40,AncientCoin:10",
                new ConfigDescription("Ingredients for Bloodgold Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader7Weapon = Config.Bind("Recipes", "17. Bloodgold Battle Idol",
                "Gold:10,Coins:40,AncientCoin:10",
                new ConfigDescription("Ingredients for Bloodgold Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            #endregion


            AddConfigurableRecipes();

        }

        [HarmonyPatch(typeof(InventoryGui))]
        static class InventoryGuiPatch
        {
            #region DoCrafting Prefix/Postfix
            [HarmonyPrefix]
            [HarmonyPatch(nameof(InventoryGui.DoCrafting))]
            static bool DoCraftingPrefix(object __instance, object player, out DoCraftingState __state)
            {
                __state = null;
                try
                {
                    var mi = player.GetType().GetMethod("GetCurrentCraftingStation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var station = mi?.Invoke(player, null);
                    bool atUpgrader = station != null && (bool)station.GetType().GetField("m_upgrader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(station);
                    if (!atUpgrader) return true;
                    // preserve previous behavior of altering shared upgrade/break chances for upgrader resources
                    var igType = __instance.GetType();
                    var recipeField = igType.GetField("m_craftRecipe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var recipeObj = recipeField?.GetValue(__instance);
                    var modified = new List<KeyValuePair<object, float>>();

                    if (recipeObj != null)
                    {
                        var resourcesField = recipeObj.GetType().GetField("m_resources", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var resources = resourcesField?.GetValue(recipeObj) as Array;
                        if (resources != null)
                        {
                            foreach (var req in resources)
                            {
                                if (req == null) continue;
                                var reqType = req.GetType();
                                var upgraderResField = reqType.GetField("m_upgraderResource", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (upgraderResField == null) continue;
                                bool isUpgraderResource = false;
                                try { isUpgraderResource = (bool)upgraderResField.GetValue(req); } catch { isUpgraderResource = false; }
                                if (!isUpgraderResource) continue;

                                var resItemObj = reqType.GetField("m_resItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(req);
                                if (resItemObj == null) continue;
                                var itemDataObj = resItemObj.GetType().GetField("m_itemData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(resItemObj);
                                if (itemDataObj == null) continue;
                                var sharedObj = itemDataObj.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemDataObj);
                                if (sharedObj == null) continue;

                                var sharedType = sharedObj.GetType();
                                var upgradeChanceField = sharedType.GetField("m_upgradeChance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                var breakChanceField = sharedType.GetField("m_breakChance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (upgradeChanceField == null || breakChanceField == null) continue;

                                try
                                {
                                    float original = Convert.ToSingle(upgradeChanceField.GetValue(sharedObj));
                                    modified.Add(new KeyValuePair<object, float>(sharedObj, original));
                                    upgradeChanceField.SetValue(sharedObj, UpgradeChance.Value);
                                    breakChanceField.SetValue(sharedObj, BreakChance.Value);
                                    Jotunn.Logger.LogDebug($"DoCraftingPrefix: Modified shared upgradeChance/breakChance for resource '{sharedObj.GetType().Name}' (original={original}, new={UpgradeChance.Value}/{BreakChance.Value})");
                                }
                                catch { /* ignore individual failures and continue */ }
                            }
                        }
                    }

                    // Snapshot the m_craftUpgradeItem and recipe ingredient names for later analysis
                    object upgradeItem = null;
                    int originalQuality = int.MinValue;
                    string prefabName = null;
                    var returnedIngredientNames = new List<string>();

                    try
                    {
                        var upgradeField = igType.GetField("m_craftUpgradeItem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        upgradeItem = upgradeField?.GetValue(__instance);
                        if (upgradeItem != null)
                        {
                            var qField = upgradeItem.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (qField != null) originalQuality = Convert.ToInt32(qField.GetValue(upgradeItem));

                            var shared = upgradeItem.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(upgradeItem);
                            prefabName = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;

                            if (string.IsNullOrEmpty(prefabName))
                            {
                                var dropPrefab = upgradeItem.GetType().GetField("m_dropPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(upgradeItem);
                                prefabName = dropPrefab?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dropPrefab) as string;
                            }
                        }

                        // collect recipe ingredient names that may be returned on break (m_recover)
                        if (recipeObj != null)
                        {
                            var resourcesField = recipeObj.GetType().GetField("m_resources", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var resources = resourcesField?.GetValue(recipeObj) as Array;
                            if (resources != null)
                            {
                                foreach (var req in resources)
                                {
                                    if (req == null) continue;
                                    var reqType = req.GetType();
                                    var recoverField = reqType.GetField("m_recover", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (recoverField == null) continue;
                                    bool recover = false;
                                    try { recover = (bool)recoverField.GetValue(req); } catch { recover = false; }
                                    if (!recover) continue;

                                    var resItemObj = reqType.GetField("m_resItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(req);
                                    if (resItemObj != null)
                                    {
                                        var name = resItemObj?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(resItemObj) as string;
                                        if (!string.IsNullOrEmpty(name)) returnedIngredientNames.Add(name);
                                    }
                                }
                            }
                        }
                    }
                    catch {}

                    InventorySnapshot beforeSnap = null;
                    try { beforeSnap = CaptureInventory(player); } catch { beforeSnap = new InventorySnapshot(); }

                    if ((modified.Count > 0) || upgradeItem != null)
                    {
                        __state = new DoCraftingState
                        {
                            ModifiedList = modified.Count > 0 ? modified : null,
                            Snapshot = upgradeItem != null ? new UpgradeSnapshot { UpgradeItem = upgradeItem, OriginalQuality = originalQuality, PrefabName = prefabName } : null,
                            InventoryBefore = beforeSnap,
                            ReturnIngredientNames = returnedIngredientNames
                        };
                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"DoCrafting prefix snapshot exception: {ex}");
                }
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(InventoryGui.DoCrafting))]
            static void DoCraftingPostfix(object __instance, object player, DoCraftingState __state)
            {
                try
                {
                    // restore modified shared upgradeChance values
                    if (__state?.ModifiedList is List<KeyValuePair<object, float>> modified)
                    {
                        foreach (var kvp in modified)
                        {
                            var sharedObj = kvp.Key;
                            float original = kvp.Value;
                            if (sharedObj == null) continue;
                            var sharedType = sharedObj.GetType();
                            var upgradeChanceField = sharedType.GetField("m_upgradeChance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (upgradeChanceField != null)
                            {
                                try { upgradeChanceField.SetValue(sharedObj, original); } catch { }
                            }
                        }
                    }

                    if (__state?.Snapshot == null) return;

                    var mi = player.GetType().GetMethod("GetCurrentCraftingStation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var station = mi?.Invoke(player, null);
                    bool atUpgrader = station != null && (bool)station.GetType().GetField("m_upgrader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(station);
                    if (!atUpgrader) return;

                    // capture post-craft inventory
                    InventorySnapshot afterSnap = null;
                    try { afterSnap = CaptureInventory(player); } catch { afterSnap = new InventorySnapshot(); }

                    var outcome = AnalyzeUpgradeResult(__state.Snapshot, __state.InventoryBefore ?? new InventorySnapshot(), afterSnap, __state.ReturnIngredientNames ?? Enumerable.Empty<string>());
                    
                    string playerName = "<unknown>";
                    try
                    {
                        if (player != null)
                        {
                            var gp = player.GetType().GetMethod("GetPlayerName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (gp != null) playerName = gp.Invoke(player, null) as string ?? playerName;
                        }
                    }
                    catch { }

                    // determine displayable item name (prefer localized shared name)
                    string itemName = __state.Snapshot?.PrefabName ?? "<unknown item>";
                    try
                    {
                        var upItem = __state.Snapshot?.UpgradeItem;
                        if (upItem != null)
                        {
                            var shared = upItem.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(upItem);
                            var sharedNameKey = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;
                            if (!string.IsNullOrEmpty(sharedNameKey))
                            {
                                try
                                {
                                    itemName = Localization.instance.Localize(sharedNameKey);
                                }
                                catch
                                {
                                    itemName = sharedNameKey;
                                }
                            }
                        }
                    }
                    catch { /* best effort */ }

                    // determine resulting level if possible
                    int reportedLevel = __state.Snapshot?.OriginalQuality ?? 0;
                    int foundQuality = int.MinValue;
                    try
                    {
                        // 1) Prefer the same instance (most reliable)
                        if (afterSnap.ByInstance.TryGetValue(__state.Snapshot.UpgradeItem, out var qSame))
                        {
                            foundQuality = qSame;
                        }
                        else
                        {
                            int origQ = __state.Snapshot?.OriginalQuality ?? int.MinValue;
                            string prefabName = __state.Snapshot?.PrefabName ?? string.Empty;

                            // Prepare sets for new vs pre-existing instances
                            var beforeKeys = (__state.InventoryBefore?.ByInstance?.Keys ?? Enumerable.Empty<object>()).ToHashSet();
                            var afterKeys = afterSnap.ByInstance.Keys.ToList();

                            // 2) Examine NEW instances only (after \ before)
                            var newInstances = afterKeys.Where(k => !beforeKeys.Contains(k)).ToList();
                            var matchingNew = new List<(object Instance, int Quality)>();
                            foreach (var it in newInstances)
                            {
                                try
                                {
                                    var shared = it.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                                    var name = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;
                                    if (string.IsNullOrEmpty(name))
                                    {
                                        var dropPrefab = it.GetType().GetField("m_dropPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                                        name = dropPrefab?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dropPrefab) as string;
                                    }
                                    if (string.IsNullOrEmpty(name)) continue;
                                    if (!string.Equals(name, prefabName, StringComparison.OrdinalIgnoreCase)) continue;

                                    int q = int.MinValue;
                                    try { q = Convert.ToInt32(it.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it)); } catch { }
                                    matchingNew.Add((it, q));
                                }
                                catch { /* ignore problematic instance */ }
                            }

                            if (matchingNew.Count > 0)
                            {
                                // Pick best candidate among new instances:
                                // 1) exact expected quality (origQ + 1)
                                // 2) any > origQ
                                // 3) any == origQ
                                // 4) any < origQ (fallback choose highest)
                                var pick = matchingNew.FirstOrDefault(x => x.Quality == origQ + 1);
                                if (pick.Instance != null) foundQuality = pick.Quality;
                                else
                                {
                                    pick = matchingNew.FirstOrDefault(x => x.Quality > origQ);
                                    if (pick.Instance != null) foundQuality = pick.Quality;
                                    else
                                    {
                                        pick = matchingNew.FirstOrDefault(x => x.Quality == origQ);
                                        if (pick.Instance != null) foundQuality = pick.Quality;
                                        else
                                        {
                                            pick = matchingNew.OrderByDescending(x => x.Quality).FirstOrDefault();
                                            if (pick.Instance != null) foundQuality = pick.Quality;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // 3) No new matches — inspect common instances that persisted and might have been updated
                                var common = afterKeys.Where(k => beforeKeys.Contains(k)).ToList();
                                foreach (var it in common)
                                {
                                    try
                                    {
                                        var shared = it.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                                        var name = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;
                                        if (string.IsNullOrEmpty(name))
                                        {
                                            var dropPrefab = it.GetType().GetField("m_dropPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                                            name = dropPrefab?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dropPrefab) as string;
                                        }
                                        if (string.IsNullOrEmpty(name)) continue;
                                        if (!string.Equals(name, prefabName, StringComparison.OrdinalIgnoreCase)) continue;

                                        int beforeQ = __state.InventoryBefore.ByInstance.TryGetValue(it, out var bq) ? bq : int.MinValue;
                                        int afterQ = afterSnap.ByInstance.TryGetValue(it, out var aq) ? aq : int.MinValue;

                                        if (afterQ == beforeQ) continue;
                                        if (afterQ == origQ + 1) { foundQuality = afterQ; break; }
                                        if (afterQ > origQ) { foundQuality = afterQ; break; }
                                        if (afterQ < origQ) { foundQuality = afterQ; break; }
                                        if (afterQ == origQ) { foundQuality = afterQ; break; }
                                    }
                                    catch { /* best-effort */ }
                                }
                            }
                        }
                    }
                    catch { }

                    if (foundQuality != int.MinValue) reportedLevel = foundQuality;
                    else
                    {
                        // fallback heuristics based on outcome
                        switch (outcome)
                        {
                            case UpgradeOutcome.Upgraded: reportedLevel = (__state.Snapshot?.OriginalQuality ?? 0) + 1; break;
                            case UpgradeOutcome.Degraded: reportedLevel = Math.Max(0, (__state.Snapshot?.OriginalQuality ?? 0) - 1); break;
                            case UpgradeOutcome.ReturnedIngredients: reportedLevel = __state.Snapshot?.OriginalQuality ?? 0; break;
                            case UpgradeOutcome.Destroyed: reportedLevel = Math.Max(0, (__state.Snapshot?.OriginalQuality ?? 0) - 1); break;
                            default: reportedLevel = __state.Snapshot?.OriginalQuality ?? 0; break;
                        }
                    }

                    // success = true when upgraded, false otherwise
                    bool success = outcome == UpgradeOutcome.Upgraded;
                    if (!success)
                    {
                        reportedLevel = (__state.Snapshot?.OriginalQuality ?? 0) + 1;
                    }
                    BroadcastUpgradeResult(playerName, itemName, reportedLevel, success);
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"DoCrafting postfix analysis exception: {ex}");
                }
            }

            // Helper types and methods used by the above prefix/postfix

            private class DoCraftingState
            {
                public List<KeyValuePair<object, float>> ModifiedList;
                public UpgradeSnapshot Snapshot;
                public InventorySnapshot InventoryBefore;
                public List<string> ReturnIngredientNames;
            }

            private class UpgradeSnapshot
            {
                public object UpgradeItem;       // original ItemData reference observed in prefix
                public int OriginalQuality;
                public string PrefabName;
            }

            private class InventorySnapshot
            {
                public Dictionary<object, int> ByInstance = new Dictionary<object, int>();
                public Dictionary<string, int> CountByPrefab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            private enum UpgradeOutcome { Upgraded, Degraded, Destroyed, ReturnedIngredients, Unchanged, Unknown }

            private static InventorySnapshot CaptureInventory(object playerObj)
            {
                var snap = new InventorySnapshot();
                if (playerObj == null) return snap;
                try
                {
                    Jotunn.Logger.LogDebug($"Capturing inventory for player object of type {playerObj.GetType().FullName}");
                    var mi = playerObj.GetType().GetMethod("GetInventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var inventory = mi?.Invoke(playerObj, null) ?? playerObj.GetType().GetField("m_inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(playerObj);
                    if (inventory == null) return snap;

                    System.Collections.IEnumerable itemsEnum = null;
                    var itemsField = inventory.GetType().GetField("m_inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (itemsField != null) itemsEnum = itemsField.GetValue(inventory) as System.Collections.IEnumerable;
                    if (itemsEnum == null)
                    {
                        var itemsProp = inventory.GetType().GetProperty("m_inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (itemsProp != null) itemsEnum = itemsProp.GetValue(inventory) as System.Collections.IEnumerable;
                    }

                    if (itemsEnum == null)
                    {
                        foreach (var f in inventory.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            var val = f.GetValue(inventory) as System.Collections.IEnumerable;
                            if (val == null) continue;
                            foreach (var it in val)
                            {
                                if (it == null) continue;
                                if (it.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                                {
                                    itemsEnum = val;
                                    break;
                                }
                            }
                            if (itemsEnum != null) break;
                        }
                    }

                    if (itemsEnum == null) return snap;

                    foreach (var it in itemsEnum)
                    {
                        if (it == null) continue;
                        int quality = int.MinValue;
                        try
                        {
                            var qf = it.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (qf != null) quality = Convert.ToInt32(qf.GetValue(it));
                        }
                        catch { }

                        // determine prefab/shared name
                        string name = null;
                        try
                        {
                            var shared = it.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                            name = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;
                            if (string.IsNullOrEmpty(name))
                            {
                                var dropPrefab = it.GetType().GetField("m_dropPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                                name = dropPrefab?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dropPrefab) as string;
                            }
                        }
                        catch { }

                        try { snap.ByInstance[it] = quality; } catch { }
                        if (!string.IsNullOrEmpty(name))
                        {
                            if (!snap.CountByPrefab.TryGetValue(name, out int c)) c = 0;
                            snap.CountByPrefab[name] = c + 1;
                        }
                    }
                }
                catch { }
                return snap;
            }

            private static UpgradeOutcome AnalyzeUpgradeResult(UpgradeSnapshot snap, InventorySnapshot before, InventorySnapshot after, IEnumerable<string> recipeIngredientNames)
            {
                if (snap == null) return UpgradeOutcome.Unknown;
                int origQ = snap.OriginalQuality;
                string prefab = snap.PrefabName ?? string.Empty;

                // 1) same instance still present?
                if (before.ByInstance.ContainsKey(snap.UpgradeItem))
                {
                    if (after.ByInstance.TryGetValue(snap.UpgradeItem, out int newQ))
                    {
                        if (newQ > origQ) return UpgradeOutcome.Upgraded;
                        if (newQ < origQ) return UpgradeOutcome.Degraded;
                        return UpgradeOutcome.Unchanged;
                    }
                }

                // Precompute sets
                var beforeKeys = new HashSet<object>(before.ByInstance.Keys);
                var afterKeys = new HashSet<object>(after.ByInstance.Keys);

                // 2) Prefer NEW instances only (after \ before) that match prefab
                var newInstances = afterKeys.Except(beforeKeys).ToList();
                var matchingNew = newInstances.Where(it =>
                {
                    try
                    {
                        var shared = it.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                        var name = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;
                        if (string.IsNullOrEmpty(name))
                        {
                            var dropPrefab = it.GetType().GetField("m_dropPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                            name = dropPrefab?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dropPrefab) as string;
                        }
                        return string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                }).ToList();

                if (matchingNew.Count > 0)
                {
                    // Prefer exact expected quality, then any > orig, then == orig, else best fallback
                    var candidates = new List<int>();
                    foreach (var it in matchingNew)
                    {
                        try
                        {
                            var qf = it.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            int q = qf != null ? Convert.ToInt32(qf.GetValue(it)) : int.MinValue;
                            candidates.Add(q);
                        }
                        catch { candidates.Add(int.MinValue); }
                    }

                    if (candidates.Any(c => c == origQ + 1)) return UpgradeOutcome.Upgraded;
                    if (candidates.Any(c => c > origQ)) return UpgradeOutcome.Upgraded;
                    if (candidates.Any(c => c == origQ)) return UpgradeOutcome.Unchanged;
                    if (candidates.Any(c => c < origQ)) return UpgradeOutcome.Degraded;

                    return UpgradeOutcome.Unknown;
                }

                // 3) No new matching instances — check common instances (present both before & after) for quality changes
                try
                {
                    var common = afterKeys.Intersect(beforeKeys).Where(k => !ReferenceEquals(k, snap.UpgradeItem));
                    foreach (var it in common)
                    {
                        try
                        {
                            var shared = it.GetType().GetField("m_shared", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                            var name = shared?.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(shared) as string;
                            if (string.IsNullOrEmpty(name))
                            {
                                var dropPrefab = it.GetType().GetField("m_dropPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it);
                                name = dropPrefab?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dropPrefab) as string;
                            }
                            if (string.IsNullOrEmpty(name)) continue;
                            if (!string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase)) continue;

                            int beforeQ = before.ByInstance.TryGetValue(it, out var bq) ? bq : int.MinValue;
                            int afterQ = after.ByInstance.TryGetValue(it, out var aq) ? aq : int.MinValue;

                            if (afterQ == beforeQ) continue;
                            if (afterQ == origQ + 1) return UpgradeOutcome.Upgraded;
                            if (afterQ > origQ) return UpgradeOutcome.Upgraded;
                            if (afterQ < origQ) return UpgradeOutcome.Degraded;
                            if (afterQ == origQ) return UpgradeOutcome.Unchanged;
                        }
                        catch { /* per-instance best-effort */ }
                    }
                }
                catch { /* ignore */ }

                // 4) check returned ingredient creation -> "broke"
                if (recipeIngredientNames != null)
                {
                    foreach (var ing in recipeIngredientNames)
                    {
                        int beforeCount = before.CountByPrefab.TryGetValue(ing, out var b) ? b : 0;
                        int afterCount = after.CountByPrefab.TryGetValue(ing, out var a) ? a : 0;
                        if (afterCount > beforeCount) return UpgradeOutcome.ReturnedIngredients;
                    }
                }

                // 5) original missing and no new candidates -> destroyed/consumed
                bool originalPresentAfter = after.ByInstance.Keys.Any(it => ReferenceEquals(it, snap.UpgradeItem));
                if (!originalPresentAfter && matchingNew.Count == 0) return UpgradeOutcome.Destroyed;

                return UpgradeOutcome.Unknown;
            }
            #endregion
            // Craft speed
            [HarmonyPrefix]
            [HarmonyPatch(nameof(InventoryGui.SetupCrafting))]
            static void SetupCraftingPrefix(ref float ___m_upgraderDuration, ref float ___m_upgraderDurationPerLevel)
            {
                ___m_upgraderDuration = UpgradeBaseDuration.Value;
                ___m_upgraderDurationPerLevel = UpgradeDurationIncreasePerLevel.Value;
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(InventoryGui.OnCraftPressed))]
            static bool OnCraftPressedPrefix(InventoryGui __instance)
            {
                try
                {
                    if (EnableBossProgression.Value == false) return true;
                    var selectedField = typeof(InventoryGui).GetField("m_selectedRecipe", BindingFlags.Instance | BindingFlags.NonPublic);
                    var selectedRecipeDataPair = selectedField.GetValue(__instance);
                    if (selectedRecipeDataPair == null) return true;
                    var pairType = selectedRecipeDataPair.GetType();
                    var recipeProp = pairType.GetProperty("Recipe", BindingFlags.Instance | BindingFlags.Public);
                    var itemProp = pairType.GetProperty("ItemData", BindingFlags.Instance | BindingFlags.Public);

                    var recipe = recipeProp?.GetValue(selectedRecipeDataPair) as Recipe;
                    var item = itemProp?.GetValue(selectedRecipeDataPair) as ItemDrop.ItemData;

                    if (recipe == null) return true;

                    var player = Player.m_localPlayer;
                    var station = player?.GetCurrentCraftingStation();
                    bool atUpgrader = station != null && station.m_upgrader;

                    if (atUpgrader && item != null)
                    {
                        foreach (var req in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
                        {
                            if (req == null || req.m_resItem == null) continue;
                            string prefabId = req.m_resItem.name;
                            if (!prefabId.Contains("Upgrader"))
                            {
                                Jotunn.Logger.LogDebug($"Iterating resources needed to upgrade for {item.m_shared.m_name} " + $"skipping non upgrader resource: {prefabId}.");
                                continue;
                            }
                            else
                            {
                                Jotunn.Logger.LogDebug($"Iterating resources needed to upgrade for {item.m_shared.m_name} " + $"found upgrader resource: {prefabId}.");
                            }

                            int highestBossTier = UpgradeHelper.GetMaxBossTier();
                            originalRequirements.TryGetValue(recipe.m_item.name, out var originalResource);
                            int itemTier = UpgradeHelper.GetEquipmentTier(originalResource);
                            if (itemTier < 0)
                            {
                                Jotunn.Logger.LogWarning(
                                    $"Could not determine equipment tier for upgrader '{originalResource}'.");

                                continue;
                            }
                            int maxUpgradeLevel = UpgradeHelper.GetMaxUpgradeLevel(itemTier, highestBossTier);
                            var itemQuality = item.m_quality;

                            if (itemQuality >= maxUpgradeLevel)
                            {
                                int requiredBoss = UpgradeHelper.GetRequiredBossForNextUpgrade(itemTier,itemQuality);

                                Jotunn.Logger.LogDebug(
                                    $"Player attempted to upgrade {item.m_shared.m_name} " +
                                    $"from quality {itemQuality} to {itemQuality + 1}. " +
                                    $"Item tier={itemTier}, " +
                                    $"highest boss tier={highestBossTier}, " +
                                    $"max allowed={maxUpgradeLevel}, " +
                                    $"required boss={requiredBoss}.");

                                string requiredBossName =
                                    requiredBoss != -1 &&
                                    bossNames.TryGetValue(requiredBoss, out string bossName)
                                        ? bossName
                                        : null;

                                Jotunn.Logger.LogDebug(
                                    $"Player attempted to upgrade {item.m_shared.m_name} " +
                                    $"from quality {itemQuality} to {itemQuality + 1}. " +
                                    $"Item tier={itemTier}, " +
                                    $"highest boss tier={highestBossTier}, " +
                                    $"max allowed={maxUpgradeLevel}, " +
                                    $"required boss={requiredBossName ?? "none"}.");

                                player?.Message(
                                    MessageHud.MessageType.Center,
                                    requiredBossName != null
                                        ? $"Defeat {requiredBossName} to upgrade this weapon further."
                                        : "Max upgrade level reached!");

                                return false;
                            }
                            return true;
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"OnCraftPressed prefix exception: {ex}");
                }
                return true;
            }
            static Dictionary<string, string> originalRequirements = new Dictionary<string, string>();
            static int currentEquivalentTier;

            #region SetRecipe
            [HarmonyPostfix]
            [HarmonyPatch(nameof(InventoryGui.SetRecipe))]
            static void SetRecipePostfix()
            {
                try
                {
                    if (Player.m_localPlayer.GetCurrentCraftingStation() is not { m_upgrader: true })
                    {
                        Jotunn.Logger.LogDebug($"SetRecipePostfix: Player is not at an upgrader station.");
                        return;
                    }
                    var selectedRecipePair = GetSelectedRecipe();
                    var selectedRecipe = selectedRecipePair.recipe;
                    var selectedItemData = selectedRecipePair.itemData;
                    if (!originalRequirements.ContainsKey(selectedRecipe.m_item.name))
                    {
                        originalRequirements.Add(selectedRecipe.m_item.name, selectedRecipe.m_resources.FirstOrDefault(r => r.m_upgraderResource).m_resItem.name);
                        originalRequirements.TryGetValue(selectedRecipe.m_item.name, out var originalResource);
                        Jotunn.Logger.LogDebug($"SetRecipePostfix: Storing original upgrader resource for {selectedItemData.m_shared.m_name} as {originalResource}");
                    }
                    if (selectedRecipe == null || selectedItemData == null)
                    {
                        Jotunn.Logger.LogDebug($"SetRecipePostfix: No selected recipe or item data found.");
                        return;
                    }
                    else
                    {
                        // Restore original upgrader resource if it exists
                        for (int i = 0; selectedRecipe.m_resources.Length > i; i++)
                        {
                            if (selectedRecipe.m_resources[i].m_upgraderResource)
                            {
                                if (originalRequirements.TryGetValue(selectedRecipe.m_item.name, out var resource))
                                {
                                    Jotunn.Logger.LogDebug($"SetRecipePostfix: Found upgrader resource for {selectedItemData.m_shared.m_name}: {resource}");
                                    int configuredMaxBoss = (bossUpgradeValues != null && bossUpgradeValues.Count > 0) ? bossUpgradeValues.Keys.Max() : 8;
                                    var selectedResource = selectedRecipe.m_resources[i];
                                    int qualityLevel = selectedItemData.m_quality;
                                    int baseTier = UpgradeHelper.GetEquipmentTier(resource);

                                    int equivTier;
                                    int candidateQuality;
                                    int candidateMax;

                                    // BOTH OFF: no idol swap, use base tier & current quality for cost
                                    if (!EnableBossProgression.Value && !EnableIdolProgression.Value)
                                    {
                                        
                                        equivTier = baseTier;
                                        candidateMax = UpgradeHelper.GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                                        candidateQuality = Math.Max(1, Math.Min(candidateMax, qualityLevel));

                                        currentEquivalentTier = equivTier;
                                        Jotunn.Logger.LogDebug($"SetRecipePostfix (BossOff/IdolOff): keeping base resource={resource}, baseTier={baseTier}, quality={qualityLevel}, candidateQuality={candidateQuality}");
                                    }
                                    // Boss progression disabled, idol progression enabled:
                                    // Map every 4 levels, but start from the equipment's base tier
                                    else if (!EnableBossProgression.Value && EnableIdolProgression.Value)
                                    {
                                        
                                        int tierBlock = Math.Max(0, (qualityLevel - 1) / 4);
                                        // Desired tier is baseTier + tierBlock
                                        int desiredTier = baseTier + tierBlock;

                                        // Clamp desiredTier to available configured max boss tier
                                        int maxAvailableTier = (bossUpgradeValues != null && bossUpgradeValues.Count > 0) ? bossUpgradeValues.Keys.Max() : 8;
                                        desiredTier = Math.Min(desiredTier, maxAvailableTier);

                                        // Determine how many blocks actually were applied (in case of clamping)
                                        int actualBlocksUsed = Math.Max(0, desiredTier - baseTier);

                                        equivTier = desiredTier;
                                        // Within-tier quality (1..4) after subtracting applied blocks
                                        candidateMax = 4;
                                        candidateQuality = qualityLevel - (actualBlocksUsed * 4);
                                        candidateQuality = Math.Max(1, Math.Min(candidateMax, candidateQuality));

                                        currentEquivalentTier = equivTier;
                                        Jotunn.Logger.LogDebug($"SetRecipePostfix (BossOff/IdolOn): baseTier={baseTier}, quality={qualityLevel}, tierBlock={tierBlock}, desiredTier={desiredTier}, actualBlocksUsed={actualBlocksUsed}, mappedTier={equivTier}, candidateQuality={candidateQuality}");
                                    }
                                    // Boss progression enabled, idol progression disabled:
                                    // Keep original resource prefab (no idol swap), compute cost from base tier & quality
                                    else if (EnableBossProgression.Value && !EnableIdolProgression.Value)
                                    {
                                        
                                        equivTier = baseTier;
                                        int maxForBase = UpgradeHelper.GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                                        candidateMax = Math.Max(1, maxForBase);
                                        candidateQuality = Math.Max(1, Math.Min(candidateMax, qualityLevel));

                                        currentEquivalentTier = equivTier;
                                        Jotunn.Logger.LogDebug($"SetRecipePostfix (BossOn/IdolOff): keeping base resource={resource}, baseTier={baseTier}, quality={qualityLevel}, candidateQuality={candidateQuality}");
                                    }
                                    else
                                    {
                                        // Default behaviorboth on: compute equivalent tier as before
                                        equivTier = UpgradeHelper.GetEquivalentTier(baseTier, qualityLevel, configuredMaxBoss);
                                        currentEquivalentTier = equivTier;
                                        int baseMax = UpgradeHelper.GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                                        candidateMax = UpgradeHelper.GetMaxUpgradeLevel(equivTier, configuredMaxBoss);
                                        int distanceFromMax = baseMax - qualityLevel;
                                        candidateQuality = candidateMax - distanceFromMax;
                                        candidateQuality = Math.Max(1, Math.Min(candidateMax, candidateQuality));

                                        Jotunn.Logger.LogDebug($"SetRecipePostfix (Default): Base tier {baseTier}, quality {qualityLevel}, equivalent tier {equivTier}, candidateQuality {candidateQuality}.");
                                    }

                                    // compute cost using per tier level (candidateQuality)
                                    int level = Math.Max(0, candidateQuality - 1);
                                    int costStartLevel = Math.Max(1, CostScalingLevelStart.Value);
                                    int cost = CostStart.Value;
                                    if (CostIncreaseInterval.Value > 0 && level >= costStartLevel)
                                    {
                                        cost += ((level - costStartLevel)
                                            / CostIncreaseInterval.Value + 1)
                                            * CostIncreasePerInterval.Value;
                                    }

                                    // Apply resource prefab & amount:
                                    // For branches where we keep the original resource prefab, set it directly.
                                    if ((!EnableIdolProgression.Value) /* idol disabled -> keep original prefab */)
                                    {
                                        var prefab = ObjectDB.instance.GetItemPrefab(resource);
                                        if (prefab != null && prefab.TryGetComponent(out ItemDrop itemDrop))
                                        {
                                            Jotunn.Logger.LogDebug($"SetRecipePostfix: Keeping original resource {itemDrop.name} and setting amount to {cost}.");
                                            selectedResource.m_resItem = itemDrop;
                                            selectedResource.m_amount = cost;
                                        }
                                        else
                                        {
                                            Jotunn.Logger.LogDebug($"SetRecipePostfix: Could not find prefab for original resource '{resource}'.");
                                        }
                                    }
                                    else
                                    {
                                        if (selectedResource.m_resItem.name.Contains("Weapon"))
                                        {
                                            Jotunn.Logger.LogDebug($"SetRecipePostfix: Selected resource is a weapon. Setting cost to {cost} and tier to {equivTier}.");
                                            var prefab = ObjectDB.instance.GetItemPrefab($"Upgrader{equivTier}Weapon");
                                            if (prefab != null && prefab.TryGetComponent(out ItemDrop itemDrop))
                                            {
                                                Jotunn.Logger.LogDebug($"SetRecipePostfix: Found prefab for Upgrader{equivTier}Weapon. Setting resource to {itemDrop.name} with amount {cost}.");
                                                selectedResource.m_resItem = itemDrop;
                                                selectedResource.m_amount = cost;
                                            }
                                        }
                                        else if (selectedResource.m_resItem.name.Contains("Armor"))
                                        {
                                            Jotunn.Logger.LogDebug($"SetRecipePostfix: Selected resource is armor. Setting cost to {cost} and tier to {equivTier}.");
                                            var prefab = ObjectDB.instance.GetItemPrefab($"Upgrader{equivTier}Armor");
                                            if (prefab != null && prefab.TryGetComponent(out ItemDrop itemDrop))
                                            {
                                                Jotunn.Logger.LogDebug($"SetRecipePostfix: Found prefab for Upgrader{equivTier}Armor. Setting resource to {itemDrop.name} with amount {cost}.");
                                                selectedResource.m_resItem = itemDrop;
                                                selectedResource.m_amount = cost;
                                            }
                                        }
                                        else
                                        {
                                            Jotunn.Logger.LogDebug($"SetRecipePostfix: Selected resource is neither weapon nor armor. Setting cost to {cost}.");
                                        }
                                    }
                                }
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"SetRecipePostfix postfix exception: {ex}");
                }
                return;
            }
            #endregion
            static object GetSelectedRecipePair()
            {
                var invGuiType = typeof(InventoryGui);
                var instanceProp = invGuiType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
                var invGui = instanceProp?.GetValue(null);
                if (invGui == null) return null;

                var selField = invGuiType.GetField("m_selectedRecipe", BindingFlags.Instance | BindingFlags.NonPublic);
                return selField?.GetValue(invGui);
            }

            static (Recipe recipe, ItemDrop.ItemData itemData) GetSelectedRecipe()
            {
                var selPair = GetSelectedRecipePair();
                if (selPair == null) return (null, null);

                var pairType = selPair.GetType();
                var recipeProp = pairType.GetProperty("Recipe", BindingFlags.Instance | BindingFlags.Public);
                var itemProp = pairType.GetProperty("ItemData", BindingFlags.Instance | BindingFlags.Public);

                var recipe = recipeProp?.GetValue(selPair) as Recipe;
                var item = itemProp?.GetValue(selPair) as ItemDrop.ItemData;
                return (recipe, item);
            }

        }
        private void AddConfigurableRecipes()
        {
            try
            {
                if (EnableRecipes == null || !EnableRecipes.Value)
                {
                    Jotunn.Logger.LogInfo("Configurable recipes are disabled; skipping AddConfigurableRecipes.");
                    return;
                }

                var targets = new (string Prefab, ConfigEntry<string> Config)[]
                {
                        ("Upgrader0Armor", Recipe_Upgrader0Armor),
                        ("Upgrader0Weapon", Recipe_Upgrader0Weapon),
                        ("Upgrader1Armor", Recipe_Upgrader1Armor),
                        ("Upgrader1Weapon", Recipe_Upgrader1Weapon),
                        ("Upgrader2Armor", Recipe_Upgrader2Armor),
                        ("Upgrader2Weapon", Recipe_Upgrader2Weapon),
                        ("Upgrader3Armor", Recipe_Upgrader3Armor),
                        ("Upgrader3Weapon", Recipe_Upgrader3Weapon),
                        ("Upgrader4Armor", Recipe_Upgrader4Armor),
                        ("Upgrader4Weapon", Recipe_Upgrader4Weapon),
                        ("Upgrader5Armor", Recipe_Upgrader5Armor),
                        ("Upgrader5Weapon", Recipe_Upgrader5Weapon),
                        ("Upgrader6Armor", Recipe_Upgrader6Armor),
                        ("Upgrader6Weapon", Recipe_Upgrader6Weapon),
                        ("Upgrader7Armor", Recipe_Upgrader7Armor),
                        ("Upgrader7Weapon", Recipe_Upgrader7Weapon)
                };

                int added = 0;
                foreach (var (prefabName, cfgEntry) in targets)
                {
                    string cfgValue = cfgEntry?.Value?.Trim();
                    if (string.IsNullOrEmpty(cfgValue))
                    {
                        Jotunn.Logger.LogInfo($"AddConfigurableRecipes: '{prefabName}' configuration is empty, skipping.");
                        continue;
                    }

                    var craftingStationId = string.IsNullOrEmpty(Station_Global.Value.Trim().Trim('$')) ? null : Station_Global.Value.Trim().Trim('$');
                    // Build RecipeConfig; Jotunn will resolve prefabs/requirements
                    var recipeConfig = new RecipeConfig
                    {
                        Item = prefabName,
                        CraftingStation = craftingStationId,
                        Enabled = true,
                        MinStationLevel = 1
                    };

                    bool anyRequirement = false;
                    var tokens = cfgValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var raw in tokens)
                    {
                        var token = raw.Trim();
                        if (string.IsNullOrEmpty(token)) continue;

                        var parts = token.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 2)
                        {
                            Jotunn.Logger.LogWarning($"AddConfigurableRecipes: invalid token '{token}' for '{prefabName}'. Use 'PrefabName:Amount'.");
                            continue;
                        }

                        string reqName = parts[0].Trim();
                        if (!int.TryParse(parts[1].Trim(), out int reqAmount) || reqAmount <= 0)
                        {
                            Jotunn.Logger.LogWarning($"AddConfigurableRecipes: invalid amount in token '{token}' for '{prefabName}'. Must be positive integer.");
                            continue;
                        }

                        recipeConfig.AddRequirement(reqName, reqAmount);
                        anyRequirement = true;
                    }

                    if (!anyRequirement)
                    {
                        Jotunn.Logger.LogWarning($"AddConfigurableRecipes: no valid ingredients for '{prefabName}' from '{cfgValue}'; skipping.");
                        continue;
                    }

                    // Register recipe via Jotunn's ItemManager using a CustomRecipe
                    ItemManager.Instance.AddRecipe(new CustomRecipe(recipeConfig));
                    added++;
                    Jotunn.Logger.LogInfo($"AddConfigurableRecipes: added recipe for '{prefabName}' (requirements: {cfgValue}).");
                }

                Jotunn.Logger.LogInfo($"AddConfigurableRecipes: finished. Added {added} configurable recipe(s).");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"AddConfigurableRecipes exception: {ex}");
            }
        }

    }
}