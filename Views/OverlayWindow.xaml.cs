using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Macro;

/// <summary>
/// A magnetic WPF overlay that snaps to a target application window.
/// </summary>
public partial class OverlayWindow : Window
{
    private DispatcherTimer? _trackerTimer;
    
    private string _targetProcessName = "PioneerGame";

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void SetTargetWindow(string processName)
    {
        _targetProcessName = processName;
    }

    public void SetBorderVisibility(bool isVisible)
    {
        MainBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Hidden;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyClickThroughStyle();
        StartTracking();
    }

    /// <summary>
    /// Applies Win32 extended styles to make the WPF window completely transparent to mouse clicks.
    /// </summary>
    private void ApplyClickThroughStyle()
    {
        // Get the Win32 handle of this WPF window
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        
        // Retrieve the current extended styles
        int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        
        // Add WS_EX_TRANSPARENT (click-through), WS_EX_LAYERED (required for transparency), 
        // and WS_EX_TOOLWINDOW (removes the window from alt-tab menu)
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, 
            extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Starts a timer that updates the WPF window bounds to match the target window's Client Area.
    /// </summary>
    private void StartTracking()
    {
        _trackerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS update rate
        };
        _trackerTimer.Tick += TrackerTimer_Tick;
        _trackerTimer.Start();
    }

