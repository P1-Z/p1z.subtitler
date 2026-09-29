using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace P1Z.Subtitler.Controls;

/// <summary>
/// Draws the live subtitle one grapheme at a time so uppercase characters can
/// wobble independently while the box around them stays anchored.
/// </summary>
/// <remarks>
/// Motion is a continuous function of time rather than a per-frame random walk:
/// every glyph rides two sine waves per axis whose frequencies share no common
/// period, so the wobble never repeats visibly, never snaps between frames, and
/// never resets when the surrounding text is rebuilt.
/// </remarks>
public sealed class JitterTextElement : FrameworkElement
{
    // Around 7-12 Hz: fast enough to read as a vibration rather than a bounce,
    // slow enough that a 60 Hz frame still lands several samples per cycle.
    private const double FrequencyXPrimary = 44.0;
    private const double FrequencyXSecondary = 68.0;
    private const double FrequencyYPrimary = 50.0;
    private const double FrequencyYSecondary = 76.0;
    private const double PrimaryWeight = 0.7;
    private const double SecondaryWeight = 0.3;
    private const double AmplitudeEaseSeconds = 0.03;
    private const double EmptyWidthFactor = 1.6;
    private const double RainbowSweepDegrees = 300;
    private const double RainbowDegreesPerSecond = 110;
    private const double RainbowFloor = 0.22;
    // A tube never lands all three guns on exactly the same spot, and its phosphor
    // keeps glowing a little outside the glyph the beam was aimed at.
    private const double AberrationFactor = 0.035;
    private const double MinimumAberration = 0.6;
    private const double GhostAlpha = 0.55;
    private const double GlowRadiusFactor = 0.42;
    private const double MinimumGlowRadius = 4;
    private const double MaximumGlowRadius = 16;
    private const double GlowOpacity = 0.8;

    private readonly List<Glyph> _glyphs = [];
    private readonly List<double> _amplitudes = [];
    private System.Windows.Media.FontFamily _fontFamily = new("Consolas");
    private System.Windows.Media.Brush _foreground = System.Windows.Media.Brushes.White;
    private string _text = string.Empty;
    private string? _builtText;
    private double _builtWidth = double.NaN;
    private double _fontSize = 30;
    private double _lineHeight = 36;
    private int _maximumJitter = 9;
    private IReadOnlyCollection<string> _rainbowWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double _requestedShake;
    private double _crtIntensity;
    private double _clock;
    private bool _hasRainbow;
    private bool _live;
    private bool _animating;
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private System.Windows.Media.Brush? _ghostLeft;
    private System.Windows.Media.Brush? _ghostRight;
    private System.Windows.Size _contentSize;
    private int _lastRenderedGlyphCount;
    private string _lastRenderedText = string.Empty;

    public JitterTextElement()
    {
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        SnapsToDevicePixels = true;
    }

    public string Text => _text;

    public System.Windows.Media.FontFamily TextFontFamily
    {
        get => _fontFamily;
        set
        {
            _fontFamily = value;
            InvalidateContent();
        }
    }

