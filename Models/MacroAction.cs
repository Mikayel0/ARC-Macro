using System;
using System.Text.Json.Serialization;

namespace Macro;

public enum ActionType
{
    MouseMove,
    MouseLeftDown,
    MouseLeftUp,
    MouseRightDown,
    MouseRightUp,
    KeyDown,
    KeyUp
}

/// <summary>
/// Represents a single recorded user input step.
/// Math relies on percentage scaling, so it can be reliably played back on any resolution.
/// </summary>
public class MacroAction
{
    public ActionType Type { get; set; }
    
    // Percentage coordinates (e.g. 0.5 is exactly the middle of the client area)
    public double XPercent { get; set; }
    public double YPercent { get; set; }

    // Virtual-Key code for keyboard strokes
    public int KeyCode { get; set; }

    // Time elapsed since the PREVIOUS action completed (milliseconds)
    public long DelayMs { get; set; }
}

public class MacroSequence
{
    public MacroAction[] Actions { get; set; } = Array.Empty<MacroAction>();
}
