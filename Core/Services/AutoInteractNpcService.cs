using System;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ShopAutoBuyer.Core.Utils;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Services;

public static class AutoInteractNpcService
{
    private static DateTime _lastNpcClickTime = DateTime.MinValue;
    private static DateTime _lastDialogClickTime = DateTime.MinValue;

    public static void TryOpenHideoutShop(GameController gc)
    {
        try
        {
            if (gc == null) return;

            var area = gc.Area?.CurrentArea;
            if (area == null || (!area.IsHideout && !area.Name.Contains("Hideout", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var ingameUi = gc.IngameState?.IngameUi ?? gc.Game?.IngameState?.IngameUi;
            if (ingameUi == null) return;

            // 1. Nếu cửa sổ Shop đã mở -> Không cần thao tác thêm
            if (ingameUi.PurchaseWindow?.IsVisible == true || ingameUi.PurchaseWindowHideout?.IsVisible == true)
            {
                return;
            }

            // 2. Nếu đang mở rương cất đồ -> Bỏ qua
            if (ingameUi.StashElement?.IsVisible == true || ingameUi.GuildStashElement?.IsVisible == true)
            {
                return;
            }

            // 3. Nếu đang mở hộp thoại NPC Dialog -> Tìm và click "Purchase Items" / "Shop"
            var dialog = ingameUi.NpcDialog;
            if (dialog != null && dialog.IsValid && dialog.IsVisible)
            {
                if ((DateTime.Now - _lastDialogClickTime).TotalMilliseconds < 500) return;
                _lastDialogClickTime = DateTime.Now;

                var lines = dialog.NpcLines;
                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        if (line == null || line.Element == null || !line.Element.IsValid || !line.Element.IsVisible) continue;
                        var text = line.Text?.Trim() ?? string.Empty;

                        if (text.Contains("Purchase Items", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Purchase", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Buy Items", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Shop", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Exchange", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Barter", StringComparison.OrdinalIgnoreCase))
                        {
                            var rect = line.Element.GetClientRect();
                            if (rect.Width > 0 && rect.Height > 0)
                            {
                                LogHelper.Info($"[NPC DIALOG] Bấm tùy chọn '{text}' để mở cửa sổ Shop...");
                                MouseHelper.LeftClickAt(new Vector2(rect.Center.X, rect.Center.Y), 40, 30);
                                return;
                            }
                        }
                    }

                    // Nếu có nút Continue để bỏ qua lời thoại
                    foreach (var line in lines)
                    {
                        if (line == null || line.Element == null || !line.Element.IsValid || !line.Element.IsVisible) continue;
                        var text = line.Text?.Trim() ?? string.Empty;
                        if (text.Equals("Continue", StringComparison.OrdinalIgnoreCase))
                        {
                            var rect = line.Element.GetClientRect();
                            if (rect.Width > 0 && rect.Height > 0)
                            {
                                LogHelper.Info("[NPC DIALOG] Bấm 'Continue' bỏ qua đối thoại...");
                                MouseHelper.LeftClickAt(new Vector2(rect.Center.X, rect.Center.Y), 30, 30);
                                return;
                            }
                        }
                    }
                }
                return;
            }

            // 4. Nếu chưa mở Shop và chưa mở Dialog -> Tìm và click đúng NPC Faustus / Merchant
            if ((DateTime.Now - _lastNpcClickTime).TotalMilliseconds < 1500) return;

            // Cách A: Tìm nhãn chữ trên mặt đất
            var labels = ingameUi.ItemsOnGroundLabels;
            if (labels != null)
            {
                foreach (var l in labels)
                {
                    if (l == null || !l.IsVisible || l.Label == null || !l.Label.IsValid || !l.Label.IsVisible) continue;
                    var txt = l.Label.Text?.Trim() ?? string.Empty;
                    var path = l.ItemOnGround?.Path ?? string.Empty;

                    // CHỈ khớp chính xác tên Faustus hoặc Merchant/Vendor
                    var isTargetNpc = txt.Contains("Faustus", StringComparison.OrdinalIgnoreCase) ||
                                      txt.Contains("Merchant", StringComparison.OrdinalIgnoreCase) ||
                                      txt.Contains("Black Market", StringComparison.OrdinalIgnoreCase) ||
                                      path.Contains("VillageFaustus", StringComparison.OrdinalIgnoreCase);

                    if (isTargetNpc)
                    {
                        var rect = l.Label.GetClientRect();
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            var center = new Vector2(rect.Center.X, rect.Center.Y);
                            // Kiểm tra tọa độ an toàn trong vùng màn hình game
                            if (center.X > 100 && center.X < gc.Window.GetWindowRectangle().Width - 100 &&
                                center.Y > 100 && center.Y < gc.Window.GetWindowRectangle().Height - 100)
                            {
                                _lastNpcClickTime = DateTime.Now;
                                LogHelper.Info($"[CLICK NPC] Tự động bấm vào nhãn NPC '{txt}' tại: ({center.X:F0}, {center.Y:F0})");
                                MouseHelper.LeftClickAt(center, 50, 40);
                                return;
                            }
                        }
                    }
                }
            }

            // Cách B: Tìm Entity Faustus 3D
            var entities = gc.EntityListWrapper?.OnlyValidEntities ?? gc.Entities;
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    if (entity == null || !entity.IsValid) continue;
                    var path = entity.Path ?? string.Empty;
                    var renderName = entity.RenderName ?? string.Empty;

                    if (path.Contains("VillageFaustus", StringComparison.OrdinalIgnoreCase) ||
                        renderName.Contains("Faustus", StringComparison.OrdinalIgnoreCase))
                    {
                        var camPos = gc.IngameState.Camera.WorldToScreen(entity.Pos);
                        if (camPos.X > 150 && camPos.Y > 150 && 
                            camPos.X < gc.Window.GetWindowRectangle().Width - 150 && 
                            camPos.Y < gc.Window.GetWindowRectangle().Height - 150)
                        {
                            _lastNpcClickTime = DateTime.Now;
                            LogHelper.Info($"[CLICK NPC ENTITY] Tự động bấm vào NPC '{renderName}' tại: ({camPos.X:F0}, {camPos.Y:F0})");
                            MouseHelper.LeftClickAt(new Vector2(camPos.X, camPos.Y), 50, 40);
                            return;
                        }
                    }
                }
            }
        }
        catch { }
    }
}
