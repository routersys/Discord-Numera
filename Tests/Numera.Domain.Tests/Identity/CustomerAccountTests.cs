using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Tests.Identity;

[TestClass]
public sealed class CustomerAccountTests
{
    private static readonly CustomerAccountId Identifier =
        CustomerAccountId.FromValue(EntityIdValue.FromBits(1));

    private static readonly PartyId Party = PartyId.FromValue(EntityIdValue.FromBits(2));
    private static readonly UtcTimestamp RegisteredAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static CustomerAccount Register() => CustomerAccount.Register(
        Identifier,
        Party,
        PublicHandle.Parse("taro"),
        DisplayName.Parse("山田太郎"),
        RegisteredAt);

    [TestMethod]
    public void RegistrationStartsActiveAtInitialVersion()
    {
        CustomerAccount account = Register();

        Assert.AreEqual(CustomerAccountStatus.Active, account.Status);
        Assert.AreEqual(VersionedEntity.InitialVersion, account.Version);
        Assert.AreEqual(RegisteredAt, account.CreatedAt);
        Assert.AreEqual(RegisteredAt, account.LastAuthenticatedAt);
        Assert.IsFalse(account.IsClosed);
    }

    [TestMethod]
    public void CanonicalTransitionsAreAccepted()
    {
        CustomerAccount account = Register();

        account.Restrict();
        Assert.AreEqual(CustomerAccountStatus.Restricted, account.Status);

        account.ClearRestriction();
        Assert.AreEqual(CustomerAccountStatus.Active, account.Status);

        account.Suspend();
        Assert.AreEqual(CustomerAccountStatus.Suspended, account.Status);

        account.Recover();
        Assert.AreEqual(CustomerAccountStatus.Active, account.Status);

        account.Close();
        Assert.AreEqual(CustomerAccountStatus.Closed, account.Status);
    }

    [TestMethod]
    public void RestrictedAccountCanBeSuspendedDirectly()
    {
        CustomerAccount account = Register();
        account.Restrict();

        account.Suspend();

        Assert.AreEqual(CustomerAccountStatus.Suspended, account.Status);
    }

    [TestMethod]
    public void SuspendedAccountCannotReturnToRestricted()
    {
        CustomerAccount account = Register();
        account.Suspend();

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(account.Restrict);

        Assert.AreEqual(InvariantViolationCode.CustomerAccountTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void ClosedAccountIsTerminal()
    {
        CustomerAccount account = Register();
        account.Close();

        Assert.ThrowsExactly<InvariantViolationException>(account.Restrict);
        Assert.ThrowsExactly<InvariantViolationException>(account.Suspend);
        Assert.ThrowsExactly<InvariantViolationException>(account.Recover);
        Assert.ThrowsExactly<InvariantViolationException>(account.Close);
        Assert.ThrowsExactly<InvariantViolationException>(() => account.Rename(DisplayName.Parse("別名")));
        Assert.ThrowsExactly<InvariantViolationException>(() => account.RecordAuthentication(RegisteredAt));
    }

    [TestMethod]
    public void EveryMutationAdvancesVersion()
    {
        CustomerAccount account = Register();
        long initial = account.Version;

        account.Restrict();
        Assert.AreEqual(initial + 1, account.Version);

        account.ClearRestriction();
        Assert.AreEqual(initial + 2, account.Version);

        account.Rename(DisplayName.Parse("別名"));
        Assert.AreEqual(initial + 3, account.Version);

        account.RecordAuthentication(RegisteredAt);
        Assert.AreEqual(initial + 4, account.Version);
    }

    [TestMethod]
    public void AuthenticationTimestampNeverMovesBackwards()
    {
        CustomerAccount account = Register();
        UtcTimestamp later = UtcTimestamp.FromUnixMilliseconds(RegisteredAt.UnixMilliseconds + 1_000);

        account.RecordAuthentication(later);
        Assert.AreEqual(later, account.LastAuthenticatedAt);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => account.RecordAuthentication(RegisteredAt));

        Assert.AreEqual(InvariantViolationCode.TimestampOutOfRange, exception.Code);
        Assert.AreEqual(later, account.LastAuthenticatedAt);
    }

    [TestMethod]
    public void RenamingDoesNotChangePublicHandle()
    {
        CustomerAccount account = Register();

        account.Rename(DisplayName.Parse("新しい名前"));

        Assert.AreEqual(PublicHandle.Parse("taro"), account.PublicHandle);
        Assert.AreEqual("新しい名前", account.DisplayName.Value);
    }

    [TestMethod]
    public void RehydrationBelowInitialVersionIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => CustomerAccount.Rehydrate(
                Identifier, Party, PublicHandle.Parse("taro"), DisplayName.Parse("山田太郎"),
                CustomerAccountStatus.Active, RegisteredAt, RegisteredAt, 0));

