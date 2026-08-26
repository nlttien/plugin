using System;
using System.Collections.Generic;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;

namespace ShopAutoBuyer.Core.Services
{
    public static class InventorySpaceChecker
    {
        private const int InvColumns = 12;
        private const int InvRows = 5;

        public static IList<ServerInventory.InventSlotItem>? GetPlayerInventorySlotItems(GameController gc)
        {
            try
            {
                var playerInvs = gc.IngameState.ServerData?.PlayerInventories;
                if (playerInvs != null)
                {
                    foreach (var holder in playerInvs)
                    {
                        var inv = holder?.Inventory;
                        if (inv == null) continue;
                        if (inv.Columns == 12 || inv.InventSlot.ToString().Contains("Main", StringComparison.OrdinalIgnoreCase) || inv.InventSlot.ToString().Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (inv.InventorySlotItems != null)
                                return inv.InventorySlotItems;
                        }
                    }
                    if (playerInvs.Count > 0 && playerInvs[0].Inventory?.InventorySlotItems != null)
                        return playerInvs[0].Inventory.InventorySlotItems;
                }
            }
            catch { }
            return null;
        }

        public static IList<NormalInventoryItem>? GetPlayerInventoryItems(GameController gc)
        {
            try
            {
                var ingameUi = gc?.Game?.IngameState?.IngameUi ?? gc?.IngameState?.IngameUi;
                return ingameUi?.InventoryPanel?[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory]?.VisibleInventoryItems;
            }
            catch { return null; }
        }

        public static bool HasSpaceForItem(GameController gc, int itemWidth, int itemHeight)
        {
            try
            {
                var ingameUi = gc?.Game?.IngameState?.IngameUi ?? gc?.IngameState?.IngameUi;
                if (ingameUi == null) return true;

                var invPanel = ingameUi.InventoryPanel;
                if (invPanel == null || !invPanel.IsValid || !invPanel.IsVisible)
                {
                    return true;
                }

                var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
                if (invElement == null || !invElement.IsValid) return true;

                var items = invElement.VisibleInventoryItems;
                if (items == null) return true;

                var grid = new bool[InvColumns, InvRows];

                foreach (var invItem in items)
                {
                    if (invItem == null || !invItem.IsValid) continue;

                    var slotX = invItem.InventPosX;
                    var slotY = invItem.InventPosY;
                    var w = Math.Max(1, invItem.ItemWidth);
                    var h = Math.Max(1, invItem.ItemHeight);

                    for (var x = slotX; x < slotX + w && x < InvColumns; x++)
                    {
                        for (var y = slotY; y < slotY + h && y < InvRows; y++)
                        {
                            if (x >= 0 && y >= 0)
                            {
                                grid[x, y] = true;
                            }
                        }
                    }
                }

                var targetW = Math.Max(1, itemWidth);
                var targetH = Math.Max(1, itemHeight);

                for (var x = 0; x <= InvColumns - targetW; x++)
                {
                    for (var y = 0; y <= InvRows - targetH; y++)
                    {
                        var canFit = true;
                        for (var dx = 0; dx < targetW; dx++)
                        {
                            for (var dy = 0; dy < targetH; dy++)
                            {
                                if (grid[x + dx, y + dy])
                                {
                                    canFit = false;
                                    break;
                                }
                            }
                            if (!canFit) break;
                        }

                        if (canFit) return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        public static int GetFreeSlotsCount(GameController gc)
        {
            try
            {
                var items = GetPlayerInventorySlotItems(gc);
                if (items != null)
                {
                    int used = 0;
                    foreach (var it in items)
                    {
                        used += Math.Max(1, it.SizeX) * Math.Max(1, it.SizeY);
                    }
                    return Math.Max(0, (InvColumns * InvRows) - used);
                }

                var uiItems = GetPlayerInventoryItems(gc);
                if (uiItems != null)
                {
                    int used = 0;
                    foreach (var it in uiItems)
                    {
                        used += Math.Max(1, it.ItemWidth) * Math.Max(1, it.ItemHeight);
                    }
                    return Math.Max(0, (InvColumns * InvRows) - used);
                }
            }
            catch { }
            return 60;
        }
    }
}