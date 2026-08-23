using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using ShopAutoBuyer.Core.Adapters;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Services;
using ShopAutoBuyer.Core.Utils;
using Vector2 = System.Numerics.Vector2;
using ImGuiNET;

namespace ShopAutoBuyer;

public class ShopAutoBuyer : BaseSettingsPlugin<ShopAutoBuyerSettings>
{
    private ShopAdapterFactory _adapterFactory = new ShopAdapterFactory();
    private PurchaseExecutor? _purchaseExecutor;
    private StashDepositService? _stashDepositService;
    private Coroutine? _currentCoroutine;
    private bool _wasShopOpenLastFrame;
    private bool _isPausedByUser = false;
    private List<ShopItemInfo> _cachedMatchingItems = new List<ShopItemInfo>();
    private List<ShopItemInfo> _cachedAllItems = new List<ShopItemInfo>();
    private DateTime _lastScanTime = DateTime.MinValue;
    private DateTime _lastNoItemsSignalTime = DateTime.MinValue;
    private DateTime _lastModalClickTime = DateTime.MinValue;
    private DateTime _lastModalCheckTime = DateTime.MinValue;
    private DateTime _lastNpcClickTime = DateTime.MinValue;
    private bool _hasScannedCurrentShop = false;
    private bool _isPriceModalOpenCached = false;
    private uint _lastAreaHash = 0;

