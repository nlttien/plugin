using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Utils;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Adapters;

public class Poe1ShopAdapter : IShopAdapter
{
    public string AdapterName => "Path of Exile 1";

    public bool IsShopOpen(GameController gc)
    {
        try
        {
            if (gc == null) return false;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return false;

            var purchaseWindow = ingameUi.PurchaseWindow;
            if (purchaseWindow != null && purchaseWindow.IsValid && purchaseWindow.IsVisible)
                return true;

            var purchaseHideout = ingameUi.PurchaseWindowHideout;
            if (purchaseHideout != null && purchaseHideout.IsValid && purchaseHideout.IsVisible)
                return true;

            var npcDialog = ingameUi.NpcDialog;
            if (npcDialog != null && npcDialog.IsValid && npcDialog.IsVisible)
                return true;

            var sellWindow = ingameUi.SellWindow;
            if (sellWindow != null && sellWindow.IsValid && sellWindow.IsVisible)
                return true;
        }
        catch { }

        return false;
    }

    public List<ShopItemInfo> GetAvailableItems(GameController gc)
    {
        var result = new List<ShopItemInfo>();
        if (gc == null) return result;

        try
        {
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return result;

            var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
            if (purchaseWindow == null || !purchaseWindow.IsValid || !purchaseWindow.IsVisible) return result;

            var tabContainer = purchaseWindow.TabContainer;
            IList<NormalInventoryItem>? items = null;

            if (tabContainer != null && tabContainer.IsValid)
            {
                var visibleStash = tabContainer.VisibleStash;
                if (visibleStash != null && visibleStash.IsValid)
                {
                    items = visibleStash.VisibleInventoryItems;
                }
            }

            if (items == null) return result;

            var currentTabIndex = GetCurrentTabIndex(gc);

            foreach (var invItem in items)
            {
                if (invItem == null || !invItem.IsValid || !invItem.IsVisible) continue;

                var clientRect = invItem.GetClientRect();
                if (clientRect.Width <= 0 || clientRect.Height <= 0) continue;

                var itemEntity = invItem.Item;
                var itemInfo = new ShopItemInfo
                {
                    InventoryItem = invItem,
                    ScreenRect = clientRect,
                    ClickPosition = new Vector2(clientRect.Center.X, clientRect.Center.Y),
                    TabIndex = currentTabIndex,
                    SlotX = invItem.InventPosX,
                    SlotY = invItem.InventPosY,
                    Width = Math.Max(1, invItem.ItemWidth),
                    Height = Math.Max(1, invItem.ItemHeight)
                };

                if (itemEntity != null && itemEntity.IsValid)
                {
                    itemInfo.ItemPath = itemEntity.Path ?? string.Empty;

                    // Base Component
                    var baseComp = itemEntity.GetComponent<Base>();
                    if (baseComp != null)
                    {
                        itemInfo.BaseName = baseComp.Name ?? string.Empty;
                    }
                    else
                    {
                        itemInfo.BaseName = ParseBaseNameFromPath(itemInfo.ItemPath);
                    }

                    // Mods Component
                    var modsComp = itemEntity.GetComponent<Mods>();
                    if (modsComp != null)
                    {
                        itemInfo.Rarity = modsComp.ItemRarity;
                        itemInfo.ItemLevel = modsComp.ItemLevel;
                        itemInfo.Name = modsComp.UniqueName ?? itemInfo.BaseName;

                        // Check Timeless Jewel identification strictly
                        CheckAndParseTimelessJewel(itemInfo, modsComp);
                    }

                    // Sockets Component
                    var socketsComp = itemEntity.GetComponent<Sockets>();
                    if (socketsComp != null)
                    {
                        itemInfo.Sockets = socketsComp.NumberOfSockets;
                        itemInfo.Links = socketsComp.LargestLinkSize;
                        itemInfo.IsRgb = socketsComp.IsRGB;
                    }

                    // Quality Component
                    var qualityComp = itemEntity.GetComponent<Quality>();
                    if (qualityComp != null)
                    {
                        itemInfo.Quality = qualityComp.ItemQuality;
                    }

                    // Parse item cost from invItem, ToolTip and children
                    ParseCost(invItem, itemInfo);
                }

                result.Add(itemInfo);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error("Lỗi khi đọc danh sách đồ trong Shop PoE 1", ex);
        }

        return result;
    }

    private static void CheckAndParseTimelessJewel(ShopItemInfo itemInfo, Mods modsComp)
    {
        if (itemInfo == null || modsComp == null) return;

        var name = itemInfo.Name ?? string.Empty;
        var baseName = itemInfo.BaseName ?? string.Empty;
        var path = itemInfo.ItemPath ?? string.Empty;

        // 1. Phải là Unique Rarity
        if (itemInfo.Rarity != ItemRarity.Unique)
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        // 2. Loại trừ Cluster Jewel hoặc đồ khác
        if (path.Contains("Large", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Medium", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Small", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Cluster", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Cluster", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Voices", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Megalomaniac", StringComparison.OrdinalIgnoreCase))
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        // 3. STRICT MATCH: Thuộc 1 trong 5 loại Timeless Jewel chuẩn
        var isExactTimeless = name.Equals("Brutal Restraint", StringComparison.OrdinalIgnoreCase) ||
                              name.Equals("Glorious Vanity", StringComparison.OrdinalIgnoreCase) ||
                              name.Equals("Lethal Pride", StringComparison.OrdinalIgnoreCase) ||
                              name.Equals("Militant Faith", StringComparison.OrdinalIgnoreCase) ||
                              name.Equals("Elegant Hubris", StringComparison.OrdinalIgnoreCase) ||
                              baseName.Equals("Timeless Jewel", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("JewelPassiveTreeExpansionMaraketh", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("JewelPassiveTreeExpansionVaal", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("JewelPassiveTreeExpansionKarui", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("JewelPassiveTreeExpansionTemplar", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("JewelPassiveTreeExpansionEternalEmpire", StringComparison.OrdinalIgnoreCase);

        if (!isExactTimeless)
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        // Collect stats text safely
        var statsList = new List<string>();
        if (modsComp.HumanStats != null) statsList.AddRange(modsComp.HumanStats);

        if (modsComp.ExplicitMods != null)
        {
            foreach (var mod in modsComp.ExplicitMods)
            {
                if (mod != null)
                {
                    statsList.Add($"{mod.DisplayName} {mod.Name} {mod.Value1} {mod.Value2}".Trim());
                }
            }
        }

        if (modsComp.ItemMods != null)
        {
            foreach (var mod in modsComp.ItemMods)
            {
                if (mod != null)
                {
                    statsList.Add($"{mod.DisplayName} {mod.Name} {mod.Value1} {mod.Value2}".Trim());
                }
            }
        }

        itemInfo.ExplicitMods = statsList;

        // 4. Kiểm tra Historic mod / Seed
        var hasHistoricOrTimelessMod = false;

        foreach (var stat in statsList)
        {
            if (string.IsNullOrWhiteSpace(stat)) continue;

            // Match seed numbers, e.g. "service of 5585 dekhara" or "15045 warriors" or "17814 verses"
            var seedMatch = Regex.Match(stat, @"(?:service of|commissioned|bathed in the blood of|chanted|carved to glorify|of)\s*(\d{2,6})\s*(?:dekhara|warriors|sacrificed|verses|victims|servants)?", RegexOptions.IgnoreCase);
            if (seedMatch.Success && int.TryParse(seedMatch.Groups[1].Value, out var seedVal))
            {
                if (seedVal > 0)
                {
                    itemInfo.TimelessSeed = seedVal;
                    hasHistoricOrTimelessMod = true;
                }
            }

            // Match leader names
            if (stat.Contains("Asenath", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Balbala", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Nasima", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Doryani", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Xibaqua", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Zerphi", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Kaom", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Rakiata", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Akoya", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Avarius", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Dominus", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Maxarius", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Cadiro", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Caspiro", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Victario", StringComparison.OrdinalIgnoreCase))
            {
                var leaderMatch = Regex.Match(stat, @"\b(Asenath|Balbala|Nasima|Doryani|Xibaqua|Zerphi|Kaom|Rakiata|Akoya|Avarius|Dominus|Maxarius|Cadiro|Caspiro|Victario)\b", RegexOptions.IgnoreCase);
                if (leaderMatch.Success)
                {
                    itemInfo.TimelessLeader = leaderMatch.Groups[1].Value;
                    hasHistoricOrTimelessMod = true;
                }
            }

            if (stat.Contains("Historic", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Conquered", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Passives in radius", StringComparison.OrdinalIgnoreCase))
            {
                hasHistoricOrTimelessMod = true;
            }
        }

        // Tự động suy ra Leader theo tên nếu chưa đọc được từ stat
        if (string.IsNullOrEmpty(itemInfo.TimelessLeader))
        {
            if (name.Contains("Brutal Restraint", StringComparison.OrdinalIgnoreCase)) itemInfo.TimelessLeader = "Asenath/Balbala/Nasima";
            else if (name.Contains("Glorious Vanity", StringComparison.OrdinalIgnoreCase)) itemInfo.TimelessLeader = "Doryani/Xibaqua/Zerphi";
            else if (name.Contains("Lethal Pride", StringComparison.OrdinalIgnoreCase)) itemInfo.TimelessLeader = "Kaom/Rakiata/Akoya";
            else if (name.Contains("Militant Faith", StringComparison.OrdinalIgnoreCase)) itemInfo.TimelessLeader = "Avarius/Dominus/Maxarius";
            else if (name.Contains("Elegant Hubris", StringComparison.OrdinalIgnoreCase)) itemInfo.TimelessLeader = "Cadiro/Caspiro/Victario";
        }

        itemInfo.IsTimelessJewel = isExactTimeless || hasHistoricOrTimelessMod;
    }

    private static void ParseCost(NormalInventoryItem invItem, ShopItemInfo itemInfo)
    {
        try
        {
            var costParts = new List<string>();
            ExtractCostTextRecursive(invItem, costParts, 0);

            if (invItem.Tooltip != null && invItem.Tooltip.IsValid)
            {
                ExtractCostTextRecursive(invItem.Tooltip, costParts, 0);
            }

            if (costParts.Count > 0)
            {
                var fullCostStr = string.Join(", ", costParts);
                itemInfo.CostString = fullCostStr;

                if (itemInfo.Cost == null) itemInfo.Cost = new CurrencyCost();

                // Parse Divine Orb amount FIRST (e.g. "5x Divine Orb", "1 Divine")
                var divineMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Divine", RegexOptions.IgnoreCase);
                if (divineMatch.Success && int.TryParse(divineMatch.Groups[1].Value, out var divAmt))
                {
                    itemInfo.Cost.CurrencyName = "Divine Orb";
                    itemInfo.Cost.Amount = divAmt;
                }
                else
                {
                    // Parse Chaos Orb amount (e.g. "20x Chaos Orb", "20 Chaos")
                    var chaosMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Chaos", RegexOptions.IgnoreCase);
                    if (chaosMatch.Success && int.TryParse(chaosMatch.Groups[1].Value, out var chaosAmt))
                    {
                        itemInfo.Cost.CurrencyName = "Chaos Orb";
                        itemInfo.Cost.Amount = chaosAmt;
                    }
                }

                // Parse Gold amount (e.g. "10,920 Gold", "6,660 Gold")
                var goldMatch = Regex.Match(fullCostStr, @"([\d,]+)\s*Gold", RegexOptions.IgnoreCase);
                if (goldMatch.Success)
                {
                    var goldDigits = goldMatch.Groups[1].Value.Replace(",", "");
                    if (int.TryParse(goldDigits, out var goldAmt))
                    {
                        itemInfo.Cost.GoldAmount = goldAmt;
                    }
                }
            }
        }
        catch { }
    }

    public static void ExtractCostTextRecursive(Element? element, List<string> costParts, int depth)
    {
        if (element == null || !element.IsValid || depth > 8) return;

        var txt = (element.Text ?? string.Empty).Trim();
        var txtNoTags = (element.TextNoTags ?? string.Empty).Trim();

        if (IsCostString(txt)) costParts.Add(txt);
        else if (IsCostString(txtNoTags)) costParts.Add(txtNoTags);

        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                ExtractCostTextRecursive(child, costParts, depth + 1);
            }
        }
    }

    private static bool IsCostString(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        return str.Contains("Chaos", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Divine", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Gold", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Cost:", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Orb", StringComparison.OrdinalIgnoreCase);
    }

    public int GetTabCount(GameController gc)
    {
        try
        {
            if (gc == null) return 1;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return 1;

            var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
            if (purchaseWindow == null || !purchaseWindow.IsValid || !purchaseWindow.IsVisible) return 1;

            var tabList = purchaseWindow.TabContainer?.TabSwitchBar;
            if (tabList != null && tabList.IsValid && tabList.Children != null && tabList.Children.Count > 0)
            {
                return tabList.Children.Count;
            }

            return 1;
        }
        catch
        {
            return 1;
        }
    }

    public int GetCurrentTabIndex(GameController gc)
    {
        return 0;
    }

    public bool SwitchToTab(GameController gc, int tabIndex)
    {
        try
        {
            if (gc == null) return false;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return false;

            var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
            if (purchaseWindow == null || !purchaseWindow.IsValid || !purchaseWindow.IsVisible) return false;

            // Check if tab buttons element exists
            var tabList = purchaseWindow.TabContainer.TabSwitchBar;
            if (tabList != null && tabList.IsValid && tabList.Children != null && tabIndex < tabList.Children.Count)
            {
                var targetTabButton = tabList.Children[tabIndex];
                if (targetTabButton != null && targetTabButton.IsValid)
                {
                    var rect = targetTabButton.GetClientRect();
                    MouseHelper.MoveMouseWithJitter(rect);
                    MouseHelper.LeftClick();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Lỗi khi chuyển Tab {tabIndex} trong PoE 1", ex);
        }

        return false;
    }

    private static string ParseBaseNameFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }
}
