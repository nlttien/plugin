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
    private bool _isPausedByUser = false;
    private List<ShopItemInfo> _cachedMatchingItems = new List<ShopItemInfo>();
    private List<ShopItemInfo> _cachedAllItems = new List<ShopItemInfo>();
    private DateTime _lastScanTime = DateTime.MinValue;
    private DateTime _lastNoItemsSignalTime = DateTime.MinValue;
    private DateTime _lastModalClickTime = DateTime.MinValue;
    private DateTime _lastModalCheckTime = DateTime.MinValue;
    private bool _hasScannedCurrentShop = false;
    private bool _isShopOpenCached;
    private bool _isPriceModalOpenCached = false;

    public override bool Initialise()
    {
        Name = "Shop Auto Buyer (PoE 1 & 2)";
        _adapterFactory = new ShopAdapterFactory();
        if (GameController != null && Settings != null)
        {
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);
        }

        // Tu dong chuyen gia tri cu (787, 545) sang toa do chuan xac (750, 575)
        _isPausedByUser = false;
        _hasScannedCurrentShop = false;
        _isPriceModalOpenCached = false;
        if (Settings != null)
        {
            if (Settings.OkButtonX.Value == 787) Settings.OkButtonX.Value = 750;
            if (Settings.OkButtonY.Value == 545) Settings.OkButtonY.Value = 575;
            if (Settings.BuyDivinePrice != null) Settings.BuyDivinePrice.Value = false;
            if (Settings.MaxDivinePrice != null) Settings.MaxDivinePrice.Value = 0;
            if (Settings.PauseAutoBuyer != null) Settings.PauseAutoBuyer.Value = false;
        }

        // Gan su kien cho nut dung khan cap trong menu
        if (Settings?.EmergencyStopButton != null)
        {
            Settings.EmergencyStopButton.OnPressed = () =>
            {
                _isPausedByUser = true;
                if (Settings.PauseAutoBuyer != null) Settings.PauseAutoBuyer.Value = true;
                StopAllPurchases();
                NotifyWebTradeStatus("STOPPED");
                LogHelper.Warn(">>> [ShopAutoBuyer] NUT DUNG KHAN CAP DA DUOC BAM! (Tam dung he thong) <<<");
            };
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

            // 1. Phim tat DUNG / TIEP TUC (F7)
            if (Settings?.StopHotkey != null && Settings.StopHotkey.PressedOnce())
            {
                _isPausedByUser = !_isPausedByUser;
                if (Settings.PauseAutoBuyer != null) Settings.PauseAutoBuyer.Value = _isPausedByUser;

                if (_isPausedByUser)
                {
                    StopAllPurchases();
                    NotifyWebTradeStatus("STOPPED");
                    LogHelper.Warn(">>> [ShopAutoBuyer] DA DUNG TOAN BO TIEN TRINH TU DONG! (Bam F7 de tiep tuc) <<<");
                }
                else
                {
                    _hasScannedCurrentShop = false;
                    NotifyWebTradeStatus("WAITING_IN_GAME");
                    LogHelper.Info(">>> [ShopAutoBuyer] DA BAT LAI TIEN TRINH TU DONG! <<<");
                }
            }

            // 2. Dong bo voi Toggle PauseAutoBuyer trong Menu
            if (Settings?.PauseAutoBuyer != null && Settings.PauseAutoBuyer.Value != _isPausedByUser)
            {
                _isPausedByUser = Settings.PauseAutoBuyer.Value;
                if (_isPausedByUser)
                {
                    StopAllPurchases();
                    NotifyWebTradeStatus("STOPPED");
                }
                else
                {
                    _hasScannedCurrentShop = false;
                    NotifyWebTradeStatus("WAITING_IN_GAME");
                }
            }

            var versionStr = Settings?.GameVersion?.Value ?? "AutoDetect";
            var adapter = _adapterFactory.GetAdapter(GameController, versionStr);
            if (adapter == null) return null!;

            var isShopOpen = adapter.IsShopOpen(GameController);
            _isShopOpenCached = isShopOpen;

            // Reset trang thai scan khi Shop vua moi duoc mo ra
            if (isShopOpen && !_wasShopOpenLastFrame)
            {
                _hasScannedCurrentShop = false;
                _isPriceModalOpenCached = false;
                PurchaseExecutor.ScannedPriceCache.Clear();
            }
            else if (!isShopOpen && _wasShopOpenLastFrame)
            {
                _hasScannedCurrentShop = false;
                _isPriceModalOpenCached = false;
                PurchaseExecutor.ScannedPriceCache.Clear();
            }

            var isRunning = (_currentCoroutine != null && !_currentCoroutine.IsDone) || (_purchaseExecutor != null && _purchaseExecutor.IsRunning);

            // 3. TU DONG PHAT HIEN VA BAM NUT [ OK ] KHI XUAT HIEN HOP THOAI CANH BAO GIA (Throttle moi 200ms de toi uu 100% FPS)
            if (isShopOpen && (DateTime.Now - _lastModalCheckTime).TotalMilliseconds > 200)
            {
                _lastModalCheckTime = DateTime.Now;
                _isPriceModalOpenCached = PurchaseExecutor.IsPriceDifferenceModalOpen(GameController);
                if (_isPriceModalOpenCached && (DateTime.Now - _lastModalClickTime).TotalMilliseconds > 350)
                {
                    _lastModalClickTime = DateTime.Now;
                    PurchaseExecutor.HandlePriceDifferenceModal(GameController, Settings);
                }
            }

            // 4. TU DONG MUA HOAN TOAN (Hands-Free): Chi chay quét & mua 1 LAN DUY NHAT moi khi mo Shop (Khong lap lai vo tan)
            if (isShopOpen && !_isPausedByUser && Settings?.HighlightOnlyMode?.Value != true && !_hasScannedCurrentShop)
            {
                if (!isRunning)
                {
                    _hasScannedCurrentShop = true;
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
                        
                        if (Settings.IsTimelessMode())
                        {
                            _cachedMatchingItems = currentItems
                                .Where(item => item != null && item.IsTimelessJewel && item.Width == 1 && item.Height == 1 && item.Sockets == 0 && !PurchaseExecutor.IsOccludedByLargerItem(item, currentItems) && ItemFilterEngine.MatchesTimelessCandidate(item, Settings))
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

                    // Neu mo Shop ma khong con vat pham nao hop le, tu dong bao ve Web Trade
                    if (_cachedMatchingItems.Count == 0 && !_isPausedByUser && (_purchaseExecutor == null || !_purchaseExecutor.IsRunning))
                    {
                        if ((DateTime.Now - _lastNoItemsSignalTime).TotalSeconds > 1.5)
                        {
                            _lastNoItemsSignalTime = DateTime.Now;
                            NotifyWebTradeStatus("COMPLETED", 0);
                        }
                    }
                }

                // 1. Ve Highlight len cac item Timeless hop le trong shop
                if (_cachedMatchingItems.Count > 0)
                {
                    var border = Settings?.BorderThickness?.Value ?? 2;
                    var labelMode = Settings?.LabelMode?.Value ?? "Compact (Seed Only)";

                    foreach (var item in _cachedMatchingItems)
                    {
                        if (item == null) continue;
                        var rect = item.ScreenRect;
                        if (rect.Width <= 0 || rect.Height <= 0) continue;

                        var isConfirmedBuy = Settings.IsTimelessMode()
                            ? ItemFilterEngine.MatchesTimelessSettings(item, Settings)
                            : ItemFilterEngine.MatchesGeneralSettings(item, Settings, Settings.GetActiveRules());

                        var isDivine = !string.IsNullOrEmpty(item.CostString) && item.CostString.Contains("Divine", StringComparison.OrdinalIgnoreCase) && Settings.BuyDivinePrice?.Value != true;

                        // Neu la Divine ma khong bat mua Divine -> Khong ve highlight
                        if (isDivine) continue;

                        var color = isConfirmedBuy ? (Settings?.HighlightColor?.Value ?? Color.LimeGreen) : Color.Cyan;

                        Graphics.DrawFrame(rect, color, border);

                        if (labelMode == "Compact (Seed Only)")
                        {
                            var compactLabel = isConfirmedBuy 
                                ? (item.TimelessSeed > 0 ? $"{item.TimelessSeed}" : "BUY")
                                : (item.TimelessSeed > 0 ? $"{item.TimelessSeed} (?)" : "SCAN");
                            var textPos = new Vector2(rect.Left + 2, rect.Top + 2);
                            Graphics.DrawText(compactLabel, textPos, isConfirmedBuy ? Color.Yellow : Color.LightCyan);
                        }
                        else if (labelMode == "Full Name")
                        {
                            var labelText = isConfirmedBuy ? $"BUY: {item.DisplayName}" : $"SCAN: {item.DisplayName}";
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

            // 3. Ve khung can chinh vi tri bam nut OK khi hop thoai canh bao gia xuat hien tren man hinh
            if (isShopOpen && Settings != null && _isPriceModalOpenCached)
            {
                var realWinRect = GameController.Window.GetWindowRectangleReal();
                if (realWinRect.Width <= 0 || realWinRect.Height <= 0)
                {
                    realWinRect = GameController.Window.GetWindowRectangle();
                }

                if (realWinRect.Width > 0 && realWinRect.Height > 0)
                {
                    var scaleX = realWinRect.Width / 1920f;
                    var scaleY = realWinRect.Height / 1080f;
                    var targetX = Settings.OkButtonX.Value * scaleX;
                    var targetY = Settings.OkButtonY.Value * scaleY;

                    var boxRect = new RectangleF(targetX - 55, targetY - 16, 110, 32);
                    Graphics.DrawFrame(boxRect, Color.OrangeRed, 2);
                    Graphics.DrawText($"[OK TARGET ({Settings.OkButtonX.Value}, {Settings.OkButtonY.Value})]", new Vector2(targetX - 55, targetY - 20), Color.OrangeRed);
                }
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
        var boxW = 350f;
        var boxH = isShopOpen ? (115f + Math.Min(_cachedMatchingItems.Count, 3) * 42f) : 75f;

        Graphics.DrawBox(new RectangleF(boxX, boxY, boxW, boxH), new Color(0, 0, 0, 215));
        Graphics.DrawFrame(new RectangleF(boxX, boxY, boxW, boxH), _isPausedByUser ? Color.Red : Color.Goldenrod, 1);

        var title = _isPausedByUser 
            ? "=== POE AUTO BUYER [DA TAM DUNG] ===" 
            : "=== POE AUTO BUYER (ACTIVE AUTO) ===";
        Graphics.DrawText(title, new Vector2(boxX + 12, boxY + 8), _isPausedByUser ? Color.Red : Color.Gold);

        var currentY = boxY + 28;

        if (isShopOpen)
        {
            var maxChaos = Settings?.MaxChaosPrice?.Value ?? 300;
            var detectedText = (Settings?.IsTimelessMode() == true)
                ? $"Shop: MO | Hop le: {_cachedMatchingItems.Count} (Timeless 10-50c)"
                : $"Shop: MO | Hop le: {_cachedMatchingItems.Count} (Gia <= {maxChaos}c)";
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

                if (_isPausedByUser)
                {
                    Graphics.DrawText("Trang thai: >> DA DUNG << (Bam [F7] de tiep tuc)", new Vector2(boxX + 12, currentY + 2), Color.Red);
                }
                else
                {
                    var isRunning = (_currentCoroutine != null && !_currentCoroutine.IsDone) || (_purchaseExecutor != null && _purchaseExecutor.IsRunning);
                    var statusMsg = isRunning ? "Trang thai: >> DANG TU DONG MUA << [F7: Dung]" : "Trang thai: >> TU DONG MUA (Hands-Free) << [F7: Dung]";
                    Graphics.DrawText(statusMsg, new Vector2(boxX + 12, currentY + 2), Color.LimeGreen);
                }
            }
            else
            {
                Graphics.DrawText("Khong con do nao trong shop -> Chuyen tiep!", new Vector2(boxX + 12, currentY), Color.Cyan);
            }
        }
        else
        {
            var waitMsg = _isPausedByUser ? "Dang tam dung. Bam [F7] de tiep tuc." : "Dang cho mo Shop (Faustus, Merchant)... [F7: Dung]";
            Graphics.DrawText(waitMsg, new Vector2(boxX + 12, currentY), _isPausedByUser ? Color.Red : Color.LightGray);
        }
    }

    private void StartPurchaseCoroutine()
    {
        if (Settings?.HighlightOnlyMode?.Value == true || _isPausedByUser) return;
        if (_currentCoroutine != null && !_currentCoroutine.IsDone) return;
        if (_purchaseExecutor != null && _purchaseExecutor.IsRunning) return;

        if (_purchaseExecutor == null && GameController != null && Settings != null)
        {
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory);
        }

        if (_purchaseExecutor != null)
        {
            _purchaseExecutor.RequestStop = false;
            _currentCoroutine = new Coroutine(
                _purchaseExecutor.ExecutePurchaseCoroutine(),
                this,
                "ShopAutoBuyer_PurchaseRoutine"
            );
            ExileCore.Core.ParallelRunner.Run(_currentCoroutine);
        }
    }

    private void StopAllPurchases()
    {
        try
        {
            if (_purchaseExecutor != null)
            {
                _purchaseExecutor.RequestStop = true;
                _purchaseExecutor.IsRunning = false;
            }
            if (_currentCoroutine != null)
            {
                _currentCoroutine.Done();
            }
        }
        catch { }
    }

    private static void NotifyWebTradeStatus(string status, int boughtCount = 0)
    {
        try
        {
            var bridgeFile = @"D:\codecuatien\trade_bridge.json";
            var json = $"{{\"status\":\"{status}\",\"items_bought\":{boughtCount},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }
}
