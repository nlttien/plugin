using ExileCore.PoEMemory.Elements;
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

    public CurrencyCost? Cost { get; set; }

    public int TabIndex { get; set; } = 0;
    public string TabName { get; set; } = string.Empty;

    public int SlotX { get; set; } = 0;
    public int SlotY { get; set; } = 0;
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    public RectangleF ScreenRect { get; set; }
    public Vector2 ClickPosition { get; set; }

    public NormalInventoryItem? InventoryItem { get; set; }
}
