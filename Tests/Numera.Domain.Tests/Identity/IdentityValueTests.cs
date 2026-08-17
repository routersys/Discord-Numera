using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Tests.Identity;

[TestClass]
public sealed class PublicHandleTests
{
    [TestMethod]
    [DataRow("abc")]
    [DataRow("a_b")]
    [DataRow("user1")]
    [DataRow("a00000000000000000000")]
    [DataRow("abcdefghijklmnopqrstuvwxyz012345")]
    public void CanonicalHandlesAreAccepted(string candidate) =>
        Assert.AreEqual(candidate, PublicHandle.Parse(candidate).Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("ab")]
    [DataRow("abcdefghijklmnopqrstuvwxyz0123456")]
    [DataRow("Abc")]
    [DataRow("aBc")]
    [DataRow("1abc")]
    [DataRow("_abc")]
    [DataRow("abc_")]
    [DataRow("a__b")]
    [DataRow("a-b")]
    [DataRow("a.b")]
    [DataRow("a b")]
    [DataRow("a@b")]
    [DataRow("あいう")]
    public void NonCanonicalHandlesAreRejected(string candidate)
    {
        Assert.IsFalse(PublicHandle.IsValid(candidate));
        Assert.IsFalse(PublicHandle.TryParse(candidate, out _));
    }

    [TestMethod]
    public void RejectionRaisesCanonicalCode()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => PublicHandle.Parse("Abc"));

        Assert.AreEqual(InvariantViolationCode.PublicHandleInvalid, exception.Code);
    }

    [TestMethod]
    public void HandleCanNeverEqualSnowflakeText() =>
        Assert.IsFalse(PublicHandle.IsValid("123456789012345678"));

    [TestMethod]
    public void EqualityIsOrdinal()
    {
        Assert.AreEqual(PublicHandle.Parse("abc"), PublicHandle.Parse("abc"));
        Assert.AreNotEqual(PublicHandle.Parse("abc"), PublicHandle.Parse("abd"));
        Assert.IsTrue(PublicHandle.Parse("abc") == PublicHandle.Parse("abc"));
        Assert.IsTrue(PublicHandle.Parse("abc") != PublicHandle.Parse("abd"));
    }
}

[TestClass]
public sealed class DisplayNameTests
{
    [TestMethod]
    public void SurroundingWhitespaceIsTrimmed() =>
        Assert.AreEqual("山田太郎", DisplayName.Parse("  山田太郎 ").Value);

    [TestMethod]
    public void InnerSpaceIsPreserved() =>
        Assert.AreEqual("山田 太郎", DisplayName.Parse("山田 太郎").Value);

    [TestMethod]
    public void SixtyFourCodePointsAreAccepted() =>
        Assert.AreEqual(64, DisplayName.Parse(new string('a', 64)).Value.Length);

    [TestMethod]
    public void SixtyFiveCodePointsAreRejected() =>
        Assert.IsFalse(DisplayName.TryParse(new string('a', 65), out _));

    [TestMethod]
    public void LengthIsCountedInCodePointsNotUtf16Units()
    {
        string sixtyFourAstralCharacters = string.Concat(Enumerable.Repeat("\U0001F600", 64));
        string sixtyFiveAstralCharacters = string.Concat(Enumerable.Repeat("\U0001F600", 65));

        Assert.AreEqual(128, sixtyFourAstralCharacters.Length);
        Assert.IsTrue(DisplayName.TryParse(sixtyFourAstralCharacters, out _));
        Assert.IsFalse(DisplayName.TryParse(sixtyFiveAstralCharacters, out _));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void BlankNamesAreRejected(string candidate) =>
        Assert.IsFalse(DisplayName.TryParse(candidate, out _));

    [TestMethod]
    public void ControlCharactersAreRejected()
    {
        int[] controlCodePoints = [0x00, 0x09, 0x0A, 0x0D, 0x1F, 0x7F, 0x9F];

        foreach (int codePoint in controlCodePoints)
        {
            string candidate = string.Create(3, codePoint, static (span, value) =>
            {
                span[0] = 'a';
                span[1] = (char)value;
                span[2] = 'b';
            });

            Assert.IsFalse(
                DisplayName.TryParse(candidate, out _),
                $"U+{codePoint:X4} が拒否されていません。");
        }
    }

    [TestMethod]
    public void UnpairedSurrogateIsRejected() =>
        Assert.IsFalse(DisplayName.TryParse("a\uD800b", out _));

    [TestMethod]
    public void MentionSyntaxIsPreservedVerbatim() =>
        Assert.AreEqual("@everyone <#1>", DisplayName.Parse("@everyone <#1>").Value);

    [TestMethod]
    public void RejectionRaisesCanonicalCode() =>
        Assert.AreEqual(
            InvariantViolationCode.DisplayNameInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => DisplayName.Parse(" ")).Code);
}

[TestClass]
public sealed class DiscordUserIdTests
{
    [TestMethod]
    public void DecimalTextRoundTrips()
    {
        DiscordUserId userId = DiscordUserId.Parse("123456789012345678");

        Assert.AreEqual(123456789012345678UL, userId.Value);
        Assert.AreEqual("123456789012345678", userId.ToString());
    }

    [TestMethod]
    public void MaximumUnsignedValueIsAccepted() =>
        Assert.AreEqual(ulong.MaxValue, DiscordUserId.Parse("18446744073709551615").Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("00")]
    [DataRow("-1")]
    [DataRow("12a")]
    [DataRow(" 12")]
    [DataRow("18446744073709551616")]
    [DataRow("184467440737095516150")]
    public void MalformedOrOverflowingTextIsRejected(string candidate) =>
        Assert.IsFalse(DiscordUserId.TryParse(candidate, out _));

    [TestMethod]
    public void ZeroIsNotAValidIdentity() =>
        Assert.AreEqual(
            InvariantViolationCode.DiscordUserIdInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => DiscordUserId.FromUInt64(0)).Code);

    [TestMethod]
    public void EqualityComparesNumericValue() =>
        Assert.AreEqual(DiscordUserId.FromUInt64(7), DiscordUserId.Parse("7"));
}
