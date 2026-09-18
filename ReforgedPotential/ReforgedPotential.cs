using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
    public class ReforgedPotential : BaseUnityPlugin
    {
        public const string PluginGUID = "akuichi.ReforgedPotential";
        public const string PluginName = "Reforged Potential";
        public const string PluginVersion = "1.1.1";

        
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
            CostScalingLevelStart = Config.Bind("Upgrade Settings", "06. Cost Scaling Level Start", 2,
                new ConfigDescription("Level at which cost scaling starts.", intRange, isAdminOnly));
            
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
            Station_Global = Config.Bind("Crafting Station", "01. Global Station", "$piece_artisanstation",
                new ConfigDescription("Global crafting station for all configurable upgrader recipes. Use station m_name (e.g. '$piece_workbench') or prefab name. Empty = craftable by hand.", null, isAdminOnly));
            //--------------
            #region Recipes
            EnableRecipes = Config.Bind("Recipes", "01. Enable Recipes", true,
                new ConfigDescription("Enable or disable all idol crafting recipes.", null, isAdminOnly));
            Recipe_Upgrader0Armor = Config.Bind("Recipes", "02. Wooden Protection Idol",
                "FineWood:20,Tin:10,GreydwarfEye:10",
                new ConfigDescription("Ingredients for Wooden Protection Idol: comma-separated entries 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader0Weapon = Config.Bind("Recipes", "03. Wooden Battle Idol",
                "FineWood:20,Tin:10,GreydwarfEye:10",
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
                "Gold:10,Coins:40",
                new ConfigDescription("Ingredients for Bloodgold Protection Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            Recipe_Upgrader7Weapon = Config.Bind("Recipes", "17. Bloodgold Battle Idol",
                "Gold:10,Coins:40",
                new ConfigDescription("Ingredients for Bloodgold Battle Idol: comma-separated 'PrefabName:Amount'.", null, isAdminOnly));
            #endregion

        }

        [HarmonyPatch(typeof(InventoryGui))]
        static class InventoryGuiPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(InventoryGui.DoCrafting))]
            static bool DoCraftingPrefix(object __instance, object player, out DoCraftingState __state)
            {
                __state = null;
                try
                {
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
                        // same instance present after craft?
                        if (afterSnap.ByInstance.TryGetValue(__state.Snapshot.UpgradeItem, out var q))
                        {
                            foundQuality = q;
                        }
                        else
                        {
                            // search for any new instance of same prefab and read its quality
                            foreach (var it in afterSnap.ByInstance.Keys)
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
                                    if (!string.Equals(name, __state.Snapshot.PrefabName, StringComparison.OrdinalIgnoreCase)) continue;

                                    var qf = it.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (qf != null) foundQuality = Convert.ToInt32(qf.GetValue(it));
                                    if (foundQuality != int.MinValue) break;
                                }
                                catch { }
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

                // 2) find new instances that weren't present before and match prefab
                var newInstances = after.ByInstance.Keys.Except(before.ByInstance.Keys).ToList();
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

                foreach (var it in matchingNew)
                {
                    int q = int.MinValue;
                    try { q = Convert.ToInt32(it.GetType().GetField("m_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(it)); } catch { }
                    if (q == origQ + 1) return UpgradeOutcome.Upgraded;
                    if (q < origQ) return UpgradeOutcome.Degraded;
                    if (q == origQ) return UpgradeOutcome.Unchanged;
                }

                // 3) check returned ingredient creation -> "broke"
                if (recipeIngredientNames != null)
                {
                    foreach (var ing in recipeIngredientNames)
                    {
                        int beforeCount = before.CountByPrefab.TryGetValue(ing, out var b) ? b : 0;
                        int afterCount = after.CountByPrefab.TryGetValue(ing, out var a) ? a : 0;
                        if (afterCount > beforeCount) return UpgradeOutcome.ReturnedIngredients;
                    }
                }

                // 4) if the original is missing and no candidate found -> destroyed/consumed
                bool originalPresentAfter = after.ByInstance.Keys.Any(it => ReferenceEquals(it, snap.UpgradeItem));
                if (!originalPresentAfter && matchingNew.Count == 0) return UpgradeOutcome.Destroyed;

                return UpgradeOutcome.Unknown;
            }

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
                    // get the private m_selectedRecipe field (struct RecipeDataPair)
                    var selField = typeof(InventoryGui).GetField("m_selectedRecipe", BindingFlags.Instance | BindingFlags.NonPublic);
                    var selPair = selField?.GetValue(__instance);
                    if (selPair == null) return true; // no selection -> run original

                    var pairType = selPair.GetType();
                    var recipeProp = pairType.GetProperty("Recipe", BindingFlags.Instance | BindingFlags.Public);
                    var itemProp = pairType.GetProperty("ItemData", BindingFlags.Instance | BindingFlags.Public);

                    var recipe = recipeProp?.GetValue(selPair) as Recipe;
                    var item = itemProp?.GetValue(selPair) as ItemDrop.ItemData;

                    if (recipe == null) return true;

                    var player = Player.m_localPlayer;
                    var station = player?.GetCurrentCraftingStation();
                    bool atUpgrader = station != null && station.m_upgrader;

                    if (atUpgrader && item != null)
                    {
                        foreach (var req in recipe.m_resources ?? Array.Empty<Piece.Requirement>())
                        {
                            if (req == null || req.m_resItem == null) continue;

                            // preferred stable identifier: prefab name
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

                            // Quality is starts from level 1 unlike the item tiers
                            // If current quality is already at the maximum,
                            // the attempted next upgrade is not allowed.
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
                    // fall through and run original on error
                }

                // return true -> run original OnCraftPressed
                return true;
            }
            static Dictionary<string, string> originalRequirements = new Dictionary<string, string>();
            static int currentEquivalentTier;
            [HarmonyPostfix]
            [HarmonyPatch(nameof(InventoryGui.SetRecipe))]
            static void SetRecipePostfix()
            {
                try
                {
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
                                if(originalRequirements.TryGetValue(selectedRecipe.m_item.name, out var resource))
                                {
                                    Jotunn.Logger.LogDebug($"SetRecipePostfix: Found upgrader resource for {selectedItemData.m_shared.m_name}: {resource}");
                                    int configuredMaxBoss = (bossUpgradeValues != null && bossUpgradeValues.Count > 0) ? bossUpgradeValues.Keys.Max() : 8;
                                    var selectedResource = selectedRecipe.m_resources[i];
                                    int qualityLevel = selectedItemData.m_quality;
                                    int baseTier = UpgradeHelper.GetEquipmentTier(resource);
                                    // find equivalent tier & equivalent quality
                                    int equivTier = UpgradeHelper.GetEquivalentTier(baseTier, qualityLevel, configuredMaxBoss);
                                    currentEquivalentTier = equivTier;
                                    int baseMax = UpgradeHelper.GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                                    int candidateMax = UpgradeHelper.GetMaxUpgradeLevel(equivTier, configuredMaxBoss);
                                    int distanceFromMax = baseMax - qualityLevel;
                                    int candidateQuality = candidateMax - distanceFromMax;
                                    Jotunn.Logger.LogDebug($"SetRecipePostfix: Base tier {baseTier}, quality {qualityLevel}, equivalent tier {equivTier}.");

                                    // clamp candidateQuality to valid range
                                    candidateQuality = Math.Max(1, Math.Min(candidateMax, candidateQuality));

                                    // compute cost using per-tier level (candidateQuality)
                                    int level = candidateQuality - 1;
                                    int costStartLevel = Math.Max(1, CostScalingLevelStart.Value);
                                    int cost = CostStart.Value;
                                    if (CostIncreaseInterval.Value > 0 && level >= costStartLevel)
                                    {
                                        cost += ((level - costStartLevel)
                                            / CostIncreaseInterval.Value + 1)
                                            * CostIncreasePerInterval.Value;
                                    }

                                    if (selectedResource.m_resItem.name.Contains("Weapon"))
                                    {
                                        Jotunn.Logger.LogDebug($"SetRecipePostfix: Selected resource is a weapon. Setting cost to {cost}.");
                                        var prefab = ObjectDB.instance.GetItemPrefab($"Upgrader{equivTier}Weapon");
                                        if (prefab.TryGetComponent(out ItemDrop itemDrop))
                                        {
                                            Jotunn.Logger.LogDebug($"SetRecipePostfix: Found prefab for Upgrader{equivTier}Weapon. Setting resource to {itemDrop.name} with amount {cost}.");
                                            selectedResource.m_resItem = itemDrop;
                                            selectedResource.m_amount = cost;
                                        }

                                    }
                                    else if (selectedResource.m_resItem.name.Contains("Armor"))
                                    {
                                        Jotunn.Logger.LogDebug($"SetRecipePostfix: Selected resource is armor. Setting cost to {cost}.");
                                        var prefab = ObjectDB.instance.GetItemPrefab($"Upgrader{equivTier}Armor");
                                        if (prefab.TryGetComponent(out ItemDrop itemDrop))
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
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"SetRecipePostfix postfix exception: {ex}");
                    // fall through and run original on error
                }
                return;
            }

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

        //    [HarmonyPostfix]
        //    [HarmonyPatch(nameof(Piece.Requirement.GetAmount))]
        //    static void GetAmountPostfix(Piece.Requirement __instance, int qualityLevel, ref int __result)
        //    {
        //        if (__instance == null) return;

        //        if (!__instance.m_upgraderResource) return;
        //        int level = qualityLevel - 1;
        //        int costStartLevel = Math.Max(1, CostScalingLevelStart.Value);
        //        int cost = CostStart.Value;

        //        if (CostIncreaseInterval.Value > 0 && level >= costStartLevel)
        //        {
        //            cost += ((level - costStartLevel)
        //                / CostIncreaseInterval.Value + 1)
        //                * CostIncreasePerInterval.Value;
        //        }

        //        __result = cost;
        //    }

        //    static object GetSelectedRecipePair()
        //    {
        //        var invGuiType = typeof(InventoryGui);
        //        var instanceProp = invGuiType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
        //        var invGui = instanceProp?.GetValue(null);
        //        if (invGui == null) return null;

        //        var selField = invGuiType.GetField("m_selectedRecipe", BindingFlags.Instance | BindingFlags.NonPublic);
        //        return selField?.GetValue(invGui);
        //    }

        //    static (Recipe recipe, ItemDrop.ItemData itemData) GetSelectedRecipe()
        //    {
        //        var selPair = GetSelectedRecipePair();
        //        if (selPair == null) return (null, null);

        //        var pairType = selPair.GetType();
        //        var recipeProp = pairType.GetProperty("Recipe", BindingFlags.Instance | BindingFlags.Public);
        //        var itemProp = pairType.GetProperty("ItemData", BindingFlags.Instance | BindingFlags.Public);

        //        var recipe = recipeProp?.GetValue(selPair) as Recipe;
        //        var item = itemProp?.GetValue(selPair) as ItemDrop.ItemData;
        //        return (recipe, item);
        //    }
        //}

        [HarmonyPatch(typeof(ObjectDB))]
        static class ObjectDBPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(ObjectDB.Awake))]
            static void AwakePostfix(ObjectDB __instance)
            {
                try
                {
                    if (__instance == null)
                    {
                        Jotunn.Logger.LogWarning("ObjectDB instance is null.");
                        return;
                    }

                    if (!EnableRecipes.Value)
                    {
                        Jotunn.Logger.LogInfo("Idol crafting recipes are disabled. Skipping recipe additions");
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
                        Jotunn.Logger.LogInfo($"Resolved global crafting station: '{globalStation.m_name ?? globalStation.gameObject.name}'");
                    }
                    else if (!string.IsNullOrEmpty(Station_Global?.Value))
                    {
                        Jotunn.Logger.LogWarning($"Global station '{Station_Global.Value}' was not found; recipes will be craftable by hand.");
                    }

                    int added = 0;
                    foreach (var t in targets)
                    {
                        string prefabName = t.Name;
                        var cfg = t.RecipeCfg;
                        if (cfg == null)
                        {
                            Jotunn.Logger.LogWarning($"No config for {prefabName}; skipping.");
                            continue;
                        }

                        string cfgValue = cfg.Value?.Trim();
                        if (string.IsNullOrEmpty(cfgValue))
                        {
                            Jotunn.Logger.LogInfo($"Empty ingredient list for {prefabName}; skipping.");
                            continue;
                        }

                        // get target prefab by name via ObjectDB helper
                        GameObject targetGO = __instance.GetItemPrefab(prefabName);
                        if (targetGO == null)
                        {
                            Jotunn.Logger.LogWarning($"Target prefab '{prefabName}' not found in ObjectDB; skipping.");
                            continue;
                        }

                        ItemDrop targetItem = targetGO.GetComponent<ItemDrop>();
                        if (targetItem == null)
                        {
                            Jotunn.Logger.LogWarning($"Target prefab '{prefabName}' has no ItemDrop; skipping.");
                            continue;
                        }

                        // skip if recipe already exists
                        bool exists = __instance.m_recipes.Exists(r => r != null && r.m_item != null &&
                                                                     (r.m_item == targetItem || string.Equals(r.m_item.name, prefabName, StringComparison.Ordinal)));
                        if (exists)
                        {
                            Jotunn.Logger.LogInfo($"Recipe for '{prefabName}' already exists; skipping.");
                            continue;
                        }

                        // parse config into requirements
                        var requirements = ParseRequirements(cfgValue, __instance).ToArray();
                        if (requirements == null || requirements.Length == 0)
                        {
                            Jotunn.Logger.LogWarning($"No valid ingredients parsed for '{prefabName}' from '{cfgValue}'; skipping.");
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
                        Jotunn.Logger.LogInfo($"Added recipe for '{prefabName}' requiring {string.Join(", ", requirements.Select(req => $"{GetReqName(req)} x{req.m_amount}"))}.");
                    }

                    if (added > 0)
                    {
                        Jotunn.Logger.LogInfo($"Finished adding {added} recipe(s). Total recipes now: {__instance.m_recipes.Count}");
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
                                Jotunn.Logger.LogInfo($"Requested InventoryGui.UpdateCraftingPanel()");
                            }
                        }
                        catch { /* non-critical */ }
                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"Exception: {ex}");
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
                        Jotunn.Logger.LogWarning($"Invalid ingredient token '{token}'. Use 'PrefabName:Amount'.");
                        continue;
                    }

                    string name = parts[0].Trim();
                    if (!int.TryParse(parts[1].Trim(), out int amount) || amount <= 0)
                    {
                        Jotunn.Logger.LogWarning($"Invalid amount in token '{token}'. Must be positive integer.");
                        continue;
                    }

                    // find prefab (prefer ObjectDB lookup)
                    GameObject prefab = odb.GetItemPrefab(name) ?? GetPrefabFromZNet(name);
                    if (prefab == null)
                    {
                        Jotunn.Logger.LogWarning($"Ingredient prefab '{name}' not found in ObjectDB or ZNetScene.");
                        continue;
                    }

                    // attempt to use ItemDrop on requirement (most mods expect ItemDrop)
                    ItemDrop drop = prefab.GetComponent<ItemDrop>();
                    if (drop == null)
                    {
                        Jotunn.Logger.LogWarning($"Ingredient prefab '{name}' missing ItemDrop component; skipping.");
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
                    Jotunn.Logger.LogWarning($"[ReforgedOfPotential] ConfigRecipes: ResolveCraftingStation exception: {ex}");
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

        #region Upgrade Helpers
        public static class UpgradeHelper
        {
            public static int GetMaxBossTier()
            {
                if (ZoneSystem.instance.CheckKey(Boss7Key, GameKeyType.Player))
                    return 7;
                if (ZoneSystem.instance.CheckKey(Boss6Key, GameKeyType.Player))
                    return 6;
                if (ZoneSystem.instance.CheckKey(Boss5Key, GameKeyType.Player))
                    return 5;
                if (ZoneSystem.instance.CheckKey(Boss4Key, GameKeyType.Player))
                    return 4;
                if (ZoneSystem.instance.CheckKey(Boss3Key, GameKeyType.Player))
                    return 3;
                if (ZoneSystem.instance.CheckKey(Boss2Key, GameKeyType.Player))
                    return 2;
                if (ZoneSystem.instance.CheckKey(Boss1Key, GameKeyType.Player))
                    return 1;
                return 0; // no bosses defeated
            }
            public static int GetEquipmentTier(string prefabName)
            {
                if (string.IsNullOrEmpty(prefabName)) return -2;
                if (prefabName.Contains("Upgrader7")) return 7;
                if (prefabName.Contains("Upgrader6")) return 6;
                if (prefabName.Contains("Upgrader5")) return 5;
                if (prefabName.Contains("Upgrader4")) return 4;
                if (prefabName.Contains("Upgrader3")) return 3;
                if (prefabName.Contains("Upgrader2")) return 2;
                if (prefabName.Contains("Upgrader1")) return 1;
                if (prefabName.Contains("Upgrader0")) return 0;
                return -1; // unknown
            }
            public static int GetMaxUpgradeLevel(int itemTier, int highestBossTier)
            {
                int maxUpgrade = BaseUpgradeLimit.Value;
                int firstRelevantBossTier = itemTier + 1;

                for (int bossTier = firstRelevantBossTier;
                     bossTier <= highestBossTier;
                     bossTier++)
                {
                    if (bossUpgradeValues.TryGetValue(bossTier, out int upgradeAmount))
                    {
                        maxUpgrade += upgradeAmount;
                    }
                }

                // If the equipment tier matches the highest defeated boss tier,alow one additional upgrade for that tier
                if (itemTier == highestBossTier)
                {
                    maxUpgrade += 1;

                    Jotunn.Logger.LogDebug(
                        $"GetMaxUpgradeLevel: itemTier={itemTier} matches " +
                        $"highestBossTier={highestBossTier}, adding +1.");
                }

                Jotunn.Logger.LogDebug(
                    $"GetMaxUpgradeLevel: itemTier={itemTier}, " +
                    $"highestBossTier={highestBossTier}, " +
                    $"maxUpgrade={maxUpgrade}");

                return maxUpgrade;
            }
            public static int GetRequiredBossForNextUpgrade(int itemTier, int currentUpgrade)
            {
                int cumulativeUpgrade = BaseUpgradeLimit.Value;
                Jotunn.Logger.LogDebug($"GetRequiredBossForNextUpgrade: itemTier={itemTier}, currentUpgrade={currentUpgrade}, base={cumulativeUpgrade}");

                // Start checking from the boss above the item's tier
                for (int bossTier = itemTier + 1; bossUpgradeValues.ContainsKey(bossTier); bossTier++)
                {
                    int bossValue = bossUpgradeValues[bossTier];
                    cumulativeUpgrade += bossValue;
                    string bossName = bossNames.ContainsKey(bossTier) ? bossNames[bossTier] : "<unknown>";
                    Jotunn.Logger.LogDebug($"Checking bossTier={bossTier}, bossValue={bossValue}, cumulativeUpgrade={cumulativeUpgrade} (bossName={bossName})");

                    if (currentUpgrade < cumulativeUpgrade)
                    {
                        Jotunn.Logger.LogDebug($"Next required bossTier={bossTier} ({bossName}) to unlock upgrades beyond {currentUpgrade}.");
                        return bossTier;
                    }
                }

                Jotunn.Logger.LogDebug($"No boss tier found that unlocks upgrades beyond currentUpgrade={currentUpgrade}. cumulativeUpgrade={cumulativeUpgrade}");
                return -1;
            }
            public static int GetEquivalentTier(int baseItemTier, int qualityLevel, int? highestBossTier = null)
            {
                // Use the supplied highest boss tier when provided.
                // Otherwise use the highest configured boss tier.
                int configuredMaxBossTier =
                    (bossUpgradeValues != null && bossUpgradeValues.Count > 0)
                        ? bossUpgradeValues.Keys.Max()
                        : 0;

                int maxBossTier = highestBossTier ?? configuredMaxBossTier;

                // Equipment cannot exist beyond the highest boss tier.
                if (baseItemTier > maxBossTier)
                    baseItemTier = maxBossTier;

                // Determine how far this item is from the maximum quality
                // available at its current equipment tier.
                int currentMaxUpgrade = GetMaxUpgradeLevel(baseItemTier, maxBossTier);
                int distanceFromMax = currentMaxUpgrade - qualityLevel;

                Jotunn.Logger.LogDebug(
                    $"GetEquivalentTier: baseTier={baseItemTier}, " +
                    $"quality={qualityLevel}, " +
                    $"currentMax={currentMaxUpgrade}, " +
                    $"distanceFromMax={distanceFromMax}, " +
                    $"maxBossTier={maxBossTier}");

                // Look for the highest equipment tier that can represent
                // the same distance from its own maximum quality.
                for (int candidateTier = maxBossTier; candidateTier >= 0; candidateTier--)
                {
                    int candidateMaxUpgrade =
                        GetMaxUpgradeLevel(candidateTier, maxBossTier);

                    int candidateQuality =
                        candidateMaxUpgrade - distanceFromMax;

                    // Quality is 1-based and cannot exceed the tier's maximum.
                    if (candidateQuality >= 1 &&
                        candidateQuality <= candidateMaxUpgrade)
                    {
                        Jotunn.Logger.LogDebug(
                            $"GetEquivalentTier: baseTier={baseItemTier}, " +
                            $"quality={qualityLevel} => " +
                            $"candidateTier={candidateTier}, " +
                            $"candidateQuality={candidateQuality}");

                        return candidateTier;
                    }
                }

                // Should only be reached in an unexpected edge case.
                Jotunn.Logger.LogDebug(
                    $"GetEquivalentTier: no equivalent tier found for " +
                    $"baseTier={baseItemTier}, quality={qualityLevel}; " +
                    $"returning baseTier.");

                return baseItemTier;
            }
        }

        #endregion
    }
}