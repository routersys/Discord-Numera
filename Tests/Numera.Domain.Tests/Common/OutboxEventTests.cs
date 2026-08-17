using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Common;

[TestClass]
public sealed class OutboxEventTests
{
    private static readonly OutboxEventId Identifier = OutboxEventId.FromValue(EntityIdValue.FromBits(1));
    private static readonly EntityIdValue ClaimToken = EntityIdValue.FromBits(2);
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp ClaimedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_001_000);
    private static readonly UtcTimestamp ExpiresAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_061_000);

    private static OutboxEvent Enqueued() =>
        OutboxEvent.Enqueue(Identifier, null, "TRANSFER_COMPLETED", "{}", CreatedAt);

    private static OutboxEvent Claimed()
    {
        OutboxEvent outboxEvent = Enqueued();
        outboxEvent.Claim(ClaimToken, ClaimedAt, ExpiresAt);
        return outboxEvent;
    }

    [TestMethod]
    public void EnqueuedEventStartsPendingWithoutClaim()
    {
        OutboxEvent outboxEvent = Enqueued();

        Assert.AreEqual(OutboxEventStatus.Pending, outboxEvent.Status);
        Assert.AreEqual(0, outboxEvent.AttemptCount);
        Assert.IsNull(outboxEvent.ClaimToken);
        Assert.IsNull(outboxEvent.PublishedAt);
    }

    [TestMethod]
    public void PayloadBeyondBudgetIsRejected()
    {
        string oversized = new('a', OutboxEvent.MaximumPayloadBytes + 1);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => OutboxEvent.Enqueue(Identifier, null, "TRANSFER_COMPLETED", oversized, CreatedAt));

        Assert.AreEqual(InvariantViolationCode.OutboxPayloadInvalid, exception.Code);
    }

    [TestMethod]
    public void PayloadBudgetIsMeasuredInBytesNotCharacters()
    {
        string justOverInBytes = new('あ', (OutboxEvent.MaximumPayloadBytes / 3) + 1);

        Assert.ThrowsExactly<InvariantViolationException>(
            () => OutboxEvent.Enqueue(Identifier, null, "TRANSFER_COMPLETED", justOverInBytes, CreatedAt));
    }

    [TestMethod]
    public void ClaimRecordsTokenAndIncrementsAttempt()
    {
        OutboxEvent outboxEvent = Claimed();

        Assert.AreEqual(OutboxEventStatus.Claimed, outboxEvent.Status);
        Assert.AreEqual(ClaimToken, outboxEvent.ClaimToken);
        Assert.AreEqual(1, outboxEvent.AttemptCount);
        Assert.IsNull(outboxEvent.NextAttemptAt);
    }

    [TestMethod]
    public void ClaimWithNonPositiveLeaseIsRejected()
    {
        OutboxEvent outboxEvent = Enqueued();

        Assert.ThrowsExactly<InvariantViolationException>(
            () => outboxEvent.Claim(ClaimToken, ClaimedAt, ClaimedAt));
    }

    [TestMethod]
    public void PendingEventCannotBePublishedWithoutClaim()
    {
        OutboxEvent outboxEvent = Enqueued();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => outboxEvent.MarkPublished(ExpiresAt));

        Assert.AreEqual(InvariantViolationCode.OutboxTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void PublishClearsClaimState()
    {
        OutboxEvent outboxEvent = Claimed();

        outboxEvent.MarkPublished(ExpiresAt);

        Assert.AreEqual(OutboxEventStatus.Published, outboxEvent.Status);
        Assert.AreEqual(ExpiresAt, outboxEvent.PublishedAt);
        Assert.IsNull(outboxEvent.ClaimToken);
        Assert.IsNull(outboxEvent.ClaimedAt);
        Assert.IsNull(outboxEvent.LastErrorCode);
    }

    [TestMethod]
    public void PublishedEventIsTerminal()
    {
        OutboxEvent outboxEvent = Claimed();
        outboxEvent.MarkPublished(ExpiresAt);

        Assert.ThrowsExactly<InvariantViolationException>(
            () => outboxEvent.Claim(ClaimToken, ClaimedAt, ExpiresAt));
        Assert.ThrowsExactly<InvariantViolationException>(
            () => outboxEvent.ScheduleRetry(ExpiresAt, "HTTP_500"));
    }

    [TestMethod]
    public void RetryReleasesClaimAndSchedulesNextAttempt()
    {
        OutboxEvent outboxEvent = Claimed();

        outboxEvent.ScheduleRetry(ExpiresAt, "HTTP_500");

        Assert.AreEqual(OutboxEventStatus.RetryDue, outboxEvent.Status);
        Assert.AreEqual(ExpiresAt, outboxEvent.NextAttemptAt);
        Assert.AreEqual("HTTP_500", outboxEvent.LastErrorCode);
        Assert.IsNull(outboxEvent.ClaimToken);
    }

    [TestMethod]
    public void RetriedEventCanBeClaimedAgain()
    {
        OutboxEvent outboxEvent = Claimed();
        outboxEvent.ScheduleRetry(ExpiresAt, "HTTP_500");

        outboxEvent.Claim(ClaimToken, ExpiresAt, UtcTimestamp.FromUnixMilliseconds(ExpiresAt.UnixMilliseconds + 60_000));

        Assert.AreEqual(OutboxEventStatus.Claimed, outboxEvent.Status);
        Assert.AreEqual(2, outboxEvent.AttemptCount);
    }

    [TestMethod]
    public void AttemptCountIsCappedAtCanonicalMaximum()
    {
        OutboxEvent outboxEvent = Enqueued();
        UtcTimestamp cursor = ClaimedAt;

        for (int attempt = 1; attempt < OutboxEvent.MaximumAttemptCount; attempt++)
        {
            UtcTimestamp lease = UtcTimestamp.FromUnixMilliseconds(cursor.UnixMilliseconds + 60_000);
            outboxEvent.Claim(ClaimToken, cursor, lease);
            outboxEvent.ScheduleRetry(lease, "HTTP_500");
            cursor = lease;
        }

        UtcTimestamp finalLease = UtcTimestamp.FromUnixMilliseconds(cursor.UnixMilliseconds + 60_000);
        outboxEvent.Claim(ClaimToken, cursor, finalLease);

        Assert.AreEqual(OutboxEvent.MaximumAttemptCount, outboxEvent.AttemptCount);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => outboxEvent.ScheduleRetry(finalLease, "HTTP_500"));

        Assert.AreEqual(InvariantViolationCode.OutboxAttemptExhausted, exception.Code);

        outboxEvent.MarkDeadLetter("HTTP_500");
        Assert.AreEqual(OutboxEventStatus.DeadLetter, outboxEvent.Status);
    }

    [TestMethod]
    public void DeadLetterIsTerminal()
    {
        OutboxEvent outboxEvent = Claimed();

        outboxEvent.MarkDeadLetter("HTTP_403");

        Assert.AreEqual(OutboxEventStatus.DeadLetter, outboxEvent.Status);
        Assert.AreEqual("HTTP_403", outboxEvent.LastErrorCode);
        Assert.ThrowsExactly<InvariantViolationException>(
            () => outboxEvent.Claim(ClaimToken, ClaimedAt, ExpiresAt));
    }

    [TestMethod]
    public void RehydrationRejectsClaimedEventWithoutClaimMetadata() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => OutboxEvent.Rehydrate(
            Identifier, null, "TRANSFER_COMPLETED", "{}", OutboxEventStatus.Claimed,
            null, null, null, null, CreatedAt, null, 1, null, VersionedEntity.InitialVersion));

    [TestMethod]
    public void RehydrationRejectsPublishedEventWithoutTimestamp() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => OutboxEvent.Rehydrate(
            Identifier, null, "TRANSFER_COMPLETED", "{}", OutboxEventStatus.Published,
            null, null, null, null, CreatedAt, null, 1, null, VersionedEntity.InitialVersion));

    [TestMethod]
    public void StatusTokensRoundTrip()
    {
        foreach (OutboxEventStatus status in Enum.GetValues<OutboxEventStatus>())
        {
            Assert.AreEqual(status, OutboxEventStatusCatalog.ParseToken(status.ToToken()));
        }

        Assert.IsFalse(OutboxEventStatusCatalog.TryParseToken("pending", out _));
    }
}

