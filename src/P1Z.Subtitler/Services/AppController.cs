using System.Windows;
using P1Z.Subtitler.Models;
using P1Z.Subtitler.Native;
using P1Z.Subtitler.Windows;

namespace P1Z.Subtitler.Services;

internal sealed class AppController : IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly LiveSubtitleWindow _liveSubtitle = new();
    private readonly TrayService _tray;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings;
    private IntPtr _previousForeground;
    private bool _exiting;

    public AppController()
    {
        _settings = _settingsStore.Load();
        _tray = new TrayService(ShowSettings, ToggleEnabled, Exit);
        _hotkey.Pressed += HandleHotkey;
        _liveSubtitle.SessionEnded += HandleSessionEnded;
    }

    public void Initialize()
    {
        StartupRegistration.Apply(_settings.LaunchWithWindows);
        if (_settings.Enabled)
        {
            try
            {
                _hotkey.Register(_settings);
            }
            catch (Exception exception)
            {
                _settings.Enabled = false;
                _settingsStore.Save(_settings);
                System.Windows.MessageBox.Show(
                    $"The configured global hotkey could not be registered.\n\n{exception.Message}",
                    "p1z.subtitler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        _tray.SetEnabled(_settings.Enabled);
    }

    private void HandleHotkey(object? sender, EventArgs args)
    {
        if (!_settings.Enabled || _exiting)
            return;
        if (_liveSubtitle.IsSessionActive)
        {
            _liveSubtitle.Activate();
            return;
        }
        var monitor = NativeMethods.GetActiveMonitor();
        _previousForeground = monitor.PreviousForeground;
        _liveSubtitle.Begin(monitor, _settings);
    }

    private void HandleSessionEnded() => NativeMethods.RestoreForeground(_previousForeground);

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.SaveRequested += ApplySettings;
        }
        _settingsWindow.LoadSettings(_settings);
        if (!_settingsWindow.IsVisible)
            _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplySettings(AppSettings candidate)
    {
        var previous = _settings.Clone();
        candidate.Enabled = previous.Enabled;
        try
        {
            if (candidate.Enabled)
                _hotkey.Register(candidate);
            else
                _hotkey.Unregister();
            StartupRegistration.Apply(candidate.LaunchWithWindows);
            _settingsStore.Save(candidate);
            _settings = candidate;
            _tray.SetEnabled(_settings.Enabled);
            _settingsWindow?.Hide();
        }
        catch (Exception exception)
        {
            try
            {
                if (previous.Enabled)
                    _hotkey.Register(previous);
            }
            catch
            {
            }
            _settingsWindow?.ShowError(exception.Message);
        }
    }

    private void ToggleEnabled()
    {
        if (_settings.Enabled)
        {
            _hotkey.Unregister();
            _settings.Enabled = false;
            _liveSubtitle.Cancel();
        }
        else
        {
            try
            {
                _hotkey.Register(_settings);
                _settings.Enabled = true;
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show(exception.Message, "p1z.subtitler", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        _settingsStore.Save(_settings);
        _tray.SetEnabled(_settings.Enabled);
    }

    private void Exit()
    {
        if (_exiting)
            return;
        _exiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _exiting = true;
        _hotkey.Dispose();
        _liveSubtitle.CloseForExit();
        _settingsWindow?.CloseForExit();
        _tray.Dispose();
    }
}
