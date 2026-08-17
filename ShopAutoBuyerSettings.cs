using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer;

public class ShopAutoBuyerSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(false);

    public ListNode GameVersion { get; set; } = new()
    {
        Values = new List<string> { "AutoDetect", "PathOfExile1", "PathOfExile2" },
        Value = "AutoDetect"
    };

    public HotkeyNode TriggerHotkey { get; set; } = new(Keys.F5);
    public ToggleNode AutoBuyOnOpen { get; set; } = new(false);
    public ToggleNode ScanAllTabs { get; set; } = new(true);
    public ToggleNode HighlightOnlyMode { get; set; } = new(false);

    // Delays
    public RangeNode<int> MinDelayMs { get; set; } = new(100, 30, 1000);
    public RangeNode<int> MaxDelayMs { get; set; } = new(220, 50, 2000);

    // Visuals
    public ColorNode HighlightColor { get; set; } = new(Color.LimeGreen);
    public RangeNode<int> BorderThickness { get; set; } = new(3, 1, 8);

    // Filter Rules
    public TextNode BaseNamesFilter { get; set; } = new("Amethyst Ring, Heavy Belt, Two-Stone Ring, Sapphire Ring, Ruby Ring, Topaz Ring, Uncut");
    public ToggleNode BuyNormal { get; set; } = new(true);
    public ToggleNode BuyMagic { get; set; } = new(true);
    public ToggleNode BuyRare { get; set; } = new(true);
    public ToggleNode BuyUnique { get; set; } = new(true);
    public RangeNode<int> MinItemLevel { get; set; } = new(0, 0, 100);
    public RangeNode<int> MinQuality { get; set; } = new(0, 0, 30);
    public RangeNode<int> MinSockets { get; set; } = new(0, 0, 6);
    public RangeNode<int> MinLinks { get; set; } = new(0, 0, 6);
    public ToggleNode BuyRgbChromatic { get; set; } = new(false);

    public List<FilterRule> GetActiveRules()
    {
        var rules = new List<FilterRule>();

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

        if (BuyRgbChromatic.Value)
        {
            rules.Add(new FilterRule
            {
                Enabled = true,
                Name = "RGB Chromatic Recipe",
                BaseNameFilter = string.Empty,
                MatchNormal = true,
                MatchMagic = true,
                MatchRare = true,
                MatchUnique = false,
                RequireRgbSockets = true
            });
        }

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
