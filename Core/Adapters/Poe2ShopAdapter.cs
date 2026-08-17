using System;
using System.Collections.Generic;
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

public class Poe2ShopAdapter : IShopAdapter
{
    public string AdapterName => "Path of Exile 2 Shop Adapter";

    public bool IsShopOpen(GameController gc)
    {
        try
        {
            if (gc == null) return false;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return false;

            // Check standard Purchase windows or PoE 2 vendor panels
            if (ingameUi.PurchaseWindow?.IsVisible == true || ingameUi.PurchaseWindowHideout?.IsVisible == true)
                return true;

            // In PoE 2, some vendors use specialized elements or NPC dialogue subpanels
            if (ingameUi.Children != null)
            {
                foreach (var child in ingameUi.Children)
                {
                    if (child != null && child.IsValid && child.IsVisible)
                    {
                        var name = child.GetType().Name;
                        if (name.Contains("Merchant", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Vendor", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Shop", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"Poe2ShopAdapter.IsShopOpen error: {ex.Message}");
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

            // 1. Check primary purchase window
            var purchaseWindow = (ingameUi.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi.PurchaseWindowHideout);
            if (purchaseWindow != null && purchaseWindow.IsValid && purchaseWindow.IsVisible)
            {
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

                if (items != null)
                {
                    var currentTabIndex = GetCurrentTabIndex(gc);
                    foreach (var invItem in items)
                    {
                        if (invItem == null || !invItem.IsValid || !invItem.IsVisible) continue;
                        var itemInfo = ParseItem(invItem, currentTabIndex);
                        if (itemInfo != null)
                        {
                            result.Add(itemInfo);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error("Lỗi khi đọc danh sách đồ trong PoE 2 Shop", ex);
        }

        return result;
    }

    public int GetTabCount(GameController gc)
    {
        try
        {
            if (gc == null) return 1;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            var purchaseWindow = (ingameUi?.PurchaseWindow?.IsVisible == true ? ingameUi.PurchaseWindow : ingameUi?.PurchaseWindowHideout);
            var tabCount = purchaseWindow?.TabContainer?.TotalStashes ?? 0L;
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

            var tabList = purchaseWindow.TabContainer.TabSwitchBar;
            if (tabList != null && tabList.IsValid && tabList.Children != null && tabIndex < tabList.Children.Count)
            {
                var targetTab = tabList.Children[tabIndex];
                if (targetTab != null && targetTab.IsValid)
                {
                    MouseHelper.MoveMouseWithJitter(targetTab.GetClientRect());
                    MouseHelper.LeftClick();
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Lỗi chuyển tab trong PoE 2 Shop (Tab {tabIndex})", ex);
            return false;
        }
    }

    private static ShopItemInfo? ParseItem(NormalInventoryItem invItem, int tabIndex)
    {
        var clientRect = invItem.GetClientRect();
        if (clientRect.Width <= 0 || clientRect.Height <= 0) return null;

        var itemEntity = invItem.Item;
        var info = new ShopItemInfo
        {
            InventoryItem = invItem,
            ScreenRect = clientRect,
            ClickPosition = new Vector2(clientRect.Center.X, clientRect.Center.Y),
            TabIndex = tabIndex,
            SlotX = invItem.InventPosX,
            SlotY = invItem.InventPosY,
            Width = Math.Max(1, invItem.ItemWidth),
            Height = Math.Max(1, invItem.ItemHeight)
        };

        if (itemEntity != null && itemEntity.IsValid)
        {
            info.ItemPath = itemEntity.Path ?? string.Empty;

            var baseComp = itemEntity.GetComponent<Base>();
            info.BaseName = baseComp?.Name ?? ParseBaseName(info.ItemPath);

            var modsComp = itemEntity.GetComponent<Mods>();
            if (modsComp != null)
            {
                info.Rarity = modsComp.ItemRarity;
                info.ItemLevel = modsComp.ItemLevel;
                info.Name = modsComp.UniqueName ?? info.BaseName;
            }

            var socketsComp = itemEntity.GetComponent<Sockets>();
            if (socketsComp != null)
            {
                info.Sockets = socketsComp.NumberOfSockets;
                info.Links = socketsComp.LargestLinkSize;
                info.IsRgb = socketsComp.IsRGB;
            }

            var qualityComp = itemEntity.GetComponent<Quality>();
            if (qualityComp != null)
            {
                info.Quality = qualityComp.ItemQuality;
            }

            // In PoE 2, items may have Gold cost
            info.Cost = new CurrencyCost
            {
                IsGold = true,
                Amount = 0
            };
        }
        else
        {
            info.BaseName = "PoE 2 Item";
        }

        return info;
    }

    private static string ParseBaseName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Item";
        var idx = path.LastIndexOf('/');
        return idx >= 0 && idx < path.Length - 1 ? path.Substring(idx + 1).Replace('_', ' ') : path;
    }
}
