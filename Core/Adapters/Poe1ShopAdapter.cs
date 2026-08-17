using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Utils;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Adapters;

public class Poe1ShopAdapter : IShopAdapter
{
    public string AdapterName => "Path of Exile 1 Shop Adapter";

    private static readonly string[] TimelessLeaders = new[]
    {
        "Asenath", "Balbala", "Nasima",
        "Doryani", "Xibaqua", "Zerphi",
        "Kaom", "Rakiata", "Akoya",
        "Avarius", "Dominus", "Venarius",
        "Cadiro", "Caspiro", "Victario"
    };

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

            var purchaseWindowHideout = ingameUi.PurchaseWindowHideout;
            if (purchaseWindowHideout != null && purchaseWindowHideout.IsValid && purchaseWindowHideout.IsVisible)
                return true;

            return false;
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"Poe1ShopAdapter.IsShopOpen error: {ex.Message}");
            return false;
        }
    }

    public List<ShopItemInfo> GetAvailableItems(GameController gc)
    {
        var result = new List<ShopItemInfo>();
        try
        {
            if (gc == null) return result;
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

                    // Try parse cost from item children or tooltip
                    ParseCost(invItem, itemInfo);
                }
                else
                {
                    itemInfo.BaseName = "Unknown Item";
                }

                result.Add(itemInfo);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error("Lỗi khi đọc danh sách đồ trong PoE1 Shop", ex);
        }

        return result;
    }

    private static void CheckAndParseTimelessJewel(ShopItemInfo itemInfo, Mods modsComp)
    {
        // 1. Timeless Jewels MUST BE UNIQUE
        if (itemInfo.Rarity != ItemRarity.Unique)
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        var path = itemInfo.ItemPath ?? string.Empty;
        var name = itemInfo.Name ?? string.Empty;
        var baseName = itemInfo.BaseName ?? string.Empty;

        // 2. EXCLUDE Cluster Jewels (Large/Medium/Small Cluster, Voices, Megalomaniac)
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

        // 3. STRICT MATCH: Must match one of the 5 exact Timeless Jewels
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

        // 4. Must verify Timeless Jewel keyword / Historic modifier
        var hasHistoricOrTimelessMod = false;

        foreach (var stat in statsList)
        {
            if (string.IsNullOrWhiteSpace(stat)) continue;

            // Match seed numbers, e.g. "service of 2213 dekhara" or "15045 warriors" or "17814 verses"
            var seedMatch = Regex.Match(stat, @"(?:service of|commissioned|bathed in the blood of|chanted|carved to glorify|of)\s*(\d{2,6})\s*(?:dekhara|warriors|sacrificed|verses|victims|servants)?", RegexOptions.IgnoreCase);
            if (seedMatch.Success && int.TryParse(seedMatch.Groups[1].Value, out var seedVal))
            {
                if (seedVal > 0)
                {
                    itemInfo.TimelessSeed = seedVal;
                    hasHistoricOrTimelessMod = true;
                }
            }
            else
            {
                // Fallback 4-5 digit number match
                var fallbackMatch = Regex.Match(stat, @"\b(\d{3,6})\b");
                if (fallbackMatch.Success && int.TryParse(fallbackMatch.Groups[1].Value, out var fbSeed))
                {
                    if (fbSeed >= 100 && itemInfo.TimelessSeed == 0)
                    {
                        itemInfo.TimelessSeed = fbSeed;
                    }
                }
            }

            // Match leader name
            foreach (var leader in TimelessLeaders)
            {
                if (stat.Contains(leader, StringComparison.OrdinalIgnoreCase))
                {
                    itemInfo.TimelessLeader = leader;
                    hasHistoricOrTimelessMod = true;
                    break;
                }
            }

            if (stat.Contains("Historic", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Conquered by", StringComparison.OrdinalIgnoreCase) ||
                stat.Contains("Passives in radius", StringComparison.OrdinalIgnoreCase))
            {
                hasHistoricOrTimelessMod = true;
            }
        }

        if (hasHistoricOrTimelessMod || isExactTimeless)
        {
            itemInfo.IsTimelessJewel = true;
            itemInfo.BaseName = "Timeless Jewel";
        }
        else
        {
            itemInfo.IsTimelessJewel = false;
        }
    }

    private static void ParseCost(NormalInventoryItem invItem, ShopItemInfo itemInfo)
    {
        try
        {
            if (invItem.Children != null)
            {
                var costParts = new List<string>();
                foreach (var child in invItem.Children)
                {
                    if (child != null && child.IsValid && child.IsVisible && !string.IsNullOrWhiteSpace(child.Text))
                    {
                        var txt = child.Text.Trim();
                        if (txt.Contains("Divine", StringComparison.OrdinalIgnoreCase) ||
                            txt.Contains("Chaos", StringComparison.OrdinalIgnoreCase) ||
                            txt.Contains("Gold", StringComparison.OrdinalIgnoreCase) ||
                            txt.Contains("Alc", StringComparison.OrdinalIgnoreCase) ||
                            txt.Contains("Orb", StringComparison.OrdinalIgnoreCase) ||
                            txt.Contains("Cost:", StringComparison.OrdinalIgnoreCase))
                        {
                            costParts.Add(txt);
                        }
                    }
                }

                if (costParts.Count > 0)
                {
                    var fullCostStr = string.Join(", ", costParts);
                    itemInfo.CostString = fullCostStr;

                    if (itemInfo.Cost == null) itemInfo.Cost = new CurrencyCost();

                    // Parse Chaos Orb amount (e.g. "48x Chaos Orb" or "48 Chaos" or "10x Chaos")
                    var chaosMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Chaos", RegexOptions.IgnoreCase);
                    if (chaosMatch.Success && int.TryParse(chaosMatch.Groups[1].Value, out var chaosAmt))
                    {
                        itemInfo.Cost.CurrencyName = "Chaos Orb";
                        itemInfo.Cost.Amount = chaosAmt;
                    }

                    // Parse Divine Orb amount (e.g. "1x Divine Orb" or "5x Divine")
                    var divineMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Divine", RegexOptions.IgnoreCase);
                    if (divineMatch.Success && int.TryParse(divineMatch.Groups[1].Value, out var divAmt))
                    {
                        itemInfo.Cost.CurrencyName = "Divine Orb";
                        itemInfo.Cost.Amount = divAmt;
                    }

                    // Parse Gold amount (e.g. "4,480 Gold" or "10,920 Gold")
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
        }
        catch { }
    }

    public int GetTabCount(GameController gc)
    {
        try
        {
            if (gc == null) return 1;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            var purchaseWindow = (ingameUi?.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi?.PurchaseWindowHideout);
            if (purchaseWindow == null) return 1;

            var tabCount = purchaseWindow.TabContainer?.TotalStashes ?? 0L;
            return tabCount > 0 ? (int)tabCount : 1;
        }
        catch
        {
            return 1;
        }
    }

    public int GetCurrentTabIndex(GameController gc)
    {
        try
        {
            if (gc == null) return 0;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            var purchaseWindow = (ingameUi?.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi?.PurchaseWindowHideout);
            return (int)(purchaseWindow?.TabContainer?.VisibleStashIndex ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    public bool SwitchToTab(GameController gc, int tabIndex)
    {
        try
        {
            if (gc == null) return false;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            var purchaseWindow = (ingameUi?.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi?.PurchaseWindowHideout);
            if (purchaseWindow?.TabContainer == null) return false;

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
            return false;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Lỗi khi chuyển sang tab {tabIndex} trong PoE 1 Shop", ex);
            return false;
        }
    }

    private static string ParseBaseNameFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < path.Length - 1)
        {
            return path.Substring(lastSlash + 1).Replace('_', ' ');
        }
        return path;
    }
}
