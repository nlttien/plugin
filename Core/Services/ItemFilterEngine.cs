using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Enums;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer.Core.Services;

public static class ItemFilterEngine
{
    /// <summary>
    /// Kiểm tra sơ bộ xem item có phải là 1 trong 5 loại Timeless Jewel đạt chuẩn tướng/seed không.
    /// Nếu item đã được quét giá, chỉ trả về true nếu giá hợp lệ (10-50c).
    /// </summary>
    public static bool MatchesTimelessCandidate(ShopItemInfo item, ShopAutoBuyerSettings settings)
    {
        if (item == null || settings == null) return false;
        if (!item.IsTimelessJewel || item.Width != 1 || item.Height != 1 || item.Sockets > 0) return false;

        var name = item.Name ?? string.Empty;

        // 1. Filter by Jewel Type (5 Loại Timeless Chuẩn)
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

        // 4. Nếu chuỗi giá có chữ Divine -> Loại ngay lập tức nếu không bật BuyDivinePrice
        if (!string.IsNullOrEmpty(item.CostString) && item.CostString.Contains("Divine", StringComparison.OrdinalIgnoreCase) && settings.BuyDivinePrice?.Value != true)
            return false;

        // 5. NẾU MÓN ĐỒ ĐÃ ĐƯỢC QUÉT GIÁ (item.Cost != null)
        if (item.Cost != null)
        {
            if (!MatchesTimelessSettings(item, settings))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Kiểm tra toàn diện bao gồm cả giá cho Timeless Jewel.
    /// </summary>
    public static bool MatchesTimelessSettings(ShopItemInfo item, ShopAutoBuyerSettings settings)
    {
        if (item == null || settings == null) return false;
        if (!item.IsTimelessJewel || item.Width != 1 || item.Height != 1 || item.Sockets > 0) return false;

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

        // 2. Filter by Leader
        var leaderFilter = settings.LeaderFilter?.Value?.Trim();
        if (!string.IsNullOrEmpty(leaderFilter))
        {
            var leaders = leaderFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var itemLeader = item.TimelessLeader ?? string.Empty;
            var matchedLeader = leaders.Any(l => itemLeader.Contains(l.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!matchedLeader) return false;
        }

        // 3. Filter by Specific Seeds
        var seedsFilter = settings.SpecificSeeds?.Value?.Trim();
        if (!string.IsNullOrEmpty(seedsFilter))
        {
            var seeds = seedsFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var itemSeedStr = item.TimelessSeed.ToString();
            var matchedSeed = seeds.Any(s => s.Trim() == itemSeedStr);
            if (!matchedSeed) return false;
        }

        // 4. KIỂM TRA GIÁ:
        if (item.Cost != null)
        {
            var curr = item.Cost.CurrencyName ?? string.Empty;

            if (curr.Contains("Divine", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.BuyDivinePrice?.Value != true) return false;
                var maxDiv = settings.MaxDivinePrice?.Value ?? 0;
                if (maxDiv > 0 && item.Cost.Amount > maxDiv) return false;
            }
            else if (curr.Contains("Chaos", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.BuyChaosPrice?.Value == false) return false;

                var minChaos = settings.MinChaosPrice?.Value ?? 0;
                var maxChaos = settings.MaxChaosPrice?.Value ?? 5000;

                if (item.Cost.Amount < minChaos || (maxChaos > 0 && item.Cost.Amount > maxChaos))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            // Check Gold Limit nếu có thiết lập
            var maxGold = settings.MaxGoldPrice?.Value ?? 0;
            if (maxGold > 0 && item.Cost.GoldAmount > maxGold)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Kiểm tra toàn diện cho các vật phẩm thông thường (Invitation, Map, Currency, Trang bị...).
    /// </summary>
    public static bool MatchesGeneralSettings(ShopItemInfo item, ShopAutoBuyerSettings settings, IEnumerable<FilterRule> activeRules)
    {
        if (item == null || settings == null) return false;

        // 1. Phải thỏa mãn luật lọc (Rarity, BaseName, Sockets...)
        if (!MatchesAnyRule(item, activeRules)) return false;

        // 2. Kiểm tra giới hạn giá nếu đã đọc được giá từ RAM
        if (item.Cost != null)
        {
            var curr = item.Cost.CurrencyName ?? string.Empty;

            if (curr.Contains("Divine", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.BuyDivinePrice?.Value != true) return false;
                var maxDiv = settings.MaxDivinePrice?.Value ?? 0;
                if (maxDiv > 0 && item.Cost.Amount > maxDiv) return false;
            }
            else if (curr.Contains("Chaos", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.BuyChaosPrice?.Value == false) return false;

                var minChaos = settings.MinChaosPrice?.Value ?? 0;
                var maxChaos = settings.MaxChaosPrice?.Value ?? 5000;

                if (item.Cost.Amount < minChaos || (maxChaos > 0 && item.Cost.Amount > maxChaos))
                {
                    return false;
                }
            }

            var maxGold = settings.MaxGoldPrice?.Value ?? 0;
            if (maxGold > 0 && item.Cost.GoldAmount > maxGold)
            {
                return false;
            }
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

        // 6. Check RGB (Chromatic)
        if (rule.RequireRgbSockets && !item.IsRgb)
        {
            return false;
        }

        // 7. Base Name / Full Name Matching
        if (!string.IsNullOrWhiteSpace(rule.BaseNameFilter))
        {
            var filters = rule.BaseNameFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var itemName = item.DisplayName ?? string.Empty;
            var itemBase = item.BaseName ?? string.Empty;
            var itemPath = item.ItemPath ?? string.Empty;

            var matches = filters.Any(f =>
            {
                var trimmed = f.Trim();
                return itemName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       itemBase.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       itemPath.Contains(trimmed.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                       itemPath.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            });

            if (!matches) return false;
        }

        return true;
    }

    public static bool MatchesAnyRule(ShopItemInfo item, IEnumerable<FilterRule> rules)
    {
        if (item == null || rules == null) return false;
        return rules.Any(rule => MatchesRule(item, rule));
    }
}
