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

                    // 4. Tự động kiểm tra & CLICK CHUẨN XÁC nút [ OK ] trên hộp thoại cảnh báo giá
                    HandlePriceDifferenceModal(_gc);
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

    public static void HandlePriceDifferenceModal(GameController gc)
    {
        try
        {
            if (gc == null) return;
            var winRect = gc.Window.GetWindowRectangle();
            if (winRect.Width <= 0 || winRect.Height <= 0) return;

            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            var scaleX = winRect.Width / 1920f;
            var scaleY = winRect.Height / 1080f;

            // Tọa độ CHUẨN XÁC 100% của nút [ OK ] tại trung tâm hộp thoại
            // Trong độ phân giải 1920x1080: Nút OK nằm chính xác tại X = 760, Y = 502 (Center.X - 200, Center.Y - 38)
            Vector2 targetOkPos;

            var priceDialog = ingameUi != null ? FindPriceDifferenceDialog(ingameUi) : null;
            if (priceDialog != null && priceDialog.IsValid && priceDialog.IsVisible)
            {
                var dialogRect = priceDialog.GetClientRect();
                if (dialogRect.Width > 200 && dialogRect.Height > 60)
                {
                    targetOkPos = new Vector2(
                        dialogRect.Left + dialogRect.Width * 0.195f,
                        dialogRect.Top + dialogRect.Height * 0.66f
                    );
                }
                else
                {
                    targetOkPos = new Vector2(
                        winRect.Center.X - (200f * scaleX),
                        winRect.Center.Y - (38f * scaleY)
                    );
                }
            }
            else
            {
                targetOkPos = new Vector2(
                    winRect.Center.X - (200f * scaleX),
                    winRect.Center.Y - (38f * scaleY)
                );
            }

            // Di chuyển chuột đến đúng tâm nút [ OK ] và Click dứt khoát
            MouseHelper.MoveMouse(targetOkPos);
            Thread.Sleep(35);
            MouseHelper.LeftClick();
            Thread.Sleep(35);
            MouseHelper.LeftClick();

            // Gửi phím hỗ trợ
            Input.KeyPress(Keys.Space);
            Input.KeyPress(Keys.Enter);

            LogHelper.Info($"Đã bấm xác nhận nút [ OK ] tại tọa độ ({targetOkPos.X:F0}, {targetOkPos.Y:F0})");
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HandlePriceDifferenceModal error: {ex.Message}");
        }
    }

    private static Element? FindPriceDifferenceDialog(Element? root)
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
