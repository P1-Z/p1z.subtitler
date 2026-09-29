using System.Windows.Input;
namespace P1Z.Subtitler.Models;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public sealed class AppSettings
{
    public const string PixelFontFamily = "Press Start 2P";
    public const string DefaultRainbowWords = "gay";

    public HotkeyModifiers HotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    public Key HotkeyKey { get; set; } = Key.S;
    public string FontFamily { get; set; } = PixelFontFamily;
    public double FontSize { get; set; } = 30;
    public double BackgroundOpacity { get; set; } = 0.68;
    public double MaximumShakePixels { get; set; } = 3;
    public double PlacementRangePercent { get; set; } = 34;
    public double CrtIntensityPercent { get; set; } = 55;
    public string RainbowWords { get; set; } = DefaultRainbowWords;
    public bool LaunchWithWindows { get; set; }
    public bool Enabled { get; set; } = true;

    public AppSettings Clone() => new()
    {
        HotkeyModifiers = HotkeyModifiers,
        HotkeyKey = HotkeyKey,
        FontFamily = FontFamily,
        FontSize = FontSize,
        BackgroundOpacity = BackgroundOpacity,
        MaximumShakePixels = MaximumShakePixels,
        PlacementRangePercent = PlacementRangePercent,
        CrtIntensityPercent = CrtIntensityPercent,
        RainbowWords = RainbowWords,
        LaunchWithWindows = LaunchWithWindows,
        Enabled = Enabled
    };

    public void Normalize()
    {
        if (HotkeyKey is Key.None or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            HotkeyKey = Key.S;
        if (string.IsNullOrWhiteSpace(FontFamily))
            FontFamily = PixelFontFamily;
        FontSize = Math.Clamp(FontSize, 12, 96);
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.1, 0.95);
        MaximumShakePixels = Math.Clamp(MaximumShakePixels, 0, 24);
        PlacementRangePercent = Math.Clamp(PlacementRangePercent, 5, 80);
        CrtIntensityPercent = Math.Clamp(CrtIntensityPercent, 0, 100);
        RainbowWords = string.Join(", ", ParseWordList(RainbowWords));
    }

    /// <summary>
    /// Splits a user supplied word list. Blank means no word ever turns rainbow.
    /// </summary>
    public static HashSet<string> ParseWordList(string? value)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return words;
        foreach (var word in value.Split([',', ';', ' ', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            words.Add(word);
        return words;
    }
}
