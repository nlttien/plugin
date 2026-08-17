using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer;

public class ShopAutoBuyerSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(true);

    public ListNode GameVersion { get; set; } = new ListNode
    {
        Values = new List<string> { "AutoDetect", "PathOfExile1", "PathOfExile2" },
        Value = "AutoDetect"
    };

    public HotkeyNode TriggerHotkey { get; set; } = new HotkeyNode(Keys.F6);
    public ToggleNode AutoBuyOnOpen { get; set; } = new ToggleNode(false);
    public ToggleNode ScanAllTabs { get; set; } = new ToggleNode(true);
    public ToggleNode HighlightOnlyMode { get; set; } = new ToggleNode(false);
    public ToggleNode ShowStatusBox { get; set; } = new ToggleNode(true);

    // TIMELESS JEWEL SPECIFIC SETTINGS
    public ToggleNode OnlyBuyTimelessJewels { get; set; } = new ToggleNode(true);
    public ToggleNode BuyBrutalRestraint { get; set; } = new ToggleNode(true);
    public ToggleNode BuyGloriousVanity { get; set; } = new ToggleNode(true);
    public ToggleNode BuyLethalPride { get; set; } = new ToggleNode(true);
    public ToggleNode BuyMilitantFaith { get; set; } = new ToggleNode(true);
    public ToggleNode BuyElegantHubris { get; set; } = new ToggleNode(true);

    public TextNode LeaderFilter { get; set; } = new TextNode("");
    public TextNode SpecificSeeds { get; set; } = new TextNode("");
    public RangeNode<int> MaxDivinePrice { get; set; } = new RangeNode<int>(0, 0, 50);
    public RangeNode<int> MaxGoldPrice { get; set; } = new RangeNode<int>(0, 0, 500000);

    // Visuals & Display Style
    public ListNode LabelMode { get; set; } = new ListNode
    {
        Values = new List<string> { "Compact (Seed Only)", "Full Name", "Border Only" },
        Value = "Compact (Seed Only)"
    };
    public ColorNode HighlightColor { get; set; } = new ColorNode(Color.LimeGreen);
    public RangeNode<int> BorderThickness { get; set; } = new RangeNode<int>(2, 1, 8);

    // Delays
    public RangeNode<int> MinDelayMs { get; set; } = new RangeNode<int>(100, 30, 1000);
    public RangeNode<int> MaxDelayMs { get; set; } = new RangeNode<int>(220, 50, 2000);

    // General Whitelist (Used only when OnlyBuyTimelessJewels is FALSE)
    public TextNode BaseNamesFilter { get; set; } = new TextNode("Amethyst Ring, Heavy Belt, Two-Stone Ring, Uncut");
    public ToggleNode BuyNormal { get; set; } = new ToggleNode(true);
    public ToggleNode BuyMagic { get; set; } = new ToggleNode(true);
    public ToggleNode BuyRare { get; set; } = new ToggleNode(true);
    public ToggleNode BuyUnique { get; set; } = new ToggleNode(true);
    public RangeNode<int> MinItemLevel { get; set; } = new RangeNode<int>(0, 0, 100);
    public RangeNode<int> MinQuality { get; set; } = new RangeNode<int>(0, 0, 30);
    public RangeNode<int> MinSockets { get; set; } = new RangeNode<int>(0, 0, 6);
    public RangeNode<int> MinLinks { get; set; } = new RangeNode<int>(0, 0, 6);
    public ToggleNode BuyRgbChromatic { get; set; } = new ToggleNode(false);

    public List<FilterRule> GetActiveRules()
    {
        var rules = new List<FilterRule>();

        if (OnlyBuyTimelessJewels?.Value == true)
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

        var baseFilter = BaseNamesFilter?.Value ?? string.Empty;
        rules.Add(new FilterRule
        {
            Enabled = true,
            Name = "User Filter",
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
