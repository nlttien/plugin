using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using ShopAutoBuyer.Core.Adapters;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Services;
using ShopAutoBuyer.Core.Utils;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer;

public class ShopAutoBuyer : BaseSettingsPlugin<ShopAutoBuyerSettings>
{
    private ShopAdapterFactory _adapterFactory = new ShopAdapterFactory();
    private PurchaseExecutor? _purchaseExecutor;
    private bool _wasShopOpenLastFrame;
    private List<ShopItemInfo> _cachedMatchingItems = new List<ShopItemInfo>();
    private List<ShopItemInfo> _cachedAllItems = new List<ShopItemInfo>();
    private DateTime _lastScanTime = DateTime.MinValue;
    private bool _isShopOpenCached;

    public override bool Initialise()
    {
        Name = "Shop Auto Buyer (PoE 1 & 2)";
        _adapterFactory = new ShopAdapterFactory();
        if (GameController != null && Settings != null)
        {
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);
        }

        LogHelper.Info("Plugin ShopAutoBuyer đã khởi tạo thành công (Hỗ trợ PoE 1 & PoE 2).");
        return true;
    }

    public override Job Tick()
    {
        if (Settings?.Enable?.Value != true) return null!;
        if (GameController == null) return null!;

        try
        {
            if (_adapterFactory == null)
            {
                _adapterFactory = new ShopAdapterFactory();
            }

            if (_purchaseExecutor == null && Settings != null)
            {
                _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);
            }

            var versionStr = Settings?.GameVersion?.Value ?? "AutoDetect";
            var adapter = _adapterFactory.GetAdapter(GameController, versionStr);
            if (adapter == null) return null!;

            var isShopOpen = adapter.IsShopOpen(GameController);
            _isShopOpenCached = isShopOpen;

            // 1. Kiểm tra phím tắt kích hoạt thủ công (Hotkey)
            if (Settings?.TriggerHotkey != null && Settings.TriggerHotkey.PressedOnce())
            {
                if (isShopOpen)
                {
                    if (_purchaseExecutor != null && !_purchaseExecutor.IsRunning)
                    {
                        StartPurchaseCoroutine();
                    }
                    else
                    {
                        LogHelper.Warn("Tiến trình mua đang chạy!");
                    }
                }
                else
                {
                    LogHelper.Warn("Vui lòng mở cửa sổ Shop NPC trước khi bấm phím tắt!");
                }
            }

            // 2. Tự động kích hoạt khi mở Shop (Auto-Buy on Open)
            if (Settings?.AutoBuyOnOpen?.Value == true && isShopOpen && !_wasShopOpenLastFrame)
            {
                if (Settings.HighlightOnlyMode?.Value != true && _purchaseExecutor != null && !_purchaseExecutor.IsRunning)
                {
                    StartPurchaseCoroutine();
                }
            }

            _wasShopOpenLastFrame = isShopOpen;
        }
        catch (Exception ex)
        {
            LogHelper.Error("Lỗi trong Tick()", ex);
        }

        return null!;
    }

    public override void Render()
    {
        if (Settings?.Enable?.Value != true) return;
        if (GameController == null || Graphics == null) return;

        try
        {
            if (_adapterFactory == null)
            {
                _adapterFactory = new ShopAdapterFactory();
            }

            var versionStr = Settings?.GameVersion?.Value ?? "AutoDetect";
            var adapter = _adapterFactory.GetAdapter(GameController, versionStr);
            var isShopOpen = adapter != null && adapter.IsShopOpen(GameController);

            if (isShopOpen && adapter != null)
            {
                // Quét lại danh sách mỗi 100ms để tối ưu hiệu năng Render
                if ((DateTime.Now - _lastScanTime).TotalMilliseconds > 100)
                {
                    _lastScanTime = DateTime.Now;
                    var currentItems = adapter.GetAvailableItems(GameController);
                    if (currentItems != null && currentItems.Count > 0)
                    {
                        _cachedAllItems = currentItems;
                        var activeRules = Settings.GetActiveRules();
                        _cachedMatchingItems = currentItems
                            .Where(item => item != null && ItemFilterEngine.MatchesAnyRule(item, activeRules))
                            .ToList();
                    }
                    else
                    {
                        _cachedAllItems.Clear();
                        _cachedMatchingItems.Clear();
                    }
                }

                // 1. Vẽ Highlight lên các item đạt điều kiện trong shop
                if (_cachedMatchingItems.Count > 0)
                {
                    var color = Settings?.HighlightColor?.Value ?? Color.LimeGreen;
                    var border = Settings?.BorderThickness?.Value ?? 3;

                    foreach (var item in _cachedMatchingItems)
                    {
                        if (item == null) continue;
                        var rect = item.ScreenRect;
                        if (rect.Width <= 0 || rect.Height <= 0) continue;

                        // Vẽ khung viền highlight
                        Graphics.DrawFrame(rect, color, border);

                        // Vẽ nhãn tên vật phẩm
                        var labelText = $"★ BUY: {item.DisplayName}";
                        var textPos = new Vector2(rect.Left + 2, rect.Top - 16);
                        Graphics.DrawText(labelText, textPos, color);
                    }
                }
            }
            else
            {
                if (_cachedMatchingItems.Count > 0)
                {
                    _cachedMatchingItems.Clear();
                    _cachedAllItems.Clear();
                }
            }

            // 2. Vẽ bảng thông tin trạng thái & kiểm tra ra màn hình (Status Overlay Box)
            if (Settings?.ShowStatusBox?.Value == true)
            {
                DrawStatusOverlayBox(isShopOpen);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"Lỗi trong Render(): {ex.Message}");
        }
    }

    private void DrawStatusOverlayBox(bool isShopOpen)
    {
        var boxX = 20f;
        var boxY = 120f;
        var boxWidth = 380f;

        var matchingCount = _cachedMatchingItems.Count;
        var totalCount = _cachedAllItems.Count;

        // Tính chiều cao động dựa trên số item tìm thấy
        var dynamicHeight = 75f + (matchingCount > 0 ? Math.Min(5, matchingCount) * 42f : 20f);
        var bgRect = new RectangleF(boxX, boxY, boxWidth, dynamicHeight);

        // Nền tối bán trong suốt
        Graphics.DrawBox(bgRect, new Color(15, 15, 20, 220));
        Graphics.DrawFrame(bgRect, isShopOpen ? Color.LimeGreen : Color.DarkGray, 2);

        // Tiêu đề
        var titleColor = isShopOpen ? Color.LimeGreen : Color.LightGray;
        var titleText = isShopOpen ? "● [ShopAutoBuyer] SHOP ĐANG MỞ" : "○ [ShopAutoBuyer] CHỜ MỞ SHOP NPC";
        Graphics.DrawText(titleText, new Vector2(boxX + 12, boxY + 10), titleColor);

        var currentY = boxY + 30;
        if (isShopOpen)
        {
            var summaryText = $"Tổng đồ trong tab: {totalCount} | Khớp bộ lọc: {matchingCount}";
            Graphics.DrawText(summaryText, new Vector2(boxX + 12, currentY), Color.White);
            currentY += 20;

            if (matchingCount > 0)
            {
                for (var i = 0; i < Math.Min(5, matchingCount); i++)
                {
                    var item = _cachedMatchingItems[i];
                    var itemName = $"★ {item.DisplayName} [Ô {item.SlotX + 1},{item.SlotY + 1}]";
                    Graphics.DrawText(itemName, new Vector2(boxX + 14, currentY), Color.Gold);
                    currentY += 18;

                    var costInfo = !string.IsNullOrWhiteSpace(item.CostString) 
                        ? $"  Giá: {item.CostString}" 
                        : $"  Độ hiếm: {item.Rarity} | ilvl: {item.ItemLevel}";
                    Graphics.DrawText(costInfo, new Vector2(boxX + 14, currentY), Color.LightCyan);
                    currentY += 22;
                }

                var hotkeyName = Settings?.TriggerHotkey?.Value.ToString() ?? "F5";
                Graphics.DrawText($"▶ Bấm [{hotkeyName}] để tự động mua ngay!", new Vector2(boxX + 12, currentY + 2), Color.Yellow);
            }
            else
            {
                Graphics.DrawText("Chưa có đồ nào khớp với bộ lọc trong Tab này.", new Vector2(boxX + 12, currentY), Color.Gray);
            }
        }
        else
        {
            Graphics.DrawText("Hãy đến NPC (Faustus, Helena, Vendor) và mở Shop.", new Vector2(boxX + 12, currentY), Color.LightGray);
        }
    }

    private void StartPurchaseCoroutine()
    {
        if (Settings?.HighlightOnlyMode?.Value == true)
        {
            LogHelper.Info("Đang ở chế độ 'Highlight Only' (Chỉ xem trước, không mua).");
            return;
        }

        if (_purchaseExecutor == null && GameController != null && Settings != null)
        {
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);
        }

        if (_purchaseExecutor != null)
        {
            ExileCore.Core.ParallelRunner.Run(new Coroutine(
                _purchaseExecutor.ExecutePurchaseCoroutine(),
                this,
                "ShopAutoBuyer_PurchaseRoutine"
            ));
        }
    }
}
