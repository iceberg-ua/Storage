using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Storage.Core.Models;

namespace Storage.Tests;

// docs/design is the visual reference, and docs/design/tokens.json lists its colours.
// These tests pin that list to the app (Colors.xaml, TagPalette) and to the mockups, which
// may only draw with a listed colour. A colour change therefore has to update tokens.json
// and whichever side it starts on. The files are copied beside the test assembly by the
// test project, so no repository checkout is needed around bin/.
public partial class DesignTokenTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    private static readonly string DesignDir = Path.Combine(AppContext.BaseDirectory, "design");

    private static readonly Lazy<Tokens> Design = new(LoadTokens);

    private static readonly Lazy<Dictionary<string, string>> AppColors = new(LoadAppColors);

    public static TheoryData<string> TokenNames() => new(Design.Value.Colors.Keys);

    public static TheoryData<string> Screens() =>
        new(Directory.GetFiles(Path.Combine(DesignDir, "screens"), "*.html").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(TokenNames))]
    public void AppColourMatchesTheDesign(string token)
    {
        Assert.True(AppColors.Value.TryGetValue(token, out var actual), $"Colors.xaml has no '{token}'");
        Assert.Equal(Rgb(Design.Value.Colors[token]), Rgb(actual!));
    }

    // The design is drawn in the light theme only; the app must still say what each
    // colour becomes in the dark one.
    [Theory]
    [MemberData(nameof(TokenNames))]
    public void EveryDesignColourHasADarkVariant(string token) =>
        Assert.True(AppColors.Value.ContainsKey(token + "Dark"), $"Colors.xaml has no '{token}Dark'");

    [Fact]
    public void TagPaletteMatchesTheDesign() =>
        Assert.Equal(Design.Value.TagPalette.Select(Rgb), TagPalette.Colors.Select(Rgb));

    // A mockup that starts using a new colour has to say what it is: a token, a tag
    // colour, or a mockup-only shade with what the app uses instead.
    [Theory]
    [MemberData(nameof(Screens))]
    public void MockupUsesOnlyListedColours(string screen)
    {
        var html = File.ReadAllText(Path.Combine(DesignDir, "screens", screen));
        var listed = Design.Value.Colors.Values
            .Concat(Design.Value.TagPalette)
            .Concat(Design.Value.MockupOnly)
            .Select(Rgb)
            .ToHashSet();

        var unlisted = HexColour().Matches(html)
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .Where(hex => !listed.Contains(Rgb(hex)))
            .ToList();

        Assert.True(unlisted.Count == 0, $"{screen} uses colours not in tokens.json: {string.Join(", ", unlisted)}");
    }

    // Compared as numbers, so #0f6e6e and #0F6E6E are the same colour. Only #RRGGBB is
    // accepted: a named colour or a short form in Colors.xaml fails with a clear message.
    private static uint Rgb(string value)
    {
        var hex = value.Trim();
        Assert.True(
            hex.Length == 7 && hex[0] == '#' && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, null, out _),
            $"'{value}' is not a #RRGGBB colour; write design colours that way in Colors.xaml and tokens.json");
        return uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber);
    }

    private static Tokens LoadTokens()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(DesignDir, "tokens.json")));
        var root = json.RootElement;

        return new Tokens(
            root.GetProperty("colors").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!),
            root.GetProperty("tagPalette").EnumerateArray().Select(c => c.GetString()!).ToList(),
            root.GetProperty("mockupOnly").EnumerateObject().Select(p => p.Name).ToList());
    }

    private static Dictionary<string, string> LoadAppColors()
    {
        var colors = XDocument.Load(Path.Combine(DesignDir, "Colors.xaml"))
            .Descendants()
            .Where(e => e.Name.LocalName == "Color" && e.Attribute(Xaml + "Key") is not null)
            .Select(e => (Key: e.Attribute(Xaml + "Key")!.Value, Value: e.Value.Trim()))
            .ToList();

        var duplicates = colors.GroupBy(c => c.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, $"Colors.xaml defines these keys more than once: {string.Join(", ", duplicates)}");

        return colors.ToDictionary(c => c.Key, c => c.Value);
    }

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}\b")]
    private static partial Regex HexColour();

    private sealed record Tokens(Dictionary<string, string> Colors, List<string> TagPalette, List<string> MockupOnly);
}
