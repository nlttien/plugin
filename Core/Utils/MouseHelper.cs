using System;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace ShopAutoBuyer.Core.Utils;

public static class MouseHelper
{
    private static readonly Random Rnd = new();

    public static void MoveMouse(Vector2 target)
    {
        Input.SetCursorPos(target);
    }

    /// <summary>
    /// Di chuyển chuột vào chính giữa tâm ô đồ (hạ thấp xuống một khoảng offsetY để vào giữa biểu tượng ngọc).
    /// </summary>
    public static void MoveMouseWithJitter(RectangleF rect, float offsetY = 8f)
    {
        // Tâm chính xác của ô đồ + độ hạ thấp xuống giữa ô
        var centerX = rect.Center.X;
        var centerY = rect.Center.Y + offsetY;

        // Jitter nhỏ tự nhiên (±2 pixel)
        var jitterX = (float)(Rnd.NextDouble() * 4.0 - 2.0);
        var jitterY = (float)(Rnd.NextDouble() * 4.0 - 2.0);

        Input.SetCursorPos(new Vector2(centerX + jitterX, centerY + jitterY));
    }

    public static void CtrlLeftClick()
    {
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(40);
        Input.LeftDown();
        Thread.Sleep(40);
        Input.LeftUp();
        Thread.Sleep(40);
        Input.KeyUp(Keys.LControlKey);
    }

    public static void LeftClick()
    {
        Input.LeftDown();
        Thread.Sleep(40);
        Input.LeftUp();
    }

    public static void LeftClickAt(Vector2 target, int hoverWaitMs = 100, int holdMs = 45)
    {
        Input.SetCursorPos(target);
        Thread.Sleep(hoverWaitMs);
        Input.LeftDown();
        Thread.Sleep(holdMs);
        Input.LeftUp();
        Thread.Sleep(30);
    }

    public static int GetRandomDelay(int minMs, int maxMs)
    {
        if (minMs >= maxMs) return minMs;
        return Rnd.Next(minMs, maxMs + 1);
    }
}
