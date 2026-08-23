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
using ShopAutoBuyer.Core.Services;
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
                    Width = Math.Max(1, invItem.ItemWidth),
                    Height = Math.Max(1, invItem.ItemHeight)
                };

                if (itemEntity != null && itemEntity.IsValid)
                {
                    itemInfo.ItemPath = itemEntity.Path ?? string.Empty;

                    // Sockets Component
                    var socketsComp = itemEntity.GetComponent<Sockets>();
                    if (socketsComp != null)
                    {
                        itemInfo.Sockets = socketsComp.NumberOfSockets;
                        itemInfo.Links = socketsComp.LargestLinkSize;
                        itemInfo.IsRgb = socketsComp.IsRGB;
                    }

                    // Base Component
                    var baseComp = itemEntity.GetComponent<Base>();
                    if (baseComp != null && !string.IsNullOrWhiteSpace(baseComp.Name))
                    {
                        itemInfo.BaseName = baseComp.Name;
                    }
                    else
                    {
                        itemInfo.BaseName = ParseBaseNameFromPath(itemInfo.ItemPath);
                    }

                    // Tự động map tên chuẩn cho Eldritch / Boss Invitations nếu chưa đúng
                    var mappedInvitation = ParseBaseNameFromPath(itemInfo.ItemPath);
                    if (!string.IsNullOrEmpty(mappedInvitation) && mappedInvitation.Contains("Invitation", StringComparison.OrdinalIgnoreCase))
                    {
                        itemInfo.BaseName = mappedInvitation;
                    }

                    // Mods Component
                    var modsComp = itemEntity.GetComponent<Mods>();
                    if (modsComp != null)
                    {
                        itemInfo.Rarity = modsComp.ItemRarity;
                        itemInfo.ItemLevel = modsComp.ItemLevel;
                        itemInfo.Name = !string.IsNullOrWhiteSpace(modsComp.UniqueName) ? modsComp.UniqueName : itemInfo.BaseName;

                        // Check Timeless Jewel identification strictly
                        CheckAndParseTimelessJewel(itemInfo, modsComp);
                    }
                    else
                    {
                        itemInfo.Name = itemInfo.BaseName;
                    }

                    // Quality Component
                    var qualityComp = itemEntity.GetComponent<Quality>();
                    if (qualityComp != null)
                    {
                        itemInfo.Quality = qualityComp.ItemQuality;
                    }

                    // LẤY GIÁ TỪ RAM QUA ĐỊA CHỈ CON TRỎ BỘ NHỚ DUY NHẤT (Address) - TUYỆT ĐỐI KHÔNG BỊ TRÙNG LẶP
                    if (PurchaseExecutor.ScannedPriceCache.TryGetValue(invItem.Address, out var cachedCost))
                    {
                        itemInfo.Cost = cachedCost;
                        itemInfo.CostString = $"{cachedCost.Amount} {cachedCost.CurrencyName}";
                    }
                    else
                    {
                        // Parse item cost
                        ParseCost(invItem, itemInfo);
                    }
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

        // 1. KÍCH THƯỚC Ô LƯỚI: JEWEL BẮT BUỘC LÀ 1x1 VÀ KHÔNG CÓ SOCKETS
        if (itemInfo.Width != 1 || itemInfo.Height != 1 || itemInfo.Sockets > 0 || itemInfo.Links > 0)
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        // 2. NGỌC TIMELESS KHÔNG BAO GIỜ CÓ SOCKETS HOẶC LINKS
        if (itemInfo.Sockets > 0 || itemInfo.Links > 0)
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        var name = itemInfo.Name ?? string.Empty;
        var baseName = itemInfo.BaseName ?? string.Empty;
        var path = itemInfo.ItemPath ?? string.Empty;

        // 3. Phải là Unique Rarity
        if (itemInfo.Rarity != ItemRarity.Unique)
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        // 4. LOẠI TRỪ 100% CÁC TRANG BỊ KHÔNG PHẢI JEWEL (Áo giáp, Vũ khí, Nhẫn, Dây chuyền, v.v.)
        if (path.Contains("Armour", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BodyArmour", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Weapon", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Amulet", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Ring", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Belt", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Flask", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Cluster", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Armour", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Garb", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Robe", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Chest", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Vest", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Amulet", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Ring", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Belt", StringComparison.OrdinalIgnoreCase) ||
            baseName.Contains("Cluster", StringComparison.OrdinalIgnoreCase))
        {
            itemInfo.IsTimelessJewel = false;
            return;
        }

        // 5. STRICT MATCH: Bắt buộc phải là 1 trong 5 loại Timeless Jewel chuẩn
        var isExactName = name.Equals("Brutal Restraint", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("Glorious Vanity", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("Lethal Pride", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("Militant Faith", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("Elegant Hubris", StringComparison.OrdinalIgnoreCase);

        var isExactPath = path.Contains("JewelPassiveTreeExpansionMaraketh", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("JewelPassiveTreeExpansionVaal", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("JewelPassiveTreeExpansionKarui", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("JewelPassiveTreeExpansionTemplar", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("JewelPassiveTreeExpansionEternalEmpire", StringComparison.OrdinalIgnoreCase);

        if (!isExactName && !isExactPath)
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

        // 6. Kiểm tra Historic mod / Seed
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
                }
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

        itemInfo.IsTimelessJewel = true;
    }

    public static void ParseCost(NormalInventoryItem invItem, ShopItemInfo itemInfo)
    {
        try
        {
            var costParts = new List<string>();
            ExtractCostTextRecursive(invItem, costParts, 0);

            if (invItem.Tooltip != null && invItem.Tooltip.IsValid)
            {
                ExtractCostTextRecursive(invItem.Tooltip, costParts, 0);
            }

            ParseCostFromTexts(costParts, itemInfo);
        }
        catch { }
    }

    public static void ParseCostFromTexts(List<string> texts, ShopItemInfo itemInfo)
    {
        try
        {
            if (texts == null || texts.Count == 0 || itemInfo == null) return;
            var fullCostStr = string.Join(" ", texts);

            if (itemInfo.Cost == null) itemInfo.Cost = new CurrencyCost();

            // 1. Kiểm tra Divine Orb
            if (fullCostStr.Contains("Divine", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.Cost.CurrencyName = "Divine Orb";
                var divMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Divine", RegexOptions.IgnoreCase);
                if (!divMatch.Success) divMatch = Regex.Match(fullCostStr, @"Divine\s*(?:Orb)?\s*x?\s*(\d+)", RegexOptions.IgnoreCase);

                itemInfo.Cost.Amount = (divMatch.Success && int.TryParse(divMatch.Groups[1].Value, out var divAmt)) ? divAmt : 1;
                itemInfo.CostString = $"{itemInfo.Cost.Amount} Divine Orb";
            }
            // 2. Kiểm tra Chaos Orb
            else if (fullCostStr.Contains("Chaos", StringComparison.OrdinalIgnoreCase))
            {
                itemInfo.Cost.CurrencyName = "Chaos Orb";
                var chaosMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Chaos", RegexOptions.IgnoreCase);
                if (!chaosMatch.Success) chaosMatch = Regex.Match(fullCostStr, @"Chaos\s*(?:Orb)?\s*x?\s*(\d+)", RegexOptions.IgnoreCase);
                if (!chaosMatch.Success) chaosMatch = Regex.Match(fullCostStr, @"Cost:\s*(\d+)", RegexOptions.IgnoreCase);
                if (!chaosMatch.Success) chaosMatch = Regex.Match(fullCostStr, @"(\d+)", RegexOptions.IgnoreCase);

                itemInfo.Cost.Amount = (chaosMatch.Success && int.TryParse(chaosMatch.Groups[1].Value, out var chaosAmt)) ? chaosAmt : 1;
                itemInfo.CostString = $"{itemInfo.Cost.Amount} Chaos Orb";
            }

            // 3. Parse Gold amount
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
        catch { }
    }

    public static void ExtractCostTextRecursive(Element? element, List<string> costParts, int depth)
    {
        if (element == null || !element.IsValid || depth > 15) return;

        var txtNoTags = (element.TextNoTags ?? string.Empty).Trim();
        var txt = (element.Text ?? string.Empty).Trim();

        var str = !string.IsNullOrWhiteSpace(txtNoTags) ? txtNoTags : txt;

        if (!string.IsNullOrWhiteSpace(str) && !costParts.Contains(str))
        {
            costParts.Add(str);
        }

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

    public static string ParseBaseNameFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;

        // Eldritch / Boss Invitations specific mapping
        if (path.Contains("ExarchInvitation", StringComparison.OrdinalIgnoreCase) || 
            path.Contains("Incandescent", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BossKeys/ExarchKey", StringComparison.OrdinalIgnoreCase))
        {
            return "Incandescent Invitation";
        }
        if (path.Contains("EaterInvitation", StringComparison.OrdinalIgnoreCase) || 
            path.Contains("Screaming", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BossKeys/EaterKey", StringComparison.OrdinalIgnoreCase))
        {
            return "Screaming Invitation";
        }
        if (path.Contains("BlackStar", StringComparison.OrdinalIgnoreCase) || 
            path.Contains("Polaric", StringComparison.OrdinalIgnoreCase))
        {
            return "Polaric Invitation";
        }
        if (path.Contains("Hunger", StringComparison.OrdinalIgnoreCase) || 
            path.Contains("Writhing", StringComparison.OrdinalIgnoreCase))
        {
            return "Writhing Invitation";
        }
        if (path.Contains("MavenInvitation", StringComparison.OrdinalIgnoreCase))
        {
            return "Maven's Invitation";
        }

        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }
}
