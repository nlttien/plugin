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
            var ingameUi = gc?.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return true; // Fail-open to allow manual safety

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
            var ingameUi = gc?.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return 60;

            var invPanel = ingameUi.InventoryPanel;
            if (invPanel == null || !invPanel.IsValid) return 60;

            var invElement = invPanel[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];
            if (invElement == null || !invElement.IsValid) return 60;

            var items = invElement.VisibleInventoryItems;
            if (items == null) return 60;

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
        catch
        {
            return 60;
        }
    }

    public static IList<NormalInventoryItem> GetPlayerInventoryItems(GameController gc)
    {
        try
        {
            var ingameUi = gc?.Game?.IngameState?.IngameUi;
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
