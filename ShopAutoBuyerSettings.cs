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
    public ToggleNode Enable { get; set; } = new(false);

    [Menu("Game Version", "Chọn phiên bản PoE đang chơi")]
    public ListNode GameVersion { get; set; } = new()
    {
        Values = new List<string> { "AutoDetect", "PathOfExile1", "PathOfExile2" },
        Value = "AutoDetect"
    };

    [Menu("Auto-Buy Trigger Hotkey", "Phím tắt để bắt đầu quét và mua đồ")]
    public HotkeyNode TriggerHotkey { get; set; } = new(Keys.F5);

    [Menu("Auto Buy When Shop Opens", "Tự động kích hoạt mua ngay khi mở Shop NPC")]
    public ToggleNode AutoBuyOnOpen { get; set; } = new(false);

    [Menu("Scan All Tabs", "Tự động chuyển qua tất cả các Tab trong Shop để mua hết")]
    public ToggleNode ScanAllTabs { get; set; } = new(true);

    [Menu("Highlight Only Mode (Preview)", "Chỉ vẽ khung sáng các item đạt chuẩn, KHÔNG tự bấm mua")]
    public ToggleNode HighlightOnlyMode { get; set; } = new(false);

    [Menu("Click Delays (ms)", "Thời gian nghỉ giữa các thao tác bấm chuột", 100)]
    public EmptyNode DelayHeader { get; set; } = new();

    [Menu("Min Delay (ms)", "Thời gian trễ tối thiểu (ms)", 101, 100)]
    public RangeNode<int> MinDelayMs { get; set; } = new(100, 30, 1000);

    [Menu("Max Delay (ms)", "Thời gian trễ tối đa (ms)", 102, 100)]
    public RangeNode<int> MaxDelayMs { get; set; } = new(220, 50, 2000);

    [Menu("Visual Highlight", "Tùy chỉnh màu sắc hiển thị", 200)]
    public EmptyNode VisualHeader { get; set; } = new();

    [Menu("Highlight Color", "Màu sắc khung viền item tìm thấy", 201, 200)]
    public ColorNode HighlightColor { get; set; } = new(Color.LimeGreen);

    [Menu("Border Thickness", "Độ dày viền highlight", 202, 200)]
    public RangeNode<int> BorderThickness { get; set; } = new(3, 1, 8);

    [Menu("Item Filter Rules", "Cấu hình bộ lọc vật phẩm cần mua", 300)]
    public EmptyNode FilterHeader { get; set; } = new();

    [Menu("Base Names (Whitelist)", "Danh sách tên phôi đồ cần mua (cách nhau bởi dấu phẩy)", 301, 300)]
    public TextNode BaseNamesFilter { get; set; } = new("Amethyst Ring, Heavy Belt, Two-Stone Ring, Sapphire Ring, Ruby Ring, Topaz Ring, Uncut");

    [Menu("Buy Normal Items", "Mua đồ trắng (Normal)", 302, 300)]
    public ToggleNode BuyNormal { get; set; } = new(true);

    [Menu("Buy Magic Items", "Mua đồ xanh (Magic)", 303, 300)]
    public ToggleNode BuyMagic { get; set; } = new(true);

    [Menu("Buy Rare Items", "Mua đồ vàng (Rare)", 304, 300)]
    public ToggleNode BuyRare { get; set; } = new(true);

    [Menu("Buy Unique Items", "Mua đồ cam (Unique)", 305, 300)]
    public ToggleNode BuyUnique { get; set; } = new(true);

    [Menu("Min Item Level", "Item Level (ilvl) tối thiểu (0 = bỏ qua)", 306, 300)]
    public RangeNode<int> MinItemLevel { get; set; } = new(0, 0, 100);

    [Menu("Min Quality", "Chất lượng (Quality) tối thiểu (0 = bỏ qua)", 307, 300)]
    public RangeNode<int> MinQuality { get; set; } = new(0, 0, 30);

    [Menu("Min Sockets (e.g. 6 Sockets)", "Số Socket tối thiểu", 308, 300)]
    public RangeNode<int> MinSockets { get; set; } = new(0, 0, 6);

    [Menu("Min Links (e.g. 6 Links)", "Số Link tối thiểu", 309, 300)]
    public RangeNode<int> MinLinks { get; set; } = new(0, 0, 6);

    [Menu("Buy RGB Items (Chromatic)", "Mua đồ có 3 socket liên kết Red-Green-Blue", 310, 300)]
    public ToggleNode BuyRgbChromatic { get; set; } = new(false);

    public List<FilterRule> GetActiveRules()
    {
        var rules = new List<FilterRule>();

        // Main configurable rule
        rules.Add(new FilterRule
        {
            Enabled = true,
            Name = "User Filter",
            BaseNameFilter = BaseNamesFilter.Value,
            MatchNormal = BuyNormal.Value,
            MatchMagic = BuyMagic.Value,
            MatchRare = BuyRare.Value,
            MatchUnique = BuyUnique.Value,
            MinItemLevel = MinItemLevel.Value,
            MinQuality = MinQuality.Value,
            MinSockets = MinSockets.Value,
            MinLinks = MinLinks.Value,
            RequireRgbSockets = BuyRgbChromatic.Value
        });

        // Additional standalone rule for Chromatic recipe if toggled
        if (BuyRgbChromatic.Value)
        {
            rules.Add(new FilterRule
            {
                Enabled = true,
                Name = "RGB Chromatic Recipe",
                BaseNameFilter = string.Empty, // any base name
                MatchNormal = true,
                MatchMagic = true,
                MatchRare = true,
                MatchUnique = false,
                RequireRgbSockets = true
            });
        }

        // Additional standalone rule for 6-Sockets if specified
        if (MinSockets.Value >= 6)
        {
            rules.Add(new FilterRule
            {
                Enabled = true,
                Name = "6 Sockets Recipe",
                BaseNameFilter = string.Empty,
                MatchNormal = true,
                MatchMagic = true,
                MatchRare = true,
                MatchUnique = false,
                MinSockets = 6
            });
        }

        return rules;
    }
}
