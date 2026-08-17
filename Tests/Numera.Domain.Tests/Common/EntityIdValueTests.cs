using Numera.Domain.Common;

namespace Numera.Domain.Tests.Common;

[TestClass]
public sealed class EntityIdValueTests
{
    [TestMethod]
    public void EmptyValueIsDetected()
    {
        Assert.IsTrue(EntityIdValue.Empty.IsEmpty);
        Assert.IsFalse(EntityIdValue.FromBits(UInt128.One).IsEmpty);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(15)]
    [DataRow(17)]
    [DataRow(32)]
    public void FromBytesRejectsWrongLength(int length)
    {
        byte[] source = new byte[length];

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => EntityIdValue.FromBytes(source));

        Assert.AreEqual(InvariantViolationCode.EntityIdLengthInvalid, exception.Code);
    }

    [TestMethod]
    public void BytesAreInterpretedBigEndian()
    {
        byte[] source = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];

        EntityIdValue value = EntityIdValue.FromBytes(source);

        Assert.AreEqual("000102030405060708090a0b0c0d0e0f", value.ToString());
        CollectionAssert.AreEqual(source, value.ToByteArray());
    }

    [TestMethod]
    public void WriteBytesRejectsWrongDestinationLength()
    {
        EntityIdValue value = EntityIdValue.FromBits(UInt128.One);
        byte[] destination = new byte[15];

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => value.WriteBytes(destination));

        Assert.AreEqual(InvariantViolationCode.EntityIdLengthInvalid, exception.Code);
    }

    [TestMethod]
    public void TextRoundTripsThroughParse()
    {
        EntityIdValue original = EntityIdValue.FromBits(
            (UInt128.MaxValue >> 3) ^ (UInt128)0x0123_4567_89AB_CDEFUL);

        Assert.AreEqual(original, EntityIdValue.Parse(original.ToString()));
    }

    [TestMethod]
    public void ParseAcceptsUpperCaseHex()
    {
        Assert.AreEqual(
            EntityIdValue.Parse("000102030405060708090a0b0c0d0e0f"),
            EntityIdValue.Parse("000102030405060708090A0B0C0D0E0F"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("000102030405060708090a0b0c0d0e0")]
    [DataRow("000102030405060708090a0b0c0d0e0f0")]
    [DataRow("000102030405060708090a0b0c0d0e0g")]
    [DataRow("000102030405060708090a0b0c0d0e0 ")]
    [DataRow("-00102030405060708090a0b0c0d0e0f")]
    public void TryParseRejectsMalformedText(string source) =>
        Assert.IsFalse(EntityIdValue.TryParse(source, out _));

    [TestMethod]
    public void ParseThrowsOnMalformedText()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => EntityIdValue.Parse("zz"));

        Assert.AreEqual(InvariantViolationCode.EntityIdTextInvalid, exception.Code);
    }

    [TestMethod]
    public void OrderingMatchesBigEndianByteOrder()
    {
        EntityIdValue low = EntityIdValue.FromBytes([0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01]);
        EntityIdValue high = EntityIdValue.FromBytes([0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00]);

        Assert.IsTrue(low < high);
        Assert.IsTrue(high > low);
        Assert.IsTrue(low <= EntityIdValue.FromBytes([0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01]));
        Assert.IsTrue(high >= EntityIdValue.FromBytes([0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00]));
    }

    [TestMethod]
    public void TextLengthIsAlwaysThirtyTwo()
    {
        Assert.AreEqual(EntityIdValue.TextLength, EntityIdValue.Empty.ToString().Length);
        Assert.AreEqual(EntityIdValue.TextLength, EntityIdValue.FromBits(UInt128.MaxValue).ToString().Length);
    }

    [TestMethod]
    public void TypedIdentifiersDoNotShareIdentity()
    {
        EntityIdValue value = EntityIdValue.FromBits(UInt128.One);

        PartyId party = PartyId.FromValue(value);
        BankId bank = BankId.FromValue(value);

        Assert.AreEqual(value, party.Value);
        Assert.AreEqual(value, bank.Value);
        Assert.AreEqual("party", PartyId.EntityName);
        Assert.AreEqual("bank", BankId.EntityName);
    }
}
