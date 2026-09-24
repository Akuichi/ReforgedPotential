using System;
using System.Collections.Generic;
using System.Linq;
namespace ReforgedPotential
{
    public partial class ReforgedPotential
    {
        #region Upgrade Helpers
        public static class UpgradeHelper
        {
            public static int GetMaxBossTier()
            {
                if (ZoneSystem.instance.CheckKey("defeated_frozenking", GameKeyType.Global) && ZoneSystem.instance.CheckKey("defeated_frozenking_p3", GameKeyType.Global))
                    return 8;
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

                //Jotunn.Logger.LogDebug($"GetMaxUpgradeLevel: itemTier={itemTier}, " + $"highestBossTier={highestBossTier}, " + $"maxUpgrade={maxUpgrade}");

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

            public static Dictionary<int, int> GetTotalIdolCosts(string originalResource, int currentQuality)
            {
                var idolCosts = new Dictionary<int, int>();

                if (string.IsNullOrEmpty(originalResource) || currentQuality <= 1)
                {
                    return idolCosts;
                }

                int baseTier = GetEquipmentTier(originalResource);
                if (baseTier < 0)
                {
                    return idolCosts;
                }

                int configuredMaxBoss =
                    bossUpgradeValues != null && bossUpgradeValues.Count > 0
                        ? bossUpgradeValues.Keys.Max()
                        : 8;

                for (int qualityLevel = 1; qualityLevel < currentQuality; qualityLevel++)
                {
                    GetEquivalentUpgradeValues( baseTier, qualityLevel, configuredMaxBoss, out int idolTier, out int candidateQuality);

                    int cost = GetUpgradeCost(candidateQuality);

                    if (idolCosts.ContainsKey(idolTier))
                    {
                        idolCosts[idolTier] += cost;
                    }
                    else
                    {
                        idolCosts.Add(idolTier, cost);
                    }
                }

                return idolCosts;
            }

            public static int GetUpgradeCost(int candidateQuality)
            {
                int level = Math.Max(0, candidateQuality - 1);
                int costStartLevel = Math.Max(1, CostScalingLevelStart.Value);
                int cost = CostStart.Value;

                if (CostIncreaseInterval.Value > 0 && level >= costStartLevel)
                {
                    cost += ((level - costStartLevel) / CostIncreaseInterval.Value + 1)
                        * CostIncreasePerInterval.Value;
                }

                return cost;
            }

            public static void GetEquivalentUpgradeValues(int baseTier,int qualityLevel,int configuredMaxBoss, out int equivalentTier,out int candidateQuality)
            {
                int candidateMax;

                if (!EnableBossProgression.Value && !EnableIdolProgression.Value)
                {
                    equivalentTier = baseTier;
                    candidateMax = GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                    candidateQuality = Math.Max(1, Math.Min(candidateMax, qualityLevel));
                    return;
                }

                if (!EnableBossProgression.Value && EnableIdolProgression.Value)
                {
                    int tierBlock = Math.Max(0, (qualityLevel - 1) / 4);
                    int desiredTier = baseTier + tierBlock;
                    int maxAvailableTier = bossUpgradeValues != null && bossUpgradeValues.Count > 0
                        ? bossUpgradeValues.Keys.Max()
                        : 8;

                    desiredTier = Math.Min(desiredTier, maxAvailableTier);

                    int actualBlocksUsed = Math.Max(0, desiredTier - baseTier);

                    equivalentTier = desiredTier;
                    candidateQuality = qualityLevel - actualBlocksUsed * 4;
                    candidateQuality = Math.Max(1, Math.Min(4, candidateQuality));
                    return;
                }

                if (EnableBossProgression.Value && !EnableIdolProgression.Value)
                {
                    equivalentTier = baseTier;
                    candidateMax = GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                    candidateQuality = Math.Max(1, Math.Min(candidateMax, qualityLevel));
                    return;
                }

                equivalentTier = GetEquivalentTier(baseTier, qualityLevel, configuredMaxBoss);

                int baseMax = GetMaxUpgradeLevel(baseTier, configuredMaxBoss);
                candidateMax = GetMaxUpgradeLevel(equivalentTier, configuredMaxBoss);
                int distanceFromMax = baseMax - qualityLevel;

                candidateQuality = candidateMax - distanceFromMax;
                candidateQuality = Math.Max(1, Math.Min(candidateMax, candidateQuality));
            }
        }

        #endregion
    }
}