        Assert.AreEqual(InvariantViolationCode.EntityVersionInvalid, exception.Code);
    }

    [TestMethod]
    public void StatusTokensRoundTrip()
    {
        foreach (CustomerAccountStatus status in Enum.GetValues<CustomerAccountStatus>())
        {
            Assert.AreEqual(status, CustomerAccountStatusCatalog.ParseToken(status.ToToken()));
        }

        Assert.IsFalse(CustomerAccountStatusCatalog.TryParseToken("active", out _));
    }
}

[TestClass]
public sealed class DiscordIdentityLinkTests
{
    private static readonly DiscordIdentityLinkId Identifier =
        DiscordIdentityLinkId.FromValue(EntityIdValue.FromBits(1));

    private static readonly CustomerAccountId Customer =
        CustomerAccountId.FromValue(EntityIdValue.FromBits(2));

    private static readonly DiscordUserId User = DiscordUserId.Parse("123456789012345678");
    private static readonly UtcTimestamp LinkedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp UnlinkedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_900_000);

    private static DiscordIdentityLink Link(bool isPrimary) =>
        DiscordIdentityLink.Link(Identifier, Customer, User, isPrimary, LinkedAt);

    [TestMethod]
    public void LinkStartsActive()
    {
        DiscordIdentityLink link = Link(isPrimary: true);

        Assert.IsTrue(link.IsActive);
        Assert.IsTrue(link.IsPrimary);
        Assert.IsNull(link.UnlinkedAt);
        Assert.AreEqual(VersionedEntity.InitialVersion, link.Version);
    }

    [TestMethod]
    public void PrimaryLinkCannotBeUnlinkedDirectly()
    {
        DiscordIdentityLink link = Link(isPrimary: true);

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => link.Unlink(UnlinkedAt));

        Assert.AreEqual(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid, exception.Code);
        Assert.IsTrue(link.IsActive);
    }

    [TestMethod]
    public void DemotedPrimaryCanBeUnlinked()
    {
        DiscordIdentityLink link = Link(isPrimary: true);

        link.DemoteFromPrimary();
        link.Unlink(UnlinkedAt);

        Assert.AreEqual(DiscordIdentityLinkStatus.Unlinked, link.Status);
        Assert.AreEqual(UnlinkedAt, link.UnlinkedAt);
        Assert.IsFalse(link.IsPrimary);
    }

    [TestMethod]
    public void UnlinkedLinkIsTerminal()
    {
        DiscordIdentityLink link = Link(isPrimary: false);
        link.Unlink(UnlinkedAt);

        Assert.ThrowsExactly<InvariantViolationException>(() => link.Unlink(UnlinkedAt));
        Assert.ThrowsExactly<InvariantViolationException>(link.PromoteToPrimary);
        Assert.ThrowsExactly<InvariantViolationException>(link.DemoteFromPrimary);
        Assert.ThrowsExactly<InvariantViolationException>(() => link.RecordAuthentication(UnlinkedAt));
    }

    [TestMethod]
    public void RedundantPrimaryChangesAreRejected()
    {
        DiscordIdentityLink primary = Link(isPrimary: true);
        Assert.ThrowsExactly<InvariantViolationException>(primary.PromoteToPrimary);

        DiscordIdentityLink secondary = Link(isPrimary: false);
        Assert.ThrowsExactly<InvariantViolationException>(secondary.DemoteFromPrimary);
    }

    [TestMethod]
    public void PromotionMakesLinkPrimary()
    {
        DiscordIdentityLink link = Link(isPrimary: false);

        link.PromoteToPrimary();

        Assert.IsTrue(link.IsPrimary);
    }

    [TestMethod]
    public void RehydrationRequiresUnlinkTimestampForUnlinkedStatus()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => DiscordIdentityLink.Rehydrate(
                Identifier, Customer, User, false, DiscordIdentityLinkStatus.Unlinked,
                LinkedAt, null, LinkedAt, VersionedEntity.InitialVersion));

        Assert.AreEqual(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsActiveLinkWithUnlinkTimestamp() =>
        Assert.ThrowsExactly<InvariantViolationException>(
            () => DiscordIdentityLink.Rehydrate(
                Identifier, Customer, User, false, DiscordIdentityLinkStatus.Active,
                LinkedAt, UnlinkedAt, LinkedAt, VersionedEntity.InitialVersion));

    [TestMethod]
    public void RehydrationRejectsUnlinkedPrimaryLink() =>
        Assert.ThrowsExactly<InvariantViolationException>(
            () => DiscordIdentityLink.Rehydrate(
                Identifier, Customer, User, true, DiscordIdentityLinkStatus.Unlinked,
                LinkedAt, UnlinkedAt, LinkedAt, VersionedEntity.InitialVersion));

    [TestMethod]
    public void StatusTokensRoundTrip()
    {
        foreach (DiscordIdentityLinkStatus status in Enum.GetValues<DiscordIdentityLinkStatus>())
        {
            Assert.AreEqual(status, DiscordIdentityLinkStatusCatalog.ParseToken(status.ToToken()));
        }

        Assert.IsFalse(DiscordIdentityLinkStatusCatalog.TryParseToken("unlinked", out _));
    }
}
