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

            // Chờ siêu ngắn (25ms) để UI ổn định
            yield return new WaitTime(25);

            if (!adapter.IsShopOpen(_gc) || (_gc.IngameState?.Data?.CurrentAreaHash != startAreaHash))
            {
                yield break;
            }

            // CHỈ QUÉT DUY NHẤT 1 TAB ĐANG MỞ (KHÔNG CHUYỂN TAB)
            var currentItems = adapter.GetAvailableItems(_gc);
            if (currentItems == null || currentItems.Count == 0)
            {
                yield return new WaitTime(35);
                currentItems = adapter.GetAvailableItems(_gc);
                if (currentItems == null || currentItems.Count == 0)
                {
                    yield break;
                }
            }

            // ----------------------------------------------------
            // LỌC DANH SÁCH VẬT PHẨM ỨNG VIÊN PHÙ HỢP
            // ----------------------------------------------------
            List<ShopItemInfo> candidateItems;
            if (_settings.IsTimelessMode())
            {
                candidateItems = currentItems
                    .Where(i => i != null && i.IsTimelessJewel && i.Width == 1 && i.Height == 1 && i.Sockets == 0 && !IsOccludedByLargerItem(i, currentItems) && ItemFilterEngine.MatchesTimelessCandidate(i, _settings))
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
                var activeRules = _settings.GetActiveRules();

                // SINGLE-PASS INSTANT BUY: Rà chuột tới đâu kiểm tra giá và Ctrl+Click mua ngay lập tức tại đó!
                foreach (var item in candidateItems)
                {
                    if (!_settings.Enable.Value || RequestStop || !adapter.IsShopOpen(_gc)) yield break;

                    if (_settings.IsTimelessMode() && IsOccludedByLargerItem(item, currentItems)) continue;
                    if (!InventorySpaceChecker.HasSpaceForItem(_gc, item.Width, item.Height)) break;

                    var clickTarget = new Vector2(item.ScreenRect.Center.X, item.ScreenRect.Center.Y + 4);
                    MouseHelper.FastDirectMove(clickTarget);
                    yield return new WaitTime(18);

                    // Đọc giá từ Tooltip
                    UpdateItemFromLiveHover(_gc, item);

                    // Kiểm tra tiêu chuẩn giá
                    var canBuy = _settings.IsTimelessMode()
                        ? ItemFilterEngine.MatchesTimelessSettings(item, _settings)
                        : ItemFilterEngine.MatchesGeneralSettings(item, _settings, activeRules);

                    if (canBuy)
                    {
                        if (_settings.IsTimelessMode() && IsHoveringNonJewelEquipment(_gc)) continue;

                        // Ctrl+Click mua ngay tại chỗ (0ms hover, 15ms hold)
                        MouseHelper.FastCtrlLeftClickAt(clickTarget, 0, 15);
                        yield return new WaitTime(15);

                        if (IsPriceDifferenceModalOpen(_gc))
                        {
                            HandlePriceDifferenceModal(_gc, _settings);
                            yield return new WaitTime(20);
                        }

                        totalPurchasedCount++;

                        var priceText = !string.IsNullOrWhiteSpace(item.CostString) 
                            ? item.CostString 
                            : ((item.Cost != null && item.Cost.Amount > 0) 
                                ? $"{item.Cost.Amount} {item.Cost.CurrencyName}" 
                                : (_settings.BuyChaosPrice?.Value == true ? $"{_settings.MaxChaosPrice?.Value} Chaos Orb (Max)" : "Đã mua"));
                        var goldText = item.Cost?.GoldAmount > 0 ? $" ({item.Cost.GoldAmount} Gold)" : "";
                        var fullBuyLog = $"{item.DisplayName} | Giá: {priceText}{goldText}";

                        LogHelper.Info($"[ĐÃ MUA THÀNH CÔNG] {fullBuyLog}");
                        purchasedDetails.Add(fullBuyLog);
                        RecentPurchases.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {fullBuyLog}");
                        if (RecentPurchases.Count > 10) RecentPurchases.RemoveAt(RecentPurchases.Count - 1);
                        AppendToHistoryLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ĐÃ MUA] {fullBuyLog}");
                    }
                }
            }
        }
        finally
        {
            IsRunning = false;
            var statusStr = RequestStop ? "STOPPED" : "COMPLETED";
            NotifyBridge(statusStr, totalPurchasedCount, purchasedDetails);
        }
    }

    private static void NotifyBridge(string status, int itemsBought, List<string>? details)
    {
        try
        {
            var bridgeFile = BridgePathHelper.GetBridgeFilePath();
            var detailsEscaped = details != null && details.Count > 0
                ? string.Join(",", details.Select(d => $"\"{d.Replace("\"", "\\\"")}\""))
                : string.Empty;
            var json = $"{{\"status\":\"{status}\",\"items_bought\":{itemsBought},\"last_items\":[{detailsEscaped}],\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
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
                Poe1ShopAdapter.ParseCostFromTexts(texts, item);

                var fullStr = string.Join(" ", texts);

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

    public static void HandlePriceDifferenceModal(GameController gc, ShopAutoBuyerSettings? settings = null)
    {
        try
        {
            if (gc == null) return;
            if (!IsPriceDifferenceModalOpen(gc)) return;

            var ingameState = gc.IngameState ?? gc.Game?.IngameState;
            var dialog = ingameState?.IngameUi != null ? FindPriceDifferenceDialogInMemory(ingameState.IngameUi, 0) : null;

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