    private void TrackerTimer_Tick(object? sender, EventArgs e)
    {
        IntPtr targetHwnd = GetHwndFromProcessName(_targetProcessName);
        
        if (targetHwnd != IntPtr.Zero)
        {
            // We want to snap specifically to the Client Area (ignoring title bar and borders)
            if (NativeMethods.GetClientRect(targetHwnd, out NativeMethods.RECT clientRect))
            {
                NativeMethods.POINT topLeft = new NativeMethods.POINT { X = 0, Y = 0 };
                // Convert the client coordinates (0,0) to absolute screen coordinates
                NativeMethods.ClientToScreen(targetHwnd, ref topLeft);

                int width = clientRect.Right - clientRect.Left;
                int height = clientRect.Bottom - clientRect.Top;

                // Move and resize the WPF overlay to perfectly shield the Client Area
                this.Left = topLeft.X;
                this.Top = topLeft.Y;
                this.Width = width;
                this.Height = height;

                if (this.Visibility != Visibility.Visible)
                {
                    this.Visibility = Visibility.Visible;
                }

                // Force the overlay to always stay on top of the game window
                IntPtr rootHwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowPos(rootHwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
        else
        {
            // If the game window is closed or the process is gone, stop the overlay entirely
            this.Close();
        }
    }

    private class MergedAction
    {
        public ActionType OriginalType { get; set; }
        public int KeyCode { get; set; }
        public double XPercent { get; set; }
        public double YPercent { get; set; }
        public string Label { get; set; } = "";
    }

    public void DrawMacroVisualization(MacroSequence sequence, bool showKeyEvents = false)
    {
        MacroCanvas.Children.Clear();
        
        if (sequence == null || sequence.Actions.Length == 0) return;

        IntPtr targetHwnd = GetHwndFromProcessName(_targetProcessName);
        if (targetHwnd == IntPtr.Zero || !NativeMethods.GetClientRect(targetHwnd, out NativeMethods.RECT clientRect))
        {
            return;
        }

        double width = clientRect.Right - clientRect.Left;
        double height = clientRect.Bottom - clientRect.Top;

        // Pre-process actions to merge Down/Up pairs
        var merged = new System.Collections.Generic.List<MergedAction>();
        for (int i = 0; i < sequence.Actions.Length; i++)
        {
            var a = sequence.Actions[i];
            
            string labelStr = "";
            bool skipNext = false;

            if (!showKeyEvents && a.Type == ActionType.MouseLeftDown && i < sequence.Actions.Length - 1 && sequence.Actions[i+1].Type == ActionType.MouseLeftUp)
            {
                labelStr = "[LMC]";
                skipNext = true;
            }
            else if (!showKeyEvents && a.Type == ActionType.MouseRightDown && i < sequence.Actions.Length - 1 && sequence.Actions[i+1].Type == ActionType.MouseRightUp)
            {
                labelStr = "[RMC]";
                skipNext = true;
            }
            else if (!showKeyEvents && a.Type == ActionType.KeyDown && i < sequence.Actions.Length - 1 && sequence.Actions[i+1].Type == ActionType.KeyUp && sequence.Actions[i+1].KeyCode == a.KeyCode)
            {
                var keyName = System.Windows.Input.KeyInterop.KeyFromVirtualKey(a.KeyCode).ToString();
                if (keyName.StartsWith("Left")) keyName = keyName.Substring(4);
                if (keyName.StartsWith("Right")) keyName = keyName.Substring(5);
                labelStr = $"[{keyName}]";
                skipNext = true;
            }
            else
            {
                switch (a.Type)
                {
                    case ActionType.MouseLeftDown: labelStr = "[LMC Down]"; break;
                    case ActionType.MouseLeftUp: labelStr = "[LMC Up]"; break;
                    case ActionType.MouseRightDown: labelStr = "[RMC Down]"; break;
                    case ActionType.MouseRightUp: labelStr = "[RMC Up]"; break;
                    case ActionType.KeyDown: 
                        var kDown = System.Windows.Input.KeyInterop.KeyFromVirtualKey(a.KeyCode).ToString();
                        labelStr = $"[{kDown.Replace("Left","").Replace("Right","")} Down]"; 
                        break;
                    case ActionType.KeyUp: 
                        var kUp = System.Windows.Input.KeyInterop.KeyFromVirtualKey(a.KeyCode).ToString();
                        labelStr = $"[{kUp.Replace("Left","").Replace("Right","")} Up]"; 
                        break;
                    default: labelStr = $"[{a.Type}]"; break;
                }
            }

            merged.Add(new MergedAction { OriginalType = a.Type, KeyCode = a.KeyCode, XPercent = a.XPercent, YPercent = a.YPercent, Label = labelStr });

            if (skipNext) i++; // skip the 'Up' pair
        }

        Point? previousPoint = null;

        for (int i = 0; i < merged.Count; i++)
        {
            var action = merged[i];
            
            double x = width * action.XPercent;
            double y = height * action.YPercent;
            Point currentPoint = new Point(x, y);

            Ellipse dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (action.OriginalType == ActionType.KeyDown || action.OriginalType == ActionType.KeyUp) ? Brushes.Cyan : Brushes.Magenta,
                ToolTip = action.Label
            };

            Canvas.SetLeft(dot, x - (dot.Width / 2));
            Canvas.SetTop(dot, y - (dot.Height / 2));
            MacroCanvas.Children.Add(dot);

            TextBlock label = new TextBlock
            {
                Text = $"{i + 1} {action.Label}",
                Foreground = Brushes.Yellow,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(label, x + 8);
            Canvas.SetTop(label, y - 8);
            MacroCanvas.Children.Add(label);

            if (previousPoint.HasValue)
            {
                Line line = new Line
                {
                    X1 = previousPoint.Value.X,
                    Y1 = previousPoint.Value.Y,
                    X2 = currentPoint.X,
                    Y2 = currentPoint.Y,
                    Stroke = Brushes.Purple,
                    StrokeThickness = 1,
                    Opacity = 0.5
                };
                MacroCanvas.Children.Insert(0, line);
            }

            previousPoint = currentPoint;
        }
    }

    private IntPtr GetHwndFromProcessName(string processName)
    {
        try
        {
            string nameWithoutExt = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            var processes = System.Diagnostics.Process.GetProcessesByName(nameWithoutExt);
            if (processes.Length > 0 && !processes[0].HasExited)
            {
                return processes[0].MainWindowHandle;
            }
        }
        catch { }
        return IntPtr.Zero;
    }
}
