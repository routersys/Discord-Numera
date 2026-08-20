using Numera.Application.Banking;

namespace Numera.Application.Tests;

[TestClass]
public sealed class PrudentialRatioTests
{
    private const int LendingCet1Floor = 700;
    private const int LeverageFloor = 300;
    private const int LiquidityFloor = 10_000;

    [TestMethod]
    public void TheLendingCet1FloorRejectsSixNineNineAndAcceptsSevenHundred()
    {
        Assert.IsFalse(Cet1(699).SatisfiesCet1(LendingCet1Floor));
        Assert.IsTrue(Cet1(700).SatisfiesCet1(LendingCet1Floor));
    }

    [TestMethod]
    public void TheLeverageFloorRejectsTwoNineNineAndAcceptsThreeHundred()
    {
        Assert.IsFalse(Leverage(299).SatisfiesLeverage(LeverageFloor));
        Assert.IsTrue(Leverage(300).SatisfiesLeverage(LeverageFloor));
    }

    [TestMethod]
    public void TheLiquidityFloorRejectsNineNineNineNineAndAcceptsTenThousand()
    {
        Assert.IsFalse(Liquidity(9_999).SatisfiesLiquidity(LiquidityFloor));
        Assert.IsTrue(Liquidity(10_000).SatisfiesLiquidity(LiquidityFloor));
    }

    [TestMethod]
    public void AZeroDenominatorPassesOnlyWhileCapitalIsNotNegative()
    {
        PrudentialRatios solvent = new(1, 0, 0, 0, 0);
        PrudentialRatios insolvent = new(-1, 0, 0, 0, 0);

        Assert.IsTrue(solvent.SatisfiesCet1(LendingCet1Floor));
        Assert.IsTrue(solvent.SatisfiesLeverage(LeverageFloor));
        Assert.IsTrue(solvent.SatisfiesLiquidity(LiquidityFloor));

        Assert.IsFalse(insolvent.SatisfiesCet1(LendingCet1Floor));
        Assert.IsFalse(insolvent.SatisfiesLeverage(LeverageFloor));
    }

    [TestMethod]
    public void TheRatiosFloorTheirDivision()
    {
        PrudentialRatios ratios = new(699, 10_000, 10_000, 999, 10_000);

        Assert.IsFalse(ratios.SatisfiesCet1(LendingCet1Floor));
        Assert.IsTrue(ratios.SatisfiesLeverage(LeverageFloor));
        Assert.IsFalse(ratios.SatisfiesLiquidity(LiquidityFloor));
    }

    private static PrudentialRatios Cet1(int basisPoints) =>
        new(basisPoints, 10_000, 0, 0, 0);

    private static PrudentialRatios Leverage(int basisPoints) =>
        new(basisPoints, 0, 10_000, 0, 0);

    private static PrudentialRatios Liquidity(int basisPoints) =>
        new(0, 0, 0, basisPoints, 10_000);
}
