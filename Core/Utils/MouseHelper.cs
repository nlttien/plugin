using System;
using System.Threading.Tasks;
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
        // Calculate safe clickable area with small padding inside the item box
        var paddingX = Math.Max(2f, rect.Width * 0.15f);
        var paddingY = Math.Max(2f, rect.Height * 0.15f);

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
        Input.KeyDown(Keys.ControlKey);
        Input.Click(MouseButtons.Left);
        Input.KeyUp(Keys.ControlKey);
    }

    public static void LeftClick()
    {
        Input.Click(MouseButtons.Left);
    }

    public static int GetRandomDelay(int minMs, int maxMs)
    {
        if (minMs >= maxMs) return minMs;
        return Rnd.Next(minMs, maxMs + 1);
    }
}
