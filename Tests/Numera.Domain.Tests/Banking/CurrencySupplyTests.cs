using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class CurrencySupplyTests
{
    private static readonly CurrencySupplyOperationId Identifier =
        CurrencySupplyOperationId.FromValue(EntityIdValue.FromBits(1));

    private static readonly CurrencyId Currency = CurrencyId.FromValue(EntityIdValue.FromBits(2));

    private static readonly BusinessOperationId Operation =
        BusinessOperationId.FromValue(EntityIdValue.FromBits(3));

    private static readonly LedgerAccountId Treasury = LedgerAccountId.FromValue(EntityIdValue.FromBits(4));

    private static readonly CurrencyMetadataVersionId MetadataIdentifier =
        CurrencyMetadataVersionId.FromValue(EntityIdValue.FromBits(5));

    private static readonly UtcTimestamp OccurredAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static CurrencySupplyOperation Mint(CurrencySupplyOperationKind kind) =>
        CurrencySupplyOperation.Create(
            Identifier,
            Currency,
            Operation,
            kind,
            MoneyMinor.FromMinor(100),
            kind == CurrencySupplyOperationKind.Burn ? Treasury : null,
            kind == CurrencySupplyOperationKind.Burn ? null : Treasury,
            "GENESIS_MINT",
            OccurredAt);

    [TestMethod]
    public void MintingKindsCarryOnlyADestination()
    {
        foreach (CurrencySupplyOperationKind kind in
            new[] { CurrencySupplyOperationKind.Genesis, CurrencySupplyOperationKind.Issue })
        {
            CurrencySupplyOperation operation = Mint(kind);

            Assert.IsNull(operation.SourceLedgerAccountId);
            Assert.AreEqual(Treasury, operation.DestinationLedgerAccountId);
        }
    }

    [TestMethod]
    public void BurnCarriesOnlyASource()
    {
        CurrencySupplyOperation operation = Mint(CurrencySupplyOperationKind.Burn);

        Assert.AreEqual(Treasury, operation.SourceLedgerAccountId);
        Assert.IsNull(operation.DestinationLedgerAccountId);
    }

    [TestMethod]
    public void MintingWithASourceAccountIsRejected()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => CurrencySupplyOperation.Create(
                Identifier,
                Currency,
                Operation,
                CurrencySupplyOperationKind.Issue,
                MoneyMinor.FromMinor(100),
                Treasury,
                Treasury,
                "ISSUE",
                OccurredAt));

        Assert.AreEqual(InvariantViolationCode.CurrencySupplyOperationEndpointsInvalid, violation.Code);
    }

    [TestMethod]
    public void NonPositiveAmountIsRejected()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => CurrencySupplyOperation.Create(
                Identifier,
                Currency,
                Operation,
                CurrencySupplyOperationKind.Issue,
                MoneyMinor.Zero,
                null,
                Treasury,
                "ISSUE",
                OccurredAt));

        Assert.AreEqual(InvariantViolationCode.CurrencySupplyOperationAmountInvalid, violation.Code);
    }

    [TestMethod]
    public void ReasonCodeIsRestrictedToUppercaseTokens()
    {
        Assert.IsTrue(CurrencySupplyOperation.IsReasonCodeValid("MONETARY_POLICY_1"));
        Assert.IsFalse(CurrencySupplyOperation.IsReasonCodeValid("monetary"));
        Assert.IsFalse(CurrencySupplyOperation.IsReasonCodeValid(""));
        Assert.IsFalse(CurrencySupplyOperation.IsReasonCodeValid(new string('A', 33)));
    }

    [TestMethod]
    public void BaseMoneySupplyIsGenesisPlusIssueMinusBurn()
    {
        CurrencySupplyTotals totals = CurrencySupplyTotals.Create(
            MoneyMinor.FromMinor(1_000), MoneyMinor.FromMinor(500), MoneyMinor.FromMinor(200));

        Assert.AreEqual(1_300L, totals.BaseMoneySupply.Value);
    }

    [TestMethod]
    public void EmptyTotalsHaveZeroSupply() =>
        Assert.IsTrue(CurrencySupplyTotals.Empty.BaseMoneySupply.IsZero);

    [TestMethod]
    public void BurnExceedingMintedTotalsIsRejected()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => CurrencySupplyTotals.Create(
                MoneyMinor.FromMinor(100), MoneyMinor.Zero, MoneyMinor.FromMinor(101)));

        Assert.AreEqual(InvariantViolationCode.CurrencySupplyNegative, violation.Code);
    }

    [TestMethod]
    public void MetadataKeepsTheCurrencyIdentityWhenTheNameChanges()
    {
        CurrencyMetadataVersion first = CurrencyMetadataVersion.Create(
            MetadataIdentifier, Currency, "ヌメラ", "NUM", "N", "{symbol}{amount}", OccurredAt, null, 1);

        CurrencyMetadataVersion renamed = CurrencyMetadataVersion.Create(
            CurrencyMetadataVersionId.FromValue(EntityIdValue.FromBits(6)),
            Currency,
            "ヌメラ改",
            "NUM",
            "N",
            "{symbol}{amount}",
            UtcTimestamp.FromUnixMilliseconds(OccurredAt.UnixMilliseconds + 1_000),
            null,
            2);

        Assert.AreEqual(first.CurrencyId, renamed.CurrencyId);
        Assert.AreNotEqual(first.Name, renamed.Name);
    }

    [TestMethod]
    public void MetadataRejectsOversizedText()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => CurrencyMetadataVersion.Create(
                MetadataIdentifier,
                Currency,
                new string('あ', 65),
                "NUM",
                "N",
                "{amount}",
                OccurredAt,
                null,
                1));

        Assert.AreEqual(InvariantViolationCode.CurrencyMetadataInvalid, violation.Code);
    }

    [TestMethod]
    public void MetadataRejectsAClosingTimestampAtOrBeforeTheStart()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => CurrencyMetadataVersion.Create(
                MetadataIdentifier, Currency, "ヌメラ", "NUM", "N", "{amount}", OccurredAt, OccurredAt, 1));

        Assert.AreEqual(InvariantViolationCode.CurrencyMetadataInvalid, violation.Code);
    }

    [TestMethod]
    public void EveryKindRoundTripsThroughItsToken()
    {
        foreach (CurrencySupplyOperationKind kind in Enum.GetValues<CurrencySupplyOperationKind>())
        {
            Assert.AreEqual(kind, CurrencySupplyOperationCatalog.ParseToken(kind.ToToken()));
        }

        Assert.IsFalse(CurrencySupplyOperationCatalog.TryParseToken("MINT", out _));
    }
}
