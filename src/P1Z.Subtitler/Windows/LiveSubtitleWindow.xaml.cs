using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using P1Z.Subtitler.Controls;
using P1Z.Subtitler.Models;
using P1Z.Subtitler.Native;
using P1Z.Subtitler.Overlay;

namespace P1Z.Subtitler.Windows;

public partial class LiveSubtitleWindow : Window
{
    private const int SafeMargin = 24;
    private const int MaximumCharacters = 1000;
    private const double WidthShareOfWorkArea = 0.72;
    private const double LineHeightFactor = 1.18;
    // The box comes up the same way it goes down: same length, same curve, so the
    // first character glows in rather than snapping on.
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(170);
    private static readonly IEasingFunction FadeEase = Frozen(
        new QuadraticEase { EasingMode = EasingMode.EaseInOut });

    private readonly Random _random = new();
    private AppSettings _settings = new();
    private System.Windows.Media.FontFamily _fontFamily = new("Consolas");
    private HashSet<string> _rainbowWords = [];
    private MonitorArea _monitor;
    private PixelPoint _anchor;
    private string _buffer = string.Empty;
    private IntPtr _handle;
    private double _pixelsPerDip = 1;
    private double _maximumTextWidth = double.PositiveInfinity;
    private double _maximumTextHeight = double.PositiveInfinity;
    private int _placedLeft = int.MinValue;
    private int _placedTop = int.MinValue;
    private int _sessionRevision;
    private bool _active;
    private bool _boxVisible;
    private bool _closing;

