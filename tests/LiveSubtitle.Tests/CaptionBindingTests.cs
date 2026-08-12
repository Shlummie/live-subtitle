using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace LiveSubtitle.Tests;

public sealed class CaptionBindingTests
{
    [Theory]
    [InlineData("src/LiveSubtitle.App/Views/MainWindow.xaml")]
    [InlineData("src/LiveSubtitle.App/Views/OverlayWindow.xaml")]
    public void ReadOnlyCaptionRunBindingsAreExplicitlyOneWay(string relativePath)
    {
        var path = Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var captionBindings = document
            .Descendants(presentation + "Run")
            .Select(run => run.Attribute("Text")?.Value)
            .Where(value => value is not null
                            && (value.Contains("StableText", StringComparison.Ordinal)
                                || value.Contains("ProvisionalText", StringComparison.Ordinal)))
            .Cast<string>()
            .ToArray();

        Assert.Equal(2, captionBindings.Length);
        Assert.All(captionBindings, value =>
            Assert.Contains("Mode=OneWay", value, StringComparison.Ordinal));
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}
