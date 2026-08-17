using System;
using ExileCore;
using ShopAutoBuyer.Core.Models;
using ShopAutoBuyer.Core.Utils;

namespace ShopAutoBuyer.Core.Adapters;

public class ShopAdapterFactory
{
    private readonly Poe1ShopAdapter _poe1Adapter = new Poe1ShopAdapter();
    private readonly Poe2ShopAdapter _poe2Adapter = new Poe2ShopAdapter();

    public IShopAdapter GetAdapter(GameController gc, string versionSetting)
    {
        if (Enum.TryParse<GameVersionEnum>(versionSetting, true, out var ver))
        {
            return GetAdapter(gc, ver);
        }
        return GetAdapter(gc, GameVersionEnum.AutoDetect);
    }

    public IShopAdapter GetAdapter(GameController gc, GameVersionEnum versionSetting)
    {
        switch (versionSetting)
        {
            case GameVersionEnum.PathOfExile1:
                return _poe1Adapter;

            case GameVersionEnum.PathOfExile2:
                return _poe2Adapter;

            case GameVersionEnum.AutoDetect:
            default:
                // Check which adapter detects an open shop
                if (_poe1Adapter.IsShopOpen(gc))
                {
                    return _poe1Adapter;
                }

                if (_poe2Adapter.IsShopOpen(gc))
                {
                    return _poe2Adapter;
                }

                // Default fallback to PoE 1 adapter
                return _poe1Adapter;
        }
    }
}
