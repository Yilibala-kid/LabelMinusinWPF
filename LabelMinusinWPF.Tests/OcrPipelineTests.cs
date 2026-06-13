using System.Windows;
using LabelMinusinWPF.OCRService;
using Xunit;

namespace LabelMinusinWPF.Tests;

public class OcrPipelineTests
{
    [Fact]
    public void SortRegionsOrdersHorizontalTextLeftToRightByRows()
    {
        var regions = new[]
        {
            Region("D", 70, 50, 40, 16),
            Region("B", 70, 10, 40, 16),
            Region("C", 10, 50, 40, 16),
            Region("A", 10, 10, 40, 16)
        };

        var sorted = OcrPipeline.SortRegions(regions, rightToLeft: false, vertical: false);

        Assert.Equal("ABCD", Join(sorted));
    }

    [Fact]
    public void SortRegionsKeepsHorizontalRightToLeftMode()
    {
        var regions = new[]
        {
            Region("A", 10, 10, 40, 16),
            Region("B", 70, 10, 40, 16),
            Region("C", 10, 50, 40, 16),
            Region("D", 70, 50, 40, 16)
        };

        var sorted = OcrPipeline.SortRegions(regions, rightToLeft: true, vertical: false);

        Assert.Equal("BADC", Join(sorted));
    }

    [Fact]
    public void SortRegionsOrdersVerticalTextRightToLeftByColumns()
    {
        var regions = new[]
        {
            Region("L2", 20, 70, 18, 44),
            Region("R2", 80, 70, 18, 44),
            Region("L1", 20, 10, 18, 44),
            Region("R1", 80, 10, 18, 44)
        };

        var sorted = OcrPipeline.SortRegions(regions, rightToLeft: false, vertical: true);

        Assert.Equal("R1R2L1L2", Join(sorted));
    }

    [Fact]
    public void IsVerticalLayoutRequiresConsistentVerticalEvidence()
    {
        var verticalRegions = new[]
        {
            Region("R1", 80, 10, 18, 44),
            Region("R2", 80, 70, 18, 44),
            Region("L1", 20, 10, 18, 44),
            Region("L2", 20, 70, 18, 44)
        };
        var mixedRegions = new[]
        {
            Region("A", 10, 10, 60, 16),
            Region("B", 10, 40, 60, 16),
            Region("Tall", 90, 10, 18, 44)
        };

        Assert.True(OcrPipeline.IsVerticalLayout(verticalRegions));
        Assert.False(OcrPipeline.IsVerticalLayout(mixedRegions));
    }

    [Fact]
    public void BuildTextBlocksMergesVerticalTextTopToBottom()
    {
        var regions = new[]
        {
            Region("B", 80, 48, 18, 42),
            Region("A", 80, 10, 18, 42)
        };

        var blocks = OcrPipeline.BuildTextBlocks(
            regions,
            new Size(120, 120),
            MergeOptions(rightToLeft: false),
            vertical: true);

        Assert.Single(blocks);
        Assert.Equal("AB", blocks[0].Text);
    }

    [Theory]
    [InlineData(false, "AB")]
    [InlineData(true, "BA")]
    public void BuildTextBlocksMergesHorizontalTextByReadingDirection(
        bool rightToLeft,
        string expected)
    {
        var regions = new[]
        {
            Region("B", 48, 10, 42, 18),
            Region("A", 10, 10, 42, 18)
        };

        var blocks = OcrPipeline.BuildTextBlocks(
            regions,
            new Size(120, 80),
            MergeOptions(rightToLeft),
            vertical: false);

        Assert.Single(blocks);
        Assert.Equal(expected, blocks[0].Text);
    }

    private static AutoOcrOptions MergeOptions(bool rightToLeft) =>
        AutoOcrOptions.Default with
        {
            RightToLeft = rightToLeft,
            MergeTextLines = true,
            MergePaddingRatio = 0.02,
            MergeMaxDistance = 16,
            MergeDistanceScale = 0.6
        };

    private static OcrTextRegion Region(
        string text,
        double left,
        double top,
        double width,
        double height) =>
        new(text, new Rect(left, top, width, height), 0.95);

    private static string Join(IEnumerable<OcrTextRegion> regions) =>
        string.Concat(regions.Select(region => region.Text));
}
