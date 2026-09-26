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
using System.Linq;
using System.Numerics;
using System.Reflection;
using UnityEngine;
using static AudioMan;
using static ClutterSystem;
using static ItemDrop;
using static Version;
using Logger = Jotunn.Logger;
using ReforgedPotential.Compatibility;
namespace ReforgedPotential
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    public partial class ReforgedPotential : BaseUnityPlugin
    {
        public const string PluginGUID = "akuichi.ReforgedPotential";
        public const string PluginName = "Reforged Potential";
        public const string PluginVersion = "2.0.5";

        
        private readonly Harmony harmony = new Harmony(PluginGUID);

        #region Config Entries
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
        internal static ConfigEntry<bool> ConsumeIdolsOnlyOnFailure;
        internal static ConfigEntry<int> CostStart;
        internal static ConfigEntry<int> CostIncreaseInterval;
        internal static ConfigEntry<int> CostIncreasePerInterval;
        internal static ConfigEntry<int> CostScalingLevelStart;
        internal static ConfigEntry<float> UpgradeBaseDuration;
        internal static ConfigEntry<float> UpgradeDurationIncreasePerLevel;
        internal static ConfigEntry<bool> RefundIdolsOnBreak;
        internal static ConfigEntry<int> RefundIdolsOnBreakStartingLevel;
        internal static ConfigEntry<float> RefundIdolsPercentageOnBreak;
        public static CustomRPC RPC_Reforged;
        public static ConfigEntry<bool> EnableGlobalUpgradeNotifications;
        public static ConfigEntry<string> SuccessMessage;
        public static ConfigEntry<string> FailedMessage;

        public static ConfigEntry<bool> EnableDebugLogging;

        #endregion

        private void Awake()
        {
            RPC_Reforged = NetworkManager.Instance.AddRPC("RPC_Reforged", RPC_ReforgedServerReceive, RPC_ReforgedClientReceive);
            InitConfig();
            CreateConfigWatcher();
            harmony.PatchAll();
        }

        private void Start()
        {
            EpicLootCompatibility.Init();
        }

        #region RPC Handlers
        private IEnumerator RPC_ReforgedServerReceive(long sender, ZPackage package)
        {
            RPC_Reforged.SendPackage(ZNet.instance.m_peers, new ZPackage(package.GetArray()));
            string message = package.ReadString();
            if (message != "")
            {
                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"[SERVER] Adding message to chat: {message}");
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
            {
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
            if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"Broadcasting upgrade result: {message}");
            RPC_Reforged.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
        }

        // Reflection helper to set Chat.instance.m_hideTimer = 0
        public static class ChatHelpers
        {
            public static void ResetChatHideTimer()
            {
                Chat.instance.m_hideTimer = 0f;
            }
        }

        #endregion

        #region Config
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

            SuccessMessage = Config.Bind("Global Notifications","02. Success Message","{PlayerName} successfully upgraded {ItemName} to level {Level}.",
                new ConfigDescription("Message shown on successful upgrade. Supports {PlayerName}, {ItemName}, {Level}.", null, isAdminOnly));

            FailedMessage = Config.Bind("Global Notifications","03. Failed Message","{PlayerName} tried to upgrade {ItemName} to level {Level}, but failed.",
                new ConfigDescription("Message shown on failed upgrade. Supports {PlayerName}, {ItemName}, {Level}.", null, isAdminOnly));
            #endregion
            //----------------
            #region Upgrade Settings
            AcceptableValueRange<float> floatRange = new AcceptableValueRange<float>(0, 1);
            UpgradeChance = Config.Bind("Upgrade Settings", "01. Upgrade Chance", 1f,
                new ConfigDescription("Chance for an upgrade to succeed.", floatRange, isAdminOnly));
            BreakChance = Config.Bind("Upgrade Settings", "02. Break Chance", 0f,
                new ConfigDescription("When an upgrade fails, this is the chance for it to break (Ex. 1 = Item will always break on upgrade failure, 0 = Item will just degrade 1 level on upgrade failure)", floatRange, isAdminOnly));
            ConsumeIdolsOnlyOnFailure = Config.Bind("Upgrade Settings", "03. Consume Idols Only On Failure", false,
                new ConfigDescription("If true, idols are just consumed and the item doesn't lose a level (Breaking still removes the item)", null, isAdminOnly));

            AcceptableValueRange<int> intRange = new AcceptableValueRange<int>(0, 1000);

            CostStart = Config.Bind("Upgrade Settings", "04. Cost Start", 1, new ConfigDescription("Base idol cost for upgrades. (Game Default is 1)", intRange, isAdminOnly));
            CostIncreasePerInterval = Config.Bind("Upgrade Settings", "05. Cost Increase Per Interval", 1,
                new ConfigDescription("Additional ingredient cost each time the cost scaling interval is reached starting at CostScalingLevelStart. (Starting at level X, the upgrade cost increases by this amount every Y levels. For example, with an increase of 1 every 2 levels starting at level 6: levels 1–5 cost 1, levels 6–7 cost 2, levels 8–9 cost 3, and so on.)", null, isAdminOnly));
            CostIncreaseInterval = Config.Bind("Upgrade Settings", "06. Cost Increase Interval", 1,
                new ConfigDescription("Number of levels between each cost increase. Set to 0 to disable cost scaling.", intRange, isAdminOnly));
            CostScalingLevelStart = Config.Bind("Upgrade Settings", "07. Cost Scaling Level Start", 1,
                new ConfigDescription("Level after at which cost scaling starts.", intRange, isAdminOnly));            
            UpgradeBaseDuration = Config.Bind("Upgrade Settings", "08. Upgrade Base Duration", 2f,
                new ConfigDescription("Base crafting duration for upgrading. This is the base time it takes to upgrade an equipment (Game Default is 8)", null, isAdminOnly));
            UpgradeDurationIncreasePerLevel = Config.Bind("Upgrade Settings", "09. Upgrade Duration Increase Per Level", 1f,
                new ConfigDescription("Additional crafting duration per item level. This is the additional time added per item level to the total crafting time. (Game Default is 1)", null, isAdminOnly));
            RefundIdolsOnBreak = Config.Bind("Upgrade Settings", "10. Refund Idols On Break", false,
                new ConfigDescription("If true, a percentage of the idols used are refunded when an equipment breaks", null, isAdminOnly));
            RefundIdolsOnBreakStartingLevel = Config.Bind("Upgrade Settings", "11. Refund Idols On Break Starting Level", 5,
                new ConfigDescription("Calculation of the refunded idols starts at this level. (Ex. at value 5: Levels 1-4 are not included for the idol refund calculations", intRange, isAdminOnly));
            RefundIdolsPercentageOnBreak = Config.Bind("Upgrade Settings", "12. Refund Idols Percentage On Break", 0.3f,
                new ConfigDescription("Percentage of idols refunded when an equipment breaks.", floatRange, isAdminOnly));
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

            EnableDebugLogging = Config.Bind("Logging", "01. Enable Debug Logging", false,
                new ConfigDescription("Show debug logging", null));

            AddConfigurableRecipes();

        }
        #endregion

        [HarmonyPatch(typeof(InventoryGui))]
        static class InventoryGuiPatch
        {
            #region DoCrafting Prefix/Postfix
            [HarmonyPrefix]
            [HarmonyPatch(nameof(InventoryGui.DoCrafting))]
            static bool DoCraftingPrefix(InventoryGui __instance, Player player)
            {
                try
                {
                    CraftingStation currentCraftingStation = player.GetCurrentCraftingStation();
                    bool isUpgraderStation = currentCraftingStation != null && currentCraftingStation.m_upgrader;
                    if (!isUpgraderStation)
                    {
                        return true;
                    }
                    int originalQuality = __instance.m_craftUpgradeItem.m_quality;
                    int craftQuality = originalQuality + 1;
                    int requiredResourceAmount;
                    ItemData singleIngredient;
                    int craftMultiplier = 1;
                    int craftedItemAmount = __instance.m_craftRecipe.GetAmount(craftQuality, out requiredResourceAmount, out singleIngredient, craftMultiplier);
                    string upgradeItemName = __instance.m_craftUpgradeItem.m_shared.m_name;
                    bool hasCraftUpgradeItem = __instance.m_craftUpgradeItem == null || player.GetInventory().ContainsItem(__instance.m_craftUpgradeItem);
                    bool hasRequiredSingleIngredient =!__instance.m_craftRecipe.m_requireOnlyOneIngredient || singleIngredient != null;
                    bool hasNoCraftCost = player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
                    bool canAddCraftedItem =   __instance.m_craftUpgradeItem != null ||  player.GetInventory().CanAddItem(__instance.m_craftRecipe.m_item.gameObject, 1);
                    var sharedName = __instance.m_craftUpgradeItem.m_shared.m_name ?? "Unknown Item";
                    if (!canAddCraftedItem)
                    {
                        return false;
                    }
                    int craftedItemVariant = __instance.m_craftVariant;
                    Vector2i craftedItemPosition = new Vector2i(-1, -1);

                    if (__instance.m_craftUpgradeItem != null)
                    {
                        craftedItemPosition = __instance.m_craftUpgradeItem.m_gridPos;
                        craftedItemVariant = __instance.m_craftUpgradeItem.m_variant;

                        player.UnequipItem(__instance.m_craftUpgradeItem);
                        player.GetInventory().RemoveItem(__instance.m_craftUpgradeItem);
                    }

                    long playerId = player.GetPlayerID();
                    string playerName = player.GetPlayerName();

                    bool inventoryContainsCheatedItems = player.GetInventory().ItemCheated(__instance.m_craftRecipe.m_resources);

                    bool craftingStationWasCheated = currentCraftingStation?.GetComponent<ZNetView>().GetZDO().GetBool(ZDOVars.s_cheated) ?? false;

                    bool craftWasCheated = (inventoryContainsCheatedItems || player.NoCostCheat() || craftingStationWasCheated) && !PlayerProfile.s_bypassCheatChecks;
                    var outcome = UpgradeOutcome.Unknown;

                    ItemData craftedItem = null;
                    bool upgradeSucceeded = false;

                    ItemData upgraderResourceItem = null;

                    foreach (Piece.Requirement requirement in __instance.m_craftRecipe.m_resources)
                    {
                        if (requirement.m_upgraderResource)
                        {
                            upgraderResourceItem = requirement.m_resItem.m_itemData;
                            break;
                        }
                    }

                    if (upgraderResourceItem == null)
                    {
                        player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
                        ZLog.LogError("No upgrader resource found in inventory");
                        return false;
                    }

                    float upgradeRandomRoll = UnityEngine.Random.Range(0f, 1f);
                    float breakRandomRoll = UnityEngine.Random.Range(0f, 1f);
                    Jotunn.Logger.LogInfo($"DoCraftingPrefix: Upgrade roll: {upgradeRandomRoll * 100}, Break roll: {breakRandomRoll * 100}, Upgrade chance: {UpgradeChance.Value * 100}%, Break chance: {BreakChance.Value * 100}%");

                    // Success
                    if (UpgradeChance.Value >= upgradeRandomRoll)
                    {
                        ZLog.Log($"Upgrader <color=yellow>succeeded</color> on " + $"{__instance.m_craftUpgradeItem.m_shared.m_name} level {craftQuality}!");
                        player.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_upgrader_success", __instance.m_craftUpgradeItem.m_shared.m_name, craftQuality.ToString()), 0, null, log: true);

                        craftedItem = player.GetInventory().AddItem(
                            __instance.m_craftRecipe.m_item.gameObject.name,
                            1,
                            craftQuality,
                            craftedItemVariant,
                            playerId,
                            playerName,
                            craftedItemPosition,
                            craftWasCheated);
                        upgradeSucceeded = true;
                        outcome = UpgradeOutcome.Upgraded;
                    }
                    // Break
                    else if (BreakChance.Value >= breakRandomRoll)
                    {
                        ZLog.Log($"Upgrader <color=red>BROKE</color> " + $"{sharedName} level " + $"{craftQuality - 1} and was destroyed!");

                        player.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_upgrader_broke", sharedName, (craftQuality - 1).ToString()), 0, null, log: true);

                        outcome = UpgradeOutcome.Destroyed;
                        // Refund resources
                        if (upgraderResourceItem.m_shared
                                .m_breakReturnIngreientsAmount > 0f)
                        {
                            foreach (Piece.Requirement requirement in
                                     __instance.m_craftRecipe.m_resources)
                            {
                                if (requirement.m_recover)
                                {
                                    int returnedResourceAmount = Mathf.CeilToInt((requirement.GetAmount(1) + requirement.GetAmount(craftQuality)) * upgraderResourceItem.m_shared.m_breakReturnIngreientsAmount);
                                    if (returnedResourceAmount > 0)
                                    {
                                        craftedItem = player.GetInventory().AddItem(
                                            requirement.m_resItem.name,
                                            returnedResourceAmount,
                                            requirement.m_resItem.m_itemData.m_quality,
                                            requirement.m_resItem.m_itemData.m_variant,
                                            playerId,
                                            playerName,
                                            new Vector2i(-1, -1),
                                            craftWasCheated);

                                        ZLog.Log($"Upgrader returned " + $"{requirement.m_resItem.m_itemData.m_shared.m_name} " + $"x{returnedResourceAmount} to inventory");
                                    }
                                }
                            }
                        }
                        // Refund idols
                        if (RefundIdolsOnBreak.Value)
                        {
                            var prefabName = __instance.m_selectedRecipe.Recipe.m_item.name;
                            var originalResource = originalRequirements.TryGetValue(prefabName, out var resource) ? resource : null;
                            var totalIdolCost = UpgradeHelper.GetTotalIdolCosts(originalResource, originalQuality, RefundIdolsOnBreakStartingLevel.Value);
                            foreach (var kvp in totalIdolCost)
                            {
                                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"DoCraftingPostfix: Total idol cost for {sharedName} at quality {originalQuality}: {kvp.Key} = {kvp.Value}");
                                if (kvp.Value > 0)
                                {
                                    var itemPrefab = PrefabManager.Instance.GetPrefab(kvp.Key);
                                    if (itemPrefab != null)
                                    {
                                        if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"Attempting to refund {RefundIdolsPercentageOnBreak.Value * 100}% of {kvp.Key} x{kvp.Value}");
                                        var refundAmount = Math.Max(1, (int)Math.Ceiling(kvp.Value * RefundIdolsPercentageOnBreak.Value));
                                        if (player.GetInventory().AddItem(itemPrefab, refundAmount))
                                        {
                                            if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"DoCraftingPostfix: Refunded {refundAmount} of {kvp.Key} to player {player.GetPlayerName()}");
                                        }
                                        else
                                        {
                                            Instantiate<GameObject>(itemPrefab, player.transform.position + player.transform.forward * 2f + UnityEngine.Vector3.up, UnityEngine.Quaternion.identity);

                                            if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"DoCraftingPostfix: Could not add {kvp.Key} to player {player.GetPlayerName()}'s inventory, dropped {refundAmount} at player's position.");
                                        }
                                    }
                                    else
                                    {
                                        if (EnableDebugLogging.Value) Jotunn.Logger.LogWarning($"DoCraftingPostfix: Could not find item prefab for {kvp.Key} to refund to player {player.GetPlayerName()}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo("Refund idols on break is disabled, skipping..");
                        }
                    }
                    // Failure
                    else
                    {
                        var failureCraftQuality = Mathf.Max(1, craftQuality - 2);
                        ZLog.Log( $"Upgrader <color=orange>FAILED</color> " + $"{__instance.m_craftUpgradeItem.m_shared.m_name} level " + $"{craftQuality - 1}, level was reduced!");
                        var failureMessage = "Upgrade failed!";
                        if (ConsumeIdolsOnlyOnFailure.Value)
                        {                            
                            failureCraftQuality = __instance.m_craftUpgradeItem.m_quality;
                            if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"DoCraftingPostfix: Upgrade failed for {sharedName}, level was not reduced due to 'Consume Idols Only On Failure' = True");
                        }
                        else
                        {                            
                            if (originalQuality == 1)
                            {
                                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"DoCraftingPostfix: Upgrade failed for {sharedName}, cannot change below level 1.");
                            }
                            else
                            {
                                failureMessage = $"Upgrade failed, {sharedName}'s level was reduced!";
                                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"DoCraftingPostfix: Upgrade failed for {sharedName}, item level was reduced.");
                            }
                        }
                        craftedItem = player.GetInventory().AddItem(
                            __instance.m_craftRecipe.m_item.gameObject.name,
                            craftedItemAmount,
                            failureCraftQuality,
                            craftedItemVariant,
                            playerId,
                            playerName,
                            craftedItemPosition,
                            craftWasCheated);
                        player.Message(MessageHud.MessageType.Center, failureMessage, 0, null, log: true);
                        outcome = UpgradeOutcome.Degraded;
                    }

                    if (!hasNoCraftCost)
                    {
                        if (singleIngredient != null)
                        {
                            player.GetInventory().RemoveItem(singleIngredient.m_shared.m_name, requiredResourceAmount, singleIngredient.m_quality);
                        }
                        else
                        {
                            player.ConsumeResources(__instance.m_craftRecipe.m_resources, craftQuality, -1, craftMultiplier);
                        }
                    }
                    __instance.UpdateCraftingPanel();

                    if (currentCraftingStation != null)
                    {
                        if (upgradeSucceeded)
                        {
                            currentCraftingStation.m_craftItemDoneEffects.Create(player.transform.position,UnityEngine.Quaternion.identity);
                        }
                        else
                        {
                            currentCraftingStation.m_craftItemDoneFailEffects.Create(player.transform.position,UnityEngine.Quaternion.identity);
                        }
                    }
                    else
                    {
                        __instance.m_craftItemDoneEffects.Create(player.transform.position,UnityEngine.Quaternion.identity);
                    }
                    var itemName = Localization.instance.Localize(upgradeItemName);

                    int targetLevel = craftQuality;

                    if (outcome == UpgradeOutcome.Degraded || outcome == UpgradeOutcome.Upgraded)
                    {
                        EpicLootCompatibility.TryCopyMagicItem(__instance.m_craftUpgradeItem, craftedItem);
                    }
                    bool success = outcome == UpgradeOutcome.Upgraded;
                    BroadcastUpgradeResult(playerName, itemName, targetLevel, success);
                    Gogan.LogEvent("Game","Crafted", __instance.m_craftRecipe.m_item.m_itemData.m_shared.m_name, craftQuality);
                    return false;
                
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"DoCrafting prefix exception: {ex}");
                }
                return true;
            }
            private enum UpgradeOutcome { Upgraded, Degraded, Destroyed, Unknown }
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

                    var selectedRecipePair = __instance.m_selectedRecipe;
                    var selectedRecipe = selectedRecipePair.Recipe;
                    var selectedItemData = selectedRecipePair.ItemData;

                    if (selectedRecipe == null) return true;

                    var player = Player.m_localPlayer;
                    var station = player.GetCurrentCraftingStation();
                    bool atUpgrader = station != null && station.m_upgrader;
                    if (!atUpgrader) return true;

                    if (selectedItemData != null)
                    {
                        foreach (var req in selectedRecipe.m_resources ?? Array.Empty<Piece.Requirement>())
                        {
                            if (req == null || req.m_resItem == null) continue;
                            string prefabId = req.m_resItem.name;
                            if (!prefabId.Contains("Upgrader"))
                            {
                                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"Iterating resources needed to upgrade for {selectedItemData.m_shared.m_name} " + $"skipping non upgrader resource: {prefabId}.");
                                continue;
                            }
                            else
                            {
                                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"Iterating resources needed to upgrade for {selectedItemData.m_shared.m_name} " + $"found upgrader resource: {prefabId}.");
                            }

                            int highestBossTier = UpgradeHelper.GetMaxBossTier();
                            originalRequirements.TryGetValue(selectedRecipe.m_item.name, out var originalResource);
                            if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"Original upgrader resource: {originalResource}");
                            int itemTier = UpgradeHelper.GetEquipmentTier(originalResource);
                            if (itemTier < 0)
                            {
                                Jotunn.Logger.LogWarning($"Could not determine equipment tier for upgrader '{originalResource}'.");
                                continue;
                            }
                            int maxUpgradeLevel = UpgradeHelper.GetMaxUpgradeLevel(itemTier, highestBossTier);
                            var itemQuality = selectedItemData.m_quality;

                            if (itemQuality >= maxUpgradeLevel)
                            {
                                int requiredBoss = UpgradeHelper.GetRequiredBossForNextUpgrade(itemTier,itemQuality);
                                string requiredBossName =
                                    requiredBoss != -1 &&
                                    bossNames.TryGetValue(requiredBoss, out string bossName)
                                        ? bossName
                                        : null;

                                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo(
                                    $"Player attempted to upgrade {selectedItemData.m_shared.m_name} " +
                                    $"from quality {itemQuality} to {itemQuality + 1}. " +
                                    $"Item tier={itemTier}, " +
                                    $"highest boss tier={highestBossTier}, " +
                                    $"max allowed={maxUpgradeLevel}, " +
                                    $"required boss={requiredBossName ?? "none"}.");

                                player?.Message(
                                    MessageHud.MessageType.Center,
                                    requiredBossName != null ? $"Defeat {requiredBossName} to upgrade this weapon further.": "Max upgrade level reached!");
                                return false;
                            }
                            return true;
                        }
                        return false;
                    }
                    else
                    {
                        return true;
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
            static void SetRecipePostfix(InventoryGui __instance)
            {
                var LogPrefix = "SetRecipePostfix: ";
                try
                {
                    var player = Player.m_localPlayer;
                    var station = player.GetCurrentCraftingStation();
                    bool atUpgrader = station != null && station.m_upgrader;
                    if (!atUpgrader) return;

                    var selectedRecipePair = __instance.m_selectedRecipe;
                    var selectedRecipe = selectedRecipePair.Recipe;
                    var selectedItemData = selectedRecipePair.ItemData;
                    if (!originalRequirements.ContainsKey(selectedRecipe.m_item.name))
                    {
                        foreach (var req in selectedRecipe.m_resources ?? Array.Empty<Piece.Requirement>())
                        {
                            if(req.m_upgraderResource)
                            {
                                var originalRequirement = req.m_resItem.name;
                                originalRequirements.Add(selectedRecipe.m_item.name, originalRequirement);
                                break;
                            }
                        }
                        originalRequirements.TryGetValue(selectedRecipe.m_item.name, out var originalResource);
                        if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"{LogPrefix}Storing original upgrader resource for {selectedItemData.m_shared.m_name} as {originalResource}");
                    }
                    if (selectedRecipe == null || selectedItemData == null)
                    {
                        if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"{LogPrefix}No selected recipe or item data found.");
                        return;
                    }
                    else
                    {
                        for (int i = 0; selectedRecipe.m_resources.Length > i; i++)
                        {
                            if (selectedRecipe.m_resources[i].m_upgraderResource)
                            {
                                if (originalRequirements.TryGetValue(selectedRecipe.m_item.name, out var resource))
                                {
                                    if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"{LogPrefix}Found upgrader resource for {selectedItemData.m_shared.m_name}: {resource}");
                                    var selectedResource = selectedRecipe.m_resources[i];
                                    int qualityLevel = selectedItemData.m_quality;
                                    int baseTier = UpgradeHelper.GetEquipmentTier(resource);
                                    int equivTier;
                                    int candidateQuality;
                                    int configuredMaxBoss = bossUpgradeValues != null && bossUpgradeValues.Count > 0 ? bossUpgradeValues.Keys.Max() : 8;

                                    if (baseTier < 0)
                                    {
                                        Jotunn.Logger.LogWarning($"{LogPrefix}Could not determine equipment tier for resource '{resource}'.");
                                        continue;
                                    }

                                    UpgradeHelper.GetEquivalentUpgradeValues(baseTier, qualityLevel, configuredMaxBoss, out equivTier, out candidateQuality);

                                    currentEquivalentTier = equivTier;

                                    int cost = UpgradeHelper.GetUpgradeCost(candidateQuality);

                                    if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo(
                                        $"{LogPrefix}Resource={resource}, " +
                                        $"baseTier={baseTier}, " +
                                        $"quality={qualityLevel}, " +
                                        $"equivalentTier={equivTier}, " +
                                        $"candidateQuality={candidateQuality}, " +
                                        $"cost={cost}.");
                                    selectedItemData.m_shared.m_breakReturnIngreientsAmount = 1;
                                    // Apply resource prefab & amount:
                                    ItemDrop itemDrop = null;

                                    if (!EnableIdolProgression.Value)
                                    {
                                        var prefab = PrefabManager.Instance.GetPrefab(resource);

                                        if (prefab != null && prefab.TryGetComponent(out itemDrop))
                                        {
                                            if (EnableDebugLogging.Value)
                                                Jotunn.Logger.LogInfo(
                                                    $"{LogPrefix}Keeping original resource {itemDrop.name} and setting amount to {cost}.");
                                        }
                                        else
                                        {
                                            Jotunn.Logger.LogError(
                                                $"{LogPrefix}Could not find prefab for original resource '{resource}'.");
                                        }
                                    }
                                    else
                                    {
                                        var resourceName = selectedResource.m_resItem.name;

                                        string prefabName = resourceName.Contains("Weapon")
                                            ? $"Upgrader{equivTier}Weapon"
                                            : resourceName.Contains("Armor")
                                                ? $"Upgrader{equivTier}Armor"
                                                : null;

                                        if (prefabName == null)
                                        {
                                            Jotunn.Logger.LogError( $"{LogPrefix}Selected resource is neither weapon nor armor. Setting cost to {cost}.");
                                        }
                                        else
                                        {
                                            var prefab = PrefabManager.Instance.GetPrefab(prefabName);

                                            if (prefab != null && prefab.TryGetComponent(out itemDrop))
                                            {
                                                if (EnableDebugLogging.Value)
                                                    Jotunn.Logger.LogInfo(
                                                        $"{LogPrefix}Found prefab for {prefabName}. Setting resource to {itemDrop.name} with amount {cost}.");
                                            }
                                            else
                                            {
                                                Jotunn.Logger.LogError(
                                                    $"{LogPrefix}Could not find prefab '{prefabName}'.");
                                            }
                                        }
                                    }

                                    if (itemDrop != null)
                                    {
                                        selectedResource.m_resItem = itemDrop;
                                        selectedResource.m_amount = cost;
                                    }
                                }
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"{LogPrefix} exception: {ex}");
                }
                return;
            }
            #endregion
        }
        private void AddConfigurableRecipes()
        {
            try
            {
                if (EnableRecipes == null || !EnableRecipes.Value)
                {
                    if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo("Configurable recipes are disabled; skipping AddConfigurableRecipes.");
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
                        if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"AddConfigurableRecipes: '{prefabName}' configuration is empty, skipping.");
                        continue;
                    }

                    var craftingStationId = string.IsNullOrEmpty(Station_Global.Value.Trim().Trim('$')) ? null : Station_Global.Value.Trim().Trim('$');
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
                    if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"AddConfigurableRecipes: added recipe for '{prefabName}' (requirements: {cfgValue}).");
                }

                if (EnableDebugLogging.Value) Jotunn.Logger.LogInfo($"AddConfigurableRecipes: finished. Added {added} configurable recipe(s).");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"AddConfigurableRecipes exception: {ex}");
            }
        }
    }
}