    public double TextFontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            ApplyCrt();
            InvalidateContent();
        }
    }

    public double TextLineHeight
    {
        get => _lineHeight;
        set
        {
            _lineHeight = value;
            InvalidateContent();
        }
    }

    public System.Windows.Media.Brush Foreground
    {
        get => _foreground;
        set
        {
            _foreground = value;
            ApplyCrt();
            InvalidateContent();
        }
    }

    /// <summary>Words that render as an animated rainbow gradient, matched whole and case-insensitively.</summary>
    public IReadOnlyCollection<string> RainbowWords
    {
        get => _rainbowWords;
        set
        {
            _rainbowWords = value;
            InvalidateContent();
        }
    }

    /// <summary>
    /// Blank space reserved on every side so the wobble can never grow the box
    /// or clip a glyph, whatever the current shake strength happens to be.
    /// </summary>
    public int MaximumJitter
    {
        get => _maximumJitter;
        set
        {
            _maximumJitter = Math.Max(0, value);
            InvalidateContent();
        }
    }

    /// <summary>
    /// Strength of the CRT treatment, 0 (plain text) to 1: phosphor bloom around
    /// every glyph plus the red/cyan fringing of three misconverged guns.
    /// </summary>
    public double CrtIntensity
    {
        get => _crtIntensity;
        set
        {
            var intensity = Math.Clamp(value, 0, 1);
            if (_crtIntensity.Equals(intensity))
                return;
            _crtIntensity = intensity;
            ApplyCrt();
            InvalidateVisual();
        }
    }

    public void SetText(string text)
    {
        if (string.Equals(_text, text, StringComparison.Ordinal))
            return;
        _text = text;
        InvalidateContent();
    }

    /// <summary>Target wobble amplitude in device-independent pixels.</summary>
    public void SetShake(double pixels)
    {
        _requestedShake = Math.Clamp(pixels, 0, _maximumJitter);
        if (_requestedShake > 0)
            StartAnimation();
    }

    public void BeginSession()
    {
        _live = true;
        _requestedShake = 0;
        _amplitudes.Clear();
        if (_hasRainbow)
            StartAnimation();
        InvalidateVisual();
    }

    public void EndSession()
    {
        // The wobble is left to ease out on its own; the render loop shuts itself
        // down once nothing is moving, so the last frames stay smooth.
        _live = false;
        _requestedShake = 0;
        InvalidateVisual();
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        Build(availableSize.Width);
        return _contentSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        // Insurance against ever painting the previous value of Text: layout runs
        // before render in a normal pass, but a rebuild here costs nothing when
        // the cache is already current.
        Build(_builtWidth);
        _lastRenderedText = _text;
        _lastRenderedGlyphCount = _glyphs.Count;

        // The misconverged guns are laid down in a pass of their own, so a fringe
        // always ends up under the white text instead of over its left neighbour.
        if (_crtIntensity > 0 && _ghostLeft is not null && _ghostRight is not null)
        {
            var aberration = _crtIntensity * Math.Max(MinimumAberration, _fontSize * AberrationFactor);
            for (var index = 0; index < _glyphs.Count; index++)
            {
                var glyph = _glyphs[index];
                Wobble(glyph, Amplitude(index), out var ghostX, out var ghostY);
                glyph.Fill.SetForegroundBrush(_ghostLeft);
                drawingContext.DrawText(glyph.Fill, new System.Windows.Point(
                    glyph.X + ghostX - aberration, glyph.Y + ghostY));
                glyph.Fill.SetForegroundBrush(_ghostRight);
                drawingContext.DrawText(glyph.Fill, new System.Windows.Point(
                    glyph.X + ghostX + aberration, glyph.Y + ghostY));
            }
        }

        System.Windows.Media.Brush? brush = null;
        double runLeft = 0, runRight = 0;
        for (var index = 0; index < _glyphs.Count; index++)
        {
            var glyph = _glyphs[index];
            Wobble(glyph, Amplitude(index), out var offsetX, out var offsetY);

            if (glyph.Rainbow)
            {
                // One brush per run, reused across its letters: the gradient is mapped
                // in element space so it sweeps the whole word, not each glyph.
                if (brush is null || glyph.RunLeft != runLeft || glyph.RunRight != runRight)
                {
                    runLeft = glyph.RunLeft;
                    runRight = glyph.RunRight;
                    brush = RainbowBrush(runLeft, runRight, _clock * RainbowDegreesPerSecond);
                }
                glyph.Fill.SetForegroundBrush(brush);
            }
            else
            {
                // The ghost pass left its gun colour on the shared FormattedText.
                glyph.Fill.SetForegroundBrush(_foreground);
            }

            drawingContext.DrawText(glyph.Fill, new System.Windows.Point(
                glyph.X + offsetX, glyph.Y + offsetY));
        }
    }

    private double Amplitude(int index) => index < _amplitudes.Count ? _amplitudes[index] : 0;

    private void Wobble(Glyph glyph, double amplitude, out double offsetX, out double offsetY)
    {
        if (amplitude <= 0.01)
        {
            offsetX = 0;
            offsetY = 0;
            return;
        }
        offsetX = amplitude * (
            PrimaryWeight * Math.Sin(_clock * FrequencyXPrimary + glyph.PhaseXPrimary)
            + SecondaryWeight * Math.Sin(_clock * FrequencyXSecondary + glyph.PhaseXSecondary));
        offsetY = amplitude * (
            PrimaryWeight * Math.Sin(_clock * FrequencyYPrimary + glyph.PhaseYPrimary)
            + SecondaryWeight * Math.Sin(_clock * FrequencyYSecondary + glyph.PhaseYSecondary));
    }

    /// <summary>
    /// Rebuilds the bloom and the two gun brushes for the current intensity and
    /// font size. Both scale with the type, so the look holds together when the
    /// overlay shrinks the text to fit the monitor.
    /// </summary>
    private void ApplyCrt()
    {
        if (_crtIntensity <= 0)
        {
            Effect = null;
            _ghostLeft = null;
            _ghostRight = null;
            return;
        }

        var alpha = (byte)Math.Round(byte.MaxValue * GhostAlpha * _crtIntensity);
        var left = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 255, 48, 48));
        var right = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 48, 255, 255));
        left.Freeze();
        right.Freeze();
        _ghostLeft = left;
        _ghostRight = right;

        Effect = new DropShadowEffect
        {
            Color = _foreground is SolidColorBrush solid ? solid.Color : System.Windows.Media.Colors.White,
            ShadowDepth = 0,
            Direction = 0,
            BlurRadius = Math.Clamp(_fontSize * GlowRadiusFactor, MinimumGlowRadius, MaximumGlowRadius),
            Opacity = GlowOpacity * _crtIntensity,
            RenderingBias = RenderingBias.Performance
        };
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new JitterTextAutomationPeer(this);

    internal static bool ShouldJitterGlyph(string text) => text.Any(char.IsUpper);

    /// <summary>
    /// Measures without touching the visual tree, so the overlay can search for a
    /// font size that fits before committing to one.
    /// </summary>
    internal static System.Windows.Size MeasureText(
        string text,
        System.Windows.Media.FontFamily fontFamily,
        double fontSize,
        double lineHeight,
        double pixelsPerDip,
        double availableWidth,
        int reserve)
    {
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return Compose(text, typeface, fontSize, lineHeight, pixelsPerDip, availableWidth, reserve,
            System.Windows.Media.Brushes.White, [], null);
    }

    private void InvalidateContent()
    {
        _builtText = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Build(double availableWidth)
    {
        if (string.Equals(_builtText, _text, StringComparison.Ordinal)
            && (availableWidth.Equals(_builtWidth) || double.IsNaN(availableWidth)))
            return;

        var typeface = new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _contentSize = Compose(_text, typeface, _fontSize, _lineHeight, pixelsPerDip,
            availableWidth, _maximumJitter, _foreground, _rainbowWords, _glyphs);
        _hasRainbow = _glyphs.Any(glyph => glyph.Rainbow);
        if (_hasRainbow && _live)
            StartAnimation();
        _builtText = _text;
        _builtWidth = availableWidth;

        // Motion state is keyed by glyph index, so appending or deleting at the
        // end leaves every earlier letter mid-wobble instead of snapping it home.
        while (_amplitudes.Count < _glyphs.Count)
            _amplitudes.Add(0);
    }

    private static System.Windows.Size Compose(
        string text,
        Typeface typeface,
        double fontSize,
        double lineHeight,
        double pixelsPerDip,
        double availableWidth,
        int reserve,
        System.Windows.Media.Brush fill,
        IReadOnlyCollection<string> rainbowWords,
        List<Glyph>? sink)
    {
        sink?.Clear();

        var maximumWidth = double.IsNaN(availableWidth) || double.IsInfinity(availableWidth)
            ? double.PositiveInfinity
            : Math.Max(1, availableWidth - reserve * 2);

        // One probe fixes the vertical box for every glyph. Aligning each glyph to
        // its own measured height instead makes spaces and punctuation sit at a
        // different height than letters.
        var probe = new FormattedText("Ag", CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, typeface, fontSize, fill, pixelsPerDip);
        var glyphHeight = probe.Height;

        var values = SplitGraphemes(text);
        var elements = new Element[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var formatted = new FormattedText(value, CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, typeface, fontSize, fill, pixelsPerDip);
            elements[index] = new Element(
                value,
                formatted,
                formatted.WidthIncludingTrailingWhitespace,
                value.All(char.IsWhiteSpace));
        }

        var rainbow = MarkRainbowWords(values, rainbowWords);
        var lines = WrapIntoLines(elements, maximumWidth);
        // An empty box still shows one console-width cell, so pressing the hotkey
        // gives immediate feedback without needing a cursor.
        var contentWidth = lines.Count == 0
            ? Math.Round(fontSize * EmptyWidthFactor)
            : lines.Max(line => line.CoreWidth);
        var size = new System.Windows.Size(
            Math.Ceiling(contentWidth + reserve * 2),
            Math.Ceiling(Math.Max(1, lines.Count) * lineHeight + reserve * 2));

        var glyphTop = Math.Max(0, (lineHeight - glyphHeight) / 2);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var top = reserve + lineIndex * lineHeight + glyphTop;
            // Centred per line: a subtitle of two short lines reads better boxed in
            // the middle than ragged against the left edge.
            var pen = reserve + Math.Round((contentWidth - line.CoreWidth) / 2);
            for (var index = line.Start; index < line.End; index++)
            {
                var element = elements[index];
                // Whitespace still takes a glyph slot so motion phases stay pinned
                // to their character position across edits.
                sink?.Add(new Glyph(
                    element.Formatted, pen, top, element.Width, lineIndex,
                    !element.IsWhitespace && ShouldJitterGlyph(element.Value),
                    rainbow[index], index));
                pen += element.Width;
            }
        }

        if (sink is not null)
            AssignRainbowRuns(sink);
        return size;
    }

    /// <summary>
    /// Flags every grapheme belonging to a whole word on the rainbow list. Runs of
    /// letters and digits are matched entire, so "gaydar" does not light up.
    /// </summary>
    internal static bool[] MarkRainbowWords(string text, IReadOnlyCollection<string> words) =>
        MarkRainbowWords(SplitGraphemes(text), words);

    internal static string[] SplitGraphemes(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var values = new string[starts.Length];
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var length = (index + 1 < starts.Length ? starts[index + 1] : text.Length) - start;
            values[index] = text.Substring(start, length);
        }
        return values;
    }

    private static bool[] MarkRainbowWords(string[] values, IReadOnlyCollection<string> words)
    {
        var flags = new bool[values.Length];
        if (words.Count == 0)
            return flags;

        var index = 0;
        var builder = new StringBuilder();
        while (index < values.Length)
        {
            if (!IsWordCharacter(values[index]))
            {
                index++;
                continue;
            }
            var start = index;
            builder.Clear();
            while (index < values.Length && IsWordCharacter(values[index]))
            {
                builder.Append(values[index]);
                index++;
            }
            if (words.Contains(builder.ToString()))
            {
                for (var inner = start; inner < index; inner++)
                    flags[inner] = true;
            }
        }
        return flags;
    }

    private static bool IsWordCharacter(string value) =>
        value.Length > 0 && (char.IsLetterOrDigit(value[0]) || value[0] == '\x27');

    /// <summary>
    /// Gives every rainbow glyph the horizontal span of its own run, so the
    /// gradient sweeps once across the whole word instead of once per letter.
    /// </summary>
    private static void AssignRainbowRuns(List<Glyph> glyphs)
    {
        var index = 0;
        while (index < glyphs.Count)
        {
            if (!glyphs[index].Rainbow)
            {
                index++;
                continue;
            }
            var start = index;
            var line = glyphs[index].LineIndex;
            while (index < glyphs.Count && glyphs[index].Rainbow && glyphs[index].LineIndex == line)
                index++;
            var left = glyphs[start].X;
            var right = glyphs[index - 1].X + glyphs[index - 1].Advance;
            for (var inner = start; inner < index; inner++)
            {
                glyphs[inner].RunLeft = left;
                glyphs[inner].RunRight = right;
            }
        }
    }

    private static System.Windows.Media.Brush RainbowBrush(double left, double right, double phase)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new System.Windows.Point(left, 0),
            EndPoint = new System.Windows.Point(Math.Max(left + 1, right), 0)
        };
        const int stops = 8;
        for (var index = 0; index < stops; index++)
        {
            var offset = index / (double)(stops - 1);
            brush.GradientStops.Add(new GradientStop(
                Hue(phase + offset * RainbowSweepDegrees), offset));
        }
        brush.Freeze();
        return brush;
    }

    /// <summary>Fully saturated hue, lifted off zero so blues stay readable on black.</summary>
    private static System.Windows.Media.Color Hue(double degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        var sector = degrees / 60;
        var ramp = 1 - Math.Abs(sector % 2 - 1);
        var (red, green, blue) = (int)sector switch
        {
            0 => (1d, ramp, 0d),
            1 => (ramp, 1d, 0d),
            2 => (0d, 1d, ramp),
            3 => (0d, ramp, 1d),
            4 => (ramp, 0d, 1d),
            _ => (1d, 0d, ramp)
        };
        static byte Channel(double value) =>
            (byte)Math.Round(byte.MaxValue * (RainbowFloor + (1 - RainbowFloor) * value));
        return System.Windows.Media.Color.FromRgb(Channel(red), Channel(green), Channel(blue));
    }

    /// <summary>
    /// Breaks at word boundaries and only falls back to breaking inside a word
    /// when a single word cannot fit on a line of its own.
    /// </summary>
    private static List<Line> WrapIntoLines(Element[] elements, double maximumWidth)
    {
        var lines = new List<Line>();
        if (elements.Length == 0)
            return lines;

        var lineStart = 0;
        var lineWidth = 0d;
        var lineCore = 0d;

        void Flush(int end)
        {
            lines.Add(new Line(lineStart, end, lineCore));
            lineStart = end;
            lineWidth = 0;
            lineCore = 0;
        }

        void Append(Element element)
        {
            lineWidth += element.Width;
            if (!element.IsWhitespace)
                lineCore = lineWidth;
        }

        var index = 0;
        while (index < elements.Length)
        {
            var wordStart = index;
            var wordWidth = 0d;
            while (index < elements.Length && !elements[index].IsWhitespace)
            {
                wordWidth += elements[index].Width;
                index++;
            }
            var wordEnd = index;
            while (index < elements.Length && elements[index].IsWhitespace)
                index++;

            if (wordEnd > wordStart)
            {
                if (lineCore > 0 && lineWidth + wordWidth > maximumWidth)
                    Flush(wordStart);

                if (wordWidth > maximumWidth)
                {
                    for (var inner = wordStart; inner < wordEnd; inner++)
                    {
                        if (lineCore > 0 && lineWidth + elements[inner].Width > maximumWidth)
                            Flush(inner);
                        Append(elements[inner]);
                    }
                }
                else
                {
                    for (var inner = wordStart; inner < wordEnd; inner++)
                        Append(elements[inner]);
                }
            }

            for (var inner = wordEnd; inner < index; inner++)
                Append(elements[inner]);
        }

        lines.Add(new Line(lineStart, elements.Length, lineCore));
        return lines;
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

        var approach = 1 - Math.Exp(-elapsed / AmplitudeEaseSeconds);
        var moving = false;
        for (var index = 0; index < _amplitudes.Count; index++)
        {
            var target = index < _glyphs.Count && _glyphs[index].Jitters ? _requestedShake : 0;
            var amplitude = _amplitudes[index] + (target - _amplitudes[index]) * approach;
            if (target <= 0 && amplitude < 0.01)
                amplitude = 0;
            _amplitudes[index] = amplitude;
            if (amplitude > 0.01)
                moving = true;
        }

        if (moving || (_live && (_hasRainbow || _requestedShake > 0)))
            InvalidateVisual();
        else
            StopAnimation();
    }

    private readonly record struct Element(
        string Value, FormattedText Formatted, double Width, bool IsWhitespace);

    private readonly record struct Line(int Start, int End, double CoreWidth);

    private sealed class Glyph
    {
        public Glyph(
            FormattedText fill, double x, double y, double advance,
            int lineIndex, bool jitters, bool rainbow, int index)
        {
            Fill = fill;
            X = x;
            Y = y;
            Advance = advance;
            LineIndex = lineIndex;
            Jitters = jitters;
            Rainbow = rainbow;
            PhaseXPrimary = PhaseFor(index, 0);
            PhaseXSecondary = PhaseFor(index, 1);
            PhaseYPrimary = PhaseFor(index, 2);
            PhaseYSecondary = PhaseFor(index, 3);
        }

        public FormattedText Fill { get; }
        public double X { get; }
        public double Y { get; }
        public double Advance { get; }
        public int LineIndex { get; }
        public bool Jitters { get; }
        public bool Rainbow { get; }
        public double RunLeft { get; set; }
        public double RunRight { get; set; }
        public double PhaseXPrimary { get; }
        public double PhaseXSecondary { get; }
        public double PhaseYPrimary { get; }
        public double PhaseYSecondary { get; }

        // Deterministic per character position: the same letter keeps the same
        // motion across rebuilds, and neighbours never move in lockstep.
        private static double PhaseFor(int index, int channel)
        {
            unchecked
            {
                var hash = (uint)(index * 374761393 + channel * 668265263 + 374761397);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                hash ^= hash >> 16;
                return hash / (double)uint.MaxValue * Math.PI * 2;
            }
        }
    }

    private sealed class JitterTextAutomationPeer(JitterTextElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => ((JitterTextElement)Owner).Text;
        protected override string GetItemStatusCore()
        {
            var owner = (JitterTextElement)Owner;
            return $"glyphs={owner._glyphs.Count}; rendered={owner._lastRenderedGlyphCount}; text={owner._lastRenderedText}";
        }
        protected override string GetClassNameCore() => nameof(JitterTextElement);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
}
