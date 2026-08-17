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

    public static void MoveMouseWithJitter(RectangleF rect)
    {
        var paddingX = Math.Max(2f, rect.Width * 0.2f);
        var paddingY = Math.Max(2f, rect.Height * 0.2f);

        var minX = rect.Left + paddingX;
        var maxX = rect.Right - paddingX;
        var minY = rect.Top + paddingY;
        var maxY = rect.Bottom - paddingY;

        var targetX = minX + (float)Rnd.NextDouble() * Math.Max(1f, maxX - minX);
        var targetY = minY + (float)Rnd.NextDouble() * Math.Max(1f, maxY - minY);

        Input.SetCursorPos(new Vector2(targetX, targetY));
    }

    public static void CtrlLeftClick()
    {
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(35);
        Input.LeftDown();
        Thread.Sleep(35);
        Input.LeftUp();
        Thread.Sleep(35);
        Input.KeyUp(Keys.LControlKey);
    }

    public static void LeftClick()
    {
        Input.LeftDown();
        Thread.Sleep(35);
        Input.LeftUp();
    }

    public static int GetRandomDelay(int minMs, int maxMs)
    {
        if (minMs >= maxMs) return minMs;
        return Rnd.Next(minMs, maxMs + 1);
    }
}
