using Numera.Domain.Banking;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class CardContrastTests
{
    [TestMethod]
    public void BlackAndWhiteAreTheMaximumRatio()
    {
        double ratio = CardContrast.Ratio(0x000000, 0xFFFFFF);

        Assert.AreEqual(21.0, ratio, 0.001);
    }

    [TestMethod]
    public void AColourAgainstItselfIsUnity()
    {
        Assert.AreEqual(1.0, CardContrast.Ratio(0x336699, 0x336699), 0.000001);
    }

    [TestMethod]
    public void RelativeLuminanceMatchesTheCanonicalEndpoints()
    {
        Assert.AreEqual(0.0, CardContrast.RelativeLuminance(0x000000), 0.000001);
        Assert.AreEqual(1.0, CardContrast.RelativeLuminance(0xFFFFFF), 0.000001);
    }

    [TestMethod]
    public void RelativeLuminanceUsesTheSrgbChannelWeights()
    {
        Assert.AreEqual(0.2126, CardContrast.RelativeLuminance(0xFF0000), 0.000001);
        Assert.AreEqual(0.7152, CardContrast.RelativeLuminance(0x00FF00), 0.000001);
        Assert.AreEqual(0.0722, CardContrast.RelativeLuminance(0x0000FF), 0.000001);
    }

    [TestMethod]
    public void TheLinearSegmentAppliesBelowTheThreshold()
    {
        double expected = (10 / 255.0 / 12.92) * (0.2126 + 0.7152 + 0.0722);

        Assert.AreEqual(expected, CardContrast.RelativeLuminance(0x0A0A0A), 0.000001);
    }

    [TestMethod]
    public void ADarkBackgroundSelectsWhiteText()
    {
        Assert.AreEqual(CardContrast.White, CardContrast.ChooseTextColor([0x000000]));
        Assert.AreEqual(CardContrast.White, CardContrast.ChooseTextColor([0x102A54]));
    }

    [TestMethod]
    public void ALightBackgroundSelectsNearBlackText()
    {
        Assert.AreEqual(CardContrast.NearBlack, CardContrast.ChooseTextColor([0xFFFFFF]));
        Assert.AreEqual(CardContrast.NearBlack, CardContrast.ChooseTextColor([0xF0E8D8]));
    }

    [TestMethod]
    public void ATieSelectsNearBlack()
    {
        int[] tie = [CardContrast.White, CardContrast.NearBlack];

        Assert.AreEqual(CardContrast.NearBlack, CardContrast.ChooseTextColor(tie));
    }

    [TestMethod]
    public void TheWorstPixelDecidesTheSelection()
    {
        int[] pixels = [0x000000, 0xFFFFFF];

        Assert.IsFalse(CardContrast.Satisfies(pixels, CardContrast.White, largeText: false));
        Assert.IsFalse(CardContrast.Satisfies(pixels, CardContrast.NearBlack, largeText: false));
    }

    [TestMethod]
    public void NormalTextRequiresAHigherRatioThanLargeText()
    {
        Assert.AreEqual(4.5, CardContrast.Minimum(largeText: false), 0.0);
        Assert.AreEqual(3.0, CardContrast.Minimum(largeText: true), 0.0);
    }

    [TestMethod]
    public void AMidToneBackgroundNeedsAScrim()
    {
        int[] pixels = [0x808080];

        Assert.IsFalse(CardContrast.Satisfies(pixels, CardContrast.White, largeText: false));

        Assert.IsTrue(CardContrast.TryResolveScrimOpacity(
            pixels, 0x000000, CardContrast.White, largeText: false, out double opacity));

        Assert.IsTrue(opacity > 0.0);
        Assert.IsTrue(opacity <= CardContrast.ScrimOpacityMaximum);
    }

    [TestMethod]
    public void TheScrimOpacityLandsOnATwentiethStep()
    {
        Assert.IsTrue(CardContrast.TryResolveScrimOpacity(
            [0x808080], 0x000000, CardContrast.White, largeText: false, out double opacity));

        Assert.AreEqual(0.0, Math.Round(opacity * 20.0) - (opacity * 20.0), 0.000001);
    }

    [TestMethod]
    public void AnAlreadyCompliantZoneNeedsNoScrim()
    {
        Assert.IsTrue(CardContrast.TryResolveScrimOpacity(
            [0x000000], 0x000000, CardContrast.White, largeText: false, out double opacity));

        Assert.AreEqual(0.0, opacity, 0.000001);
    }

    [TestMethod]
    public void AnImpossibleCombinationIsRejected()
    {
        Assert.IsFalse(CardContrast.TryResolveScrimOpacity(
            [0xFFFFFF], 0xFFFFFF, CardContrast.White, largeText: false, out _));
    }

    [TestMethod]
    public void BlendingIsLinearPerChannel()
    {
        Assert.AreEqual(0xFFFFFF, CardContrast.Blend(0xFFFFFF, 0x000000, 0.0));
        Assert.AreEqual(0x000000, CardContrast.Blend(0xFFFFFF, 0x000000, 1.0));
        Assert.AreEqual(0x808080, CardContrast.Blend(0xFFFFFF, 0x010101, 0.5));
    }
}
