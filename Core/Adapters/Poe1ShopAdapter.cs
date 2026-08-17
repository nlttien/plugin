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

public class Poe1ShopAdapter : IShopAdapter
{
    public string AdapterName => "Path of Exile 1 Shop Adapter";

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
                        // Fallback parsing from path
                        itemInfo.BaseName = ParseBaseNameFromPath(itemInfo.ItemPath);
                    }

                    // Mods Component
                    var modsComp = itemEntity.GetComponent<Mods>();
                    if (modsComp != null)
                    {
                        itemInfo.Rarity = modsComp.ItemRarity;
                        itemInfo.ItemLevel = modsComp.ItemLevel;
                        itemInfo.Name = modsComp.UniqueName ?? itemInfo.BaseName;
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
