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
    public static void MoveMouseWithJitter(RectangleF rect, float offsetY = 6f)
    {
        var centerX = rect.Center.X;
        var centerY = rect.Center.Y + offsetY;

        var jitterX = (float)(Rnd.NextDouble() * 3.0 - 1.5);
        var jitterY = (float)(Rnd.NextDouble() * 3.0 - 1.5);

        Input.SetCursorPos(new Vector2(centerX + jitterX, centerY + jitterY));
    }

    public static void FastDirectMove(Vector2 target)
    {
        Input.SetCursorPos(target);
    }

    public static void FastCtrlLeftClickAt(Vector2 target, int hoverWaitMs = 5, int holdMs = 20)
    {
        Input.SetCursorPos(target);
        if (hoverWaitMs > 0) Thread.Sleep(hoverWaitMs);
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(8);
        Input.LeftDown();
        if (holdMs > 0) Thread.Sleep(holdMs);
        Input.LeftUp();
        Thread.Sleep(8);
        Input.KeyUp(Keys.LControlKey);
    }

    /// <summary>
    /// Di chuyển chuột đến vị trí mục tiêu, đợi hover ổn định rồi thực hiện thao tác Ctrl + Click chính xác 100%.
    /// </summary>
    public static void CtrlLeftClickAt(Vector2 target, int hoverWaitMs = 130, int holdMs = 50)
    {
        Input.SetCursorPos(target);
        Thread.Sleep(hoverWaitMs);
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(30);
        Input.LeftDown();
        Thread.Sleep(holdMs);
        Input.LeftUp();
        Thread.Sleep(30);
        Input.KeyUp(Keys.LControlKey);
        Thread.Sleep(30);
    }

    /// <summary>
    /// Thực hiện Ctrl + Shift + Left Click tại vị trí mục tiêu (cất trực tiếp toàn bộ số lượng vào Guild Stash / Stash mà không hiện bảng hỏi số lượng).
    /// </summary>
    public static void CtrlShiftLeftClickAt(Vector2 target, int hoverWaitMs = 50, int holdMs = 40)
    {
        Input.SetCursorPos(target);
        Thread.Sleep(hoverWaitMs);
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(15);
        Input.KeyDown(Keys.LShiftKey);
        Thread.Sleep(15);
        Input.LeftDown();
        Thread.Sleep(holdMs);
        Input.LeftUp();
        Thread.Sleep(15);
        Input.KeyUp(Keys.LShiftKey);
        Thread.Sleep(15);
        Input.KeyUp(Keys.LControlKey);
        Thread.Sleep(20);
    }

    public static void CtrlLeftClick()
    {
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(35);
        Input.LeftDown();
        Thread.Sleep(50);
        Input.LeftUp();
        Thread.Sleep(35);
        Input.KeyUp(Keys.LControlKey);
    }

    public static void CtrlShiftLeftClick()
    {
        Input.KeyDown(Keys.LControlKey);
        Thread.Sleep(20);
        Input.KeyDown(Keys.LShiftKey);
        Thread.Sleep(20);
        Input.LeftDown();
        Thread.Sleep(40);
        Input.LeftUp();
        Thread.Sleep(20);
        Input.KeyUp(Keys.LShiftKey);
        Thread.Sleep(20);
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