    public override bool Initialise()
    {
        Name = "Shop Auto Buyer (PoE 1 & 2)";
        _adapterFactory = new ShopAdapterFactory();
        if (GameController != null && Settings != null)
        {
            _stashDepositService = new StashDepositService(GameController, Settings);
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory, _stashDepositService);
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

        // Gan su kien cho nut Test Deposit trong menu
        if (Settings?.TestDepositButton != null)
        {
            Settings.TestDepositButton.OnPressed = () =>
            {
                LogHelper.Warn(">>> [TEST MODE] BAN DA BAM NUT TEST VE HIDEOUT & CAT DO TRONG MENU! <<<");
                StartDepositCoroutine();
            };
        }

        // Gan su kien cho nut Rut do theo filter trong menu (Anh 1 & Anh 2)
        if (Settings?.RunWithdrawByFilterButton != null)
        {
            Settings.RunWithdrawByFilterButton.OnPressed = () =>
            {
                LogHelper.Warn(">>> [WITHDRAW FILTER] BAN DA BAM NUT RUT DO THEO FILTER TRONG MENU! <<<");
                StartWithdrawByFilterCoroutine();
            };
        }

        // Gan su kien cho nut Khoi chay Web Trade
        if (Settings?.ToggleWebTradeButton != null)
        {
            Settings.ToggleWebTradeButton.OnPressed = () =>
            {
                ToggleWebTradeProcess();
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
                if (_stashDepositService == null) _stashDepositService = new StashDepositService(GameController, Settings);
                _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory, _stashDepositService);
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

            // Phim tat TEST VE HIDEOUT & CAT DO (Mac dinh F6)
            if (Settings?.TestDepositHotkey != null && Settings.TestDepositHotkey.PressedOnce())
            {
                LogHelper.Warn(">>> [TEST MODE] BAN DA BAM PHIM TAT F6 DE TEST VE HIDEOUT & CAT DO! <<<");
                StartDepositCoroutine();
            }

            // Phim tat RUT DO THEO FILTER (Mac dinh F8 - Anh 1 & Anh 2)
            if (Settings?.WithdrawByFilterHotkey != null && Settings.WithdrawByFilterHotkey.PressedOnce())
            {
                LogHelper.Warn(">>> [WITHDRAW FILTER] BAN DA BAM PHIM TAT F8 DE RUT DO THEO FILTER! <<<");
                StartWithdrawByFilterCoroutine();
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
            var currentAreaHash = GameController.IngameState?.Data?.CurrentAreaHash ?? 0;
            if (currentAreaHash != _lastAreaHash)
            {
                _lastAreaHash = currentAreaHash;
                _hasScannedCurrentShop = false;
                _isPriceModalOpenCached = false;
                PurchaseExecutor.ScannedPriceCache.Clear();
                StopAllPurchases();
                LogHelper.Info($">>> [KHU VỰC MỚI] Đã chuyển khu vực ({currentAreaHash}). Hủy toàn bộ tác vụ mua cũ và sẵn sàng cho Shop mới! <<<");
            }

            // TỰ ĐỘNG RESET CỜ KHI SHOP MỚI MỞ HOẶC KHI PYTHON GỬI LỆNH TRAVEL MỚI
            if (!_wasShopOpenLastFrame && isShopOpen)
            {
                _hasScannedCurrentShop = false;
            }

            // Kiểm tra tín hiệu từ Python Live Search qua trade_bridge.json
            try
            {
                var bridgeFile = BridgePathHelper.GetBridgeFilePath();
                if (File.Exists(bridgeFile))
                {
                    var bridgeText = File.ReadAllText(bridgeFile);
                    if (bridgeText.Contains("\"TRAVELING\"") && _hasScannedCurrentShop)
                    {
                        _hasScannedCurrentShop = false;
                        PurchaseExecutor.ScannedPriceCache.Clear();
                    }
                }
            }
            catch { }

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

            // 4. TU DONG MUA HOAN TOAN (Hands-Free): Khong di chuyen, chi can mo Shop la lap tuc quet va mua do
            if (isShopOpen && !_isPausedByUser && Settings?.HighlightOnlyMode?.Value != true && !_hasScannedCurrentShop)
            {
                if (!isRunning)
                {
                    _hasScannedCurrentShop = true;
                    StartPurchaseCoroutine();
                }
            }

            // 6. TU DONG VE HIDEOUT & CAT DO KHI DAY HANH TRANG (Tu dong dong shop neu con dang mo)
            var needsDeposit = _stashDepositService != null && _stashDepositService.NeedsDeposit();
            if (!_isPausedByUser && needsDeposit && !isRunning && !_stashDepositService.IsDepositing)
            {
                if (isShopOpen)
                {
                    Input.KeyDown(Keys.Space);
                    Thread.Sleep(30);
                    Input.KeyUp(Keys.Space);
                }
                else
                {
                    StartDepositCoroutine();
                }
            }
            else if (!_isPausedByUser && Settings?.TestDepositAfterEveryPurchase?.Value == true && !isShopOpen && !isRunning && _stashDepositService != null && !_stashDepositService.IsDepositing)
            {
                StartDepositCoroutine();
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
        ImGui.SetNextWindowPos(new Vector2(560, 170), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(400, 240), ImGuiCond.FirstUseEver);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.04f, 0.06f, 0.10f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new System.Numerics.Vector4(0.18f, 0.12f, 0.05f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new System.Numerics.Vector4(0.35f, 0.22f, 0.08f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(1.0f, 0.75f, 0.0f, 0.7f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);

        var isOpen = true;
        var title = _isPausedByUser 
            ? "🛒 POE Auto Buyer [PAUSED - F7 to Resume]" 
            : "🛒 POE Auto Buyer (ACTIVE AUTO) [F7: Pause]";

        if (ImGui.Begin(title, ref isOpen, ImGuiWindowFlags.None))
        {
            if (isShopOpen)
            {
                var maxChaos = Settings?.MaxChaosPrice?.Value ?? 300;
                var detectedText = (Settings?.IsTimelessMode() == true)
                    ? $"Shop: OPEN | Matching: {_cachedMatchingItems.Count} (Timeless 10-50c)"
                    : $"Shop: OPEN | Matching: {_cachedMatchingItems.Count} (Price <= {maxChaos}c)";
                ImGui.TextColored(new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f), detectedText);

                if (_cachedMatchingItems.Count > 0)
                {
                    var displayCount = Math.Min(_cachedMatchingItems.Count, 3);
                    for (var i = 0; i < displayCount; i++)
                    {
                        var item = _cachedMatchingItems[i];
                        ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.85f, 0.2f, 1.0f), $"* {item.DisplayName} [Slot {item.SlotX + 1},{item.SlotY + 1}]");
                        var costInfo = !string.IsNullOrWhiteSpace(item.CostString) 
                            ? $"  Cost: {item.CostString}" 
                            : $"  Seed: {item.TimelessSeed} | Leader: {item.TimelessLeader}";
                        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.9f, 1.0f, 1.0f), costInfo);
                    }

                    if (_isPausedByUser)
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.2f, 0.2f, 1.0f), "Status: >> PAUSED << (Press [F7] to resume)");
                    }
                    else
                    {
                        var isRunning = (_currentCoroutine != null && !_currentCoroutine.IsDone) || (_purchaseExecutor != null && _purchaseExecutor.IsRunning);
                        var statusMsg = isRunning ? "Status: >> AUTO-BUYING << [F7: Pause]" : "Status: >> AUTO-BUYING (Hands-Free) << [F7: Pause]";
                        ImGui.TextColored(new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f), statusMsg);
                    }
                }
                else
                {
                    if (PurchaseExecutor.RecentPurchases.Count > 0)
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.8f, 1.0f, 0.2f, 1.0f), $"Bought: {PurchaseExecutor.RecentPurchases[0]}");
                    }
                    ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.9f, 1.0f, 1.0f), "No more matching items -> Next page!");
                }
            }
            else
            {
                var waitMsg = _isPausedByUser ? "Paused. Press [F7] to resume." : "Waiting for Shop (Faustus, Merchant)... [F7: Pause]";
                ImGui.TextColored(_isPausedByUser ? new System.Numerics.Vector4(1.0f, 0.3f, 0.3f, 1.0f) : new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f), waitMsg);
            }

            // Quick Direct Price Input Box
            if (Settings != null)
            {
                ImGui.Separator();
                var minC = Settings.MinChaosPrice.Value;
                var maxC = Settings.MaxChaosPrice.Value;
                ImGui.TextDisabled("Quick Price Settings:");
                ImGui.SetNextItemWidth(70);
                if (ImGui.InputInt("Min C", ref minC, 0, 0)) Settings.MinChaosPrice.Value = Math.Max(0, minC);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70);
                if (ImGui.InputInt("Max C", ref maxC, 0, 0)) Settings.MaxChaosPrice.Value = Math.Max(0, maxC);
            }
        }
        ImGui.End();

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
    }

    private void StartPurchaseCoroutine()
    {
        if (Settings?.HighlightOnlyMode?.Value == true || _isPausedByUser) return;
        if (_currentCoroutine != null && !_currentCoroutine.IsDone) return;
        if (_purchaseExecutor != null && _purchaseExecutor.IsRunning) return;
        if (_stashDepositService != null && _stashDepositService.IsDepositing) return;

        if (_purchaseExecutor == null && GameController != null && Settings != null)
        {
            if (_stashDepositService == null) _stashDepositService = new StashDepositService(GameController, Settings);
            _purchaseExecutor = new PurchaseExecutor(GameController, Settings, _adapterFactory, _stashDepositService);
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

    private void StartDepositCoroutine()
    {
        if (Settings?.HighlightOnlyMode?.Value == true || _isPausedByUser) return;
        if (_stashDepositService != null && _stashDepositService.IsDepositing) return;

        // Dừng tiến trình mua hàng nếu đang chạy để ưu tiên về cất đồ
        if (_purchaseExecutor != null)
        {
            _purchaseExecutor.RequestStop = true;
            _purchaseExecutor.IsRunning = false;
        }

        if (_stashDepositService == null && GameController != null && Settings != null)
        {
            _stashDepositService = new StashDepositService(GameController, Settings);
        }

        if (_stashDepositService != null)
        {
            _stashDepositService.RequestStop = false;
            _currentCoroutine = new Coroutine(
                _stashDepositService.ExecuteDepositCoroutine(),
                this,
                "ShopAutoBuyer_DepositRoutine"
            );
            ExileCore.Core.ParallelRunner.Run(_currentCoroutine);
        }
    }

    public void StartWithdrawByFilterCoroutine(string? customFilter = null)
    {
        if (_isPausedByUser) return;
        if (_stashDepositService != null && _stashDepositService.IsDepositing) return;

        // Dừng tiến trình mua hàng nếu đang chạy để ưu tiên rút đồ
        if (_purchaseExecutor != null)
        {
            _purchaseExecutor.RequestStop = true;
            _purchaseExecutor.IsRunning = false;
        }

        if (_stashDepositService == null && GameController != null && Settings != null)
        {
            _stashDepositService = new StashDepositService(GameController, Settings);
        }

        if (_stashDepositService != null)
        {
            _stashDepositService.RequestStop = false;
            _currentCoroutine = new Coroutine(
                _stashDepositService.ExecuteWithdrawByFilterCoroutine(customFilter),
                this,
                "ShopAutoBuyer_WithdrawByFilterRoutine"
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
            if (_stashDepositService != null)
            {
                _stashDepositService.RequestStop = true;
                _stashDepositService.IsDepositing = false;
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
            var bridgeFile = BridgePathHelper.GetBridgeFilePath();
            var json = $"{{\"status\":\"{status}\",\"items_bought\":{boughtCount},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
            File.WriteAllText(bridgeFile, json);
        }
        catch { }
    }

    private static System.Diagnostics.Process? _pyProcess;

    private void ToggleWebTradeProcess()
    {
        try
        {
            var pyPath = @"D:\codecuatien\autobuypoe\open_profile.py";
            if (_pyProcess != null && !_pyProcess.HasExited)
            {
                _pyProcess.Kill();
                _pyProcess.Dispose();
                _pyProcess = null;
                NotifyWebTradeStatus("STOPPED");
                LogHelper.Warn(">>> [WebTrade] DA DUNG CHROME RUNNER! <<<");
            }
            else
            {
                if (!File.Exists(pyPath))
                {
                    LogHelper.Error($"Khong tim thay file open_profile.py tai: {pyPath}");
                    return;
                }

                var pythonExe = FindPythonExecutable();
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{pyPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(pyPath) ?? "",
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                if (!string.IsNullOrWhiteSpace(Settings?.TargetTradeUrl?.Value))
                {
                    psi.EnvironmentVariables["TARGET_URL"] = Settings.TargetTradeUrl.Value;
                }
                if (!string.IsNullOrWhiteSpace(Settings?.PoeEmail?.Value))
                {
                    psi.EnvironmentVariables["POE_EMAIL"] = Settings.PoeEmail.Value;
                }
                if (!string.IsNullOrWhiteSpace(Settings?.PoePassword?.Value))
                {
                    psi.EnvironmentVariables["POE_PASSWORD"] = Settings.PoePassword.Value;
                }
                if (Settings?.SellerStartIndex != null)
                {
                    psi.EnvironmentVariables["SELLER_START_INDEX"] = Settings.SellerStartIndex.Value.ToString();
                }

                _pyProcess = System.Diagnostics.Process.Start(psi);
                NotifyWebTradeStatus("WAITING_IN_GAME");
                LogHelper.Info($">>> [WebTrade] DA KHOI DONG CHROME RUNNER ({pythonExe})! <<<");
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Loi khi dieu khien Web Trade process: {ex.Message}");
        }
    }

    private ExileCore.PoEMemory.MemoryObjects.Entity? FindHideoutNpc()
    {
        try
        {
            var entities = GameController?.EntityListWrapper?.ValidEntitiesByType?[ExileCore.Shared.Enums.EntityType.Npc];
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    if (entity == null || !entity.IsValid || !entity.IsTargetable) continue;
                    var path = entity.Path ?? "";
                    if (path.Contains("Faustus", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("VillageFaustusHideout", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("Merchant", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("BlackMarket", StringComparison.OrdinalIgnoreCase))
                    {
                        return entity;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string FindPythonExecutable()
    {
        var localPythonDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python");
        if (Directory.Exists(localPythonDir))
        {
            try
            {
                var found = Directory.GetFiles(localPythonDir, "python.exe", SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
            catch { }
        }

        var candidates = new[]
        {
            @"C:\Users\Admin\AppData\Local\Programs\Python\Python314\python.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Launcher\py.exe"),
            @"C:\Program Files\Python314\python.exe",
            @"C:\Program Files\Python312\python.exe",
            @"C:\Program Files\Python311\python.exe",
            @"C:\Program Files\Python310\python.exe",
            "py",
            "python"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return "python";
    }

    public override void DrawSettings()
    {
        if (Settings == null) return;

        // 1. TONG QUAN & DIEU KHIEN (GENERAL & CONTROLS)
        var enable = Settings.Enable.Value;
        if (ImGui.Checkbox("Bat Plugin (Enable)", ref enable))
            Settings.Enable.Value = enable;

        ImGui.SameLine();
        var pause = Settings.PauseAutoBuyer.Value;
        if (ImGui.Checkbox("TAM DUNG TOAN BO (PAUSE / STOP) [F7]", ref pause))
        {
            Settings.PauseAutoBuyer.Value = pause;
            _isPausedByUser = pause;
            if (pause) StopAllPurchases();
        }

        if (ImGui.Button("DUNG KHAN CAP (EMERGENCY STOP)"))
        {
            _isPausedByUser = true;
            Settings.PauseAutoBuyer.Value = true;
            StopAllPurchases();
            NotifyWebTradeStatus("STOPPED");
            LogHelper.Warn(">>> [ShopAutoBuyer] NUT DUNG KHAN CAP DA DUOC BAM! <<<");
        }

        // 2. BO LOC GIA (PRICE FILTERS - NHAP SO TRUC TIEP)
        if (ImGui.CollapsingHeader("1. BO LOC GIA (PRICE FILTERS - NHAP SO TRUC TIEP)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var buyChaos = Settings.BuyChaosPrice.Value;
            if (ImGui.Checkbox("Loc Theo Gia Chaos Orb", ref buyChaos))
                Settings.BuyChaosPrice.Value = buyChaos;

            if (buyChaos)
            {
                var minChaos = Settings.MinChaosPrice.Value;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputInt("Gia Chaos Toi Thieu (Min Chaos)", ref minChaos, 1, 10))
                    Settings.MinChaosPrice.Value = Math.Max(0, minChaos);

                var maxChaos = Settings.MaxChaosPrice.Value;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputInt("Gia Chaos Toi Da (Max Chaos)", ref maxChaos, 1, 10))
                    Settings.MaxChaosPrice.Value = Math.Max(0, maxChaos);
            }

            var buyDivine = Settings.BuyDivinePrice.Value;
            if (ImGui.Checkbox("Mua Theo Gia Divine Orb", ref buyDivine))
                Settings.BuyDivinePrice.Value = buyDivine;

            if (buyDivine)
            {
                var maxDivine = Settings.MaxDivinePrice.Value;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputInt("Gia Divine Toi Da (0 = Khong mua bang Divine)", ref maxDivine, 1, 5))
                    Settings.MaxDivinePrice.Value = Math.Max(0, maxDivine);
            }

            var maxGold = Settings.MaxGoldPrice.Value;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Gia Gold Toi Da", ref maxGold, 500, 5000))
                Settings.MaxGoldPrice.Value = Math.Max(0, maxGold);
        }

        // 3. DANH SACH VAT PHAM & WHITELIST (ITEM FILTERS)
        if (ImGui.CollapsingHeader("2. DANH SACH VAT PHAM CAN MUA (ITEM FILTERS)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var baseNames = Settings.BaseNamesFilter.Value ?? "";
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("Ten Vat Pham Can Mua (VD: Incandescent Invitation)", ref baseNames, 256))
                Settings.BaseNamesFilter.Value = baseNames;

            var buyNorm = Settings.BuyNormal.Value;
            if (ImGui.Checkbox("Buy Normal", ref buyNorm)) Settings.BuyNormal.Value = buyNorm;
            ImGui.SameLine();
            var buyMag = Settings.BuyMagic.Value;
            if (ImGui.Checkbox("Buy Magic", ref buyMag)) Settings.BuyMagic.Value = buyMag;
            ImGui.SameLine();
            var buyRar = Settings.BuyRare.Value;
            if (ImGui.Checkbox("Buy Rare", ref buyRar)) Settings.BuyRare.Value = buyRar;
            ImGui.SameLine();
            var buyUniq = Settings.BuyUnique.Value;
            if (ImGui.Checkbox("Buy Unique", ref buyUniq)) Settings.BuyUnique.Value = buyUniq;

            var minIlvl = Settings.MinItemLevel.Value;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Min Item Level", ref minIlvl)) Settings.MinItemLevel.Value = Math.Clamp(minIlvl, 0, 100);

            var minQ = Settings.MinQuality.Value;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Min Quality", ref minQ)) Settings.MinQuality.Value = Math.Clamp(minQ, 0, 30);

            var minSock = Settings.MinSockets.Value;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Min Sockets", ref minSock)) Settings.MinSockets.Value = Math.Clamp(minSock, 0, 6);

            var minLnk = Settings.MinLinks.Value;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Min Links", ref minLnk)) Settings.MinLinks.Value = Math.Clamp(minLnk, 0, 6);

            var buyRgb = Settings.BuyRgbChromatic.Value;
            if (ImGui.Checkbox("Mua 3 Mau RGB Chromatic", ref buyRgb)) Settings.BuyRgbChromatic.Value = buyRgb;
        }

        // 4. TIMELESS JEWEL SETTINGS
        if (ImGui.CollapsingHeader("3. CHE DO TIMELESS JEWEL", ImGuiTreeNodeFlags.None))
        {
            var onlyTimeless = Settings.OnlyBuyTimelessJewels.Value;
            if (ImGui.Checkbox("Che Do Chuyen Mua Timeless Jewel", ref onlyTimeless))
                Settings.OnlyBuyTimelessJewels.Value = onlyTimeless;

            var buyBrutal = Settings.BuyBrutalRestraint.Value;
            if (ImGui.Checkbox("Mua Brutal Restraint", ref buyBrutal)) Settings.BuyBrutalRestraint.Value = buyBrutal;
            ImGui.SameLine();
            var buyGlorious = Settings.BuyGloriousVanity.Value;
            if (ImGui.Checkbox("Mua Glorious Vanity", ref buyGlorious)) Settings.BuyGloriousVanity.Value = buyGlorious;

            var buyLethal = Settings.BuyLethalPride.Value;
            if (ImGui.Checkbox("Mua Lethal Pride", ref buyLethal)) Settings.BuyLethalPride.Value = buyLethal;
            ImGui.SameLine();
            var buyMilitant = Settings.BuyMilitantFaith.Value;
            if (ImGui.Checkbox("Mua Militant Faith", ref buyMilitant)) Settings.BuyMilitantFaith.Value = buyMilitant;

            var buyHubris = Settings.BuyElegantHubris.Value;
            if (ImGui.Checkbox("Mua Elegant Hubris", ref buyHubris)) Settings.BuyElegantHubris.Value = buyHubris;

            var leader = Settings.LeaderFilter.Value ?? "";
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Loc Theo Ten Tuong (Leader Filter)", ref leader, 128)) Settings.LeaderFilter.Value = leader;

            var seeds = Settings.SpecificSeeds.Value ?? "";
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Loc Theo Seed Cu The (VD: 3693, 5834)", ref seeds, 256)) Settings.SpecificSeeds.Value = seeds;
        }

        // 5. VE HIDEOUT & CAT DO VAO RUONG (STASH DEPOSIT)
        if (ImGui.CollapsingHeader("4. VE HIDEOUT & CAT DO VAO RUONG (STASH DEPOSIT)", ImGuiTreeNodeFlags.None))
        {
            var autoDeposit = Settings.AutoDepositWhenFull.Value;
            if (ImGui.Checkbox("Tu Dong Ve Cat Do Khi Day Hanh Trang", ref autoDeposit))
                Settings.AutoDepositWhenFull.Value = autoDeposit;

            var targetTab = Settings.TargetStashTabName.Value ?? "";
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputText("Ten Tab Ruong Can Cat (VD: boss)", ref targetTab, 64))
                Settings.TargetStashTabName.Value = targetTab;

            var onlyTarget = Settings.OnlyDepositToTargetTab.Value;
            if (ImGui.Checkbox("Chi Cat Vao Dung Tab Nay (Khong doi tab khac)", ref onlyTarget))
                Settings.OnlyDepositToTargetTab.Value = onlyTarget;

            var minSlots = Settings.MinFreeSlotsThreshold.Value;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Nguong O Trong Toi Thieu (Con duoi X o thi ve cat)", ref minSlots))
                Settings.MinFreeSlotsThreshold.Value = Math.Clamp(minSlots, 1, 10);

            var useStashie = Settings.UseStashiePlugin.Value;
            if (ImGui.Checkbox("Dung Plugin Stashie De Cat Do (Phim Tat F3)", ref useStashie))
                Settings.UseStashiePlugin.Value = useStashie;

            var autoNpc = Settings.AutoInteractHideoutNpc.Value;
            if (ImGui.Checkbox("Tu Dong Bam NPC/Faustus Khi Den Hideout Nguoi Ban", ref autoNpc))
                Settings.AutoInteractHideoutNpc.Value = autoNpc;

            if (ImGui.Button("TEST NGAY: Chay thu ve Hideout & Cat do (F6)"))
            {
                LogHelper.Warn(">>> [TEST MODE] BAN DA BAM NUT TEST VE HIDEOUT & CAT DO! <<<");
                StartDepositCoroutine();
            }

            var testEvery = Settings.TestDepositAfterEveryPurchase.Value;
            if (ImGui.Checkbox("Che Do Test: Luon ve cat do sau moi chuyen mua", ref testEvery))
                Settings.TestDepositAfterEveryPurchase.Value = testEvery;
        }

        // 6. DO TRE, TOA DO & GIAO DIEN (SPEED & COORDINATES)
        if (ImGui.CollapsingHeader("5. DO TRE & TOA DO (SPEED & COORDINATES)", ImGuiTreeNodeFlags.None))
        {
            var minDelay = Settings.MinDelayMs.Value;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Do Tre Toi Thieu (Min Delay Ms)", ref minDelay, 5, 20))
                Settings.MinDelayMs.Value = Math.Clamp(minDelay, 20, 2000);

            var maxDelay = Settings.MaxDelayMs.Value;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Do Tre Toi Da (Max Delay Ms)", ref maxDelay, 5, 20))
                Settings.MaxDelayMs.Value = Math.Clamp(maxDelay, 30, 3000);

            var showOverlay = Settings.ShowStatusBox.Value;
            if (ImGui.Checkbox("Hien Bang Thong Tin Trang Thai (Overlay Box)", ref showOverlay))
                Settings.ShowStatusBox.Value = showOverlay;

            var highlightOnly = Settings.HighlightOnlyMode.Value;
            if (ImGui.Checkbox("Che Do Chi Highlight (Khong Mua)", ref highlightOnly))
                Settings.HighlightOnlyMode.Value = highlightOnly;

            var scanAll = Settings.ScanAllTabs.Value;
            if (ImGui.Checkbox("Quet Tat Ca Cac Tab Trong Shop", ref scanAll))
                Settings.ScanAllTabs.Value = scanAll;

            var okX = Settings.OkButtonX.Value;
            var okY = Settings.OkButtonY.Value;
            ImGui.SetNextItemWidth(80);
            if (ImGui.InputInt("Toa Do Nut OK - X", ref okX)) Settings.OkButtonX.Value = okX;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            if (ImGui.InputInt("Y##okY", ref okY)) Settings.OkButtonY.Value = okY;

            var depX = Settings.DepositButtonX.Value;
            var depY = Settings.DepositButtonY.Value;
            ImGui.SetNextItemWidth(80);
            if (ImGui.InputInt("Toa Do Nut Cat Nhanh - X", ref depX)) Settings.DepositButtonX.Value = depX;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            if (ImGui.InputInt("Y##depY", ref depY)) Settings.DepositButtonY.Value = depY;
        }

        // 7. RUT DO THEO FILTER (WITHDRAW BY FILTER - F8)
        if (ImGui.CollapsingHeader("6. RUT DO THEO FILTER (WITHDRAW BY FILTER - F8)", ImGuiTreeNodeFlags.None))
        {
            var filterStr = Settings.StashSearchFilter.Value ?? "";
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("Chuoi Filter Nhap Vao Ruong", ref filterStr, 256))
                Settings.StashSearchFilter.Value = filterStr;

            var autoWithdraw = Settings.AutoWithdrawHighlightedItems.Value;
            if (ImGui.Checkbox("Tu Dong Ctrl-Click Rut Cac Mon Highlight", ref autoWithdraw))
                Settings.AutoWithdrawHighlightedItems.Value = autoWithdraw;

            if (ImGui.Button("CHAY RUT DO THEO FILTER NGAY (F8)"))
            {
                LogHelper.Warn(">>> [WITHDRAW FILTER] BAN DA BAM NUT RUT DO THEO FILTER TRONG MENU! <<<");
                StartWithdrawByFilterCoroutine();
            }
        }

        // 8. CAU HINH WEB TRADE (AUTOBUYPOE)
        if (ImGui.CollapsingHeader("7. CAU HINH WEB TRADE (AUTOBUYPOE)", ImGuiTreeNodeFlags.None))
        {
            var tradeUrl = Settings.TargetTradeUrl.Value ?? "";
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("Link Tim Kiem Web Trade (TARGET_URL)", ref tradeUrl, 512))
                Settings.TargetTradeUrl.Value = tradeUrl;

            var email = Settings.PoeEmail.Value ?? "";
            ImGui.SetNextItemWidth(250);
            if (ImGui.InputText("Email Dang Nhap PoE", ref email, 128))
                Settings.PoeEmail.Value = email;

            var pwd = Settings.PoePassword.Value ?? "";
            ImGui.SetNextItemWidth(250);
            if (ImGui.InputText("Mat Khau PoE", ref pwd, 128, ImGuiInputTextFlags.Password))
                Settings.PoePassword.Value = pwd;

            var startIdx = Settings.SellerStartIndex.Value;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Vi Tri Nguoi Ban Can Mua (0 = Nguoi 1, 2 = Nguoi 3...)", ref startIdx))
                Settings.SellerStartIndex.Value = Math.Clamp(startIdx, 0, 20);

            if (ImGui.Button("KHOI CHAY / DUNG CHROME WEB TRADE"))
            {
                ToggleWebTradeProcess();
            }
        }
    }
}
