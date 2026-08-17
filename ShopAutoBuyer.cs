using System;
using System.Collections.Generic;
using System.IO;
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
    private Coroutine? _currentCoroutine;
    private bool _wasShopOpenLastFrame;
    private List<ShopItemInfo> _cachedMatchingItems = new List<ShopItemInfo>();
    private List<ShopItemInfo> _cachedAllItems = new List<ShopItemInfo>();
    private DateTime _lastScanTime = DateTime.MinValue;
    private DateTime _lastNoItemsSignalTime = DateTime.MinValue;
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

            // TU DONG MUA HOAN TOAN (Hands-Free): Khong can bam F6
            if (isShopOpen && Settings?.HighlightOnlyMode?.Value != true)
            {
                var isRunning = (_currentCoroutine != null && !_currentCoroutine.IsDone) || (_purchaseExecutor != null && _purchaseExecutor.IsRunning);
                if (!isRunning)
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
                // Quet lai danh sach moi 100ms de cap nhat Render
                if ((DateTime.Now - _lastScanTime).TotalMilliseconds > 100)
                {
                    _lastScanTime = DateTime.Now;
                    var currentItems = adapter.GetAvailableItems(GameController);
                    if (currentItems != null && currentItems.Count > 0)
                    {
                        _cachedAllItems = currentItems;
                        
                        if (Settings.OnlyBuyTimelessJewels?.Value == true)
                        {
                            _cachedMatchingItems = currentItems
                                .Where(item => item != null && ItemFilterEngine.MatchesTimelessSettings(item, Settings))
                                .ToList();
                        }
                        else
                        {
                            var activeRules = Settings.GetActiveRules();
                            _cachedMatchingItems = currentItems
                                .Where(item => item != null && activeRules.Any(r => ItemFilterEngine.MatchesRule(item, r)))
                                .ToList();
                        }
                    }
                    else
                    {
                        _cachedAllItems.Clear();
                        _cachedMatchingItems.Clear();
                    }

                    // Neu mo Shop ma khong con vat pham nao hop le, tu dong bao ve Web Trade sau 1.5s
                    if (_cachedMatchingItems.Count == 0 && (_purchaseExecutor == null || !_purchaseExecutor.IsRunning))
                    {
                        if ((DateTime.Now - _lastNoItemsSignalTime).TotalSeconds > 2.0)
                        {
                            _lastNoItemsSignalTime = DateTime.Now;
                            NotifyWebTradeCompleted(0);
                        }
                    }
                }

                // 1. Ve Highlight len cac item dat dieu kien trong shop
                if (_cachedMatchingItems.Count > 0)
                {
                    var color = Settings?.HighlightColor?.Value ?? Color.LimeGreen;
                    var border = Settings?.BorderThickness?.Value ?? 2;
                    var labelMode = Settings?.LabelMode?.Value ?? "Compact (Seed Only)";

                    foreach (var item in _cachedMatchingItems)
                    {
                        if (item == null) continue;
                        var rect = item.ScreenRect;
                        if (rect.Width <= 0 || rect.Height <= 0) continue;

                        Graphics.DrawFrame(rect, color, border);

                        if (labelMode == "Compact (Seed Only)")
                        {
                            var compactLabel = item.TimelessSeed > 0 ? $"{item.TimelessSeed}" : "BUY";
                            var textPos = new Vector2(rect.Left + 2, rect.Top + 2);
                            Graphics.DrawText(compactLabel, textPos, Color.Yellow);
                        }
                        else if (labelMode == "Full Name")
                        {
                            var labelText = $"BUY: {item.DisplayName}";
                            var textPos = new Vector2(rect.Left + 2, rect.Top - 14);
                            Graphics.DrawText(labelText, textPos, color);
                        }
                    }
                }
            }

            // 2. Ve bang thong tin trang thai (Status Overlay Box)
            if (Settings?.ShowStatusBox?.Value == true)
            {
                RenderStatusOverlayBox(isShopOpen);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error("Loi trong Render()", ex);
        }
    }

    private void RenderStatusOverlayBox(bool isShopOpen)
    {
        var boxX = 20f;
        var boxY = 70f;
        var boxW = 340f;
        var boxH = isShopOpen ? (110f + Math.Min(_cachedMatchingItems.Count, 3) * 42f) : 70f;

        Graphics.DrawBox(new RectangleF(boxX, boxY, boxW, boxH), new Color(0, 0, 0, 210));
        Graphics.DrawFrame(new RectangleF(boxX, boxY, boxW, boxH), Color.Goldenrod, 1);

        var title = "=== POE AUTO BUYER (TIMELESS JEWELS) ===";
        Graphics.DrawText(title, new Vector2(boxX + 12, boxY + 8), Color.Gold);

        var currentY = boxY + 28;

        if (isShopOpen)
        {
            var detectedText = $"Cua hang: DANG MO | Tim thay: {_cachedMatchingItems.Count} Timeless Jewel";
            Graphics.DrawText(detectedText, new Vector2(boxX + 12, currentY), Color.LimeGreen);
            currentY += 20;

            if (_cachedMatchingItems.Count > 0)
            {
                var displayCount = Math.Min(_cachedMatchingItems.Count, 3);
                for (var i = 0; i < displayCount; i++)
                {
                    var item = _cachedMatchingItems[i];
                    var itemName = $"* {item.DisplayName} [O {item.SlotX + 1},{item.SlotY + 1}]";
                    Graphics.DrawText(itemName, new Vector2(boxX + 14, currentY), Color.Gold);
                    currentY += 18;

                    var costInfo = !string.IsNullOrWhiteSpace(item.CostString) 
                        ? $"  Gia: {item.CostString}" 
                        : $"  Seed: {item.TimelessSeed} | Leader: {item.TimelessLeader}";
                    Graphics.DrawText(costInfo, new Vector2(boxX + 14, currentY), Color.LightCyan);
                    currentY += 22;
                }

                var isRunning = (_currentCoroutine != null && !_currentCoroutine.IsDone) || (_purchaseExecutor != null && _purchaseExecutor.IsRunning);
                var statusMsg = isRunning ? "Trang thai: >> DANG TU DONG MUA <<..." : "Trang thai: >> TU DONG MUA HOAN TOAN (Hands-Free) <<";
                Graphics.DrawText(statusMsg, new Vector2(boxX + 12, currentY + 2), Color.LimeGreen);
            }
            else
            {
                Graphics.DrawText("Khong con vat pham nao trong shop nay -> Chuyen tiep!", new Vector2(boxX + 12, currentY), Color.Cyan);
            }
        }
        else
        {
            Graphics.DrawText("Dang cho mo Shop (Faustus, Merchant, Helena)...", new Vector2(boxX + 12, currentY), Color.LightGray);
        }
    }

    private void StartPurchaseCoroutine()
    {
        if (Settings?.HighlightOnlyMode?.Value == true) return;
        if (_currentCoroutine != null && !_currentCoroutine.IsDone) return;
        if (_purchaseExecutor != null && _purchaseExecutor.IsRunning) return;

        if (_purchaseExecutor == null && GameController != null && Settings != null)
        {
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);
        }

        if (_purchaseExecutor != null)
        {
            _currentCoroutine = new Coroutine(
                _purchaseExecutor.ExecutePurchaseCoroutine(),
                this,
                "ShopAutoBuyer_PurchaseRoutine"
            );
            Core.ParallelRunner.Run(_currentCoroutine);
        }
    }

    private static void NotifyWebTradeCompleted(int boughtCount)
    {
        try
        {
            var bridgeFile = @"D:\codecuatien\trade_bridge.json";
            var json = $"{{\"status\":\"COMPLETED\",\"items_bought\":{boughtCount},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }
}
