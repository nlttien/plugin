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

                    // 4. Tự động kiểm tra & CLICK THEO ĐÚNG ĐỊA CHỈ BỘ NHỚ CỦA NÚT [ OK ]
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
            var ingameState = gc.IngameState ?? gc.Game?.IngameState;
            if (ingameState == null) return;

            var ingameUi = ingameState.IngameUi;
            Vector2 targetPos = Vector2.Zero;
            bool foundFromMemory = false;

            // 1. TÌM TRỰC TIẾP TỪ BỘ NHỚ: IngameUi.TwoButtonWindow.TwoButtonWindowOk
            try
            {
                var twoBtn = ingameUi?.TwoButtonWindow;
                if (twoBtn != null && twoBtn.IsValid && twoBtn.IsVisible)
                {
                    var twoBtnOk = twoBtn.TwoButtonWindowOk;
                    if (twoBtnOk != null && twoBtnOk.IsValid && twoBtnOk.IsVisible)
                    {
                        var btnRect = twoBtnOk.GetClientRect();
                        if (btnRect.Width > 10 && btnRect.Height > 10)
                        {
                            targetPos = new Vector2(btnRect.Center.X, btnRect.Center.Y);
                            foundFromMemory = true;
                            LogHelper.Info($"[Bộ Nhớ] Đã tìm thấy nút OK từ TwoButtonWindow tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
                        }
                    }
                }
            }
            catch { }

            // 2. TÌM TRỰC TIẾP TỪ BỘ NHỚ: Quét cây IngameUi / UIRoot tìm hộp thoại 'price differs'
            if (!foundFromMemory && ingameUi != null)
            {
                try
                {
                    var dialogElement = FindPriceDifferenceDialogInMemory(ingameUi);
                    if (dialogElement == null && ingameState.UIRoot != null)
                    {
                        dialogElement = FindPriceDifferenceDialogInMemory(ingameState.UIRoot);
                    }

                    if (dialogElement != null && dialogElement.IsValid && dialogElement.IsVisible)
                    {
                        // Tìm nút OK con bên trong hộp thoại
                        var okChild = FindOkChildButtonInMemory(dialogElement);
                        if (okChild != null && okChild.IsValid && okChild.IsVisible)
                        {
                            var r = okChild.GetClientRect();
                            if (r.Width > 10 && r.Height > 10)
                            {
                                targetPos = new Vector2(r.Center.X, r.Center.Y);
                                foundFromMemory = true;
                                LogHelper.Info($"[Bộ Nhớ] Đã tìm thấy nút OK con bên trong hộp thoại tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
                            }
                        }

                        if (!foundFromMemory)
                        {
                            var dRect = dialogElement.GetClientRect();
                            if (dRect.Width > 100 && dRect.Height > 40)
                            {
                                targetPos = new Vector2(dRect.Center.X, dRect.Top + dRect.Height * 0.72f);
                                foundFromMemory = true;
                                LogHelper.Info($"[Bộ Nhớ] Tính tọa độ nút OK theo khung hộp thoại bộ nhớ tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. FALLBACK: Tính theo tỉ lệ khung hình cửa sổ Game nếu bộ nhớ chưa đọc kịp
            if (!foundFromMemory)
            {
                var winRect = gc.Window.GetWindowRectangle();
                if (winRect.Width > 0 && winRect.Height > 0)
                {
                    var scaleX = winRect.Width / 1920f;
                    var scaleY = winRect.Height / 1080f;
                    var customX = settings?.OkButtonX?.Value ?? 787;
                    var customY = settings?.OkButtonY?.Value ?? 545;

                    targetPos = new Vector2(winRect.Left + customX * scaleX, winRect.Top + customY * scaleY);
                }
            }

            if (targetPos != Vector2.Zero)
            {
                MouseHelper.MoveMouse(targetPos);
                Thread.Sleep(35);
                MouseHelper.LeftClick();
                Thread.Sleep(35);
                MouseHelper.LeftClick();

                Input.KeyPress(Keys.Space);
                Input.KeyPress(Keys.Enter);

                LogHelper.Info($"Đã bấm xác nhận nút [ OK ] tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HandlePriceDifferenceModal error: {ex.Message}");
        }
    }

    public static Element? FindPriceDifferenceDialogInMemory(Element? root, int depth = 0)
    {
        if (root == null || !root.IsValid || !root.IsVisible || depth > 20) return null;

        var txt = (root.Text ?? string.Empty).ToLower();
        var txtNoTags = (root.TextNoTags ?? string.Empty).ToLower();

        if (txt.Contains("price differs") || txt.Contains("initially travelled") || txt.Contains("differs from") ||
            txtNoTags.Contains("price differs") || txtNoTags.Contains("initially travelled") || txtNoTags.Contains("differs from"))
        {
            return root.Parent ?? root;
        }

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                var found = FindPriceDifferenceDialogInMemory(child, depth + 1);
                if (found != null) return found;
            }
        }

        return null;
    }

    public static Element? FindOkChildButtonInMemory(Element? root, int depth = 0)
    {
        if (root == null || !root.IsValid || !root.IsVisible || depth > 8) return null;

        var txt = (root.Text ?? string.Empty).Trim();
        var txtNoTags = (root.TextNoTags ?? string.Empty).Trim();

        if (txt.Equals("OK", StringComparison.OrdinalIgnoreCase) || 
            txt.Equals("ACCEPT", StringComparison.OrdinalIgnoreCase) ||
            txtNoTags.Equals("OK", StringComparison.OrdinalIgnoreCase) || 
            txtNoTags.Equals("ACCEPT", StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                var found = FindOkChildButtonInMemory(child, depth + 1);
                if (found != null) return found;
            }
        }

        return null;
    }
}
