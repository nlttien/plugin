using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Enums;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer.Core.Services;

public static class ItemFilterEngine
{
    public static bool MatchesRule(ShopItemInfo item, FilterRule rule)
    {
        if (item == null || rule == null || !rule.Enabled) return false;

        // Special handling for Timeless Jewel Exclusive Mode
        if (rule.Name == "Timeless Jewel Exclusive")
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

        // 7. Check Base Name / Jewel Keywords
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

                // Handle Timeless Jewels match keywords
                if (trimmed.Equals("Timeless Jewel", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("Timeless", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.IsTimelessJewel ||
                        itemBase.Contains("Timeless", StringComparison.OrdinalIgnoreCase) ||
                        itemPath.Contains("JewelPassiveTreeExpansion", StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains("Brutal Restraint", StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains("Glorious Vanity", StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains("Lethal Pride", StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains("Militant Faith", StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains("Elegant Hubris", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return itemBase.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       itemName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       itemPath.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            });

            if (!matchesKeyword) return false;
        }

        // 8. Check Max Price (if set)
        if (rule.CheckMaxPrice && item.Cost != null)
        {
            if (item.Cost.IsGold && item.Cost.Amount > rule.MaxGoldCost)
            {
                return false;
            }
            if (!item.Cost.IsGold && item.Cost.Amount > rule.MaxOrbCost)
            {
                return false;
            }
        }

        return true;
    }

    public static bool MatchesAnyRule(ShopItemInfo item, IEnumerable<FilterRule> rules)
    {
        if (rules == null) return false;
        return rules.Any(r => MatchesRule(item, r));
    }
}