[TestClass]
public sealed class BusinessOperationTests
{
    private static readonly BusinessOperationId Identifier =
        BusinessOperationId.FromValue(EntityIdValue.FromBits(1));

    private static readonly EconomyScopeId Scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp CommittedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_001_000);

    private static BusinessOperation Started() => BusinessOperation.Start(
        Identifier, "TRANSFER", Scope, null, EntityIdValue.FromBits(3),
        IdempotencyKey.Create("TRANSFER", "key-1"), CreatedAt);

    [TestMethod]
    public void StartedOperationHasNoCommitTimestamp()
    {
        BusinessOperation operation = Started();

        Assert.AreEqual(BusinessOperationStatus.Started, operation.Status);
        Assert.IsNull(operation.CommittedAt);
    }

    [TestMethod]
    public void CommitRecordsTimestampAndIsTerminal()
    {
        BusinessOperation operation = Started();

        operation.Commit(CommittedAt);

        Assert.AreEqual(BusinessOperationStatus.Committed, operation.Status);
        Assert.AreEqual(CommittedAt, operation.CommittedAt);
        Assert.ThrowsExactly<InvariantViolationException>(() => operation.Commit(CommittedAt));
        Assert.ThrowsExactly<InvariantViolationException>(operation.Fail);
    }

    [TestMethod]
    public void FailedOperationIsTerminal()
    {
        BusinessOperation operation = Started();

        operation.Fail();

        Assert.AreEqual(BusinessOperationStatus.Failed, operation.Status);
        Assert.ThrowsExactly<InvariantViolationException>(() => operation.Commit(CommittedAt));
    }

    [TestMethod]
    public void BlankOperationTypeIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => BusinessOperation.Start(
            Identifier, "  ", Scope, null, EntityIdValue.FromBits(3),
            IdempotencyKey.Create("TRANSFER", "key-1"), CreatedAt));

    [TestMethod]
    public void RehydrationRejectsCommittedOperationWithoutTimestamp() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => BusinessOperation.Rehydrate(
            Identifier, "TRANSFER", Scope, null, EntityIdValue.FromBits(3),
            IdempotencyKey.Create("TRANSFER", "key-1"), BusinessOperationStatus.Committed,
            CreatedAt, null, VersionedEntity.InitialVersion));

    [TestMethod]
    public void StatusTokensRoundTrip()
    {
        foreach (BusinessOperationStatus status in Enum.GetValues<BusinessOperationStatus>())
        {
            Assert.AreEqual(status, BusinessOperationStatusCatalog.ParseToken(status.ToToken()));
        }
    }
}

