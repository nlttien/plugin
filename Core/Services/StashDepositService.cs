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
using ExileCore.PoEMemory.MemoryObjects;
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

            // BƯỚC 2: BIẾN VỀ HIDEOUT (LỆNH CHAT /HIDEOUT + NÚT LEAVE HIDEOUT + PHÍM F2)
            var startAreaHash = _gc.IngameState?.Data?.CurrentAreaHash ?? 0;
            LogHelper.Info(">>> [BƯỚC 1: VỀ NHÀ] Đang thực hiện biến về Hideout cá nhân qua lệnh /hideout... <<<");

            var teleportSuccess = false;
            for (var tpAttempt = 0; tpAttempt < 3; tpAttempt++)
            {
                if (RequestStop) yield break;

                // 1. Thử click nút "LEAVE HIDEOUT" trên giao diện màn hình nếu có
                TryClickLeaveHideoutButton();
                yield return new WaitTime(150);

                // 2. Gõ lệnh chat /hideout trực tiếp vào game
                SendHideoutChatCommand();
                yield return new WaitTime(100);

                // 3. Bấm phím HomeHotkey (mặc định F2)
                var homeKey = _settings.HomeHotkey?.Value ?? Keys.F2;
                Input.KeyDown(homeKey);
                Thread.Sleep(40);
                Input.KeyUp(homeKey);

                // 4. Chờ xem bản đồ có bắt đầu load chuyển khu vực không (Tối đa 5s mỗi lần thử)
                for (var w = 0; w < 10; w++)
                {
                    yield return new WaitTime(500);
                    var currentHash = _gc.IngameState?.Data?.CurrentAreaHash ?? 0;
                    var area = _gc.Area?.CurrentArea;
                    if (currentHash != startAreaHash && area != null && (area.IsHideout || area.Name.Contains("Hideout", StringComparison.OrdinalIgnoreCase)))
                    {
                        teleportSuccess = true;
                        break;
                    }
                }

                if (teleportSuccess) break;
                LogHelper.Warn($"[VỀ NHÀ] Thử lại lệnh /hideout lần {tpAttempt + 2}...");
            }

            // Chờ nhân vật và các NPC/rương trong Hideout nạp đầy đủ (1.5s)
            yield return new WaitTime(1500);

            // BƯỚC 3: TÌM VÀ MỞ RƯƠNG (STASH HOẶC GUILD STASH)
            var isGuildStash = _settings.StashType?.Value?.Contains("Guild", StringComparison.OrdinalIgnoreCase) == true;
            var targetStashName = isGuildStash ? "GUILD STASH" : "STASH";
            LogHelper.Info($"[BƯỚC 2: TÌM RƯƠNG] Đang tìm rương {targetStashName} trong Hideout...");

            var stashOpened = false;
            for (var openAttempt = 0; openAttempt < 5; openAttempt++)
            {
                if (RequestStop) yield break;

                // 1. Thử click trực tiếp vào nhãn chữ trên mặt đất (STASH / GUILD STASH)
                var label = FindStashLabelOnGround(isGuildStash);
                var clicked = false;
                if (label != null && label.IsValid)
                {
                    var rect = label.GetClientRect();
                    if (rect.Width > 5 && rect.Height > 5)
                    {
                        var clickPos = new Vector2(rect.Center.X, rect.Center.Y);
                        LogHelper.Info($"[CLICK NHÃN RƯƠNG] Bấm vào nhãn {targetStashName} tại: ({clickPos.X:F0}, {clickPos.Y:F0})");
                        MouseHelper.LeftClickAt(clickPos, 80, 50);
                        clicked = true;
                    }
                }

                if (!clicked)
                {
                    // 2. Thử tìm Entity rương trong thế giới 3D và chiếu tọa độ lên màn hình (WorldToScreen)
                    var stashEntity = FindStashEntity(isGuildStash);
                    if (stashEntity != null && stashEntity.IsValid)
                    {
                        var sharpDxPos = _gc.IngameState.Camera.WorldToScreen(stashEntity.Pos);
                        var screenPos = new Vector2(sharpDxPos.X, sharpDxPos.Y);
                        LogHelper.Info($"[CLICK ENTITY RƯƠNG] Bấm vào vị trí rương {targetStashName} tại: ({screenPos.X:F0}, {screenPos.Y:F0})");
                        MouseHelper.LeftClickAt(screenPos, 80, 50);
                        clicked = true;
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
                yield return new WaitTime(500);
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
        var targetTabName = _settings.TargetStashTabName?.Value?.Trim() ?? "boss";
        var onlyTargetTab = _settings.OnlyDepositToTargetTab?.Value ?? true;

        if (!string.IsNullOrEmpty(targetTabName))
        {
            // Chuyển trực tiếp sang Tab mục tiêu (ví dụ: 'boss')
            yield return SwitchToTabNamed(targetTabName, isGuildStash);
            yield return new WaitTime(300);
        }
        else
        {
            // 1. Kích hoạt Plugin Stashie nếu không chỉ định Tab
            if (_settings.UseStashiePlugin?.Value == true)
            {
                var stashieKey = _settings.StashieHotkey?.Value ?? Keys.F3;
                LogHelper.Info($"[BƯỚC 4A: STASHIE] Bấm phím {stashieKey} kích hoạt Stashie...");
                Input.KeyDown(stashieKey);
                Thread.Sleep(50);
                Input.KeyUp(stashieKey);
                yield return new WaitTime(800);
            }

            // 2. Click Nút Cất Nhanh Affinity
            ClickAffinityDepositButton();
            yield return new WaitTime(400);
        }

        // 3. Ctrl+Click toàn bộ vật phẩm trong hành trang vào Tab hiện tại
        var maxCycles = onlyTargetTab ? 1 : 6;
        for (var tabCycle = 0; tabCycle < maxCycles; tabCycle++)
        {
            if (RequestStop || !IsStashOpen(isGuildStash)) yield break;

            var itemsToDeposit = GetPlayerInventoryItemsWithPositions();
            if (itemsToDeposit.Count == 0)
            {
                LogHelper.Info("[HOÀN TẤT] Toàn bộ vật phẩm đã vào rương thành công!");
                break;
            }

            LogHelper.Info($"[CTRL+SHIFT+CLICK] Đang cất {itemsToDeposit.Count} món vào Tab '{targetTabName}'...");
            foreach (var itemInfo in itemsToDeposit)
            {
                if (RequestStop || !IsStashOpen(isGuildStash)) yield break;

                // Ctrl + Shift + Left Click vào ô đồ
                MouseHelper.CtrlShiftLeftClickAt(itemInfo.Pos, 35, 35);
                yield return new WaitTime(60);
            }

            yield return new WaitTime(200);

            var afterItems = GetPlayerInventoryItemsWithPositions();
            if (afterItems.Count == 0)
            {
                LogHelper.Info("[HOÀN TẤT] Toàn bộ đồ đã được cất vào Stash!");
                break;
            }

            if (!onlyTargetTab)
            {
                LogHelper.Warn($"[CHUYỂN TAB] Còn {afterItems.Count} món chưa vào được Tab này -> Bấm [->] chuyển sang Tab tiếp theo...");
                Input.KeyDown(Keys.Right);
                Thread.Sleep(50);
                Input.KeyUp(Keys.Right);
                yield return new WaitTime(400);
            }
            else
            {
                LogHelper.Info($"[XONG] Đã cất toàn bộ vật phẩm có thể vào Tab '{targetTabName}'!");
                break;
            }
        }
    }

    private IEnumerator SwitchToTabNamed(string targetTabName, bool isGuildStash)
    {
        if (string.IsNullOrWhiteSpace(targetTabName)) yield break;

        LogHelper.Info($"[CHỌN TAB] Đang tìm và chuyển sang Tab '{targetTabName}'...");

        var ingameUi = _gc.IngameState?.IngameUi ?? _gc.Game?.IngameState?.IngameUi;
        if (ingameUi == null) yield break;

        var stashEl = (isGuildStash && ingameUi.GuildStashElement?.IsVisible == true)
            ? (ExileCore.PoEMemory.Elements.StashElement)ingameUi.GuildStashElement
            : ingameUi.StashElement;

        if (stashEl != null)
        {
            // 1. Thử click trực tiếp vào nút Tab có chữ targetTabName (ví dụ 'boss')
            var tabBtn = FindElementWithText(stashEl, targetTabName);
            if (tabBtn != null && tabBtn.IsValid && tabBtn.IsVisible)
            {
                var rect = tabBtn.GetClientRect();
                if (rect.Width > 0 && rect.Height > 0)
                {
                    MouseHelper.LeftClickAt(new Vector2(rect.Center.X, rect.Center.Y), 50, 30);
                    LogHelper.Info($"[CHỌN TAB] Đã click nút Tab '{targetTabName}' thành công!");
                    yield return new WaitTime(400);
                    yield break;
                }
            }

            // 2. Thử chuyển bằng index qua AllStashNames
            var names = stashEl.AllStashNames;
            if (names != null)
            {
                int targetIdx = -1;
                for (int i = 0; i < names.Count; i++)
                {
                    if (names[i].Equals(targetTabName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIdx = i;
                        break;
                    }
                }

                if (targetIdx >= 0)
                {
                    for (int step = 0; step < 20; step++)
                    {
                        var currentIdx = stashEl.IndexVisibleStash;
                        if (currentIdx == targetIdx)
                        {
                            LogHelper.Info($"[CHỌN TAB] Đã chuyển tới Tab '{targetTabName}' (Index {targetIdx})!");
                            break;
                        }

                        var key = currentIdx < targetIdx ? Keys.Right : Keys.Left;
                        Input.KeyDown(key);
                        Thread.Sleep(40);
                        Input.KeyUp(key);
                        yield return new WaitTime(250);
                    }
                }
            }
        }
    }

    private List<(int Col, int Row, Vector2 Pos, ServerInventory.InventSlotItem? Item)> GetPlayerInventoryItemsWithPositions()
    {
        var result = new List<(int, int, Vector2, ServerInventory.InventSlotItem?)>();
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi;
            var invPanel = ingameUi?.InventoryPanel;
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

            // 1. Đọc trực tiếp từ ServerInventory (Chính xác 100% tọa độ và item)
            var slotItems = InventorySpaceChecker.GetPlayerInventorySlotItems(_gc);
            if (slotItems.Count > 0)
            {
                foreach (var sItem in slotItems)
                {
                    if (sItem == null) continue;
                    var col = sItem.PosX;
                    var row = sItem.PosY;
                    var sx = Math.Max(1, sItem.SizeX);
                    var sy = Math.Max(1, sItem.SizeY);

                    var itemRect = sItem.GetClientRect();
                    Vector2 clickPos;
                    if (itemRect.Width > 10 && itemRect.Height > 10)
                    {
                        clickPos = new Vector2(itemRect.Center.X, itemRect.Center.Y);
                    }
                    else
                    {
                        clickPos = new Vector2(invRect.Left + (col + sx * 0.5f) * cellW, invRect.Top + (row + sy * 0.5f) * cellH);
                    }

                    result.Add((col, row, clickPos, sItem));
                }
                return result;
            }

            // 2. Dự phòng qua VisibleInventoryItems
            var items = invElement?.VisibleInventoryItems?.Where(i => i != null && i.IsValid).ToList() ?? new List<NormalInventoryItem>();
            foreach (var invItem in items)
            {
                var itemRect = invItem.GetClientRect();
                if (itemRect.Width > 10 && itemRect.Height > 10)
                {
                    var clickPos = new Vector2(itemRect.Center.X, itemRect.Center.Y);
                    result.Add((0, 0, clickPos, null));
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

            // 1. Tìm đúng loại rương được chọn (Guild Stash hoặc Stash thường)
            foreach (var l in labels)
            {
                if (l == null || !l.IsVisible || l.Label == null || !l.Label.IsValid) continue;
                var txt = (l.Label.Text ?? string.Empty).Trim();
                var txtNoTags = (l.Label.TextNoTags ?? string.Empty).Trim();
                var path = l.ItemOnGround?.Path ?? string.Empty;
                var renderName = l.ItemOnGround?.RenderName ?? string.Empty;

                if (isGuild)
                {
                    if (txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                        txtNoTags.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase) ||
                        renderName.Contains("Guild Stash", StringComparison.OrdinalIgnoreCase))
                    {
                        return l.Label;
                    }
                }
                else
                {
                    var isGuildLabel = txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                       txtNoTags.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                       path.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                       renderName.Contains("Guild", StringComparison.OrdinalIgnoreCase);

                    if (!isGuildLabel && (txt.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                          txtNoTags.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                          path.Contains("Stash", StringComparison.OrdinalIgnoreCase)))
                    {
                        return l.Label;
                    }
                }
            }

            // 2. Dự phòng: Nếu không tìm thấy loại yêu cầu, click bất kỳ rương nào có nhãn Stash trên màn hình
            foreach (var l in labels)
            {
                if (l == null || !l.IsVisible || l.Label == null || !l.Label.IsValid) continue;
                var txt = (l.Label.Text ?? string.Empty).Trim();
                var txtNoTags = (l.Label.TextNoTags ?? string.Empty).Trim();
                var path = l.ItemOnGround?.Path ?? string.Empty;

                if (txt.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                    txtNoTags.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("Stash", StringComparison.OrdinalIgnoreCase))
                {
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
            var entities = _gc.EntityListWrapper?.OnlyValidEntities ?? _gc.EntityListWrapper?.Entities ?? _gc.Entities;
            if (entities == null) return null;

            if (isGuild)
            {
                foreach (var e in entities)
                {
                    if (e == null || !e.IsValid) continue;
                    var path = e.Path ?? string.Empty;
                    var renderName = e.RenderName ?? string.Empty;

                    if (e.Type == EntityType.GuildStash ||
                        path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase) || 
                        renderName.Contains("Guild Stash", StringComparison.OrdinalIgnoreCase))
                    {
                        return e;
                    }
                }
            }
            else
            {
                foreach (var e in entities)
                {
                    if (e == null || !e.IsValid) continue;
                    var path = e.Path ?? string.Empty;
                    var renderName = e.RenderName ?? string.Empty;

                    if ((e.Type == EntityType.Stash || path.Contains("MiscellaneousObjects/Stash", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/Stash", StringComparison.OrdinalIgnoreCase) || renderName.Equals("Stash", StringComparison.OrdinalIgnoreCase)) &&
                        !path.Contains("Guild", StringComparison.OrdinalIgnoreCase) && !renderName.Contains("Guild", StringComparison.OrdinalIgnoreCase))
                    {
                        return e;
                    }
                }
            }

            // Fallback: Tìm bất kỳ rương nào
            foreach (var e in entities)
            {
                if (e == null || !e.IsValid) continue;
                var path = e.Path ?? string.Empty;
                if (e.Type == EntityType.Stash || e.Type == EntityType.GuildStash || path.Contains("Stash", StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }
        }
        catch { }

        return null;
    }

    private bool TryClickLeaveHideoutButton()
    {
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi ?? _gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return false;

            var leaveBtn = FindElementWithText(ingameUi, "LEAVE HIDEOUT") ?? FindElementWithText(ingameUi, "Leave Hideout");
            if (leaveBtn != null && leaveBtn.IsValid && leaveBtn.IsVisible)
            {
                var rect = leaveBtn.GetClientRect();
                if (rect.Width > 0 && rect.Height > 0)
                {
                    MouseHelper.LeftClickAt(new Vector2(rect.Center.X, rect.Center.Y), 50, 30);
                    LogHelper.Info(">>> [LEAVE HIDEOUT] Da click nut 'LEAVE HIDEOUT' tren man hinh! <<<");
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static ExileCore.PoEMemory.Element? FindElementWithText(ExileCore.PoEMemory.Element? root, string text)
    {
        if (root == null || !root.IsValid || !root.IsVisible) return null;
        if (string.Equals(root.Text?.Trim(), text, StringComparison.OrdinalIgnoreCase)) return root;

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                var found = FindElementWithText(child, text);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static void SendHideoutChatCommand()
    {
        try
        {
            // 1. Mo chat bang Enter
            Input.KeyDown(Keys.Enter);
            Thread.Sleep(40);
            Input.KeyUp(Keys.Enter);
            Thread.Sleep(100);

            // 2. Chon tat ca va xoa
            Input.KeyDown(Keys.LControlKey);
            Input.KeyDown(Keys.A);
            Thread.Sleep(25);
            Input.KeyUp(Keys.A);
            Input.KeyUp(Keys.LControlKey);
            Thread.Sleep(25);
            Input.KeyDown(Keys.Back);
            Thread.Sleep(25);
            Input.KeyUp(Keys.Back);
            Thread.Sleep(40);

            // 3. Go chuoi /hideout
            var keys = new[] { Keys.OemQuestion, Keys.H, Keys.I, Keys.D, Keys.E, Keys.O, Keys.U, Keys.T };
            foreach (var k in keys)
            {
                Input.KeyDown(k);
                Thread.Sleep(20);
                Input.KeyUp(k);
                Thread.Sleep(20);
            }

            Thread.Sleep(50);
            // 4. Gui lenh chat bang Enter
            Input.KeyDown(Keys.Enter);
            Thread.Sleep(40);
            Input.KeyUp(Keys.Enter);
            Thread.Sleep(100);
        }
        catch { }
    }

    private static void NotifyBridge(string status)
    {
        try
        {
            var bridgeFile = BridgePathHelper.GetBridgeFilePath();
            var json = $"{{\"status\":\"{status}\",\"items_bought\":0,\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }
}
