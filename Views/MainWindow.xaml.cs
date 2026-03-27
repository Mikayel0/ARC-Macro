using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Macro;

public partial class MainWindow : Window
{
    private OverlayWindow? _overlayWindow;
    private MacroRecorder? _recorder;
    private MacroSequence? _loadedSequence;
    private AppConfig _config;
    
    // Virtual Keys
    private const int HOTKEY_ID_RECORD = 9000;
    private const int HOTKEY_ID_PLAY = 9001;

    private bool _isRebindingRecord = false;
    private bool _isRebindingPlay = false;
    private bool _isInitialized = false;
    private System.Threading.CancellationTokenSource? _playCts;

    private readonly string _macrosFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Macros");

    public MainWindow()
    {
        InitializeComponent();
        _config = ConfigManager.Load();
        
        if (!Directory.Exists(_macrosFolder)) Directory.CreateDirectory(_macrosFolder);

        this.Loaded += MainWindow_Loaded;
        this.Closed += MainWindow_Closed;
        this.PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadUIConfig();
        RefreshMacrosList();
        UpdateHotKeyUI();
        StartHotkeyPoller();
    }

    private void LoadUIConfig()
    {
        _isInitialized = false;
        
        LoopMacroCheck.IsChecked = _config.LoopMacro;
        LoopDelayBox.Text = _config.LoopDelayMs;
        SkipFirstDelayCheck.IsChecked = _config.SkipFirstDelay;
        MuteAudioCheck.IsChecked = _config.MuteGameAudio;
        OverlayBorderCheck.IsChecked = _config.EnableOverlayBorder;
        OverlayKeyEventsCheck.IsChecked = _config.EnableOverlayKeyEvents;
        ManualDelayCheck.IsChecked = _config.ManualDelayEnabled;
        ManualDelayBox.Text = _config.ManualDelayMs;
        DragDurationBox.Text = _config.DragDurationMs;
        MouseDrag.IsChecked = _config.EnableMouseDrag;
        SpeedSlider.Value = _config.Speed;

        _isInitialized = true;
    }

