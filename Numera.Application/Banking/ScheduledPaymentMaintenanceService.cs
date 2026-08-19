using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record ScheduledPaymentMaintenanceReport(
    int Occurrences,
    int Collections,
    int Executed)
{
    public int Examined => Occurrences + Collections;
}

public sealed class ScheduledPaymentMaintenanceService
{
    public const int BatchSize = 100;

    private readonly IBankingWriteGateway writeGateway;
    private readonly PaymentApplicationService payments;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public ScheduledPaymentMaintenanceService(
        IBankingWriteGateway writeGateway,
        PaymentApplicationService payments,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.payments = payments;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public async Task<ScheduledPaymentMaintenanceReport> ProcessDueAsync(
        CancellationToken cancellationToken)
    {
        UtcTimestamp now = clock.Now();

        Result<IReadOnlyList<ScheduledPaymentOccurrenceId>> occurrences = await writeGateway
            .ExecuteAsync(
                unitOfWork => Result<IReadOnlyList<ScheduledPaymentOccurrenceId>>.Success(
                    [.. unitOfWork.PaymentManagement.ListDueOccurrences(now, BatchSize)
                        .Select(static occurrence => occurrence.Id)]),
                cancellationToken)
            .ConfigureAwait(false);

        int executed = 0;
        int examinedOccurrences = occurrences.IsSuccess ? occurrences.Value.Count : 0;

        foreach (ScheduledPaymentOccurrenceId id in occurrences.IsSuccess ? occurrences.Value : [])
        {
            Result<bool> outcome = await writeGateway
                .ExecuteAsync(unitOfWork => RunOccurrence(unitOfWork, id), cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsSuccess && outcome.Value)
            {
                executed++;
            }
        }

        Result<IReadOnlyList<DirectDebitCollectionId>> collections = await writeGateway
            .ExecuteAsync(
                unitOfWork => Result<IReadOnlyList<DirectDebitCollectionId>>.Success(
                    [.. unitOfWork.PaymentManagement.ListDueCollections(now, BatchSize)
                        .Select(static collection => collection.Id)]),
                cancellationToken)
            .ConfigureAwait(false);

        int examinedCollections = collections.IsSuccess ? collections.Value.Count : 0;

        foreach (DirectDebitCollectionId id in collections.IsSuccess ? collections.Value : [])
        {
            Result<bool> outcome = await writeGateway
                .ExecuteAsync(unitOfWork => RunCollection(unitOfWork, id), cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsSuccess && outcome.Value)
            {
                executed++;
            }
        }

        return new ScheduledPaymentMaintenanceReport(
            examinedOccurrences, examinedCollections, executed);
    }

    private readonly record struct TransferTarget(
        ulong GuildId,
        DepositAccount Source,
        DepositAccount Destination,
        string InstitutionCode,
        string BranchCode);

    private Result<bool> RunOccurrence(
        IBankingUnitOfWork unitOfWork,
        ScheduledPaymentOccurrenceId occurrenceId)
    {
        if (unitOfWork.PaymentManagement.FindOccurrence(occurrenceId) is not
            { Status: ScheduledPaymentOccurrenceStatus.Pending } occurrence)
        {
            return Result<bool>.Success(false);
        }

        if (unitOfWork.PaymentManagement.FindPlan(occurrence.PlanId) is not { } plan)
        {
            return Result<bool>.Success(false);
        }

        UtcTimestamp now = clock.Now();

        if (plan.Status != ScheduledPaymentPlanStatus.Active)
        {
            occurrence.Cancel(now);
            unitOfWork.PaymentManagement.UpdateOccurrence(occurrence);

            return Result<bool>.Success(false);
        }

        occurrence.Claim(now);

        if (Resolve(unitOfWork, plan.SourceDepositAccountId, plan.DestinationDepositAccountId) is not
            { } target)
        {
            return FailOccurrence(
                unitOfWork,
                plan,
                occurrence,
                ScheduledPaymentOccurrenceStatus.FailedDestination,
                permanent: true,
                now);
        }

        if (target.Destination.IsClosed)
        {
            return FailOccurrence(
                unitOfWork,
                plan,
                occurrence,
                ScheduledPaymentOccurrenceStatus.FailedDestination,
                permanent: true,
                now);
        }

        IdempotencyKey idempotencyKey = IdempotencyKey.Create(
            PaymentApplicationService.ScheduledOperationType,
            $"{plan.Id.Value}:{occurrence.ScheduledFor.UnixMilliseconds}");

        Result<PaymentApplicationService.ReservedTransfer> reserved = payments.ReserveRecurringTransfer(
            unitOfWork,
            new CreatePaymentOrderCommand(
                target.GuildId,
                plan.CustomerAccountId,
                target.Source.Id,
                target.InstitutionCode,
                target.BranchCode,
                target.Destination.AccountNumber.Value,
                plan.Amount.Value,
                null,
                idempotencyKey.Key),
            idempotencyKey);

        if (!reserved.IsSuccess)
        {
            return FailOccurrence(
                unitOfWork,
                plan,
                occurrence,
                ClassifyOccurrenceFailure(reserved.Error!),
                permanent: false,
                now);
        }

        Result<PaymentOrderView> posted = payments.PostRecurringTransfer(
            unitOfWork, reserved.Value, idempotencyKey);

        if (!posted.IsSuccess)
        {
            return Result<bool>.Failure(posted.Error!);
        }

        occurrence.Succeed(reserved.Value.OrderId, now);
        unitOfWork.PaymentManagement.UpdateOccurrence(occurrence);

        AdvancePlan(unitOfWork, plan, cancel: false);

        return Result<bool>.Success(true);
    }

    private Result<bool> FailOccurrence(
        IBankingUnitOfWork unitOfWork,
        ScheduledPaymentPlan plan,
        ScheduledPaymentOccurrence occurrence,
        ScheduledPaymentOccurrenceStatus status,
        bool permanent,
        UtcTimestamp now)
    {
        occurrence.Fail(status, now);
        unitOfWork.PaymentManagement.UpdateOccurrence(occurrence);

        AdvancePlan(unitOfWork, plan, permanent);

        return Result<bool>.Success(false);
    }

    private void AdvancePlan(IBankingUnitOfWork unitOfWork, ScheduledPaymentPlan plan, bool cancel)
    {
        if (plan.Kind == ScheduledPaymentKind.Once)
        {
            plan.Complete();
            unitOfWork.PaymentManagement.UpdatePlan(plan);

            return;
        }

        if (cancel)
        {
            plan.Cancel();
            unitOfWork.PaymentManagement.UpdatePlan(plan);

            return;
        }

        if (!ScheduledPaymentSchedule.TryResolveNext(
                plan.CanonicalTimezone,
                plan.Kind,
                plan.AnchorDayOfMonth,
                plan.NextDueAt ?? clock.Now(),
                out UtcTimestamp next))
        {
            plan.Complete();
            unitOfWork.PaymentManagement.UpdatePlan(plan);

            return;
        }

        plan.Advance(next);
        unitOfWork.PaymentManagement.UpdatePlan(plan);
        unitOfWork.PaymentManagement.AddOccurrence(ScheduledPaymentOccurrence.Schedule(
            ScheduledPaymentOccurrenceId.FromValue(idGenerator.NextId()), plan.Id, next));
    }

    private static ScheduledPaymentOccurrenceStatus ClassifyOccurrenceFailure(ApplicationError error) =>
        error.Category == ErrorCategory.InsufficientFunds
            ? ScheduledPaymentOccurrenceStatus.FailedFunds
            : string.Equals(
                error.Code, BankingErrorCodes.DestinationAccountNotOperable, StringComparison.Ordinal)
                ? ScheduledPaymentOccurrenceStatus.FailedDestination
                : ScheduledPaymentOccurrenceStatus.FailedRestricted;

    private Result<bool> RunCollection(
        IBankingUnitOfWork unitOfWork,
        DirectDebitCollectionId collectionId)
    {
        if (unitOfWork.PaymentManagement.FindCollection(collectionId) is not
            { Status: DirectDebitCollectionStatus.Pending } collection)
        {
            return Result<bool>.Success(false);
        }

        if (unitOfWork.PaymentManagement.FindMandate(collection.MandateId) is not { } mandate)
        {
            return Result<bool>.Success(false);
        }

        UtcTimestamp now = clock.Now();

        collection.Claim();

        if (!IsCollectable(mandate, collection, now))
        {
            return FailCollection(
                unitOfWork, mandate, collection, DirectDebitCollectionStatus.FailedMandate, false, now);
        }

        if (Resolve(unitOfWork, mandate.DebtorDepositAccountId, mandate.CreditorSettlementAccountId) is not
            { } target)
        {
            return FailCollection(
                unitOfWork, mandate, collection, DirectDebitCollectionStatus.FailedAccount, true, now);
        }

        if (target.Source.IsClosed || target.Destination.IsClosed)
        {
            return FailCollection(
                unitOfWork, mandate, collection, DirectDebitCollectionStatus.FailedAccount, true, now);
        }

        if (target.Source.CurrencyId != mandate.CurrencyId ||
            target.Destination.CurrencyId != mandate.CurrencyId)
        {
            return FailCollection(
                unitOfWork, mandate, collection, DirectDebitCollectionStatus.FailedMandate, false, now);
        }

        IdempotencyKey idempotencyKey = IdempotencyKey.Create(
            PaymentApplicationService.DirectDebitOperationType,
            collection.Id.Value.ToString());

        Result<PaymentApplicationService.ReservedTransfer> reserved = payments.ReserveRecurringTransfer(
            unitOfWork,
            new CreatePaymentOrderCommand(
                target.GuildId,
                mandate.DebtorCustomerAccountId,
                target.Source.Id,
                target.InstitutionCode,
                target.BranchCode,
                target.Destination.AccountNumber.Value,
                collection.Amount.Value,
                null,
                idempotencyKey.Key),
            idempotencyKey);

        if (!reserved.IsSuccess)
        {
            return FailCollection(
                unitOfWork,
                mandate,
                collection,
                reserved.Error!.Category == ErrorCategory.InsufficientFunds
                    ? DirectDebitCollectionStatus.FailedFunds
                    : DirectDebitCollectionStatus.FailedAccount,
                permanent: false,
                now);
        }

        Result<PaymentOrderView> posted = payments.PostRecurringTransfer(
            unitOfWork, reserved.Value, idempotencyKey);

        if (!posted.IsSuccess)
        {
            return Result<bool>.Failure(posted.Error!);
        }

        collection.Settle(reserved.Value.OrderId, now);
        unitOfWork.PaymentManagement.UpdateCollection(collection);

        return Result<bool>.Success(true);
    }

    private static bool IsCollectable(
        DirectDebitMandate mandate,
        DirectDebitCollection collection,
        UtcTimestamp now) =>
        mandate.IsCollectable
        && mandate.ValidFrom.UnixMilliseconds <= now.UnixMilliseconds
        && (mandate.ValidUntil is not { } until || now.UnixMilliseconds < until.UnixMilliseconds)
        && collection.Amount <= mandate.SingleCollectionLimit;

    private static Result<bool> FailCollection(
        IBankingUnitOfWork unitOfWork,
        DirectDebitMandate mandate,
        DirectDebitCollection collection,
        DirectDebitCollectionStatus status,
        bool permanent,
        UtcTimestamp now)
    {
        collection.Fail(status, now);
        unitOfWork.PaymentManagement.UpdateCollection(collection);

        if (permanent && mandate.Status is DirectDebitMandateStatus.Pending
            or DirectDebitMandateStatus.Active or DirectDebitMandateStatus.Suspended)
        {
            mandate.Revoke(now);
            unitOfWork.PaymentManagement.UpdateMandate(mandate);
        }

        return Result<bool>.Success(false);
    }

    private static TransferTarget? Resolve(
        IBankingUnitOfWork unitOfWork,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId)
    {
        if (unitOfWork.DepositAccounts.Find(sourceDepositAccountId) is not { } source ||
            unitOfWork.DepositAccounts.Find(destinationDepositAccountId) is not { } destination ||
            unitOfWork.Banks.Find(source.BankId) is not { } sourceBank ||
            unitOfWork.Banks.Find(destination.BankId) is not { } destinationBank ||
            unitOfWork.Branches.FindCodeById(destination.BranchId) is not { } branchCode ||
            unitOfWork.GuildEconomies.FindGuildId(sourceBank.EconomyScopeId) is not { } guild ||
            !ulong.TryParse(guild, NumberStyles.None, CultureInfo.InvariantCulture, out ulong guildId))
        {
            return null;
        }

        return new TransferTarget(
            guildId, source, destination, destinationBank.InstitutionCode.Value, branchCode);
    }
}
