using System;
using System.IO;

namespace ShopAutoBuyer.Core.Utils;

public static class BridgePathHelper
{
    private static string? _cachedBridgePath;
    private static string? _cachedHistoryPath;

    public static string GetBridgeFilePath()
    {
        if (!string.IsNullOrEmpty(_cachedBridgePath) && File.Exists(_cachedBridgePath))
        {
            return _cachedBridgePath;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;

        var candidates = new[]
        {
            Path.Combine(parentDir, "trade_bridge.json"),
            Path.Combine(baseDir, "trade_bridge.json"),
            @"D:\codecuatien\trade_bridge.json"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                _cachedBridgePath = c;
                return c;
            }
        }

        // Mặc định tạo ở thư mục gốc của dự án (thư mục cha của ExileApi)
        _cachedBridgePath = Path.Combine(parentDir, "trade_bridge.json");
        return _cachedBridgePath;
    }

    public static string[] GetHistoryFilePaths()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;

        return new[]
        {
            Path.Combine(parentDir, "purchase_history.txt"),
            Path.Combine(baseDir, "purchase_history.txt"),
            Path.Combine(baseDir, "Plugins", "Source", "ShopAutoBuyer", "purchase_history.txt"),
            Path.Combine(baseDir, "Plugins", "Compiled", "ShopAutoBuyer", "purchase_history.txt")
        };
    }
}