    private void SaveConfig_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _config == null) return;

        _config.LoopMacro = LoopMacroCheck.IsChecked == true;
        _config.SkipFirstDelay = SkipFirstDelayCheck.IsChecked == true;
        _config.MuteGameAudio = MuteAudioCheck.IsChecked == true;
        _config.EnableOverlayBorder = OverlayBorderCheck.IsChecked == true;
        _config.EnableOverlayKeyEvents = OverlayKeyEventsCheck.IsChecked == true;
        _config.ManualDelayEnabled = ManualDelayCheck.IsChecked == true;
        _config.EnableMouseDrag = MouseDrag.IsChecked == true;
        _config.Speed = SpeedSlider.Value;
        
        ConfigManager.Save(_config);

        if (sender == OverlayBorderCheck && _overlayWindow != null)
        {
            _overlayWindow.SetBorderVisibility(_config.EnableOverlayBorder);
        }

        if (sender == OverlayKeyEventsCheck && _overlayWindow != null && _loadedSequence != null)
        {
            _overlayWindow.DrawMacroVisualization(_loadedSequence, _config.EnableOverlayKeyEvents);
        }
    }

    private void SaveConfig_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_isInitialized || _config == null) return;
        _config.LoopDelayMs = LoopDelayBox.Text;
        _config.ManualDelayMs = ManualDelayBox.Text;
        _config.DragDurationMs = DragDurationBox.Text;
        ConfigManager.Save(_config);
    }

    private async void StartHotkeyPoller()
    {
        bool wasPlayPressed = false;
        bool wasRecordPressed = false;

        while (true)
        {
            await System.Threading.Tasks.Task.Delay(20);

            if (_isRebindingPlay || _isRebindingRecord) continue;

            bool isPlayPressed = (NativeMethods.GetAsyncKeyState(_config.PlayHotKey) & 0x8000) != 0;
            if (isPlayPressed && !wasPlayPressed)
            {
                wasPlayPressed = true;
                Dispatcher.Invoke(() => TogglePlayback());
            }
            else if (!isPlayPressed)
            {
                wasPlayPressed = false;
            }

            bool isRecordPressed = (NativeMethods.GetAsyncKeyState(_config.RecordHotKey) & 0x8000) != 0;
            if (isRecordPressed && !wasRecordPressed)
            {
                wasRecordPressed = true;
                Dispatcher.Invoke(() => ToggleRecording());
            }
            else if (!isRecordPressed)
            {
                wasRecordPressed = false;
            }
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
    }

    private void ToggleOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null)
        {
            _overlayWindow = new OverlayWindow();
            _overlayWindow.Closed += (s, ev) => 
            {
                _overlayWindow = null;
                ToggleOverlayBtn.Content = "Start Overlay";
                ToggleOverlayBtn.Foreground = Brushes.White;
            };

            _overlayWindow.SetTargetWindow("PioneerGame");
            _overlayWindow.Show();
            _overlayWindow.SetBorderVisibility(_config.EnableOverlayBorder);
            
            // If a macro is already loaded in memory, draw it immediately when overlay opens
            if (_loadedSequence != null)
            {
                _overlayWindow.DrawMacroVisualization(_loadedSequence, _config.EnableOverlayKeyEvents);
            }
            
            ToggleOverlayBtn.Content = "Stop Overlay";
            // UpdateStatus("Overlay launched and attempting to snap to window.");
        }
        else
        {
            _overlayWindow.Close();
            _overlayWindow = null;
            
            ToggleOverlayBtn.Content = "Start Overlay";
            ToggleOverlayBtn.Foreground = Brushes.White;
            // UpdateStatus("Overlay stopped.");
        }
    }

    private void ToggleRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleRecording();
    }

    private void ToggleRecording()
    {
        if (_recorder == null)
            _recorder = new MacroRecorder();

        if (!_recorder.IsRecording)
        {
            IntPtr hwnd = GetHwndFromProcessName("PioneerGame");
            if (hwnd == IntPtr.Zero)
            {
                MessageBox.Show("Target window not found. Cannot start recording.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _recorder.StartRecording(hwnd, _config.RecordHotKey);
            SaveMacroBtn.IsEnabled = false;
            ToggleRecordBtn.Content = "Stop Record";
            ToggleRecordBtn.Foreground = Brushes.Red;
            // UpdateStatus("Recording inputs... (Press Record Bind or Stop when finished)");
        }
        else
        {
            _recorder.StopRecording();
            SaveMacroBtn.IsEnabled = true;
            ToggleRecordBtn.Content = "Toggle Record";
            ToggleRecordBtn.Foreground = Brushes.White;
            // UpdateStatus("Recording stopped. You can now save the macro.");
        }
    }

    private void SaveMacroBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder == null) return;

        try
        {
            string fileName = string.IsNullOrWhiteSpace(SaveNameBox.Text) ? "MyMacro" : SaveNameBox.Text;
            if (!fileName.EndsWith(".json")) fileName += ".json";

            string path = Path.Combine(_macrosFolder, fileName);
            _recorder.SaveToJson(path);
            
            RefreshMacrosList();
            // UpdateStatus($"Macro saved successfully to:\n{fileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save macro. {ex.Message}", "Error");
        }
    }

    private void TogglePlayBtn_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private void TogglePlayback()
    {
        if (_playCts != null)
        {
            // Cancel current playback
            _playCts.Cancel();
            // UpdateStatus("Cancelling playback...");
            TogglePlayBtn.Content = "Play/Stop Macro";
            TogglePlayBtn.Foreground = Brushes.White;
            return;
        }

        PlayMacro();
    }

    private async void PlayMacro()
    {

        if (_loadedSequence == null)
        {
            MessageBox.Show("Please load a macro first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IntPtr hwnd = GetHwndFromProcessName("PioneerGame");
        if (hwnd == IntPtr.Zero)
        {
            MessageBox.Show("PioneerGame window not found. Cannot play macro.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        double speed = SpeedSlider.Value;

        string nameWithoutExt = "PioneerGame";
        var processes = System.Diagnostics.Process.GetProcessesByName(nameWithoutExt);
        int targetPid = processes.Length > 0 ? processes[0].Id : 0;
        bool shouldMute = MuteAudioCheck.IsChecked == true && targetPid != 0;

        _playCts = new System.Threading.CancellationTokenSource();
        var token = _playCts.Token;

        if (shouldMute) AudioManager.SetProcessMute(targetPid, "PioneerGame.exe", true);

        // UpdateStatus($"Playing macro (Speed: {speed}X)...");
        SaveMacroBtn.IsEnabled = false;
        TogglePlayBtn.Content = "Stop Playback";
        TogglePlayBtn.Foreground = Brushes.Red;

        bool loop = LoopMacroCheck.IsChecked == true;
        int sleepMs = int.TryParse(LoopDelayBox.Text, out int s) ? s : 1000;

        bool skipFirstDelay = SkipFirstDelayCheck.IsChecked == true;
        bool manualDelayEnabled = ManualDelayCheck.IsChecked == true;
        bool enableMouseDrag = MouseDrag.IsChecked == true;
        int.TryParse(ManualDelayBox.Text, out int manualDelayMs);
        int.TryParse(DragDurationBox.Text, out int dragDurationMs);

        try
        {
            await System.Threading.Tasks.Task.Run(async () =>
            {
                do
                {
                    if (token.IsCancellationRequested) break;
                    await MacroRecorder.PlayMacro(_loadedSequence, hwnd, speed, skipFirstDelay, manualDelayEnabled, manualDelayMs, dragDurationMs, enableMouseDrag, token);

                    if (loop && !token.IsCancellationRequested)
                    {
                        // Dispatcher.Invoke(() => UpdateStatus($"Sleeping {sleepMs}ms before next loop cycle..."));
                        await System.Threading.Tasks.Task.Delay(sleepMs, token);
                    }

                } while (loop && !token.IsCancellationRequested);
            }, token);
        }
        catch (System.Threading.Tasks.TaskCanceledException) { }
        catch (OperationCanceledException) { }

        _playCts.Dispose();
        _playCts = null;

        // UpdateStatus("Playback finished or cancelled.");
        SaveMacroBtn.IsEnabled = true;
        TogglePlayBtn.Content = "Play/Stop Macro";
        TogglePlayBtn.Foreground = Brushes.White;

        if (shouldMute) AudioManager.SetProcessMute(targetPid, "PioneerGame.exe", false);
    }

    private void RefreshMacrosList()
    {
        if (!Directory.Exists(_macrosFolder)) return;
        
        MacroListBox.Items.Clear();
        var files = Directory.GetFiles(_macrosFolder, "*.json");
        var sortedFiles = files.OrderBy(f => File.GetCreationTime(f)).ToArray();
        
        foreach (var file in sortedFiles)
        {
            MacroListBox.Items.Add(Path.GetFileName(file));
        }
    }

    private void RefreshListBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshMacrosList();
    }

    private void ClearOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow != null && _overlayWindow.IsVisible)
        {
            _overlayWindow.DrawMacroVisualization(new MacroSequence { Actions = new MacroAction[0] }, _config.EnableOverlayKeyEvents);
            // UpdateStatus("Overlay visualization cleared.");
        }
        MacroListBox.SelectedItem = null;
        _loadedSequence = null;
    }

    private void DeleteMacroBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MacroListBox.SelectedItem == null)
        {
            MessageBox.Show("Please select a macro to delete.", "No Macro Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string fileName = MacroListBox.SelectedItem.ToString() ?? "";
        string path = Path.Combine(_macrosFolder, fileName);

        if (MessageBox.Show($"Are you sure you want to delete {fileName}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                
                RefreshMacrosList();
                ClearOverlayBtn_Click(sender, e);
                // UpdateStatus($"Deleted macro: {fileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete macro. {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }



    private void MacroListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MacroListBox.SelectedItem == null) return;
        string fileName = MacroListBox.SelectedItem.ToString() ?? "";
        string path = Path.Combine(_macrosFolder, fileName);
        
        try
        {
            _loadedSequence = MacroRecorder.LoadFromJson(path);
            
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.DrawMacroVisualization(_loadedSequence, _config.EnableOverlayKeyEvents);
            }
            
            // UpdateStatus($"Loaded: {fileName} ({_loadedSequence.Actions.Length} actions).");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load {fileName}. {ex.Message}");
        }
    }

    private void UpdateHotKeyUI()
    {
        // Find WPF Key name from Virtual Key code for visual friendlyness
        var recordKey = System.Windows.Input.KeyInterop.KeyFromVirtualKey(_config.RecordHotKey);
        var playKey = System.Windows.Input.KeyInterop.KeyFromVirtualKey(_config.PlayHotKey);
        
        RebindRecordBtn.Content = $"Bind: [{recordKey}]";
        RebindPlayBtn.Content = $"Bind: [{playKey}]";
    }

    private void RebindRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRebindingRecord = true;
        _isRebindingPlay = false;
        RebindRecordBtn.Content = "Press any key...";
    }

    private void RebindPlayBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRebindingPlay = true;
        _isRebindingRecord = false;
        RebindPlayBtn.Content = "Press any key...";
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRebindingRecord && !_isRebindingPlay) return;

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        int vkCode = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        var helper = new System.Windows.Interop.WindowInteropHelper(this);

        if (_isRebindingRecord)
        {
            _config.RecordHotKey = vkCode;
            _isRebindingRecord = false;
        }
        else if (_isRebindingPlay)
        {
            _config.PlayHotKey = vkCode;
            _isRebindingPlay = false;
        }

        ConfigManager.Save(_config);
        UpdateHotKeyUI();
        e.Handled = true;
    }

    private void UpdateStatus(string message)
    {
        // StatusText.Text = $"Status: {message}";
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

    protected override void OnClosed(EventArgs e)
    {
        _recorder?.Dispose();
        _overlayWindow?.Close();
        base.OnClosed(e);
    }
}
