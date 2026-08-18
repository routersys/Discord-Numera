using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record ExpiryMaintenanceReport(
    int Authorizations,
    int Mandates,
    int Holds)
{
    public int Total => Authorizations + Mandates + Holds;
}

public sealed class ExpiryMaintenanceService
{
    public const int BatchSize = 100;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;

    public ExpiryMaintenanceService(IBankingWriteGateway writeGateway, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);

        this.writeGateway = writeGateway;
        this.clock = clock;
    }

    public async Task<ExpiryMaintenanceReport> ProcessDueAsync(CancellationToken cancellationToken)
    {
        Result<ExpiryMaintenanceReport> outcome = await writeGateway
            .ExecuteAsync(ProcessDue, cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? outcome.Value : new ExpiryMaintenanceReport(0, 0, 0);
    }

    private Result<ExpiryMaintenanceReport> ProcessDue(IBankingUnitOfWork unitOfWork)
    {
        UtcTimestamp now = clock.Now();

        int authorizations = ExpireAuthorizations(unitOfWork, now);
        int mandates = ExpireMandates(unitOfWork, now);
        int holds = ExpireStandaloneHolds(unitOfWork, now);

        return Result<ExpiryMaintenanceReport>.Success(
            new ExpiryMaintenanceReport(authorizations, mandates, holds));
    }

    private static int ExpireAuthorizations(IBankingUnitOfWork unitOfWork, UtcTimestamp now)
    {
        int processed = 0;

        foreach (DebitCardAuthorizationRecord authorization in
            unitOfWork.DebitCardAuthorizations.ListExpired(now, BatchSize))
        {
            DebitCardAuthorizationStatus next = authorization.Status switch
            {
                DebitCardAuthorizationStatus.Authorized => DebitCardAuthorizationStatus.Expired,
                DebitCardAuthorizationStatus.PartiallyCaptured => DebitCardAuthorizationStatus.Captured,
                _ => authorization.Status,
            };

            if (next == authorization.Status ||
                !DebitCardAuthorizationStatusCatalog.IsAllowed(authorization.Status, next))
            {
                continue;
            }

            ReleaseRemaining(unitOfWork, authorization, now);

            unitOfWork.DebitCardAuthorizations.Update(authorization with
            {
                Status = next,
                CompletedAt = now,
                Version = authorization.Version + 1,
            });

            processed++;
        }

        return processed;
    }

    private static void ReleaseRemaining(
        IBankingUnitOfWork unitOfWork,
        DebitCardAuthorizationRecord authorization,
        UtcTimestamp now)
    {
        if (authorization.HoldId is not { } holdId ||
            unitOfWork.Holds.Find(holdId) is not { Status: HoldStatus.Active } hold)
        {
            return;
        }

        MoneyMinor remaining = hold.Remaining;

        hold.Release(now);
        unitOfWork.Holds.Update(hold);

        ReduceHeld(unitOfWork, hold.DepositAccountId, remaining, now);
    }

    private static int ExpireMandates(IBankingUnitOfWork unitOfWork, UtcTimestamp now)
    {
        int processed = 0;

        foreach (DirectDebitMandate mandate in
            unitOfWork.PaymentManagement.ListExpiredMandates(now, BatchSize))
        {
            mandate.Expire(now);
            unitOfWork.PaymentManagement.UpdateMandate(mandate);
            processed++;
        }

        return processed;
    }

    private static int ExpireStandaloneHolds(IBankingUnitOfWork unitOfWork, UtcTimestamp now)
    {
        int processed = 0;

        foreach (Hold hold in unitOfWork.Holds.ListExpiredStandalone(now, BatchSize))
        {
            MoneyMinor remaining = hold.Remaining;

            hold.Expire(now);
            unitOfWork.Holds.Update(hold);

            ReduceHeld(unitOfWork, hold.DepositAccountId, remaining, now);
            processed++;
        }

        return processed;
    }

    private static void ReduceHeld(
        IBankingUnitOfWork unitOfWork,
        DepositAccountId? depositAccountId,
        MoneyMinor amount,
        UtcTimestamp now)
    {
        if (depositAccountId is not { } accountId ||
            amount.IsZero ||
            unitOfWork.DepositAccounts.Find(accountId) is not { } account ||
            unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId) is not { } balance)
        {
            return;
        }

        unitOfWork.LedgerAccounts.UpsertProjection(
            account.LedgerAccountId, balance.DecreaseHold(amount), now);
    }
}
