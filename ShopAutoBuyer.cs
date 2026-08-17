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

        LogHelper.Info("Plugin ShopAutoBuyer da khoi tao thanh cong (Ho tro PoE 1 & PoE 2).");
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

            // 1. Kiem tra phim tat kich hoat thu cong (Hotkey)
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
                        LogHelper.Warn("Tien trinh mua dang chay!");
                    }
                }
                else
                {
                    LogHelper.Warn("Vui long mo cua so Shop NPC truoc khi bam phim tat!");
                }
            }

            // 2. Tu dong kich hoat khi mo Shop (Auto-Buy on Open)
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
            LogHelper.Error("Loi trong Tick()", ex);
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
                // Quet lai danh sach moi 100ms de toi uu hieu nang Render
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

                // 1. Ve Highlight len cac item dat dieu kien trong shop
                if (_cachedMatchingItems.Count > 0)
                {
                    var color = Settings?.HighlightColor?.Value ?? Color.LimeGreen;
                    var border = Settings?.BorderThickness?.Value ?? 3;

                    foreach (var item in _cachedMatchingItems)
                    {
                        if (item == null) continue;
                        var rect = item.ScreenRect;
                        if (rect.Width <= 0 || rect.Height <= 0) continue;

                        // Ve khung vien highlight
                        Graphics.DrawFrame(rect, color, border);

                        // Ve nhan ten vat pham
                        var labelText = $"BUY: {item.DisplayName}";
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

            // 2. Ve bang thong tin trang thai ra ngoai man hinh (Status Overlay Box)
            if (Settings?.ShowStatusBox?.Value == true)
            {
                DrawStatusOverlayBox(isShopOpen);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Debug($"Loi trong Render(): {ex.Message}");
        }
    }

    private void DrawStatusOverlayBox(bool isShopOpen)
    {
        var boxX = 20f;
        var boxY = 120f;
        var boxWidth = 390f;

        var matchingCount = _cachedMatchingItems.Count;
        var totalCount = _cachedAllItems.Count;

        // Tinh chieu cao dong dua tren so item tim thay
        var dynamicHeight = 75f + (matchingCount > 0 ? Math.Min(5, matchingCount) * 44f : 22f);
        var bgRect = new RectangleF(boxX, boxY, boxWidth, dynamicHeight);

        // Nen toi ban trong suot
        Graphics.DrawBox(bgRect, new Color(15, 15, 20, 225));
        Graphics.DrawFrame(bgRect, isShopOpen ? Color.LimeGreen : Color.DarkGray, 2);

        // Tieu de
        var titleColor = isShopOpen ? Color.LimeGreen : Color.LightGray;
        var titleText = isShopOpen ? "[ShopAutoBuyer] SHOP DANG MO" : "[ShopAutoBuyer] CHO MO SHOP NPC";
        Graphics.DrawText(titleText, new Vector2(boxX + 12, boxY + 10), titleColor);

        var currentY = boxY + 30;
        if (isShopOpen)
        {
            var summaryText = $"Tong do trong tab: {totalCount} | Khop bo loc: {matchingCount}";
            Graphics.DrawText(summaryText, new Vector2(boxX + 12, currentY), Color.White);
            currentY += 20;

            if (matchingCount > 0)
            {
                for (var i = 0; i < Math.Min(5, matchingCount); i++)
                {
                    var item = _cachedMatchingItems[i];
                    var itemName = $"* {item.DisplayName} [O {item.SlotX + 1},{item.SlotY + 1}]";
                    Graphics.DrawText(itemName, new Vector2(boxX + 14, currentY), Color.Gold);
                    currentY += 18;

                    var costInfo = !string.IsNullOrWhiteSpace(item.CostString) 
                        ? $"  Gia: {item.CostString}" 
                        : $"  Do hiem: {item.Rarity} | ilvl: {item.ItemLevel}";
                    Graphics.DrawText(costInfo, new Vector2(boxX + 14, currentY), Color.LightCyan);
                    currentY += 22;
                }

                var hotkeyName = Settings?.TriggerHotkey?.Value.ToString() ?? "F6";
                Graphics.DrawText($"Bam [{hotkeyName}] de tu dong mua ngay!", new Vector2(boxX + 12, currentY + 2), Color.Yellow);
            }
            else
            {
                Graphics.DrawText("Chua co do nao khop voi bo loc trong Tab nay.", new Vector2(boxX + 12, currentY), Color.Gray);
            }
        }
        else
        {
            Graphics.DrawText("Hay den NPC (Faustus, Helena, Vendor) va mo Shop.", new Vector2(boxX + 12, currentY), Color.LightGray);
        }
    }

    private void StartPurchaseCoroutine()
    {
        if (Settings?.HighlightOnlyMode?.Value == true)
        {
            LogHelper.Info("Dang o che do 'Highlight Only' (Chi xem truoc, khong mua).");
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
