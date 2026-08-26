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
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
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
    private readonly StashDepositService? _stashDepositService;

    // Bộ nhớ cache lưu giá theo con trỏ bộ nhớ Address duy nhất của từng ô UI đồ trong game
    public static readonly Dictionary<long, CurrencyCost> ScannedPriceCache = new();

    // Lưu danh sách các vật phẩm vừa mua gần nhất để hiển thị lên giao diện
    public static readonly List<string> RecentPurchases = new();

    public bool IsRunning { get; set; }
    public bool RequestStop { get; set; }

    public PurchaseExecutor(GameController gc, ShopAutoBuyerSettings settings, ShopAdapterFactory adapterFactory, StashDepositService? stashDepositService = null)
    {
        _gc = gc;
        _settings = settings;
        _adapterFactory = adapterFactory;
        _stashDepositService = stashDepositService ?? new StashDepositService(gc, settings);
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

        var startAreaHash = _gc.IngameState?.Data?.CurrentAreaHash ?? 0;
        NotifyBridge("BUYING", 0, null);

        var totalPurchasedCount = 0;
        var purchasedDetails = new List<string>();

        try
        {
            var versionStr = _settings.GameVersion?.Value ?? "AutoDetect";
            var adapter = _adapterFactory.GetAdapter(_gc, versionStr);

            // Chờ siêu ngắn (10ms) để UI ổn định
            yield return new WaitTime(10);

            if (!adapter.IsShopOpen(_gc) || (_gc.IngameState?.Data?.CurrentAreaHash != startAreaHash))
            {
                yield break;
            }

            var activeRules = _settings.GetActiveRules();
            var skippedAddresses = new HashSet<long>();

            // Chờ 20ms ban đầu để server PoE nạp danh sách ô đồ vào RAM
            yield return new WaitTime(20);

            // DYNAMIC LIVE-QUEUE SCAN: Quét trực tiếp trên Tab hiện tại cho tới khi mua sạch 100% item thỏa mãn
            while (true)
            {
                if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;
                if (InventorySpaceChecker.GetFreeSlotsCount(_gc) <= 0) break;

                var liveItems = adapter.GetAvailableItems(_gc);
                if (liveItems == null || liveItems.Count == 0) break;

                var matchingItems = liveItems
                    .Where(i => i != null && !skippedAddresses.Contains(i.InventoryItem?.Address ?? 0) &&
                                (_settings.IsTimelessMode() ? (i.IsTimelessJewel && i.Width == 1 && i.Height == 1 && i.Sockets == 0 && !IsOccludedByLargerItem(i, liveItems) && ItemFilterEngine.MatchesTimelessCandidate(i, _settings)) : ItemFilterEngine.MatchesAnyRule(i, activeRules)))
                    .OrderBy(i => i.ScreenRect.Top)
                    .ThenBy(i => i.ScreenRect.Left)
                    .ToList();

                // Nếu tab hiện tại không còn item nào thỏa mãn -> Kết thúc để sang hideout mới
                if (matchingItems.Count == 0)
                {
                    break;
                }

                // Lấy món đồ đầu tiên chưa bị bỏ qua
                var item = matchingItems[0];
                var itemAddress = item.InventoryItem?.Address ?? 0;

                if (!InventorySpaceChecker.HasSpaceForItem(_gc, item.Width, item.Height)) break;

                var clickTarget = new Vector2(item.ScreenRect.Center.X, item.ScreenRect.Center.Y + 4);
                var boughtSuccessfully = false;

                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;

                    // 1. DI CHUỘT VÀO ITEM ĐỂ HIỆN TOOLTIP SIÊU TỐC (12ms)
                    MouseHelper.FastDirectMove(clickTarget);
                    yield return new WaitTime(12);

                    // 2. CẬP NHẬT & TÁI KIỂM TRA GIÁ TRƯỚC KHI CLICK (Đảm bảo 100% chính xác)
                    UpdateItemFromLiveHover(_gc, item);

                    var canBuy = _settings.IsTimelessMode()
                        ? ItemFilterEngine.MatchesTimelessSettings(item, _settings)
                        : ItemFilterEngine.MatchesGeneralSettings(item, _settings, activeRules);

                    if (!canBuy)
                    {
                        LogHelper.Warn($"[GIÁ NGOÀI PHẠM VI] Bỏ qua {item.DisplayName} vì giá không thỏa mãn ({item.CostString}).");
                        skippedAddresses.Add(itemAddress);
                        break;
                    }

                    if (_settings.IsTimelessMode() && IsHoveringNonJewelEquipment(_gc))
                    {
                        skippedAddresses.Add(itemAddress);
                        break;
                    }

                    // Ghi nhận số lượng item trong túi đồ trước khi click
                    var invCountBefore = GetPlayerInventoryItemCount(_gc);

                    // 3. Ctrl+Click mua với phím Ctrl nhận chắc chắn 100% (8ms Ctrl buffer, 15ms hold)
                    MouseHelper.FastCtrlLeftClickAt(clickTarget, 0, 15);

                    // 4. KIỂM TRA TRẠNG THÁI BỘ NHỚ SIÊU NHANH (8ms/tick, phản hồi ngay khi túi đồ +1)
                    var confirmedByMemory = false;
                    for (var tick = 0; tick < 12; tick++)
                    {
                        yield return new WaitTime(8);

                        if (IsPriceDifferenceModalOpen(_gc))
                        {
                            var accepted = HandlePriceDifferenceModal(_gc, _settings);
                            if (!accepted)
                            {
                                // Người dùng TẮT tính năng mua chênh lệch giá -> Đã bấm ESC hủy bỏ, LẬP TỨC BỎ QUA VÀ KHÔNG RETRY LẠI!
                                LogHelper.Warn($"[BỎ QUA VẬT PHẨM] Đã hủy mua do chênh lệch giá (Diff Price) theo cài đặt!");
                                skippedAddresses.Add(itemAddress);
                                break; // Thoát khỏi for attempt (không retry lần 2)
                            }
                            yield return new WaitTime(25);
                        }

                        // Kiểm tra thực tế số lượng item trong túi đồ nhân vật (Ground Truth)
                        var currentInvCount = GetPlayerInventoryItemCount(_gc);
                        if (invCountBefore >= 0 && currentInvCount > invCountBefore)
                        {
                            confirmedByMemory = true;
                            boughtSuccessfully = true;
                            break;
                        }
                    }

                    if (confirmedByMemory)
                    {
                        break; // Mua thành công món này!
                    }

                    // NẾU CHƯA VÀO TÚI ĐỒ (Game báo thiếu tiền / trễ sync) -> RETRY ĐÚNG 1 LẦN VỚI DELAY 500MS
                    if (attempt < 2)
                    {
                        LogHelper.Warn($"[THỬ LẠI #{attempt}] Item chưa vào túi. Đang đợi 500ms đồng bộ tiền và mua lại lần 2...");
                        yield return new WaitTime(500); // CHỜ ĐÚNG 500MS VÀ THỬ LẠI LẦN 2
                    }
                    else
                    {
                        skippedAddresses.Add(itemAddress);
                    }
                }

                if (boughtSuccessfully)
                {
                    totalPurchasedCount++;

                    var priceText = !string.IsNullOrWhiteSpace(item.CostString) 
                        ? item.CostString 
                        : ((item.Cost != null && item.Cost.Amount > 0) 
                            ? $"{item.Cost.Amount} {item.Cost.CurrencyName}" 
                            : (_settings.BuyChaosPrice?.Value == true ? $"{_settings.MaxChaosPrice?.Value} Chaos Orb (Max)" : "Đã mua"));
                    var goldText = item.Cost?.GoldAmount > 0 ? $" ({item.Cost.GoldAmount} Gold)" : "";
                    var fullBuyLog = $"{item.DisplayName} | Giá: {priceText}{goldText}";

                    LogHelper.Info($"[ĐÃ MUA THÀNH CÔNG #{totalPurchasedCount}] {fullBuyLog}");
                    purchasedDetails.Add(fullBuyLog);
                    RecentPurchases.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {fullBuyLog}");
                    if (RecentPurchases.Count > 10) RecentPurchases.RemoveAt(RecentPurchases.Count - 1);
                    AppendToHistoryLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ĐÃ MUA] {fullBuyLog}");

                    // CẬP NHẬT TRẠNG THÁI BUYING CHO PYTHON BIẾT VẪN ĐANG MUA TIẾP
                    NotifyBridge("BUYING", totalPurchasedCount, purchasedDetails);

                    // Chờ siêu ngắn (20ms) trước khi mua tiếp món thứ 2, 3... trong cùng Shop
                    yield return new WaitTime(20);
                }
            }
        }
        finally
        {
            IsRunning = false;
            
            // Nếu hành trang đầy (hoặc <= MinFreeSlotsThreshold), tự động đóng shop và chuyển trạng thái DEPOSITING
            var freeSlots = InventorySpaceChecker.GetFreeSlotsCount(_gc);
            var threshold = _settings?.MinFreeSlotsThreshold?.Value ?? 2;
            if (_settings?.AutoDepositWhenFull?.Value == true && freeSlots <= threshold)
            {
                LogHelper.Warn($"[HÀNH TRANG ĐẦY] Còn {freeSlots} ô trống (<= {threshold}). Đang tự động đóng Shop và kích hoạt cất đồ vào rương...");
                Input.KeyDown(Keys.Space);
                Thread.Sleep(30);
                Input.KeyUp(Keys.Space);
                NotifyBridge("DEPOSITING", totalPurchasedCount, purchasedDetails);
            }
            else
            {
                var statusStr = RequestStop ? "STOPPED" : "COMPLETED";
                NotifyBridge(statusStr, totalPurchasedCount, purchasedDetails);
            }
        }
    }

    public static int GetPlayerInventoryItemCount(GameController gc)
    {
        try
        {
            if (gc == null) return -1;
            var slotItems = InventorySpaceChecker.GetPlayerInventorySlotItems(gc);
            if (slotItems != null && slotItems.Count >= 0)
            {
                return slotItems.Count;
            }

            var uiItems = InventorySpaceChecker.GetPlayerInventoryItems(gc);
            if (uiItems != null && uiItems.Count >= 0)
            {
                return uiItems.Count;
            }
        }
        catch { }
        return -1;
    }

    private static void NotifyBridge(string status, int itemsBought, List<string>? details)
    {
        try
        {
            // Bắn tín hiệu tức thì qua TCP Socket (0ms)
            SocketBridgeClient.SendStatus(status, itemsBought, details);

            // Dự phòng ghi file
            var bridgeFile = BridgePathHelper.GetBridgeFilePath();
            var tradeId = "";
            if (File.Exists(bridgeFile))
            {
                try
                {
                    var text = File.ReadAllText(bridgeFile);
                    if (text.Contains("\"trade_id\""))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(text, "\"trade_id\"\\s*:\\s*\"([^\"]+)\"");
                        if (match.Success) tradeId = match.Groups[1].Value;
                    }
                }
                catch { }
            }

            var detailsEscaped = details != null && details.Count > 0
                ? string.Join(",", details.Select(d => $"\"{d.Replace("\"", "\\\"")}\""))
                : string.Empty;
            var json = $"{{\"status\":\"{status}\",\"trade_id\":\"{tradeId}\",\"items_bought\":{itemsBought},\"last_items\":[{detailsEscaped}],\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }

    public static bool IsOccludedByLargerItem(ShopItemInfo item, IEnumerable<ShopItemInfo>? allItems)
    {
        if (item == null || allItems == null) return false;
        var center = item.ClickPosition;

        foreach (var other in allItems)
        {
            if (other == null || other == item) continue;

            // CHỈ XÉT TRANG BỊ LỚN THỰC SỰ (Áo giáp 2x3, Vũ khí...) có kích thước lớn hơn ngọc
            if (other.Sockets > 0 || other.Width > 1 || other.Height > 1 || other.ScreenRect.Height > 90 || other.ScreenRect.Width > 90)
            {
                var rect = other.ScreenRect;
                if (rect.Width > 0 && rect.Height > 0)
                {
                    // Nếu tâm của viên ngọc nằm gọn bên trong khung hình của trang bị lớn -> ĐANG BỊ ĐÈ!
                    if (center.X >= rect.Left + 6 && center.X <= rect.Right - 6 &&
                        center.Y >= rect.Top + 6 && center.Y <= rect.Bottom - 6)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public static bool IsHoveringNonJewelEquipment(GameController gc)
    {
        try
        {
            if (gc == null) return false;
            var ingameState = gc.IngameState ?? gc.Game?.IngameState;
            if (ingameState == null) return false;

            var hoveredElement = ingameState.UIHover ?? ingameState.UIHoverElement;
            if (hoveredElement != null && hoveredElement.IsValid)
            {
                if (hoveredElement is NormalInventoryItem invItem && invItem.Item != null && invItem.Item.IsValid)
                {
                    var item = invItem.Item;
                    var path = item.Path ?? string.Empty;
                    var socketsComp = item.GetComponent<Sockets>();
                    if (socketsComp != null && (socketsComp.NumberOfSockets > 0 || socketsComp.LargestLinkSize > 0))
                    {
                        return true; // Có socket -> 100% là áo/vũ khí/găng/mũ/giày!
                    }

                    if (path.Contains("BodyArmour", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
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
            if (item.InventoryItem != null && item.InventoryItem.IsValid)
            {
                if (item.InventoryItem.Tooltip != null && item.InventoryItem.Tooltip.IsValid)
                {
                    Poe1ShopAdapter.ExtractCostTextRecursive(item.InventoryItem.Tooltip, texts, 0);
                }
            }

            if (texts.Count > 0)
            {
                var fullStr = string.Join(" ", texts);
                item.TooltipFullText = fullStr;

                // Cập nhật tên chính xác từ nội dung Tooltip
                if (fullStr.Contains("Incandescent Invitation", StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = "Incandescent Invitation";
                    item.BaseName = "Incandescent Invitation";
                }
                else if (fullStr.Contains("Screaming Invitation", StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = "Screaming Invitation";
                    item.BaseName = "Screaming Invitation";
                }
                else if (fullStr.Contains("Polaric Invitation", StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = "Polaric Invitation";
                    item.BaseName = "Polaric Invitation";
                }
                else if (fullStr.Contains("Writhing Invitation", StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = "Writhing Invitation";
                    item.BaseName = "Writhing Invitation";
                }
                else if (fullStr.Contains("Maven's Invitation", StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = "Maven's Invitation";
                    item.BaseName = "Maven's Invitation";
                }

                Poe1ShopAdapter.ParseCostFromTexts(texts, item);

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

            // Quét nhanh IngameUi (depth <= 6) - Cực kỳ mượt và không gây tụt FPS
            var ingameUi = ingameState.IngameUi;
            if (ingameUi != null && ingameUi.IsValid)
            {
                var d = FindPriceDifferenceDialogInMemory(ingameUi, 0);
                if (d != null && d.IsValid) return true;
            }
        }
        catch { }

        return false;
    }

    public static bool HandlePriceDifferenceModal(GameController gc, ShopAutoBuyerSettings? settings = null)
    {
        try
        {
            if (gc == null) return false;
            if (!IsPriceDifferenceModalOpen(gc)) return false;

            var accept = settings?.AcceptPriceDifference?.Value ?? true;

            if (accept)
            {
                // 1. NẾU BẬT: Nhấn Enter để chấp nhận mua với giá mới
                LogHelper.Info("[Diff Price] Đã BẬT chấp nhận chênh lệch giá -> Nhấn ENTER để mua!");
                MouseHelper.PressKey(Keys.Return, 30);

                // Đồng thời click dự phòng nút [ OK ] nếu giao diện game yêu cầu click chuột
                var ingameState = gc.IngameState ?? gc.Game?.IngameState;
                var dialog = ingameState?.IngameUi != null ? FindPriceDifferenceDialogInMemory(ingameState.IngameUi, 0) : null;
                var okButtonElement = FindOkButtonInDialog(dialog);
                if (okButtonElement != null && okButtonElement.IsValid)
                {
                    var okRect = okButtonElement.GetClientRect();
                    if (okRect.Width > 0 && okRect.Height > 0)
                    {
                        var targetPos = new Vector2(okRect.Center.X, okRect.Center.Y);
                        MouseHelper.LeftClickAt(targetPos, 40, 30);
                    }
                }
                return true;
            }
            else
            {
                // 2. NẾU TẮT: Nhấn ESC để hủy bỏ và KHÔNG mua món này
                LogHelper.Info("[Diff Price] Đã TẮT chấp nhận chênh lệch giá -> Nhấn ESC để hủy bỏ và KHÔNG mua món này!");
                MouseHelper.PressKey(Keys.Escape, 40);
                return false;
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"HandlePriceDifferenceModal error: {ex.Message}");
            return false;
        }
    }

    public static Element? FindOkButtonInDialog(Element? dialog)
    {
        if (dialog == null || !dialog.IsValid) return null;
        return FindOkButtonRecursive(dialog, 0);
    }

    private static Element? FindOkButtonRecursive(Element? root, int depth)
    {
        if (root == null || !root.IsValid || depth > 6) return null;
        var txt = root.Text;
        var txtNoTags = root.TextNoTags;

        if (!string.IsNullOrEmpty(txt) && txt.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            return root.Parent != null && root.Parent.GetClientRect().Width > root.GetClientRect().Width ? root.Parent : root;
        }
        if (!string.IsNullOrEmpty(txtNoTags) && txtNoTags.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
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
        if (root == null || !root.IsValid || depth > 6) return null;

        var txt = root.Text;
        var txtNoTags = root.TextNoTags;

        // Kiểm tra nhanh không cấp phát chuỗi
        if (!string.IsNullOrEmpty(txt))
        {
            if (txt.Contains("price differs", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("differs from", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("this item's price", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("initially travelled", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("different price", StringComparison.OrdinalIgnoreCase))
            {
                return root.Parent ?? root;
            }
        }

        if (!string.IsNullOrEmpty(txtNoTags) && txtNoTags != txt)
        {
            if (txtNoTags.Contains("price differs", StringComparison.OrdinalIgnoreCase) ||
                txtNoTags.Contains("differs from", StringComparison.OrdinalIgnoreCase) ||
                txtNoTags.Contains("this item's price", StringComparison.OrdinalIgnoreCase) ||
                txtNoTags.Contains("initially travelled", StringComparison.OrdinalIgnoreCase) ||
                txtNoTags.Contains("different price", StringComparison.OrdinalIgnoreCase))
            {
                return root.Parent ?? root;
            }
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

    public static void AppendToHistoryLog(string logLine)
    {
        var paths = BridgePathHelper.GetHistoryFilePaths();

        foreach (var path in paths)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                writer.WriteLine(logLine);
                writer.Flush();
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Lỗi khi ghi lịch sử vào {path}: {ex.Message}");
            }
        }
    }
}
