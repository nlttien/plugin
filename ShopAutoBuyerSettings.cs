using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer;

public class ShopAutoBuyerSettings : ISettings
{
    [Menu("Bat Plugin (Enable)")]
    public ToggleNode Enable { get; set; } = new ToggleNode(true);

    // ==========================================
    // KHU VUC NUT DUNG / TAM DUNG (STOP / PAUSE)
    // ==========================================
    [Menu("TAM DUNG TOAN BO (PAUSE / STOP)")]
    public ToggleNode PauseAutoBuyer { get; set; } = new ToggleNode(false);

    [Menu("Nut Dung Khan Cap (Emergency STOP Button)")]
    public ButtonNode EmergencyStopButton { get; set; } = new ButtonNode();

    [Menu("Phim Tat Dung / Tiep Tuc (Mac dinh F7)")]
    public HotkeyNode StopHotkey { get; set; } = new HotkeyNode(Keys.F7);

    // ==========================================
    // TOA DO NUT OK (1920x1080)
    // ==========================================
    [Menu("Toa Do Nut OK - Truc X (Chuan 1080p: 763)")]
    public RangeNode<int> OkButtonX { get; set; } = new RangeNode<int>(763, 100, 1920);

    [Menu("Toa Do Nut OK - Truc Y (Chuan 1080p: 570)")]
    public RangeNode<int> OkButtonY { get; set; } = new RangeNode<int>(570, 100, 1080);

    // ==========================================
    // TU DONG VE HIDEOUT & CAT DO VAO RUONG (STASH DEPOSIT)
    // ==========================================
    [Menu("Tu Dong Ve Cat Do Khi Day Hanh Trang")]
    public ToggleNode AutoDepositWhenFull { get; set; } = new ToggleNode(true);

    [Menu("Phim Tat Ve Hideout (Mac dinh F2)")]
    public HotkeyNode HomeHotkey { get; set; } = new HotkeyNode(Keys.F2);

    [Menu("Dung Plugin Stashie De Cat Do (Phim Tat F3)")]
    public ToggleNode UseStashiePlugin { get; set; } = new ToggleNode(true);

    [Menu("Phim Tat Stashie (Mac dinh F3)")]
    public HotkeyNode StashieHotkey { get; set; } = new HotkeyNode(Keys.F3);

    [Menu("Loai Ruong Muon Cat Do")]
    public ListNode StashType { get; set; } = new ListNode
    {
        Values = new List<string> { "Ruong Thuong (Personal Stash)", "Ruong Guild (Guild Stash)" },
        Value = "Ruong Thuong (Personal Stash)"
    };

    [Menu("Che Do Cat Do")]
    public ListNode DepositMode { get; set; } = new ListNode
    {
        Values = new List<string> { "Dung Stashie (F3) + Nut Cat Nhanh (Anh 2)", "Chi Dung Stashie (F3)", "Nut Cat Nhanh Affinity (Anh 2)", "Ctrl+Click Vao Tab Con Trong" },
        Value = "Dung Stashie (F3) + Nut Cat Nhanh (Anh 2)"
    };

    [Menu("Nguong O Trong Toi Thieu (Con duoi X o thi ve cat do)")]
    public RangeNode<int> MinFreeSlotsThreshold { get; set; } = new RangeNode<int>(2, 1, 10);

    [Menu("Toa Do Nut Cat Nhanh - Truc X (Chuan 1080p: 630)")]
    public RangeNode<int> DepositButtonX { get; set; } = new RangeNode<int>(630, 100, 1920);

    [Menu("Toa Do Nut Cat Nhanh - Truc Y (Chuan 1080p: 615)")]
    public RangeNode<int> DepositButtonY { get; set; } = new RangeNode<int>(615, 100, 1080);

    // ==========================================
    // CAI DAT CHUNG
    // ==========================================
    [Menu("Phien Ban Game (Game Version)")]
    public ListNode GameVersion { get; set; } = new ListNode
    {
        Values = new List<string> { "AutoDetect", "PathOfExile1", "PathOfExile2" },
        Value = "AutoDetect"
    };

    [Menu("Quet Tat Ca Cac Tab Trong Shop")]
    public ToggleNode ScanAllTabs { get; set; } = new ToggleNode(true);

    [Menu("Che Do Chi Highlight (Khong Mua)")]
    public ToggleNode HighlightOnlyMode { get; set; } = new ToggleNode(false);

    [Menu("Hien Bang Thong Tin Trang Thai (Overlay Box)")]
    public ToggleNode ShowStatusBox { get; set; } = new ToggleNode(true);

    // ==========================================
    // DANH SACH VAT PHAM CAN MUA (INVITATIONS, MAPS, ITEMS...)
    // ==========================================
    [Menu("Ten Vat Pham Can Mua (VD: Incandescent Invitation)")]
    public TextNode BaseNamesFilter { get; set; } = new TextNode("Incandescent Invitation");

    // ==========================================
    // TIMELESS JEWEL SPECIFIC SETTINGS
    // ==========================================
    [Menu("Che Do Chuyen Mua Timeless Jewel")]
    public ToggleNode OnlyBuyTimelessJewels { get; set; } = new ToggleNode(false);

    [Menu("Mua Brutal Restraint")]
    public ToggleNode BuyBrutalRestraint { get; set; } = new ToggleNode(true);

    [Menu("Mua Glorious Vanity")]
    public ToggleNode BuyGloriousVanity { get; set; } = new ToggleNode(true);

