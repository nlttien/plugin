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
        LogHelper.Info("=== Bắt đầu tiến trình tự động lướt quét tất cả ô đồ trong Shop ===");

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

                // LỌC CHÍNH XÁC: CHỈ LẤY CÁC MÓN TIMELESS JEWEL ĐỦ ĐIỀU KIỆN (KHÔNG LIA VÀO ĐỒ KHÁC)
                List<ShopItemInfo> itemsToScan;
                if (_settings.OnlyBuyTimelessJewels?.Value == true)
                {
                    itemsToScan = currentItems
                        .Where(i => i != null && i.IsTimelessJewel && ItemFilterEngine.MatchesTimelessCandidate(i, _settings))
                        .OrderBy(i => i.SlotY)
                        .ThenBy(i => i.SlotX)
                        .ToList();
                }
                else
                {
                    var activeRules = _settings.GetActiveRules();
                    itemsToScan = currentItems
                        .Where(i => i != null && ItemFilterEngine.MatchesAnyRule(i, activeRules))
                        .OrderBy(i => i.SlotY)
                        .ThenBy(i => i.SlotX)
                        .ToList();
                }

                if (itemsToScan.Count == 0) continue;

                LogHelper.Info($"Tìm thấy {itemsToScan.Count} viên Timeless Jewel hợp điều kiện trong Tab {tabIndex + 1}. Bắt đầu lia chuột quét giá và mua...");

                foreach (var item in itemsToScan)
                {
                    if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;

                    // 1. TỰ ĐỘNG DI CHUỘT LƯỚT QUA Ô NGỌC TIMELESS ĐỂ NẠP DỮ LIỆU TOOLTIP VÀ GIÁ
                    MouseHelper.MoveMouseWithJitter(item.ScreenRect, 8f);
                    yield return new WaitTime(MouseHelper.GetRandomDelay(60, 90));

                    // 2. Cập nhật dữ liệu giá, tên, tướng, seed từ Tooltip vừa hiển thị
                    UpdateItemFromLiveHover(_gc, item);

                    // 4. KIỂM TRA GIÁ: CHỈ MUA KHI GIÁ LÀ CHAOS (10 - 50c), TỪ CHỐI 100% MÓN GIÁ DIVINE ORB
                    if (!ItemFilterEngine.MatchesTimelessSettings(item, _settings))
                    {
                        LogHelper.Warn($"[BỎ QUA] {item.DisplayName} vì giá không hợp lệ: {item.CostString}");
                        continue; // Bỏ qua không bấm mua!
                    }

                    // 5. Kiểm tra ô trống hành trang trước khi mua
                    if (!InventorySpaceChecker.HasSpaceForItem(_gc, item.Width, item.Height))
                    {
                        LogHelper.Warn("Hành trang (Inventory) đã đầy! Dừng tự động mua.");
                        yield break;
                    }

                    // 6. THỰC HIỆN BẤM MUA NGAY LẬP TỨC (Ctrl + Left Click)
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

                    // 7. BẤM NÚT [ OK ] ĐÚNG 1 LẦN KHI CÓ HỘP THOẠI CẢNH BÁO GIÁ
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

                    // 8. Nghỉ ngơi giữa các lần mua
                    yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));
                }
            }

            LogHelper.Info($"=== Hoàn thành quét & mua đồ! Tổng cộng đã mua: {totalPurchasedCount} vật phẩm. ===");
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

    public static void UpdateItemFromLiveHover(GameController gc, ShopItemInfo item)
    {
        try
        {
            if (gc == null || item == null) return;
            var ingameState = gc.IngameState ?? gc.Game?.IngameState;
            if (ingameState == null) return;

            var texts = new List<string>();

            if (ingameState.UIHover != null && ingameState.UIHover.IsValid)
            {
                Poe1ShopAdapter.ExtractCostTextRecursive(ingameState.UIHover, texts, 0);
            }
            if (ingameState.UIHoverTooltip != null && ingameState.UIHoverTooltip.IsValid)
            {
                Poe1ShopAdapter.ExtractCostTextRecursive(ingameState.UIHoverTooltip, texts, 0);
            }
            if (ingameState.UIHoverElement != null && ingameState.UIHoverElement.IsValid)
            {
                Poe1ShopAdapter.ExtractCostTextRecursive(ingameState.UIHoverElement, texts, 0);
            }

            if (texts.Count > 0)
            {
                var fullStr = string.Join(" ", texts);

                // 1. Cập nhật Cost
                var cost = new CurrencyCost();
                if (fullStr.Contains("Divine", StringComparison.OrdinalIgnoreCase))
                {
                    cost.CurrencyName = "Divine Orb";
                    var divMatch = Regex.Match(fullStr, @"(\d+)\s*x?\s*Divine", RegexOptions.IgnoreCase);
                    if (!divMatch.Success) divMatch = Regex.Match(fullStr, @"Divine\s*(?:Orb)?\s*x?\s*(\d+)", RegexOptions.IgnoreCase);
                    cost.Amount = (divMatch.Success && int.TryParse(divMatch.Groups[1].Value, out var divAmt)) ? divAmt : 1;
                    item.Cost = cost;
                    item.CostString = $"{cost.Amount} Divine Orb";
                }
                else if (fullStr.Contains("Chaos", StringComparison.OrdinalIgnoreCase))
                {
                    cost.CurrencyName = "Chaos Orb";
                    var chaosMatch = Regex.Match(fullStr, @"(\d+)\s*x?\s*Chaos", RegexOptions.IgnoreCase);
                    if (!chaosMatch.Success) chaosMatch = Regex.Match(fullStr, @"Chaos\s*(?:Orb)?\s*x?\s*(\d+)", RegexOptions.IgnoreCase);
                    cost.Amount = (chaosMatch.Success && int.TryParse(chaosMatch.Groups[1].Value, out var chaosAmt)) ? chaosAmt : 1;
                    item.Cost = cost;
                    item.CostString = $"{cost.Amount} Chaos Orb";
                }

                // 2. Cập nhật Gold nếu có
                var goldMatch = Regex.Match(fullStr, @"([\d,]+)\s*Gold", RegexOptions.IgnoreCase);
                if (goldMatch.Success)
                {
                    var goldDigits = goldMatch.Groups[1].Value.Replace(",", "");
                    if (int.TryParse(goldDigits, out var goldAmt) && item.Cost != null)
                    {
                        item.Cost.GoldAmount = goldAmt;
                    }
                }

                // 3. Cập nhật Seed nếu chưa có
                if (item.TimelessSeed <= 0)
                {
                    var seedMatch = Regex.Match(fullStr, @"(?:service of|commissioned|bathed in the blood of|chanted|carved to glorify|of)\s*(\d{2,6})", RegexOptions.IgnoreCase);
                    if (seedMatch.Success && int.TryParse(seedMatch.Groups[1].Value, out var seedVal))
                    {
                        item.TimelessSeed = seedVal;
                    }
                }

                // 4. Cập nhật Leader nếu chưa có
                if (string.IsNullOrEmpty(item.TimelessLeader))
                {
                    var leaderMatch = Regex.Match(fullStr, @"\b(Asenath|Balbala|Nasima|Doryani|Xibaqua|Zerphi|Kaom|Rakiata|Akoya|Avarius|Dominus|Maxarius|Cadiro|Caspiro|Victario)\b", RegexOptions.IgnoreCase);
                    if (leaderMatch.Success)
                    {
                        item.TimelessLeader = leaderMatch.Groups[1].Value;
                    }
                }
            }
        }
        catch { }
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

            // DI CHUYỂN CHUỘT RA VÙNG AN TOÀN TRÁNH HOVER VÀO Ô ĐỒ PHÍA DƯỚI
            MouseHelper.MoveMouse(new Vector2(realWinRect.Left + 150, realWinRect.Top + 150));

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
