using System.Collections.Generic;
using ExileCore;
using ShopAutoBuyer.Core.Models;
using SharpDX;

namespace ShopAutoBuyer.Core.Adapters;

public interface IShopAdapter
{
    string AdapterName { get; }

    /// <summary>
    /// Checks if the relevant NPC merchant/purchase shop window is open and visible.
    /// </summary>
    bool IsShopOpen(GameController gc);

    /// <summary>
    /// Reads and parses all items visible in the currently active shop tab.
    /// </summary>
    List<ShopItemInfo> GetAvailableItems(GameController gc);

    /// <summary>
    /// Returns total number of tabs available in the shop.
    /// </summary>
    int GetTabCount(GameController gc);

    /// <summary>
    /// Returns 0-based index of the currently active tab.
    /// </summary>
    int GetCurrentTabIndex(GameController gc);

    /// <summary>
    /// Switches to the specified tab index by clicking its tab button.
    /// </summary>
    bool SwitchToTab(GameController gc, int tabIndex);
}
