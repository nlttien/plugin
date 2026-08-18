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
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared;
using SharpDX;
using ShopAutoBuyer.Core.Utils;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Services;

public class StashDepositService
{
    private readonly GameController _gc;
    private readonly ShopAutoBuyerSettings _settings;

    public bool IsDepositing { get; set; }
    public bool RequestStop { get; set; }

    public StashDepositService(GameController gc, ShopAutoBuyerSettings settings)
    {
        _gc = gc;
        _settings = settings;
    }

    public bool NeedsDeposit()
    {
        if (_settings?.AutoDepositWhenFull?.Value != true) return false;
        var freeSlots = InventorySpaceChecker.GetFreeSlotsCount(_gc);
        var threshold = _settings?.MinFreeSlotsThreshold?.Value ?? 2;
        return freeSlots <= threshold;
    }

    public IEnumerator ExecuteDepositCoroutine()
    {
        if (IsDepositing) yield break;

        IsDepositing = true;
        RequestStop = false;

        LogHelper.Warn(">>> [HÀNH TRANG ĐẦY] BẮT ĐẦU QUY TRÌNH BIẾN VỀ HIDEOUT & CẤT ĐỒ VÀO RƯƠNG... <<<");

        try
        {
            // BƯỚC 1: Báo cho Web Trade tạm dừng chờ cất đồ
            NotifyBridge("DEPOSITING");

            // Đóng các cửa sổ đang mở (Shop / Menu) bằng phím Space
            Input.KeyDown(Keys.Space);
            Thread.Sleep(40);
            Input.KeyUp(Keys.Space);
            yield return new WaitTime(200);

            // BƯỚC 2: BẤM PHÍM TẮT VỀ HIDEOUT (Mặc định F2)
            var homeKey = _settings.HomeHotkey?.Value ?? Keys.F2;
            LogHelper.Info($"[BƯỚC 1: VỀ NHÀ] Bấm phím {homeKey} để biến về Hideout...");
            Input.KeyDown(homeKey);
            Thread.Sleep(60);
            Input.KeyUp(homeKey);

            // Chờ màn hình tải bản đồ chuyển về Hideout (Tối đa 15s)
            yield return new WaitTime(2500);

            var waitHideoutAttempts = 0;
            while (waitHideoutAttempts < 25)
            {
                if (RequestStop) yield break;

                var area = _gc.Area?.CurrentArea;
                if (area != null && (area.IsHideout || area.Name.Contains("Hideout", StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }
                yield return new WaitTime(500);
                waitHideoutAttempts++;
            }

            yield return new WaitTime(1000); // Đợi ổn định vị trí nhân vật sau khi load

            // BƯỚC 3: TÌM VÀ MỞ RƯƠNG (STASH HOẶC GUILD STASH)
            var isGuildStash = _settings.StashType?.Value?.Contains("Guild", StringComparison.OrdinalIgnoreCase) == true;
            var targetStashName = isGuildStash ? "GUILD STASH" : "STASH";
            LogHelper.Info($"[BƯỚC 2: TÌM RƯƠNG] Đang tìm rương {targetStashName} trong Hideout...");

            var stashOpened = false;
            for (var openAttempt = 0; openAttempt < 4; openAttempt++)
            {
                if (RequestStop) yield break;

                // 1. Thử click trực tiếp vào nhãn chữ trên mặt đất (STASH / GUILD STASH)
                var label = FindStashLabelOnGround(isGuildStash);
                if (label != null && label.IsValid && label.IsVisible)
                {
                    var rect = label.GetClientRect();
                    if (rect.Width > 0 && rect.Height > 0)
                    {
                        var clickPos = new Vector2(rect.Center.X, rect.Center.Y);
                        LogHelper.Info($"[CLICK NHÃN RƯƠNG] Bấm vào nhãn {targetStashName} tại: ({clickPos.X:F0}, {clickPos.Y:F0})");
                        MouseHelper.LeftClickAt(clickPos, 100, 60);
                    }
                }
                else
                {
                    // 2. Thử tìm Entity rương trong thế giới 3D và chiếu tọa độ lên màn hình (WorldToScreen)
                    var stashEntity = FindStashEntity(isGuildStash);
                    if (stashEntity != null && stashEntity.IsValid)
                    {
                        var sharpDxPos = _gc.IngameState.Camera.WorldToScreen(stashEntity.Pos);
                        var screenPos = new Vector2(sharpDxPos.X, sharpDxPos.Y);
                        LogHelper.Info($"[CLICK ENTITY RƯƠNG] Bấm vào vị trí rương {targetStashName} tại: ({screenPos.X:F0}, {screenPos.Y:F0})");
                        MouseHelper.LeftClickAt(screenPos, 100, 60);
                    }
                }

                // Chờ nhân vật chạy lại gần và mở cửa sổ rương (Tối đa 3.5 giây)
                for (var w = 0; w < 35; w++)
                {
                    yield return new WaitTime(100);
                    if (IsStashOpen(isGuildStash))
                    {
                        stashOpened = true;
                        break;
                    }
                }

                if (stashOpened) break;
                yield return new WaitTime(600);
            }

            if (!stashOpened)
            {
                LogHelper.Warn($"[CẢNH BÁO] Không thể mở cửa sổ rương {targetStashName}. Vui lòng kiểm tra lại vị trí rương.");
                yield break;
            }

            LogHelper.Info($"[BƯỚC 3: MỞ RƯƠNG XONG] Cửa sổ rương {targetStashName} đã mở! Bắt đầu cất đồ...");
            yield return new WaitTime(400);

            // BƯỚC 4: TIẾN HÀNH CẤT ĐỒ THÔNG MINH (STASHIE + NÚT CẤT NHANH + CTRL-CLICK + TỰ ĐỔI TAB)
            yield return ExecuteSmartDepositRoutine(isGuildStash);

            yield return new WaitTime(300);

            // BƯỚC 5: ĐÓNG CỬA SỔ RƯƠNG
            LogHelper.Info("[BƯỚC 5: ĐÓNG RƯƠNG] Hoàn tất cất đồ vào rương. Đóng rương và sẵn sàng tiếp tục săn đồ...");
            Input.KeyDown(Keys.Space);
            Thread.Sleep(40);
            Input.KeyUp(Keys.Space);
            yield return new WaitTime(300);

            // BƯỚC 6: Báo hoàn tất để Web Trade tiếp tục hoạt động
            NotifyBridge("COMPLETED");
            LogHelper.Info(">>> [HOÀN TẤT CẤT ĐỒ] HÀNH TRANG ĐÃ SẠCH SẼ. TIẾP TỤC CHU KỲ MUA HÀNG! <<<");
        }
        finally
        {
            IsDepositing = false;
        }
    }

    private IEnumerator ExecuteSmartDepositRoutine(bool isGuildStash)
    {
        // 1. Kích hoạt Plugin Stashie bằng phím tắt (Mặc định F3)
        if (_settings.UseStashiePlugin?.Value == true)
        {
            var stashieKey = _settings.StashieHotkey?.Value ?? Keys.F3;
            LogHelper.Info($"[BƯỚC 4A: STASHIE] Bấm phím {stashieKey} kích hoạt Stashie...");
            Input.KeyDown(stashieKey);
            Thread.Sleep(50);
            Input.KeyUp(stashieKey);
            yield return new WaitTime(800);
        }

        // 2. Click Nút Cất Nhanh Affinity (Ảnh 2)
        LogHelper.Info("[BƯỚC 4B: NÚT CẤT NHANH] Bấm nút cất nhanh Affinity (Mũi tên cạnh số Gold)...");
        ClickAffinityDepositButton();
        yield return new WaitTime(400);

        // 3. Kiểm tra các món đồ còn sót lại trong hành trang và Ctrl+Click
        var remainingItems = GetPlayerInventoryItemsWithPositions();
        if (remainingItems.Count == 0)
        {
            LogHelper.Info("[HOÀN TẤT CẤT ĐỒ] Toàn bộ vật phẩm đã được cất sạch sẽ vào rương!");
            yield break;
        }

        LogHelper.Info($"[BƯỚC 4C: CTRL+CLICK] Còn {remainingItems.Count} món trong hành trang. Bắt đầu Ctrl+Click và tự động chuyển Tab nếu cần...");

        // Thử cất qua các Tab (Tối đa 6 lần đổi Tab nếu Tab hiện tại không nhận hoặc bị đầy)
        for (var tabCycle = 0; tabCycle < 6; tabCycle++)
        {
            if (RequestStop || !IsStashOpen(isGuildStash)) yield break;

            var itemsToDeposit = GetPlayerInventoryItemsWithPositions();
            if (itemsToDeposit.Count == 0)
            {
                LogHelper.Info("[HOÀN TẤT] Toàn bộ vật phẩm đã vào rương thành công!");
                break;
            }

            foreach (var itemInfo in itemsToDeposit)
            {
                if (RequestStop || !IsStashOpen(isGuildStash)) yield break;

                // Ctrl + Click vào chính xác ô đồ
                MouseHelper.CtrlLeftClickAt(itemInfo.Pos, 40, 40);
                yield return new WaitTime(70);
            }

            yield return new WaitTime(250);

            // Kiểm tra lại xem số đồ còn lại có giảm không
            var afterItems = GetPlayerInventoryItemsWithPositions();
            if (afterItems.Count == 0)
            {
                LogHelper.Info("[HOÀN TẤT] Toàn bộ đồ đã được cất vào Stash!");
                break;
            }

            // Nếu vẫn còn đồ chưa cất được (do Tab hiện tại như curr không nhận Invitation, hoặc Tab đầy)
            LogHelper.Warn($"[CHUYỂN TAB] Còn {afterItems.Count} món chưa vào được Tab này -> Bấm [->] chuyển sang Tab tiếp theo...");
            Input.KeyDown(Keys.Right);
            Thread.Sleep(50);
            Input.KeyUp(Keys.Right);
            yield return new WaitTime(400);
        }
    }

    private List<(int Col, int Row, Vector2 Pos, NormalInventoryItem? Item)> GetPlayerInventoryItemsWithPositions()
    {
        var result = new List<(int, int, Vector2, NormalInventoryItem?)>();
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi;
            if (ingameUi == null) return result;

            var invPanel = ingameUi.InventoryPanel;
            var invElement = invPanel?[ExileCore.Shared.Enums.InventoryIndex.PlayerInventory];

            // Tọa độ bounding box của lưới hành trang
            RectangleF invRect;
            if (invElement != null && invElement.IsValid && invElement.GetClientRect().Width > 100)
            {
                invRect = invElement.GetClientRect();
            }
            else
            {
                // Fallback theo tỉ lệ độ phân giải màn hình
                var realWin = _gc.Window.GetWindowRectangleReal();
                if (realWin.Width <= 0 || realWin.Height <= 0) realWin = _gc.Window.GetWindowRectangle();
                var scaleX = realWin.Width / 1920f;
                var scaleY = realWin.Height / 1080f;
                invRect = new RectangleF(realWin.Left + 1295 * scaleX, realWin.Top + 615 * scaleY, 570 * scaleX, 240 * scaleY);
            }

            var cellW = invRect.Width / 12f;
            var cellH = invRect.Height / 5f;

            var items = invElement?.VisibleInventoryItems?.Where(i => i != null && i.IsValid).ToList() ?? new List<NormalInventoryItem>();

            foreach (var invItem in items)
            {
                var col = invItem.InventPosX;
                var row = invItem.InventPosY;
                if (col >= 0 && col < 12 && row >= 0 && row < 5)
                {
                    var itemRect = invItem.GetClientRect();
                    Vector2 clickPos;
                    if (itemRect.Width > 10 && itemRect.Height > 10)
                    {
                        clickPos = new Vector2(itemRect.Center.X, itemRect.Center.Y);
                    }
                    else
                    {
                        clickPos = new Vector2(invRect.Left + (col + 0.5f) * cellW, invRect.Top + (row + 0.5f) * cellH);
                    }
                    result.Add((col, row, clickPos, invItem));
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"GetPlayerInventoryItemsWithPositions error: {ex.Message}");
        }

        return result;
    }

    public void ClickAffinityDepositButton()
    {
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi;
            if (ingameUi == null) return;

            // 1. Thử tìm element nút Affinity Deposit trực tiếp từ bộ nhớ UI
            var btnElement = FindAffinityButtonInMemory(ingameUi);
            if (btnElement != null && btnElement.IsValid)
            {
                var rect = btnElement.GetClientRect();
                if (rect.Width > 0 && rect.Height > 0)
                {
                    var pos = new Vector2(rect.Center.X, rect.Center.Y);
                    MouseHelper.LeftClickAt(pos, 60, 50);
                    LogHelper.Info($"[Affinity Button - RAM] Đã bấm nút cất nhanh tại: ({pos.X:F0}, {pos.Y:F0})");
                    return;
                }
            }

            // 2. Fallback: Dùng tọa độ chuẩn góc dưới bên phải Stash Panel (1080p: X=632, Y=705)
            var realWin = _gc.Window.GetWindowRectangleReal();
            if (realWin.Width <= 0 || realWin.Height <= 0) realWin = _gc.Window.GetWindowRectangle();
            if (realWin.Width <= 0 || realWin.Height <= 0) return;

            var scaleX = realWin.Width / 1920f;
            var scaleY = realWin.Height / 1080f;

            var customX = _settings?.DepositButtonX?.Value ?? 632;
            var customY = _settings?.DepositButtonY?.Value ?? 705;

            var targetPos = new Vector2(realWin.Left + customX * scaleX, realWin.Top + customY * scaleY);
            MouseHelper.LeftClickAt(targetPos, 60, 50);
            LogHelper.Info($"[Affinity Button - Tọa độ] Đã bấm nút cất nhanh tại: ({targetPos.X:F0}, {targetPos.Y:F0})");
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"ClickAffinityDepositButton error: {ex.Message}");
        }
    }

