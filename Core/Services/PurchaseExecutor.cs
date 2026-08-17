using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
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
            yield return new WaitTime(150);

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
                    yield return new WaitTime(MouseHelper.GetRandomDelay(200, 300));
                }

                var currentItems = adapter.GetAvailableItems(_gc);
                if (currentItems == null || currentItems.Count == 0)
                {
                    yield return new WaitTime(80);
                    currentItems = adapter.GetAvailableItems(_gc);
                    if (currentItems == null || currentItems.Count == 0) continue;
                }

                // Lấy danh sách item đạt chuẩn
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

                    // 2.5 KIỂM TRA LẠI GIÁ TRỰC TIẾP TỪ TOOLTIP SAU KHI RÊ CHUỘT
                    var liveCost = ReadLiveHoveredCost(_gc);
                    if (liveCost != null)
                    {
                        item.Cost = liveCost;
                        item.CostString = $"{liveCost.Amount} {liveCost.CurrencyName}";
                        if (!ItemFilterEngine.MatchesTimelessSettings(item, _settings))
                        {
                            LogHelper.Warn($"[BỎ QUA KHÔNG MUA] {item.DisplayName} vì giá không hợp lệ: {liveCost.Amount} {liveCost.CurrencyName}");
                            continue;
                        }
                    }

                    // 3. Thực hiện thao tác Ctrl + Left Click để mua
                    MouseHelper.CtrlLeftClick();

                    // Đợi server phản hồi và quét chủ động xem hộp thoại cảnh báo giá có xuất hiện không (trong 450ms)
                    var modalDetected = false;
                    for (var checkStep = 0; checkStep < 9; checkStep++)
                    {
                        yield return new WaitTime(50);
                        if (IsPriceDifferenceModalOpen(_gc))
                        {
                            modalDetected = true;
                            break;
                        }
                    }

                    // 4. BẤM NÚT [ OK ] ĐÚNG 1 LẦN KHI CÓ HỘP THOẠI
                    if (modalDetected || IsPriceDifferenceModalOpen(_gc))
                    {
                        LogHelper.Info("Phát hiện hộp thoại cảnh báo giá! Bấm [ OK ] ngay...");
                        yield return new WaitTime(50);
                        HandlePriceDifferenceModal(_gc, _settings);
                        
                        // Đợi hộp thoại đóng hoàn toàn
                        var waitCount = 0;
                        while (IsPriceDifferenceModalOpen(_gc) && waitCount < 8)
                        {
                            yield return new WaitTime(50);
                            waitCount++;
                        }
                    }

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
            // Ghi tín hiệu hoàn thành vào file cầu nối trade_bridge.json
            try
            {
                var bridgeFile = @"D:\codecuatien\trade_bridge.json";
                var statusStr = RequestStop ? "STOPPED" : "COMPLETED";
                var json = $"{{\"status\":\"{statusStr}\",\"items_bought\":{totalPurchasedCount},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
                File.WriteAllText(bridgeFile, json);
            }
            catch { }
        }
    }

    public static CurrencyCost? ReadLiveHoveredCost(GameController gc)
    {
        try
        {
            if (gc == null) return null;
            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return null;

            var costParts = new List<string>();

            if (ingameUi.ItemOnHover != null && ingameUi.ItemOnHover.IsValid)
            {
                Poe1ShopAdapter.ExtractCostTextRecursive(ingameUi.ItemOnHover, costParts, 0);
                if (ingameUi.ItemOnHover.ToolTip != null && ingameUi.ItemOnHover.ToolTip.IsValid)
                {
                    Poe1ShopAdapter.ExtractCostTextRecursive(ingameUi.ItemOnHover.ToolTip, costParts, 0);
                }
            }

            if (costParts.Count > 0)
            {
                var fullCostStr = string.Join(", ", costParts);
                var cost = new CurrencyCost();

                var divMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Divine", RegexOptions.IgnoreCase);
                if (divMatch.Success && int.TryParse(divMatch.Groups[1].Value, out var divAmt))
                {
                    cost.CurrencyName = "Divine Orb";
                    cost.Amount = divAmt;
                    return cost;
                }

                var chaosMatch = Regex.Match(fullCostStr, @"(\d+)\s*x?\s*Chaos", RegexOptions.IgnoreCase);
                if (chaosMatch.Success && int.TryParse(chaosMatch.Groups[1].Value, out var chaosAmt))
                {
                    cost.CurrencyName = "Chaos Orb";
                    cost.Amount = chaosAmt;
                    return cost;
                }
            }
        }
        catch { }

        return null;
    }

    public static bool IsPriceDifferenceModalOpen(GameController gc)
    {
        try
        {
            if (gc == null) return false;
            var ingameState = gc.IngameState ?? gc.Game?.IngameState;
            if (ingameState == null) return false;

            // 1. Quét IngameUi
            var ingameUi = ingameState.IngameUi;
            if (ingameUi != null && ingameUi.IsValid)
            {
                var d = FindPriceDifferenceDialogInMemory(ingameUi, 0);
                if (d != null && d.IsValid) return true;
            }

            // 2. Quét UIRoot
            var uiRoot = ingameState.UIRoot;
            if (uiRoot != null && uiRoot.IsValid)
            {
                var d = FindPriceDifferenceDialogInMemory(uiRoot, 0);
                if (d != null && d.IsValid) return true;
            }
        }
        catch { }

        return false;
    }

    public static void HandlePriceDifferenceModal(GameController gc, ShopAutoBuyerSettings? settings = null)
    {
        try
        {
            if (gc == null) return;
            if (!IsPriceDifferenceModalOpen(gc)) return;

            var realWinRect = gc.Window.GetWindowRectangleReal();
            if (realWinRect.Width <= 0 || realWinRect.Height <= 0)
            {
                realWinRect = gc.Window.GetWindowRectangle();
            }
            if (realWinRect.Width <= 0 || realWinRect.Height <= 0) return;

            var scaleX = realWinRect.Width / 1920f;
            var scaleY = realWinRect.Height / 1080f;
            var customX = settings?.OkButtonX?.Value ?? 750;
            var customY = settings?.OkButtonY?.Value ?? 575;

            var targetPos = new Vector2(realWinRect.Left + customX * scaleX, realWinRect.Top + customY * scaleY);

            // BẤM ĐÚNG 1 LẦN DUY NHẤT VÀO TÂM NÚT [ OK ] (ĐỢI 100ms ĐỂ HOVER RỒI CLICK)
            MouseHelper.LeftClickAt(targetPos, 100, 45);

            LogHelper.Info($"Đã bấm xác nhận nút [ OK ] tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HandlePriceDifferenceModal error: {ex.Message}");
        }
    }

    public static Element? FindPriceDifferenceDialogInMemory(Element? root, int depth)
    {
        if (root == null || !root.IsValid || depth > 25) return null;

        var txt = (root.Text ?? string.Empty).ToLowerInvariant();
        var txtNoTags = (root.TextNoTags ?? string.Empty).ToLowerInvariant();

        // Kiểm tra từ khóa hộp thoại cảnh báo giá
        if (txt.Contains("price differs") || txt.Contains("initially travelled") || txt.Contains("differs from") || txt.Contains("this item's price") || txt.Contains("differs") ||
            txtNoTags.Contains("price differs") || txtNoTags.Contains("initially travelled") || txtNoTags.Contains("differs from") || txtNoTags.Contains("this item's price") || txtNoTags.Contains("differs"))
        {
            return root.Parent ?? root;
        }

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                if (child != null && child.IsValid)
                {
                    var found = FindPriceDifferenceDialogInMemory(child, depth + 1);
                    if (found != null) return found;
                }
            }
        }

        return null;
    }
}