    [Menu("Mua Lethal Pride")]
    public ToggleNode BuyLethalPride { get; set; } = new ToggleNode(true);

    [Menu("Mua Militant Faith")]
    public ToggleNode BuyMilitantFaith { get; set; } = new ToggleNode(true);

    [Menu("Mua Elegant Hubris")]
    public ToggleNode BuyElegantHubris { get; set; } = new ToggleNode(true);

    // ==========================================
    // BO LOC GIA (PRICE FILTERS)
    // ==========================================
    [Menu("Loc Theo Gia Chaos Orb")]
    public ToggleNode BuyChaosPrice { get; set; } = new ToggleNode(true);

    [Menu("Gia Chaos Toi Thieu (Min Chaos)")]
    public RangeNode<int> MinChaosPrice { get; set; } = new RangeNode<int>(1, 0, 500);

    [Menu("Gia Chaos Toi Da (Max Chaos)")]
    public RangeNode<int> MaxChaosPrice { get; set; } = new RangeNode<int>(300, 0, 5000);

    [Menu("Mua Theo Gia Divine Orb")]
    public ToggleNode BuyDivinePrice { get; set; } = new ToggleNode(false);

    [Menu("Gia Divine Toi Da (0 = Khong mua bang Divine)")]
    public RangeNode<int> MaxDivinePrice { get; set; } = new RangeNode<int>(0, 0, 50);

    [Menu("Gia Gold Toi Da")]
    public RangeNode<int> MaxGoldPrice { get; set; } = new RangeNode<int>(50000, 0, 500000);

    [Menu("Loc Theo Ten Tuong (Leader Filter)")]
    public TextNode LeaderFilter { get; set; } = new TextNode("");

    [Menu("Loc Theo Seed Cu The (VD: 3693, 5834)")]
    public TextNode SpecificSeeds { get; set; } = new TextNode("");

    // ==========================================
    // HIEN THI & DO TRE
    // ==========================================
    [Menu("Kieu Hien Thi Label (Label Mode)")]
    public ListNode LabelMode { get; set; } = new ListNode
    {
        Values = new List<string> { "Compact (Seed Only)", "Full Name", "Border Only" },
        Value = "Full Name"
    };

    [Menu("Mau Khung Highlight")]
    public ColorNode HighlightColor { get; set; } = new ColorNode(Color.LimeGreen);

    [Menu("Do Day Vien Khung")]
    public RangeNode<int> BorderThickness { get; set; } = new RangeNode<int>(3, 1, 8);

    [Menu("Do Tre Toi Thieu (Min Delay Ms)")]
    public RangeNode<int> MinDelayMs { get; set; } = new RangeNode<int>(100, 30, 1000);

    [Menu("Do Tre Toi Da (Max Delay Ms)")]
    public RangeNode<int> MaxDelayMs { get; set; } = new RangeNode<int>(220, 50, 2000);

    // ==========================================
    // GENERAL WHITELIST ATTRIBUTES
    // ==========================================
    public ToggleNode BuyNormal { get; set; } = new ToggleNode(true);
    public ToggleNode BuyMagic { get; set; } = new ToggleNode(true);
    public ToggleNode BuyRare { get; set; } = new ToggleNode(true);
    public ToggleNode BuyUnique { get; set; } = new ToggleNode(true);
    public RangeNode<int> MinItemLevel { get; set; } = new RangeNode<int>(0, 0, 100);
    public RangeNode<int> MinQuality { get; set; } = new RangeNode<int>(0, 0, 30);
    public RangeNode<int> MinSockets { get; set; } = new RangeNode<int>(0, 0, 6);
    public RangeNode<int> MinLinks { get; set; } = new RangeNode<int>(0, 0, 6);
    public ToggleNode BuyRgbChromatic { get; set; } = new ToggleNode(false);

    public bool IsTimelessMode()
    {
        // Chỉ chạy chế độ Timeless Jewel nếu BẬT OnlyBuyTimelessJewels VÀ KHÔNG điền tên item cụ thể trong BaseNamesFilter
        var customFilter = BaseNamesFilter?.Value?.Trim();
        return OnlyBuyTimelessJewels?.Value == true && string.IsNullOrEmpty(customFilter);
    }

    public List<FilterRule> GetActiveRules()
    {
        var rules = new List<FilterRule>();
        var baseFilter = BaseNamesFilter?.Value?.Trim() ?? string.Empty;

        if (IsTimelessMode())
        {
            rules.Add(new FilterRule
            {
                Enabled = true,
                Name = "Timeless Jewel Mode",
                BaseNameFilter = "Timeless Jewel",
                MatchNormal = true,
                MatchMagic = true,
                MatchRare = true,
                MatchUnique = true
            });
            return rules;
        }

        rules.Add(new FilterRule
        {
            Enabled = true,
            Name = "Custom Item Filter",
            BaseNameFilter = baseFilter,
            MatchNormal = BuyNormal?.Value ?? true,
            MatchMagic = BuyMagic?.Value ?? true,
            MatchRare = BuyRare?.Value ?? true,
            MatchUnique = BuyUnique?.Value ?? true,
            MinItemLevel = MinItemLevel?.Value ?? 0,
            MinQuality = MinQuality?.Value ?? 0,
            MinSockets = MinSockets?.Value ?? 0,
            MinLinks = MinLinks?.Value ?? 0,
            RequireRgbSockets = BuyRgbChromatic?.Value ?? false
        });

        return rules;
    }
}
