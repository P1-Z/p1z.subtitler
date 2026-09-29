using P1Z.Subtitler.Models;
using P1Z.Subtitler.Native;
using P1Z.Subtitler.Overlay;
using P1Z.Subtitler.Controls;
using System.Windows.Media;

namespace P1Z.Subtitler.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            UppercaseIntensityUsesLettersOnly();
            OnlyUppercaseGlyphsAreEligibleToJitter();
            ShakeIntensityTracksTheCurrentBuffer();
            AnchorsStayInsideTheSafeCentralRegion();
            NewSessionsReceiveDifferentAnchors();
            SettingsAreClampedToSafeRanges();
            TextWrapsWithinTheAvailableWidth();
            RainbowWordsAreMatchedWholeAndCaseInsensitively();
            WordListsAreParsedAndNormalized();
            CrtIntensityIsCarriedAcrossClones();
            Console.WriteLine("PASS 10/10");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void UppercaseIntensityUsesLettersOnly()
    {
        Equal(0, PlacementEngine.UppercaseRatio("bro what are you doing 123?!"), 0.0001);
        Equal(1, PlacementEngine.UppercaseRatio("BRO WHAT 123?!"), 0.0001);
        Equal(4d / 10d, PlacementEngine.UppercaseRatio("bro WHAT now"), 0.0001);
        Equal(0, PlacementEngine.UppercaseRatio("123?!"), 0.0001);
    }

    private static void OnlyUppercaseGlyphsAreEligibleToJitter()
    {
        True(!JitterTextElement.ShouldJitterGlyph("b"), "lowercase glyph stays fixed");
        True(JitterTextElement.ShouldJitterGlyph("B"), "uppercase glyph jitters");
        True(!JitterTextElement.ShouldJitterGlyph("!"), "punctuation stays fixed");
        True(!JitterTextElement.ShouldJitterGlyph(" "), "whitespace stays fixed");
    }

    private static void ShakeIntensityTracksTheCurrentBuffer()
    {
        Equal(0, PlacementEngine.ShakePixels("bro", 9), 0.001);
        var partial = PlacementEngine.ShakePixels("bro WHAT", 9);
        var full = PlacementEngine.ShakePixels("BRO WHAT", 9);
        True(partial > 0, "partial caps should shake");
        True(full > partial, "full caps should shake more strongly");
    }

    private static void AnchorsStayInsideTheSafeCentralRegion()
    {
        var work = new PixelRect(-1920, 0, 0, 1040);
        var random = new Random(17);
        for (var index = 0; index < 500; index++)
        {
            var anchor = PlacementEngine.ChooseAnchor(work, 34, 24, 9, random);
            True(anchor.X >= work.Left + 34 && anchor.X <= work.Right - 34, "horizontal safe margin");
            True(anchor.Y >= work.Top + 34 && anchor.Y <= work.Bottom - 34, "vertical safe margin");
            True(Math.Abs(anchor.X - (work.Left + work.Width / 2)) <= work.Width * 0.17 + 1, "central horizontal range");
            True(Math.Abs(anchor.Y - (work.Top + work.Height / 2)) <= work.Height * 0.17 + 1, "central vertical range");
        }
    }

    private static void NewSessionsReceiveDifferentAnchors()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var random = new Random(3);
        var first = PlacementEngine.ChooseAnchor(work, 34, 24, 9, random);
        var second = PlacementEngine.ChooseAnchor(work, 34, 24, 9, random);
        True(first != second, "successive sessions should choose new anchors");
    }

    private static void SettingsAreClampedToSafeRanges()
    {
        var settings = new AppSettings
        {
            FontFamily = " ",
            FontSize = 999,
            BackgroundOpacity = 4,
            MaximumShakePixels = 80,
            PlacementRangePercent = 0,
            CrtIntensityPercent = 400
        };
        settings.Normalize();
        True(settings.FontFamily == AppSettings.PixelFontFamily, "fallback pixel font");
        Equal(96, settings.FontSize, 0.001);
        Equal(0.95, settings.BackgroundOpacity, 0.001);
        Equal(24, settings.MaximumShakePixels, 0.001);
        Equal(5, settings.PlacementRangePercent, 0.001);
        Equal(100, settings.CrtIntensityPercent, 0.001);

        var off = new AppSettings { CrtIntensityPercent = -5 };
        off.Normalize();
        Equal(0, off.CrtIntensityPercent, 0.001);
    }

    private static void CrtIntensityIsCarriedAcrossClones()
    {
        var settings = new AppSettings { CrtIntensityPercent = 42 };
        Equal(42, settings.Clone().CrtIntensityPercent, 0.001);
    }

    private static void TextWrapsWithinTheAvailableWidth()
    {
        var font = new FontFamily("Consolas");
        const string sentence = "alpha beta gamma delta epsilon zeta";
        var wide = JitterTextElement.MeasureText(sentence, font, 24, 28, 1, 4000, 5);
        var narrow = JitterTextElement.MeasureText(sentence, font, 24, 28, 1, 260, 5);
        True(wide.Height < narrow.Height, "a narrow overlay wraps onto more lines");
        True(narrow.Width <= 260, "wrapped text never exceeds the width it was given");

        var unbreakable = JitterTextElement.MeasureText(
            "supercalifragilisticexpialidocious", font, 24, 28, 1, 160, 5);
        True(unbreakable.Width <= 160, "a single long word is broken rather than overflowing");
    }

    private static void RainbowWordsAreMatchedWholeAndCaseInsensitively()
    {
        var words = AppSettings.ParseWordList(AppSettings.DefaultRainbowWords);

        True(JitterTextElement.MarkRainbowWords("gay", words).All(flag => flag),
            "an exact match lights every letter");
        True(JitterTextElement.MarkRainbowWords("GAY", words).All(flag => flag),
            "matching ignores case");
        True(JitterTextElement.MarkRainbowWords("gaydar", words).All(flag => !flag),
            "a longer word containing a trigger stays plain");
        True(JitterTextElement.MarkRainbowWords("ga", words).All(flag => !flag),
            "a prefix of a trigger stays plain");

        var mixed = JitterTextElement.MarkRainbowWords("so gay!", words);
        True(mixed.Take(3).All(flag => !flag), "text before the trigger stays plain");
        True(mixed.Skip(3).Take(3).All(flag => flag), "the trigger itself lights up");
        True(!mixed[6], "trailing punctuation stays plain");

        True(JitterTextElement.MarkRainbowWords("gay", AppSettings.ParseWordList("")).All(flag => !flag),
            "an empty word list disables the effect");
    }

    private static void WordListsAreParsedAndNormalized()
    {
        var words = AppSettings.ParseWordList(" gay ,, wow;yay  GAY ");
        True(words.Count == 3, "separators and duplicates collapse");
        True(words.Contains("YAY"), "lookups ignore case");

        var settings = new AppSettings { RainbowWords = "  alpha ,, beta " };
        settings.Normalize();
        True(settings.RainbowWords == "alpha, beta", "the stored list is tidied on save");

        var empty = new AppSettings { RainbowWords = "   " };
        empty.Normalize();
        True(empty.RainbowWords.Length == 0, "a blank list is allowed and means off");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
