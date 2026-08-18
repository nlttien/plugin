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

    // Bộ nhớ cache lưu giá theo con trỏ bộ nhớ Address duy nhất của từng ô UI đồ trong game
    public static readonly Dictionary<long, CurrencyCost> ScannedPriceCache = new();

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
        LogHelper.Info("=== Bắt đầu tiến trình tự động quét & mua đồ trong Tab hiện tại ===");

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

            // CHỈ QUÉT DUY NHẤT 1 TAB ĐANG MỞ (KHÔNG CHUYỂN TAB)
            var currentItems = adapter.GetAvailableItems(_gc);
            if (currentItems == null || currentItems.Count == 0)
            {
                yield return new WaitTime(100);
                currentItems = adapter.GetAvailableItems(_gc);
                if (currentItems == null || currentItems.Count == 0)
                {
                    LogHelper.Info("Không tìm thấy vật phẩm nào trong Tab Shop hiện tại.");
                    yield break;
                }
            }

            // ----------------------------------------------------
            // BƯỚC 1: LỌC TẤT CẢ CÁC VIÊN TIMELESS JEWEL ĐẠT CHUẨN ĐỂ QUÉT GIÁ TRƯỚC
            // SẮP XẾP CHUẨN XÁC THEO TỌA ĐỘ MÀN HÌNH TỪ TRÊN XUỐNG DƯỚI, TỪ TRÁI QUA PHẢI
            // ----------------------------------------------------
            List<ShopItemInfo> candidateItems;
            if (_settings.OnlyBuyTimelessJewels?.Value == true)
            {
                candidateItems = currentItems
                    .Where(i => i != null && i.IsTimelessJewel && i.ScreenRect.Width <= 68 && i.ScreenRect.Height <= 68 && i.Sockets == 0 && ItemFilterEngine.MatchesTimelessCandidate(i, _settings))
                    .OrderBy(i => i.ScreenRect.Top)
                    .ThenBy(i => i.ScreenRect.Left)
                    .ToList();
            }
            else
            {
                var activeRules = _settings.GetActiveRules();
                candidateItems = currentItems
                    .Where(i => i != null && ItemFilterEngine.MatchesAnyRule(i, activeRules))
                    .OrderBy(i => i.ScreenRect.Top)
                    .ThenBy(i => i.ScreenRect.Left)
                    .ToList();
            }

            if (candidateItems.Count > 0)
            {
                LogHelper.Info($"[BƯỚC 1: QUÉT GIÁ] Tìm thấy {candidateItems.Count} viên Timeless Jewel trong Tab. Bắt đầu lia chuột quét giá toàn bộ...");

                // QUÉT TOÀN BỘ CÁC VIÊN TIMELESS TRƯỚC
                foreach (var item in candidateItems)
                {
                    if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;

                    // Lia chuột qua viên ngọc để nạp dữ liệu Tooltip vào RAM
                    MouseHelper.MoveMouseWithJitter(item.ScreenRect, 6f);
                    yield return new WaitTime(MouseHelper.GetRandomDelay(70, 95));

                    // Đọc và cập nhật trực tiếp dữ liệu giá và mod từ Tooltip
                    UpdateItemFromLiveHover(_gc, item);
                }

                // ----------------------------------------------------
                // BƯỚC 2: TIẾN HÀNH MUA CÁC VIÊN ĐẠT CHUẨN GIÁ (10 - 50 CHAOS)
                // ----------------------------------------------------
                var validItemsToBuy = candidateItems
                    .Where(i => ItemFilterEngine.MatchesTimelessSettings(i, _settings))
                    .ToList();

                if (validItemsToBuy.Count == 0)
                {
                    LogHelper.Info($"[HOÀN TẤT QUÉT] Đã quét xong {candidateItems.Count} viên ngọc. Không có viên nào có giá Chaos hợp lệ (10-50c).");
                }
                else
                {
                    LogHelper.Info($"[BƯỚC 2: MUA ĐỒ] Tìm thấy {validItemsToBuy.Count} viên ngọc đạt chuẩn giá Chaos (10-50c). Bắt đầu mua...");

                    foreach (var item in validItemsToBuy)
                    {
                        if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;

                        // 1. Kiểm tra ô trống hành trang trước khi mua
                        if (!InventorySpaceChecker.HasSpaceForItem(_gc, item.Width, item.Height))
                        {
                            LogHelper.Warn("Hành trang (Inventory) đã đầy! Dừng tự động mua.");
                            yield break;
                        }

                        // 2. Tọa độ tâm chính xác của ô đồ (+6px vào giữa icon)
                        var clickTarget = new Vector2(item.ScreenRect.Center.X, item.ScreenRect.Center.Y + 6);

                        // 3. Thực hiện Ctrl + Click chuẩn xác 100% (chờ 130ms để game nhận hover, giữ click 50ms)
                        MouseHelper.CtrlLeftClickAt(clickTarget, 130, 50);

                        // 4. Đợi server phản hồi và quét xem hộp thoại cảnh báo giá có xuất hiện không (trong 1000ms)
                        var modalDetected = false;
                        for (var checkStep = 0; checkStep < 20; checkStep++)
                        {
                            yield return new WaitTime(50);
                            if (IsPriceDifferenceModalOpen(_gc))
                            {
                                modalDetected = true;
                                break;
                            }
                        }

                        // 5. BẤM NÚT [ OK ] ĐÚNG 1 LẦN KHI CÓ HỘP THOẠI CẢNH BÁO GIÁ
                        if (modalDetected || IsPriceDifferenceModalOpen(_gc))
                        {
                            LogHelper.Info("Phát hiện hộp thoại cảnh báo giá! Bấm [ OK ] ngay...");
                            yield return new WaitTime(60);
                            HandlePriceDifferenceModal(_gc, _settings);
                            
                            // Đợi hộp thoại đóng hoàn toàn
                            var waitCount = 0;
                            while (IsPriceDifferenceModalOpen(_gc) && waitCount < 10)
                            {
                                yield return new WaitTime(50);
                                waitCount++;
                            }
                        }

                        totalPurchasedCount++;
                        LogHelper.Info($"[ĐÃ MUA] {item.DisplayName} (Giá: {item.CostString})");

                        // 6. Nghỉ ngơi giữa các lần mua
                        yield return new WaitTime(MouseHelper.GetRandomDelay(_settings.MinDelayMs.Value, _settings.MaxDelayMs.Value));
                    }
                }
            }

            LogHelper.Info($"=== Hoàn thành quét & mua đồ trong Tab! Tổng cộng đã mua: {totalPurchasedCount} vật phẩm. ===");
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
                    if (!chaosMatch.Success) chaosMatch = Regex.Match(fullStr, @"Cost:\s*(\d+)", RegexOptions.IgnoreCase);
                    
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

                // Lưu giá vào cache theo địa chỉ Address duy nhất trong RAM
                if (item.InventoryItem != null && item.Cost != null)
                {
                    ScannedPriceCache[item.InventoryItem.Address] = item.Cost;
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

            var ingameState = gc.IngameState ?? gc.Game?.IngameState;
            var dialog = ingameState?.IngameUi != null ? FindPriceDifferenceDialogInMemory(ingameState.IngameUi, 0) : null;
            if (dialog == null && ingameState?.UIRoot != null)
            {
                dialog = FindPriceDifferenceDialogInMemory(ingameState.UIRoot, 0);
            }

            Vector2 targetPos;

            // 1. NẾU TÌM THẤY NÚT [ OK ] TRỰC TIẾP TỪ RAM -> BẤM THẲNG VÀO TÂM NÚT
            var okButtonElement = FindOkButtonInDialog(dialog);
            if (okButtonElement != null && okButtonElement.IsValid)
            {
                var okRect = okButtonElement.GetClientRect();
                if (okRect.Width > 0 && okRect.Height > 0)
                {
                    targetPos = new Vector2(okRect.Center.X, okRect.Center.Y);
                    MouseHelper.LeftClickAt(targetPos, 80, 50);
                    var realWin = gc.Window.GetWindowRectangleReal();
                    MouseHelper.MoveMouse(new Vector2(realWin.Left + 150, realWin.Top + 150));
                    LogHelper.Info($"[Bộ nhớ RAM] Đã bấm xác nhận nút [ OK ] tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
                    return;
                }
            }

            // 2. NẾU KHÔNG -> DÙNG TỌA ĐỘ CHUẨN XÁC ĐÃ ĐƯỢC CÂN CHỈNH (763, 570)
            var realWinRect = gc.Window.GetWindowRectangleReal();
            if (realWinRect.Width <= 0 || realWinRect.Height <= 0)
            {
                realWinRect = gc.Window.GetWindowRectangle();
            }
            if (realWinRect.Width <= 0 || realWinRect.Height <= 0) return;

            var scaleX = realWinRect.Width / 1920f;
            var scaleY = realWinRect.Height / 1080f;
            var customX = (settings?.OkButtonX?.Value == 750 || settings?.OkButtonX?.Value == 778 || settings?.OkButtonX?.Value == 787) 
                ? 763 
                : (settings?.OkButtonX?.Value ?? 763);
            var customY = (settings?.OkButtonY?.Value == 575 || settings?.OkButtonY?.Value == 572 || settings?.OkButtonY?.Value == 545) 
                ? 570 
                : (settings?.OkButtonY?.Value ?? 570);

            targetPos = new Vector2(realWinRect.Left + customX * scaleX, realWinRect.Top + customY * scaleY);

            // BẤM ĐÚNG 1 LẦN VÀO TÂM NÚT [ OK ]
            MouseHelper.LeftClickAt(targetPos, 80, 50);

            // DI CHUYỂN CHUỘT RA VÙNG AN TOÀN TRÁNH HOVER VÀO Ô ĐỒ PHÍA DƯỚI
            MouseHelper.MoveMouse(new Vector2(realWinRect.Left + 150, realWinRect.Top + 150));

            LogHelper.Info($"[Tọa độ màn hình] Đã bấm xác nhận nút [ OK ] tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HandlePriceDifferenceModal error: {ex.Message}");
        }
    }

    public static Element? FindOkButtonInDialog(Element? dialog)
    {
        if (dialog == null || !dialog.IsValid) return null;
        return FindOkButtonRecursive(dialog, 0);
    }

    private static Element? FindOkButtonRecursive(Element? root, int depth)
    {
        if (root == null || !root.IsValid || depth > 10) return null;
        var txt = (root.Text ?? string.Empty).Trim();
        var txtNoTags = (root.TextNoTags ?? string.Empty).Trim();
        if (txt.Equals("OK", StringComparison.OrdinalIgnoreCase) || txtNoTags.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            return root.Parent != null && root.Parent.GetClientRect().Width > root.GetClientRect().Width ? root.Parent : root;
        }
        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                var found = FindOkButtonRecursive(child, depth + 1);
                if (found != null) return found;
            }
        }
        return null;
    }

    public static Element? FindPriceDifferenceDialogInMemory(Element? root, int depth)
    {
        if (root == null || !root.IsValid || depth > 25) return null;

        var txt = (root.Text ?? string.Empty).ToLowerInvariant();
        var txtNoTags = (root.TextNoTags ?? string.Empty).ToLowerInvariant();

        // Kiểm tra từ khóa hộp thoại cảnh báo giá
        if (txt.Contains("price differs") || txt.Contains("differs from") || txt.Contains("this item's price") || txt.Contains("initially travelled") || txt.Contains("this shop for") || txt.Contains("different price") || txt.Contains("differs") ||
            txtNoTags.Contains("price differs") || txtNoTags.Contains("differs from") || txtNoTags.Contains("this item's price") || txtNoTags.Contains("initially travelled") || txtNoTags.Contains("this shop for") || txtNoTags.Contains("different price") || txtNoTags.Contains("differs"))
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
