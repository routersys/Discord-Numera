using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CommerceFulfillmentReport(int Examined, int Succeeded, int Failed);

public sealed class CommerceFulfillmentService
{
    public const int BatchSize = 25;

    public const int MaximumAttempts = 5;

    public const long RetryBackoffMilliseconds = 60 * 1000;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IDiscordRoleGateway roles;
    private readonly IClock clock;

    public CommerceFulfillmentService(
        IBankingWriteGateway writeGateway,
        IDiscordRoleGateway roles,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(clock);

        this.writeGateway = writeGateway;
        this.roles = roles;
        this.clock = clock;
    }

    private readonly record struct GrantTarget(
        string GuildId,
        string DiscordUserId,
        string DiscordRoleId,
        bool FullyReturned);

    public async Task<CommerceFulfillmentReport> ProcessDueAsync(CancellationToken cancellationToken)
    {
        UtcTimestamp now = clock.Now();

        Result<IReadOnlyList<CommerceFulfillmentId>> claimed = await writeGateway
            .ExecuteAsync(unitOfWork => ClaimGrants(unitOfWork, now), cancellationToken)
            .ConfigureAwait(false);

        int succeeded = 0;
        int failed = 0;
        int examined = claimed.IsSuccess ? claimed.Value.Count : 0;

        foreach (CommerceFulfillmentId id in claimed.IsSuccess ? claimed.Value : [])
        {
            Result<GrantTarget> target = await writeGateway
                .ExecuteAsync(unitOfWork => ResolveGrant(unitOfWork, id), cancellationToken)
                .ConfigureAwait(false);

            if (!target.IsSuccess)
            {
                continue;
            }

            if (target.Value.FullyReturned)
            {
                await writeGateway
                    .ExecuteAsync(unitOfWork => Cancel(unitOfWork, id), cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            DiscordRoleOutcome outcome = await roles.GrantAsync(
                target.Value.GuildId,
                target.Value.DiscordUserId,
                target.Value.DiscordRoleId,
                cancellationToken).ConfigureAwait(false);

            Result<bool> recorded = await writeGateway
                .ExecuteAsync(
                    unitOfWork => RecordGrant(unitOfWork, id, outcome, clock.Now()),
                    cancellationToken)
                .ConfigureAwait(false);

            if (recorded.IsSuccess && recorded.Value)
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        Result<IReadOnlyList<CommerceFulfillmentReversalId>> reversals = await writeGateway
            .ExecuteAsync(unitOfWork => ClaimReversals(unitOfWork, now), cancellationToken)
            .ConfigureAwait(false);

        foreach (CommerceFulfillmentReversalId id in reversals.IsSuccess ? reversals.Value : [])
        {
            examined++;

            Result<GrantTarget> target = await writeGateway
                .ExecuteAsync(unitOfWork => ResolveReversal(unitOfWork, id), cancellationToken)
                .ConfigureAwait(false);

            if (!target.IsSuccess)
            {
                continue;
            }

            DiscordRoleOutcome outcome = await roles.RevokeAsync(
                target.Value.GuildId,
                target.Value.DiscordUserId,
                target.Value.DiscordRoleId,
                cancellationToken).ConfigureAwait(false);

            Result<bool> recorded = await writeGateway
                .ExecuteAsync(
                    unitOfWork => RecordReversal(unitOfWork, id, outcome, clock.Now()),
                    cancellationToken)
                .ConfigureAwait(false);

            if (recorded.IsSuccess && recorded.Value)
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        return new CommerceFulfillmentReport(examined, succeeded, failed);
    }

    private static Result<IReadOnlyList<CommerceFulfillmentId>> ClaimGrants(
        IBankingUnitOfWork unitOfWork,
        UtcTimestamp now) =>
        Result<IReadOnlyList<CommerceFulfillmentId>>.Success(
            [.. unitOfWork.Commerce.ListDueFulfillments(now, BatchSize).Select(f => f.Id)]);

    private static Result<IReadOnlyList<CommerceFulfillmentReversalId>> ClaimReversals(
        IBankingUnitOfWork unitOfWork,
        UtcTimestamp now) =>
        Result<IReadOnlyList<CommerceFulfillmentReversalId>>.Success(
            [.. unitOfWork.Commerce.ListDueFulfillmentReversals(now, BatchSize).Select(r => r.Id)]);

    private static Result<GrantTarget> ResolveGrant(
        IBankingUnitOfWork unitOfWork,
        CommerceFulfillmentId id)
    {
        if (unitOfWork.Commerce.FindFulfillment(id) is not { } fulfillment ||
            unitOfWork.Commerce.FindOrderLine(fulfillment.CommerceOrderLineId) is not { } line ||
            unitOfWork.Commerce.FindOrder(line.CommerceOrderId) is not { } order ||
            unitOfWork.Commerce.FindFulfillmentPolicy(fulfillment.FulfillmentPolicyVersionId)
                is not { DiscordRoleId: { } roleId })
        {
            return Result<GrantTarget>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceFulfillmentNotFound);
        }

        bool fullyReturned =
            unitOfWork.Commerce.SumCompletedReturnedQuantity(line.Id) >= line.Quantity;

        return Result<GrantTarget>.Success(new GrantTarget(
            order.OriginGuildId, order.PurchaserDiscordUserIdSnapshot, roleId, fullyReturned));
    }

    private static Result<GrantTarget> ResolveReversal(
        IBankingUnitOfWork unitOfWork,
        CommerceFulfillmentReversalId id)
    {
        if (unitOfWork.Commerce.FindFulfillmentReversal(id) is not { } reversal ||
            unitOfWork.Commerce.FindFulfillment(reversal.CommerceFulfillmentId) is not { } fulfillment ||
            unitOfWork.Commerce.FindOrderLine(fulfillment.CommerceOrderLineId) is not { } line ||
            unitOfWork.Commerce.FindOrder(line.CommerceOrderId) is not { } order ||
            unitOfWork.Commerce.FindFulfillmentPolicy(fulfillment.FulfillmentPolicyVersionId)
                is not { DiscordRoleId: { } roleId })
        {
            return Result<GrantTarget>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceFulfillmentNotFound);
        }

        return Result<GrantTarget>.Success(new GrantTarget(
            order.OriginGuildId, order.PurchaserDiscordUserIdSnapshot, roleId, FullyReturned: false));
    }

    private static Result<bool> Cancel(IBankingUnitOfWork unitOfWork, CommerceFulfillmentId id)
    {
        if (unitOfWork.Commerce.FindFulfillment(id) is not { } fulfillment ||
            !CommerceFulfillmentStatusCatalog.IsAllowed(
                fulfillment.Status, CommerceFulfillmentStatus.CancelledReturned))
        {
            return Result<bool>.Success(false);
        }

        CommerceFulfillmentStatusCatalog.EnsureTransition(
            fulfillment.Status, CommerceFulfillmentStatus.CancelledReturned);

        unitOfWork.Commerce.UpdateFulfillment(fulfillment with
        {
            Status = CommerceFulfillmentStatus.CancelledReturned,
            NextAttemptAt = null,
            Version = fulfillment.Version + 1,
        });

        return Result<bool>.Success(true);
    }

    private static Result<bool> RecordGrant(
        IBankingUnitOfWork unitOfWork,
        CommerceFulfillmentId id,
        DiscordRoleOutcome outcome,
        UtcTimestamp now)
    {
        if (unitOfWork.Commerce.FindFulfillment(id) is not { } fulfillment)
        {
            return Result<bool>.Success(false);
        }

        int attempts = fulfillment.AttemptCount + 1;
        CommerceFulfillmentStatus target = Next(outcome, attempts);

        if (!CommerceFulfillmentStatusCatalog.IsAllowed(fulfillment.Status, target))
        {
            return Result<bool>.Success(false);
        }

        CommerceFulfillmentStatusCatalog.EnsureTransition(fulfillment.Status, target);

        unitOfWork.Commerce.UpdateFulfillment(fulfillment with
        {
            Status = target,
            AttemptCount = attempts,
            NextAttemptAt = target == CommerceFulfillmentStatus.FailedRetryable
                ? now.AddMilliseconds(RetryBackoffMilliseconds * attempts)
                : null,
            FailureCode = outcome.FailureCode,
            Version = fulfillment.Version + 1,
        });

        return Result<bool>.Success(target == CommerceFulfillmentStatus.Succeeded);
    }

    private static Result<bool> RecordReversal(
        IBankingUnitOfWork unitOfWork,
        CommerceFulfillmentReversalId id,
        DiscordRoleOutcome outcome,
        UtcTimestamp now)
    {
        if (unitOfWork.Commerce.FindFulfillmentReversal(id) is not { } reversal)
        {
            return Result<bool>.Success(false);
        }

        int attempts = reversal.AttemptCount + 1;
        CommerceFulfillmentReversalStatus target = NextReversal(outcome, attempts);

        if (!CommerceFulfillmentReversalStatusCatalog.IsAllowed(reversal.Status, target))
        {
            return Result<bool>.Success(false);
        }

        CommerceFulfillmentReversalStatusCatalog.EnsureTransition(reversal.Status, target);

        unitOfWork.Commerce.UpdateFulfillmentReversal(reversal with
        {
            Status = target,
            AttemptCount = attempts,
            NextAttemptAt = target == CommerceFulfillmentReversalStatus.FailedRetryable
                ? now.AddMilliseconds(RetryBackoffMilliseconds * attempts)
                : null,
            FailureCode = outcome.FailureCode,
            Version = reversal.Version + 1,
        });

        return Result<bool>.Success(target == CommerceFulfillmentReversalStatus.Succeeded);
    }

    private static CommerceFulfillmentStatus Next(DiscordRoleOutcome outcome, int attempts) =>
        outcome.Kind switch
        {
            DiscordRoleOutcomeKind.Succeeded => CommerceFulfillmentStatus.Succeeded,
            DiscordRoleOutcomeKind.Retryable when attempts < MaximumAttempts =>
                CommerceFulfillmentStatus.FailedRetryable,
            _ => CommerceFulfillmentStatus.FailedManual,
        };

    private static CommerceFulfillmentReversalStatus NextReversal(
        DiscordRoleOutcome outcome,
        int attempts) =>
        outcome.Kind switch
        {
            DiscordRoleOutcomeKind.Succeeded => CommerceFulfillmentReversalStatus.Succeeded,
            DiscordRoleOutcomeKind.Retryable when attempts < MaximumAttempts =>
                CommerceFulfillmentReversalStatus.FailedRetryable,
            _ => CommerceFulfillmentReversalStatus.FailedManual,
        };
}
