using System;
using System.IO;
using System.Text.Json;

namespace Macro;

public class AppConfig
{
    // Default F8 (0x77)
    public int RecordHotKey { get; set; } = 0x77;
    
    // Default F9 (0x78)
    public int PlayHotKey { get; set; } = 0x78;

    // Optional modifiers (Alt, Ctrl, Shift)
    public uint RecordModifiers { get; set; } = 0;
    public uint PlayModifiers { get; set; } = 0;

    // UI Settings
    public bool LoopMacro { get; set; } = false;
    public string LoopDelayMs { get; set; } = "1000";
    public bool SkipFirstDelay { get; set; } = false;
    public bool MuteGameAudio { get; set; } = false;
    public bool EnableOverlayBorder { get; set; } = false;
    public bool EnableOverlayKeyEvents { get; set; } = false;
    public bool ManualDelayEnabled { get; set; } = false;
    public string ManualDelayMs { get; set; } = "50";
    public string DragDurationMs { get; set; } = "100";
    public bool EnableMouseDrag { get; set; } = false;
    public double Speed { get; set; } = 1.0;
}

public static class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(ConfigPath, json);
    }
}
