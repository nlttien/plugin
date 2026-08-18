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
                        var screenPos = _gc.IngameState.Camera.WorldToScreen(stashEntity.Pos);
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
            yield return new WaitTime(350);

            // BƯỚC 4: TIẾN HÀNH CẤT ĐỒ
            var mode = _settings.DepositMode?.Value ?? "Ket Hop Ca Hai";

            // 1. Click Nút Cất Nhanh Affinity (Ảnh 2)
            if (mode.Contains("Nut Cat Nhanh", StringComparison.OrdinalIgnoreCase) || mode.Contains("Ket Hop", StringComparison.OrdinalIgnoreCase))
            {
                LogHelper.Info("[BƯỚC 4A] Bấm nút cất nhanh Affinity (Mũi tên sang trái cạnh số Gold)...");
                ClickAffinityDepositButton();
                yield return new WaitTime(500);
            }

            // 2. Nếu còn sót đồ trong hành trang -> Ctrl + Click từng món vào Tab còn trống
            if (mode.Contains("Ctrl+Click", StringComparison.OrdinalIgnoreCase) || mode.Contains("Ket Hop", StringComparison.OrdinalIgnoreCase))
            {
                yield return ExecuteCtrlClickDepositRoutine(isGuildStash);
            }

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
        catch (Exception ex)
        {
            LogHelper.Error("Lỗi trong quá trình StashDepositService", ex);
        }
        finally
        {
            IsDepositing = false;
        }
    }

    private IEnumerator ExecuteCtrlClickDepositRoutine(bool isGuild)
    {
        var inventoryItems = InventorySpaceChecker.GetPlayerInventoryItems(_gc);
        if (inventoryItems.Count == 0) yield break;

        LogHelper.Info($"[BƯỚC 4B: CTRL+CLICK] Bắt đầu cất {inventoryItems.Count} món đồ còn lại vào Stash...");

        foreach (var invItem in inventoryItems)
        {
            if (RequestStop || !IsStashOpen(isGuild)) yield break;
            if (invItem == null || !invItem.IsValid || !invItem.IsVisible) continue;

            var rect = invItem.GetClientRect();
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            var targetPos = new Vector2(rect.Center.X, rect.Center.Y);
            MouseHelper.CtrlLeftClickAt(targetPos, 40, 40);
            yield return new WaitTime(60);
        }
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

            // 2. Fallback: Dùng tọa độ chuẩn tỉ lệ màn hình (Mặc định 1080p: X=630, Y=615 hoặc cấu hình người dùng)
            var realWin = _gc.Window.GetWindowRectangleReal();
            if (realWin.Width <= 0 || realWin.Height <= 0) realWin = _gc.Window.GetWindowRectangle();
            if (realWin.Width <= 0 || realWin.Height <= 0) return;

            var scaleX = realWin.Width / 1920f;
            var scaleY = realWin.Height / 1080f;

            var customX = _settings?.DepositButtonX?.Value ?? 630;
            var customY = _settings?.DepositButtonY?.Value ?? 615;

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
