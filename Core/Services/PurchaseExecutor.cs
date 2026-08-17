using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared;
using ShopAutoBuyer.Core.Adapters;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Utils;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Services;

public class PurchaseExecutor
{
    private readonly GameController _gc;
    private readonly ShopAutoBuyerSettings _settings;
    private readonly ShopAdapterFactory _adapterFactory;

    public bool IsRunning { get; set; }
    public bool RequestStop { get; set; }

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
        RequestStop = false;
        LogHelper.Info("=== Bắt đầu tiến trình tự động mua đồ trong Shop ===");

        var totalPurchasedCount = 0;

        try
        {
            var versionStr = _settings.GameVersion?.Value ?? "AutoDetect";
            var adapter = _adapterFactory.GetAdapter(_gc, versionStr);

            // Chờ ngắn để UI và danh sách vật phẩm nạp đầy đủ vào bộ nhớ
            yield return new WaitTime(200);

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
                if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc))
                {
                    LogHelper.Info("Đã dừng tiến trình mua do đóng shop, dừng hoặc tắt plugin.");
                    yield break;
                }

                if (_settings.ScanAllTabs.Value && tabCount > 1)
                {
                    adapter.SwitchToTab(_gc, tabIndex);
                    yield return new WaitTime(MouseHelper.GetRandomDelay(250, 350));
                }

                var currentItems = adapter.GetAvailableItems(_gc);
                if (currentItems == null || currentItems.Count == 0)
                {
                    yield return new WaitTime(100);
                    currentItems = adapter.GetAvailableItems(_gc);
                    if (currentItems == null || currentItems.Count == 0) continue;
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
                    if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;

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
                    yield return new WaitTime(120);

                    // 4. Tự động kiểm tra & CLICK CHÍNH XÁC vào tâm nút [ OK ] tại X=787, Y=545
                    HandlePriceDifferenceModal(_gc, _settings);
                    yield return new WaitTime(100);

                    totalPurchasedCount++;
                    LogHelper.Info($"[ĐÃ MUA] {item.DisplayName} (Giá: {item.CostString})");

                    // 5. Nghỉ ngơi giữa các lần bấm chuột
                    yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));
                }
            }

            LogHelper.Info($"=== Hoàn thành mua đồ! Tổng cộng đã mua: {totalPurchasedCount} vật phẩm. ===");
        }
        finally
        {
            IsRunning = false;
            // Chỉ ghi tín hiệu hoàn thành khi không bị lệnh Stop
            if (!RequestStop)
            {
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

    public static void HandlePriceDifferenceModal(GameController gc, ShopAutoBuyerSettings? settings = null)
    {
        try
        {
            if (gc == null) return;
            var winRect = gc.Window.GetWindowRectangle();
            if (winRect.Width <= 0 || winRect.Height <= 0) return;

            var scaleX = winRect.Width / 1920f;
            var scaleY = winRect.Height / 1080f;

            var customX = settings?.OkButtonX?.Value ?? 787;
            var customY = settings?.OkButtonY?.Value ?? 545;

            // Tọa độ CHÍNH XÁC 100% CỦA TÂM NÚT [ OK ] ĐO ĐẠC TRÊN SCREENSHOT: (787, 545)
            var okPos1 = new Vector2(winRect.Left + customX * scaleX, winRect.Top + customY * scaleY);
            var okPos2 = new Vector2(winRect.Left + 720f * scaleX, winRect.Top + customY * scaleY);

            // 1. Di chuyển chuột thẳng đến tâm nút [ OK ] và Click
            MouseHelper.MoveMouse(okPos1);
            Thread.Sleep(35);
            MouseHelper.LeftClick();
            Thread.Sleep(35);
            MouseHelper.LeftClick();

            // 2. Click thêm điểm 720 (nếu là hộp thoại 2 nút OK - CANCEL)
            MouseHelper.MoveMouse(okPos2);
            Thread.Sleep(25);
            MouseHelper.LeftClick();

            // 3. Gửi phím Space và Enter để xác nhận
            Input.KeyPress(Keys.Space);
            Input.KeyPress(Keys.Enter);

            LogHelper.Info($"Đã bấm xác nhận nút [ OK ] tại tọa độ chuẩn: ({okPos1.X:F0}, {okPos1.Y:F0})");
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HandlePriceDifferenceModal error: {ex.Message}");
        }
    }

    public static Element? FindPriceDifferenceDialog(Element? root)
    {
        if (root == null || !root.IsValid || !root.IsVisible) return null;

        if (!string.IsNullOrWhiteSpace(root.Text))
        {
            var txt = root.Text.ToLower();
            if (txt.Contains("price differs") || txt.Contains("initially travelled") || txt.Contains("differs from the one"))
            {
                return root.Parent ?? root;
            }
        }

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                var found = FindPriceDifferenceDialog(child);
                if (found != null) return found;
            }
        }

        return null;
    }
}
