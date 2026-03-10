using System;

namespace Macro;

/// <summary>
/// Handles resolution-independent and position-independent macro logic
/// by converting Absolute Screen Coordinates to Target Window Percentage Math.
/// </summary>
public static class CoordinateMath
{
    /// <summary>
    /// Converts an absolute global screen coordinate into a normalized 0.0 to 1.0 percentage 
    /// relative to the top-left of the target window's actual Client Area.
    /// </summary>
    public static (double XPercent, double YPercent) ScreenToClientPercentage(IntPtr targetHwnd, int screenX, int screenY)
    {
        if (targetHwnd == IntPtr.Zero || !NativeMethods.GetClientRect(targetHwnd, out NativeMethods.RECT clientRect))
        {
            return (0.0, 0.0);
        }

        NativeMethods.POINT topLeft = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(targetHwnd, ref topLeft);

        // Dimensions of the Client Area
        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;

        if (width <= 0 || height <= 0) return (0.0, 0.0);

        // Calculate offset from the client area's Top-Left corner
        int relativeX = screenX - topLeft.X;
        int relativeY = screenY - topLeft.Y;

        // Convert to percentage
        // Note: A value less than 0 or greater than 1 means the cursor was outside the client area during capture
        double percentX = (double)relativeX / width;
        double percentY = (double)relativeY / height;

        return (percentX, percentY);
    }

    /// <summary>
    /// Converts a saved normalized percentage (0.0 to 1.0) back into global absolute screen coordinates
    /// based on the target window's current position and size.
    /// </summary>
    public static (int ScreenX, int ScreenY) ClientPercentageToScreen(IntPtr targetHwnd, double percentX, double percentY)
    {
        if (targetHwnd == IntPtr.Zero || !NativeMethods.GetClientRect(targetHwnd, out NativeMethods.RECT clientRect))
        {
            return (0, 0);
        }

        NativeMethods.POINT topLeft = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(targetHwnd, ref topLeft);

        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;

        // Rehydrate screen positions from the exact client boundaries
        int screenX = topLeft.X + (int)(width * percentX);
        int screenY = topLeft.Y + (int)(height * percentY);

        return (screenX, screenY);
    }
}
