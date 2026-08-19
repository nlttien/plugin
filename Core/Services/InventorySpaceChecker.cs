using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ShopAutoBuyer.Core.Utils;

namespace ShopAutoBuyer.Core.Services;

public static class InventorySpaceChecker
{
    private const int InvColumns = 12;
    private const int InvRows = 5;

    public static bool HasSpaceForItem(GameController gc, int itemWidth, int itemHeight)
    {
        try
        {
            var freeSlots = GetFreeSlotsCount(gc);
            var neededSlots = Math.Max(1, itemWidth) * Math.Max(1, itemHeight);
            if (freeSlots < neededSlots) return false;

            var ingameUi = gc?.IngameState?.IngameUi ?? gc?.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return freeSlots >= neededSlots;

            var invPanel = ingameUi.InventoryPanel;
            if (invPanel == null || !invPanel.IsValid)
            {
                return freeSlots >= neededSlots;
            }

            var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
            if (invElement == null || !invElement.IsValid) return freeSlots >= neededSlots;

            var items = invElement.VisibleInventoryItems;
            if (items == null) return freeSlots >= neededSlots;

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

            // Find free rectangle of size itemWidth x itemHeight
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
        catch (Exception ex)
        {
            LogHelper.Debug($"InventorySpaceChecker error: {ex.Message}");
            return true;
        }
    }

    public static int GetFreeSlotsCount(GameController gc)
    {
        try
        {
            var ingameUi = gc?.IngameState?.IngameUi ?? gc?.Game?.IngameState?.IngameUi;
            if (ingameUi != null)
            {
                var invPanel = ingameUi.InventoryPanel;
                if (invPanel != null && invPanel.IsValid)
                {
                    var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
                    if (invElement != null && invElement.IsValid)
                    {
                        var items = invElement.VisibleInventoryItems;
                        if (items != null)
                        {
                            var occupied = 0;
                            foreach (var invItem in items)
                            {
                                if (invItem != null && invItem.IsValid)
                                {
                                    occupied += Math.Max(1, invItem.ItemWidth) * Math.Max(1, invItem.ItemHeight);
                                }
                            }
                            return Math.Max(0, (InvColumns * InvRows) - occupied);
                        }
                    }
                }
            }

            // Fallback: check ServerData PlayerInventories
            var serverInventories = gc?.IngameState?.ServerData?.PlayerInventories;
            if (serverInventories != null)
            {
                foreach (var sInv in serverInventories)
                {
                    if (sInv.Inventory?.InventType == ExileCore.Shared.Enums.InventoryTypeE.Main)
                    {
                        var items = sInv.Inventory.Items;
                        if (items != null)
                        {
                            var occupied = 0;
                            foreach (var it in items)
                            {
                                if (it != null && it.IsValid)
                                {
                                    occupied += Math.Max(1, it.ItemWidth) * Math.Max(1, it.ItemHeight);
                                }
                            }
                            return Math.Max(0, (InvColumns * InvRows) - occupied);
                        }
                    }
                }
            }
        }
        catch { }

        return 60;
    }

    public static IList<NormalInventoryItem> GetPlayerInventoryItems(GameController gc)
    {
        try
        {
            var ingameUi = gc?.IngameState?.IngameUi ?? gc?.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return new List<NormalInventoryItem>();

            var invPanel = ingameUi.InventoryPanel;
            if (invPanel == null || !invPanel.IsValid) return new List<NormalInventoryItem>();

            var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
            if (invElement == null || !invElement.IsValid) return new List<NormalInventoryItem>();

            return invElement.VisibleInventoryItems?.Where(i => i != null && i.IsValid).ToList() ?? new List<NormalInventoryItem>();
        }
        catch
        {
            return new List<NormalInventoryItem>();
        }
    }
}
