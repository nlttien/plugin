using System;
using ExileCore;

namespace ShopAutoBuyer.Core.Utils;

public static class LogHelper
{
    private const string Prefix = "[ShopAutoBuyer] ";

    public static void Info(string message)
    {
        DebugWindow.LogMsg(Prefix + message, 3f);
    }

    public static void Debug(string message)
    {
        DebugWindow.LogDebug(Prefix + message, 2f);
    }

    public static void Warn(string message)
    {
        DebugWindow.LogMsg(Prefix + "[WARN] " + message, 5f, SharpDX.Color.Yellow);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{Prefix}[ERROR] {message}: {ex.Message}" : $"{Prefix}[ERROR] {message}";
        DebugWindow.LogError(msg, 7f);
    }
}
