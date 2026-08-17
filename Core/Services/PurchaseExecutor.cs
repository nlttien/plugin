using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.Shared;
using ShopAutoBuyer.Core.Adapters;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Utils;

namespace ShopAutoBuyer.Core.Services;

public class PurchaseExecutor
{
    private readonly GameController _gc;
    private readonly ShopAutoBuyerSettings _settings;
    private readonly ShopAdapterFactory _adapterFactory;

    public bool IsRunning { get; private set; }

    public PurchaseExecutor(GameController gc, ShopAutoBuyerSettings settings, ShopAdapterFactory adapterFactory)
    {
        _gc = gc;
        _settings = settings;
        _adapterFactory = adapterFactory;
    }

    public IEnumerator ExecutePurchaseCoroutine()
    {
        if (IsRunning) yield break;
        IsRunning = true;

        try
        {
            var adapter = _adapterFactory.GetAdapter(_gc, _settings.GameVersion.Value);
            if (!adapter.IsShopOpen(_gc))
            {
                LogHelper.Warn("Không thể mua: Cửa sổ Shop NPC chưa mở!");
                yield break;
            }

            LogHelper.Info($"Bắt đầu quét và mua đồ bằng [{adapter.AdapterName}]...");

            var processedTabIndices = new HashSet<int>();
            var totalPurchased = 0;

            var tabCount = _settings.ScanAllTabs.Value ? Math.Max(1, adapter.GetTabCount(_gc)) : 1;

            for (var tabIndex = 0; tabIndex < tabCount; tabIndex++)
            {
                if (!_settings.Enable.Value || !adapter.IsShopOpen(_gc))
                {
                    LogHelper.Info("Đã dừng tiến trình mua do đóng shop hoặc tắt plugin.");
                    yield break;
                }

                if (_settings.ScanAllTabs.Value && tabCount > 1)
                {
                    adapter.SwitchToTab(_gc, tabIndex);
                    yield return new WaitTime(MouseHelper.GetRandomDelay(250, 400));
                }

                var currentItems = adapter.GetAvailableItems(_gc);
                if (currentItems == null || currentItems.Count == 0)
                {
                    yield return new WaitTime(100);
                    continue;
                }

                // Get matching items
                var activeRules = _settings.GetActiveRules();
                var matchingItems = currentItems.Where(i => ItemFilterEngine.MatchesAnyRule(i, activeRules)).ToList();

                if (matchingItems.Count == 0)
                {
                    continue;
                }

                LogHelper.Info($"Tìm thấy {matchingItems.Count} vật phẩm đạt chuẩn trong Tab {tabIndex + 1}.");

                foreach (var item in matchingItems)
                {
                    if (!_settings.Enable.Value || !adapter.IsShopOpen(_gc)) yield break;

                    // 1. Kiểm tra ô trống hành trang
                    if (!InventorySpaceChecker.HasSpaceForItem(_gc, item.Width, item.Height))
                    {
                        LogHelper.Warn("Hành trang (Inventory) đã đầy! Dừng tự động mua.");
                        yield break;
                    }

                    // 2. Di chuyển chuột tới item với độ lệch ngẫu nhiên
                    MouseHelper.MoveMouseWithJitter(item.ScreenRect);
                    yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));

                    // 3. Thực hiện Ctrl + Click mua
                    MouseHelper.CtrlLeftClick();
                    totalPurchased++;
                    LogHelper.Info($"Đã mua: {item.BaseName} ({item.Rarity})");

                    // 4. Nghỉ ngẫu nhiên giữa các lần click
                    yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));
                }
            }

            LogHelper.Info($"Hoàn tất chu kỳ mua! Tổng số vật phẩm đã mua: {totalPurchased}.");
        }
        finally
        {
            IsRunning = false;
        }
    }
}