    public LiveSubtitleWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _handle = NativeMethods.EnsureHandle(this);
            NativeMethods.MakeTypingOverlay(_handle);
        };
    }

    public event Action? SessionEnded;

    public bool IsSessionActive => _active;
    public string CurrentText => _buffer;

    public void Begin(MonitorArea monitor, AppSettings settings)
    {
        if (_active)
        {
            Activate();
            Keyboard.Focus(this);
            return;
        }

        _sessionRevision++;
        _monitor = monitor;
        _settings = settings.Clone();
        _anchor = PlacementEngine.ChooseAnchor(
            monitor.WorkArea, settings.PlacementRangePercent, SafeMargin,
            (int)Math.Ceiling(settings.MaximumShakePixels), _random);
        _buffer = string.Empty;
        _placedLeft = int.MinValue;
        _placedTop = int.MinValue;
        _active = true;

        _handle = NativeMethods.EnsureHandle(this);
        NativeMethods.MakeTypingOverlay(_handle);
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _fontFamily = ResolveFontFamily(settings.FontFamily);
        _rainbowWords = AppSettings.ParseWordList(settings.RainbowWords);
        ApplyAppearance(settings);
        RecalculateLimits();

        // The window is shown transparent so it can hold the keyboard, but nothing
        // is painted until there is text: an empty rectangle on screen is just noise.
        _boxVisible = false;
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 0;
        SubtitleText.SetText(string.Empty);
        SubtitleText.BeginSession();
        CrtSurface.BeginSession();
        ApplyTypography();

        if (!IsVisible)
            Show();
        Reposition();
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    public void Cancel()
    {
        if (_active)
            Finish();
    }

    public void CloseForExit()
    {
        _closing = true;
        _active = false;
        SubtitleText.EndSession();
        CrtSurface.EndSession();
        Close();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_active)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Return:
            case Key.Escape:
                e.Handled = true;
                Finish();
                break;
            case Key.Back:
                e.Handled = true;
                RemoveLastTextElement();
                UpdateVisual();
                break;
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_active || string.IsNullOrEmpty(e.Text))
            return;

        var printable = new string(e.Text.Where(character => !char.IsControl(character)).ToArray());
        if (printable.Length == 0)
            return;

        e.Handled = true;
        var available = MaximumCharacters - _buffer.Length;
        if (available <= 0)
            return;
        _buffer += printable.Length <= available ? printable : printable[..available];
        UpdateVisual();
    }

    // The HWND has not been resized yet when this fires, so GetWindowRect would
    // still report the previous size and re-centre the card half a keystroke behind.
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => PlaceCard(
        (int)Math.Round(e.NewSize.Width * _pixelsPerDip),
        (int)Math.Round(e.NewSize.Height * _pixelsPerDip));

    private void Window_DpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        _pixelsPerDip = e.NewDpi.PixelsPerDip;
        if (!_active)
            return;
        RecalculateLimits();
        ApplyTypography();
    }

    private void RemoveLastTextElement()
    {
        if (_buffer.Length == 0)
            return;
        var starts = StringInfo.ParseCombiningCharacters(_buffer);
        _buffer = starts.Length <= 1 ? string.Empty : _buffer[..starts[^1]];
    }

    /// <summary>
    /// Hands the new text to the element and lets WPF do the rest: SizeToContent
    /// resizes the window from the measured content, and the resulting SizeChanged
    /// re-centres it.
    /// </summary>
    private void UpdateVisual()
    {
        ApplyTypography();
        SubtitleText.SetText(_buffer);
        SubtitleText.SetShake(PlacementEngine.ShakePixels(_buffer, _settings.MaximumShakePixels));
        if (_buffer.Length > 0)
            ShowBox();
        else
            HideBox();
    }

    private void ShowBox()
    {
        if (_boxVisible)
            return;
        _boxVisible = true;
        Root.BeginAnimation(UIElement.OpacityProperty, FadeTo(1));
    }

    private void HideBox()
    {
        if (!_boxVisible)
            return;
        _boxVisible = false;
        Root.BeginAnimation(UIElement.OpacityProperty, FadeTo(0));
    }

    /// <summary>
    /// Always starts from the opacity on screen right now, so interrupting a fade
    /// half way turns it around instead of jumping.
    /// </summary>
    private DoubleAnimation FadeTo(double opacity) =>
        new(Root.Opacity, opacity, Fade) { EasingFunction = FadeEase };

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }

    /// <summary>
    /// Picks the largest font size at or below the configured one whose wrapped
    /// text still fits the monitor, measured without disturbing live layout.
    /// </summary>
    private void ApplyTypography()
    {
        var reserve = (int)Math.Ceiling(_settings.MaximumShakePixels);
        var fontSize = Math.Max(8, _settings.FontSize);
        for (var attempt = 0; attempt < 12 && fontSize > 8; attempt++)
        {
            var measured = JitterTextElement.MeasureText(
                _buffer, _fontFamily, fontSize, Math.Ceiling(fontSize * LineHeightFactor),
                _pixelsPerDip, _maximumTextWidth, reserve);
            if (measured.Height <= _maximumTextHeight)
                break;
            fontSize = Math.Max(8, fontSize * _maximumTextHeight / measured.Height * 0.97);
        }

        SubtitleText.TextFontFamily = _fontFamily;
        SubtitleText.RainbowWords = _rainbowWords;
        SubtitleText.MaximumJitter = reserve;
        SubtitleText.TextFontSize = fontSize;
        SubtitleText.TextLineHeight = Math.Ceiling(fontSize * LineHeightFactor);
        SubtitleText.MaxWidth = _maximumTextWidth;
    }

    private void RecalculateLimits()
    {
        var work = _monitor.WorkArea;
        var inset = SafeMargin;
        var widthPixels = Math.Max(120, Math.Min(
            (int)Math.Round(work.Width * WidthShareOfWorkArea), work.Width - inset * 2));
        var heightPixels = Math.Max(60, work.Height - inset * 2);
        var horizontal = SubtitleBox.Padding.Left + SubtitleBox.Padding.Right
            + SubtitleBox.BorderThickness.Left + SubtitleBox.BorderThickness.Right;
        var vertical = SubtitleBox.Padding.Top + SubtitleBox.Padding.Bottom
            + SubtitleBox.BorderThickness.Top + SubtitleBox.BorderThickness.Bottom;
        _maximumTextWidth = Math.Max(40, widthPixels / _pixelsPerDip - horizontal);
        _maximumTextHeight = Math.Max(20, heightPixels / _pixelsPerDip - vertical);
    }

    private void ApplyAppearance(AppSettings settings)
    {
        var alpha = (byte)Math.Round(Math.Clamp(settings.BackgroundOpacity, 0.1, 0.95) * byte.MaxValue);
        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 0, 0, 0));
        fill.Freeze();
        SubtitleBox.Background = fill;

        var crt = Math.Clamp(settings.CrtIntensityPercent / 100, 0, 1);
        SubtitleText.CrtIntensity = crt;
        CrtSurface.Intensity = crt;
    }

    private void PlayOutro(int revision)
    {
        var fade = FadeTo(0);
        fade.Completed += (_, _) => Conclude(revision);
        Root.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Conclude(int revision)
    {
        if (revision != _sessionRevision || _active || _closing)
            return;
        // The tube keeps rolling and flickering all the way through the fade out;
        // only once the box is gone does the surface stop repainting.
        CrtSurface.EndSession();
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 0;
        Hide();
        _buffer = string.Empty;
        SubtitleText.SetText(string.Empty);
    }

    private void Reposition()
    {
        if (_handle != IntPtr.Zero && NativeMethods.TryGetWindowBounds(_handle, out var bounds))
            PlaceCard(bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Keeps the card centred on the session anchor. Only the position is set
    /// here — the size belongs to WPF, so the surface and its contents can never
    /// disagree about how large the overlay is.
    /// </summary>
    private void PlaceCard(int width, int height)
    {
        if (!_active || _handle == IntPtr.Zero || width <= 0 || height <= 0)
            return;

        var work = _monitor.WorkArea;
        var left = _anchor.X - width / 2;
        var top = _anchor.Y - height / 2;
        left = Math.Clamp(left, work.Left + SafeMargin,
            Math.Max(work.Left + SafeMargin, work.Right - SafeMargin - width));
        top = Math.Clamp(top, work.Top + SafeMargin,
            Math.Max(work.Top + SafeMargin, work.Bottom - SafeMargin - height));

        if (left == _placedLeft && top == _placedTop)
            return;
        _placedLeft = left;
        _placedTop = top;
        NativeMethods.MoveTypingOverlay(_handle, left, top);
    }

    private void Finish()
    {
        if (!_active)
            return;
        _active = false;
        // The text stays on screen through the fade; clearing it here would shrink
        // the box to nothing for the length of the animation.
        SubtitleText.EndSession();
        if (!_closing)
            SessionEnded?.Invoke();
        if (_boxVisible)
        {
            _boxVisible = false;
            PlayOutro(_sessionRevision);
        }
        else
        {
            Conclude(_sessionRevision);
        }
    }

    private static System.Windows.Media.FontFamily ResolveFontFamily(string fontFamily) =>
        string.Equals(fontFamily, AppSettings.PixelFontFamily, StringComparison.OrdinalIgnoreCase)
            ? new System.Windows.Media.FontFamily(
                new Uri("pack://application:,,,/"),
                "./Assets/Fonts/#Press Start 2P")
            : new System.Windows.Media.FontFamily(fontFamily);
}
