using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        if (IsRunning)
        {
            LogHelper.Warn("Tiến trình mua đang thực hiện, vui lòng chờ.");
            yield break;
        }

        IsRunning = true;
        LogHelper.Info("=== Bắt đầu tiến trình tự động mua đồ trong Shop ===");

        var totalPurchasedCount = 0;

        try
        {
            var versionStr = _settings.GameVersion?.Value ?? "AutoDetect";
            var adapter = _adapterFactory.GetAdapter(_gc, versionStr);

            if (!adapter.IsShopOpen(_gc))
            {
                LogHelper.Warn("Cửa sổ Shop chưa được mở!");
                yield break;
            }

            var tabCount = _settings.ScanAllTabs.Value ? adapter.GetTabCount(_gc) : 1;
            var startTabIndex = _settings.ScanAllTabs.Value ? 0 : adapter.GetCurrentTabIndex(_gc);
            var endTabIndex = _settings.ScanAllTabs.Value ? tabCount : startTabIndex + 1;

            for (var tabIndex = startTabIndex; tabIndex < endTabIndex; tabIndex++)
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
                List<ShopItemInfo> matchingItems;
                if (_settings.OnlyBuyTimelessJewels?.Value == true)
                {
                    matchingItems = currentItems.Where(i => ItemFilterEngine.MatchesTimelessSettings(i, _settings)).ToList();
                }
                else
                {
                    var activeRules = _settings.GetActiveRules();
                    matchingItems = currentItems.Where(i => ItemFilterEngine.MatchesAnyRule(i, activeRules)).ToList();
                }

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

                    // 2. Di chuột đến vị trí item trong Shop
                    MouseHelper.MoveMouseWithJitter(item.ScreenRect);
                    yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));

                    // 3. Thực hiện thao tác Ctrl + Left Click để mua
                    MouseHelper.CtrlLeftClick();
                    totalPurchasedCount++;
                    LogHelper.Info($"[ĐÃ MUA] {item.DisplayName} (Giá: {item.CostString})");

                    // 4. Nghỉ ngơi giữa các lần bấm chuột để giống thao tác người thật
                    yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));
                }
            }

            LogHelper.Info($"=== Hoàn thành mua đồ! Tổng cộng đã mua: {totalPurchasedCount} vật phẩm. ===");
        }
        finally
        {
            IsRunning = false;
            // Write bridge status for web trade automation
            try
            {
                var bridgeFile = @"D:\codecuatien\trade_bridge.json";
                var json = $"{{\"status\":\"COMPLETED\",\"items_bought\":{totalPurchasedCount},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
                File.WriteAllText(bridgeFile, json);
            }
            catch { }
        }
    }
}
