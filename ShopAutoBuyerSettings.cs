using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using ShopAutoBuyer.Core.Models;

namespace ShopAutoBuyer;

public class ShopAutoBuyerSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);

    public ListNode GameVersion { get; set; } = new ListNode
    {
        Values = new List<string> { "AutoDetect", "PathOfExile1", "PathOfExile2" },
        Value = "AutoDetect"
    };

    public HotkeyNode TriggerHotkey { get; set; } = new HotkeyNode(Keys.F5);
    public ToggleNode AutoBuyOnOpen { get; set; } = new ToggleNode(false);
    public ToggleNode ScanAllTabs { get; set; } = new ToggleNode(true);
    public ToggleNode HighlightOnlyMode { get; set; } = new ToggleNode(false);
    public ToggleNode ShowStatusBox { get; set; } = new ToggleNode(true);

    // Delays
    public RangeNode<int> MinDelayMs { get; set; } = new RangeNode<int>(100, 30, 1000);
    public RangeNode<int> MaxDelayMs { get; set; } = new RangeNode<int>(220, 50, 2000);

    // Visuals
    public ColorNode HighlightColor { get; set; } = new ColorNode(Color.LimeGreen);
    public RangeNode<int> BorderThickness { get; set; } = new RangeNode<int>(3, 1, 8);

    // Filter Rules
    public TextNode BaseNamesFilter { get; set; } = new TextNode("Timeless Jewel, Brutal Restraint, Glorious Vanity, Lethal Pride, Militant Faith, Elegant Hubris, Amethyst Ring, Heavy Belt, Two-Stone Ring, Uncut");
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

        var baseFilter = BaseNamesFilter?.Value ?? string.Empty;
        var buyNorm = BuyNormal?.Value ?? true;
        var buyMag = BuyMagic?.Value ?? true;
        var buyRar = BuyRare?.Value ?? true;
        var buyUniq = BuyUnique?.Value ?? true;
        var minIlvl = MinItemLevel?.Value ?? 0;
        var minQual = MinQuality?.Value ?? 0;
        var minSock = MinSockets?.Value ?? 0;
        var minLink = MinLinks?.Value ?? 0;
        var buyRgb = BuyRgbChromatic?.Value ?? false;

        rules.Add(new FilterRule
        {
            Enabled = true,
            Name = "User Filter",
            BaseNameFilter = baseFilter,
            MatchNormal = buyNorm,
            MatchMagic = buyMag,
            MatchRare = buyRar,
            MatchUnique = buyUniq,
            MinItemLevel = minIlvl,
            MinQuality = minQual,
            MinSockets = minSock,
            MinLinks = minLink,
            RequireRgbSockets = buyRgb
        });

        if (buyRgb)
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

        if (minSock >= 6)
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
