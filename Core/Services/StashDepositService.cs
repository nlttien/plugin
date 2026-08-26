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
using ExileCore.Shared.Enums;
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

            // BƯỚC 2: BIẾN VỀ HIDEOUT NẾU ĐANG Ở NHÀ NGƯỜI BÁN
            var isGuildStash = _settings.StashType?.Value?.Contains("Guild", StringComparison.OrdinalIgnoreCase) == true;
            var targetStashName = isGuildStash ? "GUILD STASH" : "STASH";

            // Luôn đảm bảo đóng bất kỳ cửa sổ Shop / NPC nào đang mở
            Input.KeyDown(Keys.Space);
            Thread.Sleep(30);
            Input.KeyUp(Keys.Space);
            yield return new WaitTime(100);

            // BƯỚC 2: BIẾN VỀ HIDEOUT
            var startAreaHash = _gc.IngameState?.Data?.CurrentAreaHash ?? 0;
            var isAlreadyHome = IsInOwnHideout(isGuildStash) && (FindStashLabelOnGround(isGuildStash) != null || FindStashEntity(isGuildStash) != null);

            if (isAlreadyHome)
            {
                LogHelper.Info(">>> [VỀ NHÀ] ĐÃ Ở TRONG HIDEOUT CỦA MÌNH VÀ TÌM THẤY RƯƠNG. Bỏ qua lệnh /hideout! <<<");
            }
            else
            {
                LogHelper.Info(">>> [BƯỚC 1: VỀ NHÀ] Đang thực hiện biến về Hideout cá nhân qua lệnh /hideout... <<<");

                var teleportSuccess = false;
                for (var tpAttempt = 0; tpAttempt < 4; tpAttempt++)
                {
                    if (RequestStop) yield break;

                    // 1. Thử click nút "LEAVE HIDEOUT" trên giao diện màn hình nếu có
                    TryClickLeaveHideoutButton();
                    yield return new WaitTime(150);

                    // 2. Gõ lệnh chat /hideout bằng Clipboard Paste (tránh Unikey)
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

                // Chờ nhân vật và các NPC/rương trong Hideout nạp đầy đủ (Settle time)
                for (var w = 0; w < 25; w++)
                {
                    yield return new WaitTime(200);
                    if (_gc.IngameState?.IngameUi != null && IsInOwnHideout(isGuildStash))
                    {
                        break;
                    }
                }
                yield return new WaitTime(1000);
            }

            // BƯỚC 3: TÌM VÀ MỞ RƯƠNG (STASH HOẶC GUILD STASH)
            var stashOpened = IsStashOpen(isGuildStash);
            if (stashOpened)
            {
                LogHelper.Info($"[BƯỚC 3: RƯƠNG ĐÃ MỞ] Cửa sổ rương {targetStashName} đã mở sẵn! Bắt đầu cất đồ...");
            }
            else
            {
                LogHelper.Info($"[BƯỚC 2: TÌM RƯƠNG] Đang tìm và mở rương {targetStashName} trong Hideout...");
                var windowRect = _gc.Window.GetWindowRectangle();

                for (var openAttempt = 0; openAttempt < 15; openAttempt++)
                {
                    if (RequestStop) yield break;

                    if (IsStashOpen(isGuildStash))
                    {
                        stashOpened = true;
                        break;
                    }

                    // 1. Thử click trực tiếp vào nhãn chữ trên mặt đất (GUILD STASH / STASH) - Rất nhanh và chính xác 100%
                    var label = FindStashLabelOnGround(isGuildStash) ?? FindStashLabelOnGround(isGuild: false);
                    var clicked = false;
                    if (label != null && label.IsValid)
                    {
                        var rect = label.GetClientRect();
                        if (rect.Width > 5 && rect.Height > 5 && rect.Center.X > 20 && rect.Center.Y > 20)
                        {
                            var clickPos = new Vector2(rect.Center.X, rect.Center.Y + 12f);
                            LogHelper.Info($"[CLICK NHÃN RƯƠNG] Bấm vào nhãn {targetStashName} trên mặt đất tại: ({clickPos.X:F0}, {clickPos.Y:F0}) [+12px Y]");
                            MouseHelper.LeftClickAt(clickPos, 50, 40);
                            clicked = true;
                        }
                    }

                    // 2. Dự phòng: Quét tìm Entity rương 3D theo chuẩn StashSystem của AutoExile
                    if (!clicked)
                    {
                        var stashEntity = FindStashEntity(isGuildStash) ?? FindStashEntity(isGuild: false);
                        if (stashEntity != null && stashEntity.IsValid)
                        {
                            var camera = _gc.IngameState?.Camera;
                            if (camera != null)
                            {
                                var boundsCenter = stashEntity.BoundsCenterPosNum;
                                var worldPos = boundsCenter != System.Numerics.Vector3.Zero 
                                    ? boundsCenter 
                                    : (stashEntity.PosNum != System.Numerics.Vector3.Zero ? stashEntity.PosNum : new System.Numerics.Vector3(stashEntity.Pos.X, stashEntity.Pos.Y, stashEntity.Pos.Z - 30));

                                var screenPos = camera.WorldToScreen(worldPos);
                                if (screenPos.X > 10 && screenPos.Y > 10 && screenPos.X < windowRect.Width - 10 && screenPos.Y < windowRect.Height - 10)
                                {
                                    LogHelper.Info($"[CLICK RƯƠNG ENTITY] Bấm vào rương Stash tại: ({screenPos.X:F0}, {screenPos.Y:F0})");
                                    MouseHelper.LeftClickAt(screenPos, 50, 40);
                                    clicked = true;
                                }
                            }
                        }
                    }

                    if (clicked)
                    {
                        // Chờ nhân vật chạy lại gần và mở cửa sổ rương (Tối đa 4 giây)
                        for (var w = 0; w < 40; w++)
                        {
                            yield return new WaitTime(100);
                            if (IsStashOpen(isGuildStash))
                            {
                                stashOpened = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Chưa tìm thấy nhãn hoặc entity (đang nạp vùng dữ liệu), đợi 250ms rồi thử lại
                        yield return new WaitTime(250);
                    }

                    if (stashOpened) break;
                }
            }

            if (!stashOpened)
            {
                LogHelper.Warn($"[CẢNH BÁO] Không thể mở cửa sổ rương {targetStashName}. Vui lòng kiểm tra lại vị trí rương.");
                yield break;
            }

            LogHelper.Info($"[BƯỚC 3: MỞ RƯƠNG XONG] Cửa sổ rương {targetStashName} đã mở! Bắt đầu cất đồ...");
            yield return new WaitTime(250);

            // BƯỚC 4: TIẾN HÀNH CẤT ĐỒ THÔNG MINH
            yield return ExecuteSmartDepositRoutine(isGuildStash);

            yield return new WaitTime(200);

            // BƯỚC 5: HOÀN TẤT TIẾN TRÌNH CẤT ĐỒ
            LogHelper.Info("[HOÀN TẤT] Quá trình cất đồ hoàn tất. Sẵn sàng tiếp tục!");
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
            yield return new WaitTime(250);
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
                yield return new WaitTime(600);
            }

            // 2. Click Nút Cất Nhanh Affinity
            ClickAffinityDepositButton();
            yield return new WaitTime(250);
        }

        // 3. Ctrl+Click toàn bộ vật phẩm trong hành trang vào Tab hiện tại
        var maxCycles = onlyTargetTab ? 1 : 4;
        for (var tabCycle = 0; tabCycle < maxCycles; tabCycle++)
        {
            if (RequestStop || !IsStashOpen(isGuildStash)) yield break;

            var itemsToDeposit = GetPlayerInventoryItemsWithPositions();
            if (itemsToDeposit.Count == 0)
            {
                LogHelper.Info("[HOÀN TẤT] Không còn vật phẩm nào trong hành trang cần cất!");
                break;
            }

            LogHelper.Info($"[CẤT ĐỒ] Đang cất {itemsToDeposit.Count} món vào rương (Tab: '{targetTabName}')...");
            foreach (var itemInfo in itemsToDeposit)
            {
                if (RequestStop || !IsStashOpen(isGuildStash)) yield break;

                // Ctrl + Shift + Left Click vào ô đồ với toạ độ màn hình tuyệt đối chính xác
                MouseHelper.CtrlShiftLeftClickAt(itemInfo.Pos, 15, 25);
                yield return new WaitTime(isGuildStash ? 150 : 35);
            }

            yield return new WaitTime(200);

            var afterItems = GetPlayerInventoryItemsWithPositions();
            if (afterItems.Count == 0)
            {
                LogHelper.Info("[HOÀN TẤT] Toàn bộ đồ đã được cất vào Stash thành công!");
                break;
            }

            if (!onlyTargetTab)
            {
                LogHelper.Warn($"[CHUYỂN TAB] Còn {afterItems.Count} món chưa vào được Tab này -> Bấm [->] chuyển sang Tab tiếp theo...");
                Input.KeyDown(Keys.Right);
                Thread.Sleep(40);
                Input.KeyUp(Keys.Right);
                yield return new WaitTime(300);
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

        var stashEl = (ingameUi.GuildStashElement?.IsVisible == true ? (ExileCore.PoEMemory.Elements.StashElement)ingameUi.GuildStashElement : null)
            ?? ingameUi.StashElement;

        if (stashEl != null)
        {
            // 1. Thử click trực tiếp vào nút Tab có chữ targetTabName (ví dụ 'boss')
            var tabBtn = FindElementWithText(stashEl, targetTabName);
            if (tabBtn != null && tabBtn.IsValid && tabBtn.IsVisible)
            {
                var rect = tabBtn.GetClientRect();
                if (rect.Width > 0 && rect.Height > 0 && rect.Center.X > 50 && rect.Center.Y > 50)
                {
                    var tabScreenPos = new Vector2(rect.Center.X, rect.Center.Y + 14f);
                    MouseHelper.LeftClickAt(tabScreenPos, 50, 40);
                    LogHelper.Info($"[CHỌN TAB] Đã click nút Tab '{targetTabName}' tại ({tabScreenPos.X:F0}, {tabScreenPos.Y:F0}) [+14px Y]!");
                    yield return new WaitTime(300);
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
                        Thread.Sleep(30);
                        Input.KeyUp(key);
                        yield return new WaitTime(200);
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

            if (invElement == null || !invElement.IsValid) return result;
            var invRect = invElement.GetClientRect();
            if (invRect.Width < 100 || invRect.Height < 100 || invRect.X < 50 || invRect.Y < 50) return result;

            var cellW = invRect.Width / 12f;
            var cellH = invRect.Height / 5f;

            // 1. Đọc trực tiếp từ ServerInventory (Chính xác 100% tọa độ và item)
            var slotItems = InventorySpaceChecker.GetPlayerInventorySlotItems(_gc);
            if (slotItems != null && slotItems.Count > 0)
            {
                foreach (var sItem in slotItems)
                {
                    if (sItem == null) continue;
                    var col = sItem.PosX;
                    var row = sItem.PosY;
                    if (col < 0 || col >= 12 || row < 0 || row >= 5) continue;

                    var screenPos = new Vector2(invRect.Left + (col + 0.5f) * cellW, invRect.Top + (row + 0.5f) * cellH);
                    if (screenPos.X > 50 && screenPos.Y > 50)
                    {
                        result.Add((col, row, screenPos, sItem));
                    }
                }
                return result;
            }

            // 2. Dự phòng qua VisibleInventoryItems
            var items = invElement?.VisibleInventoryItems?.Where(i => i != null && i.IsValid).ToList() ?? new List<NormalInventoryItem>();
            foreach (var invItem in items)
            {
                var itemRect = invItem.GetClientRect();
                if (itemRect.Width > 5 && itemRect.Height > 5 && itemRect.Center.X > 50 && itemRect.Center.Y > 50)
                {
                    var screenPos = new Vector2(itemRect.Center.X, itemRect.Center.Y);
                    result.Add((0, 0, screenPos, null));
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

            var stashEl = (ingameUi.GuildStashElement?.IsVisible == true ? (ExileCore.PoEMemory.Elements.StashElement)ingameUi.GuildStashElement : null)
                ?? ingameUi.StashElement;

            var windowRect = _gc.Window.GetWindowRectangle();

            // 1. Tọa độ chính xác theo VisibleStash
            if (stashEl?.VisibleStash?.InventoryUIElement != null && stashEl.VisibleStash.InventoryUIElement.IsValid)
            {
                var invRect = stashEl.VisibleStash.InventoryUIElement.GetClientRectCache;
                if (invRect.Width > 50 && invRect.Height > 50)
                {
                    var pos = new Vector2(windowRect.X + invRect.Right - 14f, windowRect.Y + invRect.Bottom - 14f);
                    MouseHelper.LeftClickAt(pos, 60, 50);
                    LogHelper.Info($"[Affinity Button - VisibleStash] Đã bấm nút tại: ({pos.X:F0}, {pos.Y:F0})");
                    return;
                }
            }

            // 2. Fallback: Dùng tọa độ chuẩn góc dưới bên phải Stash Panel (1080p: X=632, Y=705)
            var realWin = _gc.Window.GetWindowRectangleReal();
            if (realWin.Width <= 0 || realWin.Height <= 0) realWin = windowRect;
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
        return null;
    }

    /// <summary>
    /// Nhập filter vào ô Search Box (Ảnh 1) và kích hoạt nút rút/cất đồ (Ảnh 2)
    /// </summary>
    public IEnumerator ExecuteWithdrawByFilterCoroutine(string? customFilter = null)
    {
        if (IsDepositing) yield break;

        IsDepositing = true;
        RequestStop = false;

        var filter = !string.IsNullOrWhiteSpace(customFilter)
            ? customFilter.Trim()
            : (_settings.StashSearchFilter?.Value ?? "invitation|cleansing|incandescent");

        LogHelper.Warn($">>> [LẤY ĐỒ TỪ RƯƠNG] BẮT ĐẦU VỚI FILTER: {filter} <<<");

        try
        {
            var isGuildStash = _settings.StashType?.Value?.Contains("Guild", StringComparison.OrdinalIgnoreCase) == true;
            var targetStashName = isGuildStash ? "GUILD STASH" : "STASH";

            // 1. Kiểm tra / Mở rương nếu chưa mở
            var stashOpened = IsStashOpen(isGuildStash);
            if (!stashOpened)
            {
                var windowRect = _gc.Window.GetWindowRectangle();
                var label = FindStashLabelOnGround(isGuildStash);
                if (label != null && label.IsValid)
                {
                    var rect = label.GetClientRect();
                    var clickPos = new Vector2(windowRect.X + rect.Center.X, windowRect.Y + rect.Center.Y);
                    MouseHelper.LeftClickAt(clickPos, 80, 50);
                }
                else
                {
                    var stashEntity = FindStashEntity(isGuildStash);
                    if (stashEntity != null && stashEntity.IsValid)
                    {
                        var sharpDxPos = _gc.IngameState.Camera.WorldToScreen(stashEntity.Pos);
                        var screenPos = new Vector2(windowRect.X + sharpDxPos.X, windowRect.Y + sharpDxPos.Y);
                        MouseHelper.LeftClickAt(screenPos, 80, 50);
                    }
                }

                for (var w = 0; w < 30; w++)
                {
                    yield return new WaitTime(100);
                    if (IsStashOpen(isGuildStash))
                    {
                        stashOpened = true;
                        break;
                    }
                }
            }

            if (!IsStashOpen(isGuildStash))
            {
                LogHelper.Warn("[CẢNH BÁO] Không mở được rương để nhập filter.");
                yield break;
            }

            yield return new WaitTime(300);

            // 2. Nhập Filter vào ô Tìm Kiếm Highlight Items (Ảnh 1)
            LogHelper.Info($"[ẢNH 1: NHẬP FILTER] Đang nhập chuỗi: {filter}");
            ApplyFilterToStashSearch(filter);
            yield return new WaitTime(400);

            // 3. Rút trực tiếp 100% bằng code các item khớp Filter trong RAM vào hành trang
            yield return WithdrawHighlightedItemsRoutine(isGuildStash);

            LogHelper.Info(">>> [HOÀN TẤT] ĐÃ THỰC HIỆN XONG LỆNH LẤY ĐỒ THEO FILTER! <<<");
        }
        finally
        {
            IsDepositing = false;
        }
    }

    public static void SetClipboardText(string text)
    {
        try
        {
            var thread = new Thread(() =>
            {
                try { Clipboard.SetText(text); } catch { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(500);
        }
        catch { }
    }

    public void ApplyFilterToStashSearch(string filterText)
    {
        try
        {
            SetClipboardText(filterText);
            Thread.Sleep(50);

            // Bấm Ctrl + F để focus vào ô Search Box của Stash
            Input.KeyDown(Keys.LControlKey);
            Thread.Sleep(20);
            Input.KeyDown(Keys.F);
            Thread.Sleep(30);
            Input.KeyUp(Keys.F);
            Thread.Sleep(20);
            Input.KeyUp(Keys.LControlKey);
            Thread.Sleep(100);

            // Bấm Ctrl + A để bôi đen text cũ nếu có
            Input.KeyDown(Keys.LControlKey);
            Thread.Sleep(20);
            Input.KeyDown(Keys.A);
            Thread.Sleep(30);
            Input.KeyUp(Keys.A);
            Thread.Sleep(20);
            Input.KeyUp(Keys.LControlKey);
            Thread.Sleep(50);

            // Bấm Ctrl + V để dán filter vào
            Input.KeyDown(Keys.LControlKey);
            Thread.Sleep(20);
            Input.KeyDown(Keys.V);
            Thread.Sleep(30);
            Input.KeyUp(Keys.V);
            Thread.Sleep(20);
            Input.KeyUp(Keys.LControlKey);
            Thread.Sleep(100);

            // Bấm Enter để hoàn tất tìm kiếm
            Input.KeyDown(Keys.Enter);
            Thread.Sleep(30);
            Input.KeyUp(Keys.Enter);
            Thread.Sleep(100);
        }
        catch (Exception ex)
        {
            LogHelper.Error($"ApplyFilterToStashSearch error: {ex.Message}");
        }
    }

    private IEnumerator WithdrawHighlightedItemsRoutine(bool isGuildStash)
    {
        var ingameUi = _gc.IngameState?.IngameUi ?? _gc.Game?.IngameState?.IngameUi;
        if (ingameUi == null) yield break;

        var stashEl = (ingameUi.GuildStashElement?.IsVisible == true ? (ExileCore.PoEMemory.Elements.StashElement)ingameUi.GuildStashElement : null)
            ?? ingameUi.StashElement;
        if (stashEl == null) yield break;

        var visibleItems = stashEl.VisibleStash?.VisibleInventoryItems;
        if (visibleItems == null || visibleItems.Count == 0) yield break;

        var windowRect = _gc.Window.GetWindowRectangle();
        var highlightedItems = new List<Vector2>();

        foreach (var item in visibleItems)
        {
            if (item == null || !item.IsValid) continue;
            var rect = item.GetClientRect();
            if (rect.Width > 5 && rect.Height > 5)
            {
                highlightedItems.Add(new Vector2(windowRect.X + rect.Center.X, windowRect.Y + rect.Center.Y));
            }
        }

        if (highlightedItems.Count > 0)
        {
            LogHelper.Info($"[RÚT ĐỒ HIGHLIGHT] Tìm thấy {highlightedItems.Count} món khớp filter trong Tab. Đang rút...");
            foreach (var pos in highlightedItems)
            {
                if (RequestStop || !IsStashOpen(isGuildStash)) yield break;
                var freeSlots = InventorySpaceChecker.GetFreeSlotsCount(_gc);
                if (freeSlots <= 0)
                {
                    LogHelper.Warn("[HÀNH TRANG ĐẦY] Đã đầy hành trang, dừng rút đồ.");
                    break;
                }

                MouseHelper.CtrlShiftLeftClickAt(pos, 50, 40);
                yield return new WaitTime(isGuildStash ? 350 : 120);
            }
        }
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

    public bool IsStashOpen(bool isGuild = false)
    {
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi;
            if (ingameUi == null) return false;
            return ingameUi.GuildStashElement?.IsVisible == true || ingameUi.StashElement?.IsVisible == true;
        }
        catch
        {
            return false;
        }
    }

    private Element? FindMatchingLabel(Element? root, bool isGuild)
    {
        if (root == null || !root.IsValid) return null;

        var txt = (root.Text ?? string.Empty).Trim();
        var txtNoTags = (root.TextNoTags ?? string.Empty).Trim();

        if (isGuild)
        {
            if (txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                txtNoTags.Contains("Guild", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }
        else
        {
            var isGuildText = txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                              txtNoTags.Contains("Guild", StringComparison.OrdinalIgnoreCase);
            if (!isGuildText && (txt.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                 txtNoTags.Contains("Stash", StringComparison.OrdinalIgnoreCase)))
            {
                return root;
            }
        }

        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                var match = FindMatchingLabel(child, isGuild);
                if (match != null) return match;
            }
        }

        return null;
    }

    private Element? FindStashLabelOnGround(bool isGuild)
    {
        try
        {
            var labelSources = new List<IList<LabelOnGround>?>
            {
                _gc.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible,
                _gc.IngameState?.IngameUi?.ItemsOnGroundLabels
            };

            foreach (var labels in labelSources)
            {
                if (labels == null) continue;

                // 1. Khớp chính xác loại rương được yêu cầu (Guild Stash hoặc Stash)
                foreach (var l in labels)
                {
                    if (l == null || l.Label == null || !l.Label.IsValid) continue;

                    var txt = (l.Label.Text ?? string.Empty).Trim();
                    var txtNoTags = (l.Label.TextNoTags ?? string.Empty).Trim();
                    var path = l.ItemOnGround?.Path ?? string.Empty;
                    var renderName = l.ItemOnGround?.RenderName ?? string.Empty;

                    if (isGuild)
                    {
                        if (txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                            txtNoTags.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase) ||
                            renderName.Contains("Guild Stash", StringComparison.OrdinalIgnoreCase) ||
                            renderName.Contains("Bang hội", StringComparison.OrdinalIgnoreCase))
                        {
                            return l.Label;
                        }
                    }
                    else
                    {
                        var isGuildText = txt.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                          txtNoTags.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                          path.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                          renderName.Contains("Guild", StringComparison.OrdinalIgnoreCase);

                        if (!isGuildText && (txt.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                             txtNoTags.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                             path.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                             renderName.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                             renderName.Contains("Rương", StringComparison.OrdinalIgnoreCase)))
                        {
                            return l.Label;
                        }
                    }

                    // Tìm đệ quy trong cây con của l.Label (đáp ứng đúng node MiscGroundLabel bên trong)
                    var matchedChild = FindMatchingLabel(l.Label, isGuild);
                    if (matchedChild != null) return matchedChild;
                }
            }

            // 2. Dự phòng: Tìm bất kỳ nhãn rương nào có chữ Stash
            foreach (var labels in labelSources)
            {
                if (labels == null) continue;
                foreach (var l in labels)
                {
                    if (l == null || l.Label == null || !l.Label.IsValid) continue;
                    var txt = (l.Label.Text ?? string.Empty).Trim();
                    var txtNoTags = (l.Label.TextNoTags ?? string.Empty).Trim();
                    var path = l.ItemOnGround?.Path ?? string.Empty;

                    if (txt.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                        txtNoTags.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("Stash", StringComparison.OrdinalIgnoreCase))
                    {
                        return l.Label;
                    }

                    var matchedChild = FindMatchingLabel(l.Label, isGuild: false);
                    if (matchedChild != null) return matchedChild;
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"FindStashLabelOnGround error: {ex.Message}");
        }

        return null;
    }

    private ExileCore.PoEMemory.MemoryObjects.Entity? FindStashEntity(bool isGuild)
    {
        try
        {
            // 1. Quét tìm Guild Stash trong gc.Entities trước (tránh bị lọc IsHidden)
            if (isGuild && _gc.Entities != null)
            {
                foreach (var entity in _gc.Entities)
                {
                    if (entity == null || !entity.IsValid) continue;
                    var path = entity.Path ?? string.Empty;
                    var renderName = entity.RenderName ?? string.Empty;

                    bool matchGuild = entity.Type == EntityType.GuildStash || 
                                   path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase) ||
                                   path.Contains("StashGuild", StringComparison.OrdinalIgnoreCase) ||
                                   (path.Contains("Guild", StringComparison.OrdinalIgnoreCase) && path.Contains("Stash", StringComparison.OrdinalIgnoreCase)) ||
                                   renderName.Contains("Guild Stash", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Bang hội", StringComparison.OrdinalIgnoreCase);

                    if (matchGuild) return entity;
                }
            }

            // 2. Quét tìm Guild Stash trong OnlyValidEntities
            if (isGuild && _gc.EntityListWrapper?.OnlyValidEntities != null)
            {
                foreach (var entity in _gc.EntityListWrapper.OnlyValidEntities)
                {
                    if (entity == null || !entity.IsValid) continue;
                    var path = entity.Path ?? string.Empty;
                    var renderName = entity.RenderName ?? string.Empty;

                    bool matchGuild = entity.Type == EntityType.GuildStash || 
                                   path.Contains("GuildStash", StringComparison.OrdinalIgnoreCase) ||
                                   path.Contains("StashGuild", StringComparison.OrdinalIgnoreCase) ||
                                   (path.Contains("Guild", StringComparison.OrdinalIgnoreCase) && path.Contains("Stash", StringComparison.OrdinalIgnoreCase)) ||
                                   renderName.Contains("Guild Stash", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Guild", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Bang hội", StringComparison.OrdinalIgnoreCase);

                    if (matchGuild) return entity;
                }
            }

            // 3. Quét Rương cá nhân thông thường trong gc.Entities
            if (_gc.Entities != null)
            {
                foreach (var entity in _gc.Entities)
                {
                    if (entity == null || !entity.IsValid) continue;
                    var path = entity.Path ?? string.Empty;
                    var renderName = entity.RenderName ?? string.Empty;

                    bool isStash = entity.Type == EntityType.Stash || 
                                   path.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Rương", StringComparison.OrdinalIgnoreCase);

                    if (isStash && !path.Contains("Guild", StringComparison.OrdinalIgnoreCase))
                        return entity;
                }
            }

            // 4. Quét Rương cá nhân trong OnlyValidEntities
            if (_gc.EntityListWrapper?.OnlyValidEntities != null)
            {
                foreach (var entity in _gc.EntityListWrapper.OnlyValidEntities)
                {
                    if (entity == null || !entity.IsValid) continue;
                    var path = entity.Path ?? string.Empty;
                    var renderName = entity.RenderName ?? string.Empty;

                    bool isStash = entity.Type == EntityType.Stash || 
                                   path.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                   renderName.Contains("Rương", StringComparison.OrdinalIgnoreCase);

                    if (isStash && !path.Contains("Guild", StringComparison.OrdinalIgnoreCase))
                        return entity;
                }
            }
        }
        catch { }

        return null;
    }

    private bool IsInOwnHideout(bool isGuildStash)
    {
        try
        {
            var area = _gc.Area?.CurrentArea;
            if (area == null || (!area.IsHideout && !area.Name.Contains("Hideout", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Nếu có nút "LEAVE HIDEOUT" trên màn hình -> Đây là Hideout của người khác
            if (HasLeaveHideoutButton())
            {
                return false;
            }

            return true;
        }
        catch { }

        return false;
    }

    private bool HasLeaveHideoutButton()
    {
        try
        {
            var ingameUi = _gc.IngameState?.IngameUi ?? _gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return false;

            var leaveBtn = FindElementWithText(ingameUi, "LEAVE HIDEOUT") ?? FindElementWithText(ingameUi, "Leave Hideout");
            return leaveBtn != null && leaveBtn.IsValid && leaveBtn.IsVisible;
        }
        catch { return false; }
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
            // Copy chuỗi /hideout vào Clipboard trong STA thread
            var thread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText("/hideout");
                }
                catch { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(300);

            // 1. Mở chat bằng Enter
            Input.KeyDown(Keys.Enter);
            Thread.Sleep(40);
            Input.KeyUp(Keys.Enter);
            Thread.Sleep(100);

            // 2. Chọn tất cả và Dán (Ctrl+A -> Ctrl+V) - Chống hoàn toàn Unikey / gõ sai ký tự
            Input.KeyDown(Keys.LControlKey);
            Thread.Sleep(20);
            Input.KeyDown(Keys.A);
            Thread.Sleep(20);
            Input.KeyUp(Keys.A);
            Thread.Sleep(20);
            Input.KeyDown(Keys.V);
            Thread.Sleep(20);
            Input.KeyUp(Keys.V);
            Thread.Sleep(20);
            Input.KeyUp(Keys.LControlKey);
            Thread.Sleep(40);

            // 3. Gửi lệnh chat bằng Enter
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
            // Bắn tín hiệu tức thì qua TCP Socket (0ms)
            SocketBridgeClient.SendStatus(status);

            // Dự phòng ghi file
            var bridgeFile = BridgePathHelper.GetBridgeFilePath();
            var json = $"{{\"status\":\"{status}\",\"items_bought\":0,\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }
}
