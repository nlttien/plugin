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

        // 1. Đọc từ CONFIG_DUONG_DAN.ini nếu có
        var iniFiles = new[]
        {
            Path.Combine(baseDir, "CONFIG_DUONG_DAN.ini"),
            Path.Combine(parentDir, "CONFIG_DUONG_DAN.ini"),
            @"D:\codecuatien\CONFIG_DUONG_DAN.ini"
        };

        foreach (var ini in iniFiles)
        {
            if (File.Exists(ini))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(ini))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("AUTOBUYPOE_FOLDER=", StringComparison.OrdinalIgnoreCase))
                        {
                            var customFolder = trimmed.Substring("AUTOBUYPOE_FOLDER=".Length).Trim('"', '\'', ' ');
                            if (!string.IsNullOrEmpty(customFolder))
                            {
                                var customBridge = Path.Combine(customFolder, "..", "trade_bridge.json");
                                if (File.Exists(customBridge))
                                {
                                    _cachedBridgePath = customBridge;
                                    return _cachedBridgePath;
                                }
                                var customDirect = Path.Combine(customFolder, "trade_bridge.json");
                                if (File.Exists(customDirect))
                                {
                                    _cachedBridgePath = customDirect;
                                    return _cachedBridgePath;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

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
