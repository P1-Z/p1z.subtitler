using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using P1Z.Subtitler.Models;
using P1Z.Subtitler.Services;

namespace P1Z.Subtitler.Windows;

public partial class SettingsWindow : Window
{
    private AppSettings _editing = new();
    private bool _capturingHotkey;
    private bool _allowClose;

    public SettingsWindow()
    {
        InitializeComponent();
        FontPicker.ItemsSource = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Append(AppSettings.PixelFontFamily)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public event Action<AppSettings>? SaveRequested;

    public void LoadSettings(AppSettings settings)
    {
        _editing = settings.Clone();
        HotkeyButton.Content = GlobalHotkeyService.Format(_editing);
        FontPicker.SelectedItem = _editing.FontFamily;
        if (FontPicker.SelectedItem is null)
            FontPicker.Text = _editing.FontFamily;
        FontSizeInput.Text = _editing.FontSize.ToString("0.#", CultureInfo.CurrentCulture);
        OpacityInput.Text = (_editing.BackgroundOpacity * 100).ToString("0", CultureInfo.CurrentCulture);
        ShakeInput.Text = _editing.MaximumShakePixels.ToString("0.#", CultureInfo.CurrentCulture);
        PlacementInput.Text = _editing.PlacementRangePercent.ToString("0", CultureInfo.CurrentCulture);
        CrtInput.Text = _editing.CrtIntensityPercent.ToString("0", CultureInfo.CurrentCulture);
        RainbowInput.Text = _editing.RainbowWords;
        StartupCheck.IsChecked = _editing.LaunchWithWindows;
        StatusText.Text = "changes apply when saved";
        _capturingHotkey = false;
    }

    public void ShowError(string message) => StatusText.Text = message;

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyButton.Content = "press a key combination...";
        HotkeyButton.Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey)
            return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return;
        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;
        if (modifiers == HotkeyModifiers.None && (key < Key.F1 || key > Key.F24))
        {
            StatusText.Text = "use a modifier, or choose F1-F24";
            return;
        }
        _editing.HotkeyModifiers = modifiers;
        _editing.HotkeyKey = key;
        HotkeyButton.Content = GlobalHotkeyService.Format(_editing);
        StatusText.Text = "hotkey captured";
        _capturingHotkey = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDouble(FontSizeInput.Text, 12, 96, out var fontSize)
            || !TryReadDouble(OpacityInput.Text, 10, 95, out var opacity)
            || !TryReadDouble(ShakeInput.Text, 0, 24, out var shake)
            || !TryReadDouble(PlacementInput.Text, 5, 80, out var placement)
            || !TryReadDouble(CrtInput.Text, 0, 100, out var crt))
        {
            StatusText.Text = "check the numeric values and their ranges";
            return;
        }
        _editing.FontFamily = FontPicker.SelectedItem as string ?? FontPicker.Text;
        _editing.FontSize = fontSize;
        _editing.BackgroundOpacity = opacity / 100;
        _editing.MaximumShakePixels = shake;
        _editing.PlacementRangePercent = placement;
        _editing.CrtIntensityPercent = crt;
        _editing.RainbowWords = RainbowInput.Text;
        _editing.LaunchWithWindows = StartupCheck.IsChecked == true;
        _editing.Normalize();
        SaveRequested?.Invoke(_editing.Clone());
    }

    private static bool TryReadDouble(string text, double minimum, double maximum, out double result) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
        && result >= minimum && result <= maximum;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }
}