[TestClass]
public sealed class IdempotencyKeyTests
{
    [TestMethod]
    public void CanonicalKeyIsAccepted()
    {
        IdempotencyKey key = IdempotencyKey.Create("TRANSFER", "user.1:op-2_3");

        Assert.AreEqual("TRANSFER", key.Scope);
        Assert.AreEqual("user.1:op-2_3", key.Key);
    }

    [TestMethod]
    [DataRow("", "key")]
    [DataRow("scope", "")]
    [DataRow("scope with space", "key")]
    [DataRow("scope", "key/slash")]
    [DataRow("scope", "キー")]
    public void MalformedKeysAreRejected(string scope, string key) =>
        Assert.IsFalse(IdempotencyKey.TryCreate(scope, key, out _));

    [TestMethod]
    public void OverlongComponentsAreRejected()
    {
        Assert.IsFalse(IdempotencyKey.TryCreate(new string('a', 65), "key", out _));
        Assert.IsFalse(IdempotencyKey.TryCreate("scope", new string('a', 129), out _));
    }

    [TestMethod]
    public void EqualityComparesScopeAndKey()
    {
        Assert.AreEqual(IdempotencyKey.Create("a", "b"), IdempotencyKey.Create("a", "b"));
        Assert.AreNotEqual(IdempotencyKey.Create("a", "b"), IdempotencyKey.Create("a", "c"));
        Assert.AreNotEqual(IdempotencyKey.Create("a", "b"), IdempotencyKey.Create("c", "b"));
    }

    [TestMethod]
    public void RejectionRaisesCanonicalCode() =>
        Assert.AreEqual(
            InvariantViolationCode.IdempotencyKeyInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => IdempotencyKey.Create("", "")).Code);
}
