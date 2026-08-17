using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed class SqliteBankingUnitOfWork : IBankingUnitOfWork
{
    internal SqliteBankingUnitOfWork(SqliteUnitOfWork unitOfWork)
    {
        Parties = new SqlitePartyRepository(unitOfWork);
        CustomerAccounts = new SqliteCustomerAccountRepository(unitOfWork);
        DiscordIdentityLinks = new SqliteDiscordIdentityLinkRepository(unitOfWork);
        BusinessOperations = new SqliteBusinessOperationRepository(unitOfWork);
        Outbox = new SqliteOutboxRepository(unitOfWork);
        InteractionSessions = new SqliteInteractionSessionRepository(unitOfWork);
        Banks = new SqliteBankRepository(unitOfWork);
        Relationships = new SqliteBankCustomerRelationshipRepository(unitOfWork);
        LedgerAccounts = new SqliteLedgerAccountRepository(unitOfWork);
        DepositAccounts = new SqliteDepositAccountRepository(unitOfWork);
        AccountProducts = new SqliteAccountProductRepository(unitOfWork);
        Branches = new SqliteBranchRepository(unitOfWork);
        AccountingPeriods = new SqliteAccountingPeriodRepository(unitOfWork);
        AccountingTransactions = new SqliteAccountingTransactionRepository(unitOfWork);
        Holds = new SqliteHoldRepository(unitOfWork);
        PaymentOrders = new SqlitePaymentOrderRepository(unitOfWork);
        EconomyCalendars = new SqliteEconomyCalendarRepository(unitOfWork);
        FeeSchedules = new SqliteFeeScheduleRepository(unitOfWork);
        FeeWaiverCounters = new SqliteFeeWaiverCounterRepository(unitOfWork);
        FeeAssessments = new SqliteFeeAssessmentRepository(unitOfWork);
        BankPolicies = new SqliteBankPolicyRepository(unitOfWork);
        AccountLimitPreferences = new SqliteAccountLimitPreferenceRepository(unitOfWork);
        SettlementInstructions = new SqliteSettlementInstructionRepository(unitOfWork);
        SettlementParticipations = new SqliteSettlementParticipationRepository(unitOfWork);
        CentralBankSettlementAccounts = new SqliteCentralBankSettlementAccountRepository(unitOfWork);
        PaymentPreferences = new SqlitePaymentPreferenceRepository(unitOfWork);
        PaymentNetworks = new SqlitePaymentNetworkRepository(unitOfWork);
    }

    public IPartyRepository Parties { get; }

    public ICustomerAccountRepository CustomerAccounts { get; }

    public IDiscordIdentityLinkRepository DiscordIdentityLinks { get; }

    public IBusinessOperationRepository BusinessOperations { get; }

    public IOutboxRepository Outbox { get; }

    public IInteractionSessionRepository InteractionSessions { get; }

    public IBankRepository Banks { get; }

    public IBankCustomerRelationshipRepository Relationships { get; }

    public ILedgerAccountRepository LedgerAccounts { get; }

    public IDepositAccountRepository DepositAccounts { get; }

    public IAccountProductRepository AccountProducts { get; }

    public IBranchRepository Branches { get; }

    public IAccountingPeriodRepository AccountingPeriods { get; }

    public IAccountingTransactionRepository AccountingTransactions { get; }

    public IHoldRepository Holds { get; }

    public IPaymentOrderRepository PaymentOrders { get; }

    public IEconomyCalendarRepository EconomyCalendars { get; }

    public IFeeScheduleRepository FeeSchedules { get; }

    public IFeeWaiverCounterRepository FeeWaiverCounters { get; }

    public IFeeAssessmentRepository FeeAssessments { get; }

    public IBankPolicyRepository BankPolicies { get; }

    public IAccountLimitPreferenceRepository AccountLimitPreferences { get; }

    public ISettlementInstructionRepository SettlementInstructions { get; }

    public ISettlementParticipationRepository SettlementParticipations { get; }

    public ICentralBankSettlementAccountRepository CentralBankSettlementAccounts { get; }

    public IPaymentPreferenceRepository PaymentPreferences { get; }

    public IPaymentNetworkRepository PaymentNetworks { get; }
}

public sealed class SqliteBankingWriteGateway : IBankingWriteGateway
{
    private readonly FinancialWriteCoordinator coordinator;

    public SqliteBankingWriteGateway(FinancialWriteCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        this.coordinator = coordinator;
    }

    public async Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IBankingUnitOfWork, Result<TValue>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        WriteOutcome<Result<TValue>> outcome = await coordinator.ExecuteWithDecisionAsync(
            unitOfWork =>
            {
                Result<TValue> result = operation(new SqliteBankingUnitOfWork(unitOfWork));

                return result.IsSuccess
                    ? WriteDecision<Result<TValue>>.Commit(result)
                    : WriteDecision<Result<TValue>>.Rollback(result);
            },
            cancellationToken).ConfigureAwait(false);

        return outcome.Status switch
        {
            WriteOutcomeStatus.Committed or WriteOutcomeStatus.RolledBack => outcome.Value,
            WriteOutcomeStatus.RejectedSystemBusy => Result<TValue>.Failure(
                ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.SystemBusy),
            _ => Result<TValue>.Failure(ErrorCategory.OperationExpired, BankingErrorCodes.OperationCancelled),
        };
    }
}
