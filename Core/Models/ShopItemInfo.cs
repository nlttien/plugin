using System.Collections.Generic;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Models;

public class ShopItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string BaseName { get; set; } = string.Empty;
    public string ItemPath { get; set; } = string.Empty;
    public ItemRarity Rarity { get; set; } = ItemRarity.Normal;
    public int ItemLevel { get; set; } = 1;
    public int Quality { get; set; } = 0;
    public int Sockets { get; set; } = 0;
    public int Links { get; set; } = 0;
    public bool IsRgb { get; set; } = false;

    // Timeless Jewel specific data
    public bool IsTimelessJewel { get; set; } = false;
    public int TimelessSeed { get; set; } = 0;
    public string TimelessLeader { get; set; } = string.Empty;
    public List<string> ExplicitMods { get; set; } = new List<string>();

    public CurrencyCost? Cost { get; set; }
    public string CostString { get; set; } = string.Empty;
    public string TooltipFullText { get; set; } = string.Empty;

    public int TabIndex { get; set; } = 0;
    public string TabName { get; set; } = string.Empty;

    public int SlotX { get; set; } = 0;
    public int SlotY { get; set; } = 0;
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    public RectangleF ScreenRect { get; set; }
    public Vector2 ClickPosition { get; set; }

    public NormalInventoryItem? InventoryItem { get; set; }

    public string DisplayName
    {
        get
        {
            if (IsTimelessJewel)
            {
                var jewelName = !string.IsNullOrWhiteSpace(Name) ? Name : "Timeless Jewel";
                var seedInfo = TimelessSeed > 0 
                    ? (!string.IsNullOrWhiteSpace(TimelessLeader) ? $"[{TimelessSeed} {TimelessLeader}]" : $"[{TimelessSeed}]")
                    : (!string.IsNullOrWhiteSpace(TimelessLeader) ? $"[{TimelessLeader}]" : string.Empty);
                
                return string.IsNullOrWhiteSpace(seedInfo) 
                    ? $"{jewelName} (Timeless Jewel)" 
                    : $"{jewelName} {seedInfo}";
            }

            return string.IsNullOrWhiteSpace(Name) || Name == BaseName 
                ? BaseName 
                : $"{Name} ({BaseName})";
        }
    }
}
