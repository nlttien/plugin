using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Enums;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer.Core.Services;

public static class ItemFilterEngine
{
    public static bool MatchesTimelessSettings(ShopItemInfo item, ShopAutoBuyerSettings settings)
    {
        if (item == null || settings == null) return false;
        if (!item.IsTimelessJewel) return false;

        var name = item.Name ?? string.Empty;

        // 1. Filter by Jewel Type
        if (name.Contains("Brutal Restraint", StringComparison.OrdinalIgnoreCase) && settings.BuyBrutalRestraint?.Value == false)
            return false;
        if (name.Contains("Glorious Vanity", StringComparison.OrdinalIgnoreCase) && settings.BuyGloriousVanity?.Value == false)
            return false;
        if (name.Contains("Lethal Pride", StringComparison.OrdinalIgnoreCase) && settings.BuyLethalPride?.Value == false)
            return false;
        if (name.Contains("Militant Faith", StringComparison.OrdinalIgnoreCase) && settings.BuyMilitantFaith?.Value == false)
            return false;
        if (name.Contains("Elegant Hubris", StringComparison.OrdinalIgnoreCase) && settings.BuyElegantHubris?.Value == false)
            return false;

        // 2. Filter by Leader (e.g. Asenath, Balbala, Kaom, Doryani)
        var leaderFilter = settings.LeaderFilter?.Value?.Trim();
        if (!string.IsNullOrEmpty(leaderFilter))
        {
            var leaders = leaderFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var itemLeader = item.TimelessLeader ?? string.Empty;
            var matchedLeader = leaders.Any(l => itemLeader.Contains(l.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!matchedLeader) return false;
        }

        // 3. Filter by Specific Seeds (e.g. 3693, 5834)
        var seedsFilter = settings.SpecificSeeds?.Value?.Trim();
        if (!string.IsNullOrEmpty(seedsFilter))
        {
            var seeds = seedsFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var itemSeedStr = item.TimelessSeed.ToString();
            var matchedSeed = seeds.Any(s => s.Trim() == itemSeedStr);
            if (!matchedSeed) return false;
        }

        // 4. Filter by Max Divine Price
        var maxDivine = settings.MaxDivinePrice?.Value ?? 0;
        if (maxDivine > 0 && item.Cost != null)
        {
            if (item.Cost.CurrencyType == "Divine Orb" && item.Cost.Amount > maxDivine)
                return false;
        }

        // 5. Filter by Max Gold Price
        var maxGold = settings.MaxGoldPrice?.Value ?? 0;
        if (maxGold > 0 && item.Cost != null)
        {
            if (item.Cost.IsGold && item.Cost.Amount > maxGold)
                return false;
        }

        return true;
    }

    public static bool MatchesRule(ShopItemInfo item, FilterRule rule)
    {
        if (item == null || rule == null || !rule.Enabled) return false;

        if (rule.Name == "Timeless Jewel Mode")
        {
            return item.IsTimelessJewel;
        }

        // 1. Check Rarity
        switch (item.Rarity)
        {
            case ItemRarity.Normal when !rule.MatchNormal:
            case ItemRarity.Magic when !rule.MatchMagic:
            case ItemRarity.Rare when !rule.MatchRare:
            case ItemRarity.Unique when !rule.MatchUnique:
                return false;
        }

        // 2. Check Item Level
        if (rule.MinItemLevel > 0 && item.ItemLevel < rule.MinItemLevel)
        {
            return false;
        }

        // 3. Check Quality
        if (rule.MinQuality > 0 && item.Quality < rule.MinQuality)
        {
            return false;
        }

        // 4. Check Sockets
        if (rule.MinSockets > 0 && item.Sockets < rule.MinSockets)
        {
            return false;
        }

        // 5. Check Links
        if (rule.MinLinks > 0 && item.Links < rule.MinLinks)
        {
            return false;
        }

        // 6. Check RGB (Chromatic recipe)
        if (rule.RequireRgbSockets && !item.IsRgb)
        {
            return false;
        }

        // 7. Check Base Name Keywords
        if (!string.IsNullOrWhiteSpace(rule.BaseNameFilter))
        {
            var keywords = rule.BaseNameFilter.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var itemBase = item.BaseName ?? string.Empty;
            var itemName = item.Name ?? string.Empty;
            var itemPath = item.ItemPath ?? string.Empty;

            var matchesKeyword = keywords.Any(k =>
            {
                var trimmed = k.Trim();
                if (string.IsNullOrEmpty(trimmed)) return false;

                return itemBase.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       itemName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       itemPath.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            });

            if (!matchesKeyword) return false;
        }

        return true;
    }
}
