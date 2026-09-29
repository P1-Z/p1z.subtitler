using System.Windows;
using System.Windows.Media;

namespace P1Z.Subtitler.Controls;

/// <summary>
/// Paints the glass of a CRT over the subtitle box: phosphor scanlines, a slow
/// retrace band rolling down the tube, and a faint mains flicker.
/// </summary>
/// <remarks>
/// The element measures to nothing, so it can sit on top of the box and stretch
/// to whatever size the text gives it without ever growing the window.
/// </remarks>
public sealed class CrtOverlayElement : FrameworkElement
{
    // Three device pixels per line: dense enough to read as a raster at any font
    // size, sparse enough that a lit pixel row still survives between two dark ones.
    private const double ScanlinePeriodPixels = 3;
    private const double ScanlineAlpha = 0.34;
    private const double RollSeconds = 6.5;
    private const double RollHeightFactor = 0.8;
    private const double RollMinimumHeight = 36;
    private const double RollAlpha = 0.055;
    private const double FlickerAlpha = 0.022;
    private const double FlickerPrimary = 7.3;
    private const double FlickerSecondary = 11.9;

    private double _intensity;
    private double _clock;
    private bool _live;
    private bool _animating;
    private TimeSpan _lastFrame = TimeSpan.MinValue;

    public CrtOverlayElement()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Strength of the whole effect, 0 (off) to 1.</summary>
    public double Intensity
    {
        get => _intensity;
        set
        {
            _intensity = Math.Clamp(value, 0, 1);
            if (_live && _intensity > 0)
                StartAnimation();
            else if (_intensity <= 0)
                StopAnimation();
            InvalidateVisual();
        }
    }

    public void BeginSession()
    {
        _live = true;
        if (_intensity > 0)
            StartAnimation();
        InvalidateVisual();
    }

    public void EndSession()
    {
        _live = false;
        StopAnimation();
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) =>
        new(0, 0);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = RenderSize;
        if (_intensity <= 0 || size.Width <= 0 || size.Height <= 0)
            return;

        DrawScanlines(drawingContext, size);
        DrawRetraceBand(drawingContext, size);
        DrawFlicker(drawingContext, size);
    }

    /// <summary>
    /// One dark row every three device pixels, laid out in device space so the
    /// raster stays crisp at any DPI instead of shimmering as the box resizes.
    /// </summary>
    private void DrawScanlines(DrawingContext drawingContext, System.Windows.Size size)
    {
        var scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (scale <= 0)
            scale = 1;
        var period = ScanlinePeriodPixels / scale;
        var thickness = 1 / scale;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(byte.MaxValue * ScanlineAlpha * _intensity), 0, 0, 0));
        brush.Freeze();

        for (var top = 0d; top < size.Height; top += period)
        {
            var height = Math.Min(thickness, size.Height - top);
            drawingContext.DrawRectangle(brush, null,
                new Rect(0, top, size.Width, height));
        }
    }

    private void DrawRetraceBand(DrawingContext drawingContext, System.Windows.Size size)
    {
        var height = Math.Max(size.Height * RollHeightFactor, RollMinimumHeight);
        var travel = size.Height + height;
        var top = -height + _clock % RollSeconds / RollSeconds * travel;
        var peak = System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(byte.MaxValue * RollAlpha * _intensity), 255, 255, 255);
        var clear = System.Windows.Media.Color.FromArgb(0, 255, 255, 255);

        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(clear, 0));
        brush.GradientStops.Add(new GradientStop(peak, 0.5));
        brush.GradientStops.Add(new GradientStop(clear, 1));
        brush.Freeze();
        drawingContext.DrawRectangle(brush, null, new Rect(0, top, size.Width, height));
    }

    /// <summary>
    /// Two beat frequencies with no common period, so the brightness never settles
    /// into a rhythm the eye can lock onto.
    /// </summary>
    private void DrawFlicker(DrawingContext drawingContext, System.Windows.Size size)
    {
        var wave = 0.6 * Math.Sin(_clock * FlickerPrimary) + 0.4 * Math.Sin(_clock * FlickerSecondary);
        var alpha = FlickerAlpha * _intensity * (wave + 1) / 2;
        if (alpha <= 0.001)
            return;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(byte.MaxValue * alpha), 255, 255, 255));
        brush.Freeze();
        drawingContext.DrawRectangle(brush, null, new Rect(size));
    }

    private void StartAnimation()
    {
        if (_animating)
            return;
        _animating = true;
        _lastFrame = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        if (args is not RenderingEventArgs rendering)
            return;
        // WPF raises this more than once for the same frame; only advance on new ones.
        if (rendering.RenderingTime == _lastFrame)
            return;
        var elapsed = _lastFrame == TimeSpan.MinValue
            ? 0.016
            : Math.Clamp((rendering.RenderingTime - _lastFrame).TotalSeconds, 0, 0.1);
        _lastFrame = rendering.RenderingTime;
        _clock += elapsed;
        InvalidateVisual();
    }
}
