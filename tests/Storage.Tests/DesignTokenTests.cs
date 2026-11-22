using System.Text.Json;
using System.Xml.Linq;
using Storage.Core.Models;

namespace Storage.Tests;

// The design in docs/design is the visual reference. Its colours are listed in
// docs/design/tokens.json; these tests fail when the app's colours drift from them, so a
// colour change has to touch the design and the code together.
public class DesignTokenTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    private static readonly string RepoRoot = FindRepoRoot();

    public static TheoryData<string> TokenNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Tokens().RootElement.GetProperty("colors").EnumerateObject())
            data.Add(name.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(TokenNames))]
    public void AppColourMatchesTheDesign(string token)
    {
        var expected = Tokens().RootElement.GetProperty("colors").GetProperty(token).GetString();
        var colors = AppColors();

        Assert.True(colors.ContainsKey(token), $"Colors.xaml has no '{token}'");
        Assert.Equal(expected, colors[token], StringComparer.OrdinalIgnoreCase);
    }

    // The design is drawn in the light theme only; the app must still say what each
    // colour becomes in the dark one.
    [Theory]
    [MemberData(nameof(TokenNames))]
    public void EveryDesignColourHasADarkVariant(string token) =>
        Assert.True(AppColors().ContainsKey(token + "Dark"), $"Colors.xaml has no '{token}Dark'");

    [Fact]
    public void TagPaletteMatchesTheDesign()
    {
        var expected = Tokens().RootElement.GetProperty("tagPalette")
            .EnumerateArray()
            .Select(c => c.GetString()!.ToUpperInvariant());

        Assert.Equal(expected, TagPalette.Colors.Select(c => c.ToUpperInvariant()));
    }

    private static JsonDocument Tokens() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "design", "tokens.json")));

    private static Dictionary<string, string> AppColors()
    {
        var path = Path.Combine(RepoRoot, "src", "Storage.App", "Resources", "Styles", "Colors.xaml");
        return XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "Color" && e.Attribute(Xaml + "Key") is not null)
            .ToDictionary(e => e.Attribute(Xaml + "Key")!.Value, e => e.Value.Trim());
    }

    // The tests run from bin/…; the files they check live in the source tree.
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Storage.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Storage.sln not found above " + AppContext.BaseDirectory);
    }
}
