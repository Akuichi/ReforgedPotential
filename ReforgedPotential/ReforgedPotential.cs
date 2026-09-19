using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using PlayFab.ExperimentationModels;
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
            static bool DoCraftingPrefix(InventoryGui __instance, Player player, out DoCraftingState __state)
            {
                __state = null;
                try
                {
                    var station = player.GetCurrentCraftingStation();
                    bool atUpgrader = station.m_upgrader;
                    Jotunn.Logger.LogDebug($"DoCraftingPrefix: Player is at upgrader station: {atUpgrader}");
                    if (!atUpgrader) return true;
                    // preserve previous behavior of altering shared upgrade/break chances for upgrader resources
                    var recipe = __instance.m_craftRecipe;
                    var modified = new List<KeyValuePair<object, float>>();

                    if (recipe != null) 
                    {
                    
                        var resources = recipe.m_resources as Array;
                        if (resources != null)
                        {
                            foreach (Piece.Requirement req in resources)
                            {
                                bool isUpgraderResource = req.m_upgraderResource;
                                if (!isUpgraderResource) continue;

                                var resItemObj = req.m_resItem;
                                if (resItemObj == null) continue;
                                var itemDataObj = resItemObj.m_itemData;
                                if (itemDataObj == null) continue;
                                var sharedObj = itemDataObj.m_shared;
                                if (sharedObj == null) continue;
                                var upgradeChanceField = sharedObj.m_upgradeChance;
                                var breakChanceField = sharedObj.m_breakChance;

                                try
                                {
                                    float original = upgradeChanceField;
                                    modified.Add(new KeyValuePair<object, float>(sharedObj, original));
                                    upgradeChanceField = UpgradeChance.Value;
                                    breakChanceField = BreakChance.Value;
                                    Jotunn.Logger.LogDebug($"DoCraftingPrefix: Modified shared upgradeChance/breakChance for resource '{sharedObj.m_name}' (original={original}, new={UpgradeChance.Value}/{BreakChance.Value})");
                                }
                                catch { /* ignore individual failures and continue */ }
                            }
                        }
                    }

                    // Snapshot the m_craftUpgradeItem and recipe ingredient names for later analysis
                    int originalQuality = int.MinValue;
                    string prefabName = null;
                    int variant = __instance.m_craftVariant;
                    var gridPos = new Vector2i();
                    var returnedIngredientNames = new List<string>();
                    ItemDrop.ItemData itemData = null;
                    try
                    {
                        itemData = __instance.m_craftUpgradeItem;

                        if (itemData != null)
                        {
                            originalQuality = itemData.m_quality;
                            gridPos = itemData.m_gridPos;
                            prefabName = itemData.m_dropPrefab.name;
                            Jotunn.Logger.LogDebug($"DoCraftingPrefix: Captured upgrade item snapshot: prefab={prefabName}, quality={originalQuality}, gridPos={gridPos}, variant={variant}");
                        }
                    }
                    catch {}

                    if ((modified.Count > 0) || itemData != null)
                    {
                        __state = new DoCraftingState
                        {
                            ModifiedList = modified.Count > 0 ? modified : null,
                            Snapshot = new UpgradeSnapshot { OriginalQuality = originalQuality, PrefabName = prefabName, GridPos = gridPos }
                        };
                        Jotunn.Logger.LogDebug($"DoCraftingPrefix: Created DoCraftingState with {modified.Count} modified shared objects and snapshot of upgrade item.");
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
            static void DoCraftingPostfix(object __instance, DoCraftingState __state)
            {
                try
                {
                    // restore modified shared upgradeChance values
                    //if (__state?.ModifiedList is List<KeyValuePair<object, float>> modified)
                    //{
                    //    foreach (var kvp in modified)
                    //    {
                    //        var sharedObj = kvp.Key;
                    //        float original = kvp.Value;
                    //        if (sharedObj == null) continue;
                    //        var sharedType = sharedObj.GetType();
                    //        var upgradeChanceField = sharedType.GetField("m_upgradeChance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    //        if (upgradeChanceField != null)
                    //        {
                    //            try { upgradeChanceField.SetValue(sharedObj, original); } catch { }
                    //        }
                    //    }
                    //}

                    if (__state?.Snapshot == null) return;

                    var localPlayer = Player.m_localPlayer;
                    var station = localPlayer.GetCurrentCraftingStation();
                    bool atUpgrader = station != null && station.m_upgrader;
                    if (!atUpgrader) return;
                    var gridPosX = __state.Snapshot.GridPos.x;
                    var gridPosY = __state.Snapshot.GridPos.y;
                    var newItem = localPlayer.GetInventory().GetItemAt(gridPosX, gridPosY);
                    var outcome = UpgradeOutcome.Unknown;
                    if (newItem != null)
                    {
                        if (newItem.m_dropPrefab.name != __state.Snapshot.PrefabName)
                        {
                            Jotunn.Logger.LogDebug($"DoCraftingPostfix: Upgrade result prefab name mismatch (original={__state.Snapshot.PrefabName}, new={newItem.m_dropPrefab.name})");
                        }
                        else
                        {
                            Jotunn.Logger.LogDebug($"DoCraftingPostfix: Upgrade result item found at grid position ({gridPosX},{gridPosY}) with quality {newItem.m_quality} (original={__state.Snapshot.OriginalQuality})");
                            if (newItem.m_quality > __state.Snapshot.OriginalQuality)
                            {
                                outcome = UpgradeOutcome.Upgraded;
                            }
                            else if (newItem.m_quality < __state.Snapshot.OriginalQuality)
                            {
                                outcome = UpgradeOutcome.Degraded;
                            }
                            else
                            {
                                outcome = UpgradeOutcome.ReturnedIngredients;
                            }
                        }
                    }
                    else
                    {
                        Jotunn.Logger.LogDebug($"DoCraftingPostfix: No item found at grid position ({gridPosX},{gridPosY}) after crafting.");
                    }
                    string playerName = localPlayer.GetPlayerName();
                    string itemName = __state.Snapshot.PrefabName ?? "<unknown item>";
                    itemName = Localization.instance.Localize(itemName);

                    int reportedLevel = newItem.m_quality;
                    bool success = outcome == UpgradeOutcome.Upgraded;
                    BroadcastUpgradeResult(playerName, itemName, reportedLevel, success);
                }
                catch (Exception ex)
                {
                }
            }

            // Helper types and methods used by the above prefix/postfix

            private class DoCraftingState
            {
                public List<KeyValuePair<object, float>> ModifiedList;
                public UpgradeSnapshot Snapshot;
            }

            private class UpgradeSnapshot
            {
                public int OriginalQuality;
                public string PrefabName;
                public Vector2i GridPos;
            }

            private enum UpgradeOutcome { Upgraded, Degraded, Destroyed, ReturnedIngredients, Unchanged, Unknown }
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