    private static Element? FindAffinityButtonInMemory(Element? ingameUi)
    {
        if (ingameUi == null) return null;

        var candidates = new List<Element>();
        FindElementsRecursive(ingameUi, candidates, 0);

        foreach (var c in candidates)
        {
            var rect = c.GetClientRect();
            // Nút trên Ảnh 2 có kích thước khoảng 20x20 đến 45x45 px
            if (rect.Width >= 18 && rect.Width <= 50 && rect.Height >= 18 && rect.Height <= 50)
            {
                var txt = c.Text ?? string.Empty;
                var tooltip = c.Tooltip?.Text ?? string.Empty;

                if (tooltip.Contains("Affinity", StringComparison.OrdinalIgnoreCase) ||
                    tooltip.Contains("Deposit", StringComparison.OrdinalIgnoreCase) ||
                    tooltip.Contains("Transfer", StringComparison.OrdinalIgnoreCase) ||
                    tooltip.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Affinity", StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
        }

        return null;
    }

    private static void FindElementsRecursive(Element? root, List<Element> result, int depth)
    {
        if (root == null || !root.IsValid || depth > 8) return;
        result.Add(root);

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                FindElementsRecursive(child, result, depth + 1);
            }
        }
    }

    public bool IsStashOpen(bool isGuild)
    {
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi;
            if (ingameUi == null) return false;

            if (isGuild)
            {
                return ingameUi.GuildStashElement?.IsVisible == true || ingameUi.StashElement?.IsVisible == true;
            }

            return ingameUi.StashElement?.IsVisible == true;
        }
        catch
        {
            return false;
        }
    }

    private Element? FindStashLabelOnGround(bool isGuild)
    {
        try
        {
            var labels = _gc.IngameState?.IngameUi?.ItemsOnGroundLabels;
            if (labels == null) return null;

            foreach (var l in labels)
            {
                if (l == null || !l.IsVisible || l.Label == null || !l.Label.IsValid || !l.Label.IsVisible) continue;
                var txt = (l.Label.Text ?? string.Empty).Trim();
                var path = l.ItemOnGround?.Path ?? string.Empty;

                if (isGuild)
                {
                    if (txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) && txt.Contains("Stash", StringComparison.OrdinalIgnoreCase))
                        return l.Label;
                    if (path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase))
                        return l.Label;
                }
                else
                {
                    if (txt.Equals("Stash", StringComparison.OrdinalIgnoreCase) || (txt.Contains("Stash", StringComparison.OrdinalIgnoreCase) && !txt.Contains("Guild", StringComparison.OrdinalIgnoreCase)))
                        return l.Label;
                    if (path.Contains("Stash", StringComparison.OrdinalIgnoreCase) && !path.Contains("Guild", StringComparison.OrdinalIgnoreCase))
                        return l.Label;
                }
            }
        }
        catch { }

        return null;
    }

    private ExileCore.PoEMemory.MemoryObjects.Entity? FindStashEntity(bool isGuild)
    {
        try
        {
            var entities = _gc.EntityListWrapper?.Entities ?? _gc.Entities;
            if (entities == null) return null;

            foreach (var e in entities)
            {
                if (e == null || !e.IsValid) continue;
                var path = e.Path ?? string.Empty;
                var renderName = e.RenderName ?? string.Empty;

                if (isGuild)
                {
                    if (path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase) || renderName.Contains("Guild Stash", StringComparison.OrdinalIgnoreCase))
                        return e;
                }
                else
                {
                    if ((path.Contains("MiscellaneousObjects/Stash", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/Stash", StringComparison.OrdinalIgnoreCase)) && !path.Contains("Guild", StringComparison.OrdinalIgnoreCase))
                        return e;
                    if (renderName.Equals("Stash", StringComparison.OrdinalIgnoreCase))
                        return e;
                }
            }
        }
        catch { }

        return null;
    }

    private static void NotifyBridge(string status)
    {
        try
        {
            var bridgeFile = @"D:\codecuatien\trade_bridge.json";
            var json = $"{{\"status\":\"{status}\",\"items_bought\":0,\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }
}
