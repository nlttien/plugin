using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ShopAutoBuyer.Core.Utils;

namespace ShopAutoBuyer.Core.Services;

public static class InventorySpaceChecker
{
    public const int InvColumns = 12;
    public const int InvRows = 5;
    public const int TotalSlots = InvColumns * InvRows; // 60 slots

    /// <summary>
    /// Tìm đúng ServerInventory của hành trang chính (Main Inventory 12x5 = 60 ô)
    /// </summary>
    public static ServerInventory? GetMainInventory(GameController gc)
    {
        try
        {
            var playerInvs = gc?.IngameState?.ServerData?.PlayerInventories;
            if (playerInvs == null || playerInvs.Count == 0) return null;

            foreach (var holder in playerInvs)
            {
                var inv = holder?.Inventory;
                if (inv == null) continue;

                // Khớp theo kích thước 12x5 của hành trang chính
                if (inv.Columns == InvColumns && inv.Rows == InvRows)
                {
                    return inv;
                }
            }

            return playerInvs[0]?.Inventory;
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"GetMainInventory error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Đếm số ô trống còn lại trong hành trang chính (từ 0 đến 60)
    /// </summary>
    public static int GetFreeSlotsCount(GameController gc)
    {
        try
        {
            // 1. Nguồn dữ liệu máy chủ ServerData.PlayerInventories (Chính xác 100% dù đóng hay mở UI)
            var mainInv = GetMainInventory(gc);
            if (mainInv != null)
            {
                var slotItems = mainInv.InventorySlotItems;
                if (slotItems != null)
                {
                    var occupied = 0;
                    foreach (var sItem in slotItems)
                    {
                        if (sItem != null)
                        {
                            var sx = Math.Max(1, sItem.SizeX);
                            var sy = Math.Max(1, sItem.SizeY);
                            occupied += sx * sy;
                        }
                    }
                    var free = Math.Max(0, TotalSlots - occupied);
                    return free;
                }
            }

            // 2. Dự phòng qua UI Element nếu InventoryPanel đang mở
            var ingameUi = gc?.IngameState?.IngameUi ?? gc?.Game?.IngameState?.IngameUi;
            if (ingameUi != null)
            {
                var invPanel = ingameUi.InventoryPanel;
                if (invPanel != null && invPanel.IsValid && invPanel.IsVisible)
                {
                    var invElement = invPanel[InventoryIndex.PlayerInventory];
                    if (invElement != null && invElement.IsValid)
                    {
                        var items = invElement.VisibleInventoryItems;
                        if (items != null && items.Count > 0)
                        {
                            var occupied = 0;
                            foreach (var invItem in items)
                            {
                                if (invItem != null && invItem.IsValid)
                                {
                                    occupied += Math.Max(1, invItem.ItemWidth) * Math.Max(1, invItem.ItemHeight);
                                }
                            }
                            return Math.Max(0, TotalSlots - occupied);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"GetFreeSlotsCount error: {ex.Message}");
        }

        return 60;
    }

    /// <summary>
    /// Kiểm tra xem còn khoảng trống hình chữ nhật (itemWidth x itemHeight) để nhét item vào không
    /// </summary>
    public static bool HasSpaceForItem(GameController gc, int itemWidth, int itemHeight)
    {
        try
        {
            var targetW = Math.Max(1, itemWidth);
            var targetH = Math.Max(1, itemHeight);
            var neededSlots = targetW * targetH;

            var freeSlots = GetFreeSlotsCount(gc);
            if (freeSlots < neededSlots) return false;

            var grid = new bool[InvColumns, InvRows];

            var mainInv = GetMainInventory(gc);
            if (mainInv != null)
            {
                var slotItems = mainInv.InventorySlotItems;
                if (slotItems != null && slotItems.Count > 0)
                {
                    foreach (var sItem in slotItems)
                    {
                        if (sItem == null) continue;
                        var sx = Math.Max(1, sItem.SizeX);
                        var sy = Math.Max(1, sItem.SizeY);
                        var px = sItem.PosX;
                        var py = sItem.PosY;

                        for (var x = px; x < px + sx && x < InvColumns; x++)
                        {
                            for (var y = py; y < py + sy && y < InvRows; y++)
                            {
                                if (x >= 0 && y >= 0) grid[x, y] = true;
                            }
                        }
                    }

                    // Tìm khoảng trống hình chữ nhật targetW x targetH trong grid
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
            }

            return freeSlots >= neededSlots;
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HasSpaceForItem error: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Lấy danh sách toàn bộ vật phẩm trong hành trang người chơi từ ServerData
    /// </summary>
    public static IList<ServerInventory.InventSlotItem> GetPlayerInventorySlotItems(GameController gc)
    {
        try
        {
            var mainInv = GetMainInventory(gc);
            return mainInv?.InventorySlotItems?.Where(i => i != null).ToList() ?? new List<ServerInventory.InventSlotItem>();
        }
        catch
        {
            return new List<ServerInventory.InventSlotItem>();
        }
    }

    public static IList<NormalInventoryItem> GetPlayerInventoryItems(GameController gc)
    {
        try
        {
            var ingameUi = gc?.IngameState?.IngameUi ?? gc?.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return new List<NormalInventoryItem>();

            var invPanel = ingameUi.InventoryPanel;
            if (invPanel == null || !invPanel.IsValid) return new List<NormalInventoryItem>();

            var invElement = invPanel[InventoryIndex.PlayerInventory];
            if (invElement == null || !invElement.IsValid) return new List<NormalInventoryItem>();

            return invElement.VisibleInventoryItems?.Where(i => i != null && i.IsValid).ToList() ?? new List<NormalInventoryItem>();
        }
        catch
        {
            return new List<NormalInventoryItem>();
        }
    }
}
