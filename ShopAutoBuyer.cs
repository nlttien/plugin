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
    private ShopAdapterFactory _adapterFactory = null!;
    private PurchaseExecutor _purchaseExecutor = null!;
    private bool _wasShopOpenLastFrame;
    private List<ShopItemInfo> _cachedMatchingItems = new();
    private DateTime _lastScanTime = DateTime.MinValue;

    public override bool Initialise()
    {
        Name = "Shop Auto Buyer (PoE 1 & 2)";
        _adapterFactory = new ShopAdapterFactory();
        _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);

        LogHelper.Info("Plugin ShopAutoBuyer đã khởi tạo thành công (Hỗ trợ PoE 1 & PoE 2).");
        return true;
    }

    public override Job? Tick()
    {
        if (!Settings.Enable.Value) return null;

        try
        {
            var adapter = _adapterFactory.GetAdapter(GameController, Settings.GameVersion.Value);
            var isShopOpen = adapter.IsShopOpen(GameController);

            // 1. Kiểm tra phím tắt kích hoạt thủ công (Hotkey)
            if (Settings.TriggerHotkey.PressedOnce())
            {
                if (isShopOpen)
                {
                    if (!_purchaseExecutor.IsRunning)
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
            if (Settings.AutoBuyOnOpen.Value && isShopOpen && !_wasShopOpenLastFrame)
            {
                if (!Settings.HighlightOnlyMode.Value && !_purchaseExecutor.IsRunning)
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

        return null;
    }

    public override void Render()
    {
        if (!Settings.Enable.Value) return;

        try
        {
            var adapter = _adapterFactory.GetAdapter(GameController, Settings.GameVersion.Value);
            if (!adapter.IsShopOpen(GameController))
            {
                _cachedMatchingItems.Clear();
                return;
            }

            // Quét lại danh sách mỗi 100ms để tối ưu hiệu năng Render
            if ((DateTime.Now - _lastScanTime).TotalMilliseconds > 100)
            {
                _lastScanTime = DateTime.Now;
                var currentItems = adapter.GetAvailableItems(GameController);
                if (currentItems != null && currentItems.Count > 0)
                {
                    var activeRules = Settings.GetActiveRules();
                    _cachedMatchingItems = currentItems
                        .Where(item => ItemFilterEngine.MatchesAnyRule(item, activeRules))
                        .ToList();
                }
                else
                {
                    _cachedMatchingItems.Clear();
                }
            }

            // Vẽ Highlight lên các item đạt điều kiện trong shop
            if (_cachedMatchingItems.Count > 0)
            {
                var color = Settings.HighlightColor.Value;
                var border = Settings.BorderThickness.Value;

                foreach (var item in _cachedMatchingItems)
                {
                    var rect = item.ScreenRect;
                    if (rect.Width <= 0 || rect.Height <= 0) continue;

                    // Vẽ khung viền highlight
                    Graphics.DrawFrame(rect, color, border);

                    // Vẽ tên vật phẩm bên trên ô
                    var labelText = $"★ BUY: {item.BaseName}";
                    var textPos = new Vector2(rect.Left + 2, rect.Top - 16);
                    Graphics.DrawText(labelText, textPos, color);
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"Lỗi trong Render(): {ex.Message}");
        }
    }

    private void StartPurchaseCoroutine()
    {
        if (Settings.HighlightOnlyMode.Value)
        {
            LogHelper.Info("Đang ở chế độ 'Highlight Only' (Chỉ xem trước, không mua).");
            return;
        }

        Core.ParallelRunner.Run(new Coroutine(
            _purchaseExecutor.ExecutePurchaseCoroutine(),
            this,
            "ShopAutoBuyer_PurchaseRoutine"
        ));
    }
}
