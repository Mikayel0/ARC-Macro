using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Macro;

/// <summary>
/// Uses low-level Windows Hooks (LL) to capture keyboard and mouse input system-wide,
/// translating the coordinates to Window-Relative percentages.
/// </summary>
public class MacroRecorder : IDisposable
{
    private IntPtr _mouseHookID = IntPtr.Zero;
    private IntPtr _keyboardHookID = IntPtr.Zero;

    // Delegates must be kept alive to avoid garbage collection exceptions during hooks
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;

    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly List<MacroAction> _recordedActions = new List<MacroAction>();
    private readonly HashSet<int> _keysDown = new HashSet<int>();
    private IntPtr _targetHwnd;
    private int _recordHotKey = 0;

    public bool IsRecording { get; private set; }

    public MacroRecorder()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public void StartRecording(IntPtr targetHwnd, int recordHotKey)
    {
        _targetHwnd = targetHwnd;
        _recordHotKey = recordHotKey;
        _recordedActions.Clear();
        _keysDown.Clear();
        
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;
        
        if (curModule?.ModuleName == null) return;
        
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule.ModuleName);
        
        // Register system-wide hooks
        _mouseHookID = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        _keyboardHookID = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        
        _stopwatch.Restart();
        IsRecording = true;
    }

    public void StopRecording()
    {
        if (_mouseHookID != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookID);
            _mouseHookID = IntPtr.Zero;
        }

        if (_keyboardHookID != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookID);
            _keyboardHookID = IntPtr.Zero;
        }

        _stopwatch.Stop();
        IsRecording = false;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            ActionType? actionType = DetermineMouseAction((int)wParam);
            
            if (actionType.HasValue)
            {
                RecordMouseAction(actionType.Value, hookStruct.pt.X, hookStruct.pt.Y); 
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            
            int msg = (int)wParam;
            
            // Ignore the user's bound Record key entirely so it doesn't pollute the macro
            if (hookStruct.vkCode == _recordHotKey)
            {
                return NativeMethods.CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
            }

            // WM_KEYDOWN (0x0100), WM_SYSKEYDOWN (0x0104)
            if (msg == 0x0100 || msg == 0x0104)
            {
                if (!_keysDown.Contains(hookStruct.vkCode))
                {
                    _keysDown.Add(hookStruct.vkCode);
                    RecordKeyboardAction(ActionType.KeyDown, hookStruct.vkCode);
                }
            }
            // WM_KEYUP (0x0101), WM_SYSKEYUP (0x0105)
            else if (msg == 0x0101 || msg == 0x0105)
            {
                _keysDown.Remove(hookStruct.vkCode);
                RecordKeyboardAction(ActionType.KeyUp, hookStruct.vkCode);
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
    }

    private ActionType? DetermineMouseAction(int wParam)
    {
        return wParam switch
        {
            // Nullifying 0x0200 intentionally so the cursor jumps instead of dragging
            0x0201 => ActionType.MouseLeftDown,
            0x0202 => ActionType.MouseLeftUp,
            0x0204 => ActionType.MouseRightDown,
            0x0205 => ActionType.MouseRightUp,
            _ => null
        };
    }

    private void RecordMouseAction(ActionType type, int screenX, int screenY)
    {
        long delay = _stopwatch.ElapsedMilliseconds;
        _stopwatch.Restart();

        var (percentX, percentY) = CoordinateMath.ScreenToClientPercentage(_targetHwnd, screenX, screenY);

        _recordedActions.Add(new MacroAction
        {
            Type = type,
            XPercent = percentX,
            YPercent = percentY,
            DelayMs = delay
        });
    }

    private void RecordKeyboardAction(ActionType type, int vkCode)
    {
        long delay = _stopwatch.ElapsedMilliseconds;
        _stopwatch.Restart();

        // The user specifically requested that keypresses ALSO record the current mouse coordinate.
        NativeMethods.POINT pt = new NativeMethods.POINT();
        NativeMethods.GetCursorPos(out pt);
        var (percentX, percentY) = CoordinateMath.ScreenToClientPercentage(_targetHwnd, pt.X, pt.Y);

        _recordedActions.Add(new MacroAction
        {
            Type = type,
            KeyCode = vkCode,
            XPercent = percentX,
            YPercent = percentY,
            DelayMs = delay
        });
    }

    /// <summary>
    /// Serialize the recorded JSON macro definition to file.
    /// </summary>
    public void SaveToJson(string filePath)
    {
        var seq = new MacroSequence { Actions = _recordedActions.ToArray() };
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(seq, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Deserialize a JSON macro from file for playback.
    /// </summary>
    public static MacroSequence LoadFromJson(string filePath)
    {
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<MacroSequence>(json) ?? new MacroSequence();
    }

    /// <summary>
    /// Replays a sequence of actions by converting percentages back to absolute pixels and sending hardware inputs.
    /// </summary>
    public static async System.Threading.Tasks.Task PlayMacro(MacroSequence sequence, IntPtr targetHwnd, double speedModifier = 1.0, bool skipFirstDelay = false, bool skipLastDelay = false, System.Threading.CancellationToken token = default)
    {
        if (sequence == null || sequence.Actions.Length == 0 || targetHwnd == IntPtr.Zero) return;

        bool isFirst = true;

        for (int i = 0; i < sequence.Actions.Length; i++)
        {
            var action = sequence.Actions[i];
            
            if (token.IsCancellationRequested) break;
            
            // Explicitly yield back to the OS message pump to ensure hotkeys are intercepted cleanly
            await System.Threading.Tasks.Task.Delay(1, token);

            // Wait for the recorded delay
            double ms = action.DelayMs;
            if (speedModifier > 0) ms /= speedModifier;
            if (isFirst && skipFirstDelay) ms = 0;
            if (skipLastDelay && i == sequence.Actions.Length - 1) ms = 0;
            isFirst = false;

            int adjustedDelay = (int)ms;

            if (adjustedDelay > 0)
            {
                try { await System.Threading.Tasks.Task.Delay(adjustedDelay, token); }
                catch (System.Threading.Tasks.TaskCanceledException) { break; }
            }

            // Convert back to absolute pixels
            var (absX, absY) = CoordinateMath.ClientPercentageToScreen(targetHwnd, action.XPercent, action.YPercent);

            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_MOUSE; // Default to mouse
            inputs[0].U.mi.dx = (absX * 65535) / NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            inputs[0].U.mi.dy = (absY * 65535) / NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            inputs[0].U.mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_MOVE;

            switch (action.Type)
            {
                case ActionType.MouseMove:
                    // Just move
                    break;
                case ActionType.MouseLeftDown:
                    inputs[0].U.mi.dwFlags |= NativeMethods.MOUSEEVENTF_LEFTDOWN;
                    break;
                case ActionType.MouseLeftUp:
                    inputs[0].U.mi.dwFlags |= NativeMethods.MOUSEEVENTF_LEFTUP;
                    break;
                case ActionType.MouseRightDown:
                    inputs[0].U.mi.dwFlags |= NativeMethods.MOUSEEVENTF_RIGHTDOWN;
                    break;
                case ActionType.MouseRightUp:
                    inputs[0].U.mi.dwFlags |= NativeMethods.MOUSEEVENTF_RIGHTUP;
                    break;
                case ActionType.KeyDown:
                    inputs[0].type = NativeMethods.INPUT_KEYBOARD;
                    inputs[0].U.ki.wVk = (short)action.KeyCode;
                    inputs[0].U.ki.dwFlags = 0;
                    break;
                case ActionType.KeyUp:
                    inputs[0].type = NativeMethods.INPUT_KEYBOARD;
                    inputs[0].U.ki.wVk = (short)action.KeyCode;
                    inputs[0].U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
                    break;
            }

            NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }

    public void Dispose()
    {
        StopRecording();
    }
}
