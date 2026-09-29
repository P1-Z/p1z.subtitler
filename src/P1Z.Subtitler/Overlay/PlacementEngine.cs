using P1Z.Subtitler.Native;

namespace P1Z.Subtitler.Overlay;

internal static class PlacementEngine
{
    public static PixelPoint ChooseAnchor(
        PixelRect workArea,
        double placementRangePercent,
        int safeMargin,
        int shakeMargin,
        Random random)
    {
        var centerX = workArea.Left + workArea.Width / 2;
        var centerY = workArea.Top + workArea.Height / 2;
        var horizontalRange = workArea.Width * Math.Clamp(placementRangePercent, 5, 80) / 200d;
        var verticalRange = workArea.Height * Math.Clamp(placementRangePercent, 5, 80) / 200d;
        var inset = Math.Max(0, safeMargin + shakeMargin + 1);
        var x = (int)Math.Round(centerX + (random.NextDouble() * 2 - 1) * horizontalRange);
        var y = (int)Math.Round(centerY + (random.NextDouble() * 2 - 1) * verticalRange);
        x = Math.Clamp(x, workArea.Left + inset, workArea.Right - inset);
        y = Math.Clamp(y, workArea.Top + inset, workArea.Bottom - inset);
        return new PixelPoint(x, y);
    }

    public static double UppercaseRatio(string text)
    {
        var letters = 0;
        var uppercase = 0;
        foreach (var character in text)
        {
            if (!char.IsLetter(character))
                continue;
            letters++;
            if (char.IsUpper(character))
                uppercase++;
        }
        return letters == 0 ? 0 : uppercase / (double)letters;
    }

    public static int ShakePixels(string text, double maximumPixels) =>
        (int)Math.Round(Math.Clamp(maximumPixels, 0, 24) * Math.Pow(UppercaseRatio(text), 1.45));
}
