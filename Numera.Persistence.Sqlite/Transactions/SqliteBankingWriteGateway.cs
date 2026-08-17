using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork : IBankingUnitOfWork
{
    private readonly SqliteUnitOfWork unitOfWork;

    private IPartyRepository? parties;
    private ICustomerAccountRepository? customerAccounts;
    private IDiscordIdentityLinkRepository? discordIdentityLinks;
    private IBusinessOperationRepository? businessOperations;
    private IOutboxRepository? outbox;
    private IInteractionSessionRepository? interactionSessions;
    private IBankRepository? banks;
    private IBankCustomerRelationshipRepository? relationships;
    private ILedgerAccountRepository? ledgerAccounts;
    private IDepositAccountRepository? depositAccounts;
    private IAccountProductRepository? accountProducts;
    private IBranchRepository? branches;
    private IAccountingPeriodRepository? accountingPeriods;
    private IAccountingTransactionRepository? accountingTransactions;
    private IHoldRepository? holds;
    private IPaymentOrderRepository? paymentOrders;
    private IEconomyCalendarRepository? economyCalendars;
    private IFeeScheduleRepository? feeSchedules;
    private IFeeWaiverCounterRepository? feeWaiverCounters;
    private IFeeAssessmentRepository? feeAssessments;
    private IBankPolicyRepository? bankPolicies;
    private IAccountLimitPreferenceRepository? accountLimitPreferences;
    private ISettlementInstructionRepository? settlementInstructions;
    private ISettlementParticipationRepository? settlementParticipations;
    private ICentralBankSettlementAccountRepository? centralBankSettlementAccounts;
    private IPaymentPreferenceRepository? paymentPreferences;
    private IPaymentNetworkRepository? paymentNetworks;
    private IClearingRepository? clearing;
    private ISystemOwnerRepository? systemOwners;
    private IGuildEconomyRepository? guildEconomies;

    internal SqliteBankingUnitOfWork(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public IPartyRepository Parties => parties ??= new SqlitePartyRepository(unitOfWork);

    public ICustomerAccountRepository CustomerAccounts =>
        customerAccounts ??= new SqliteCustomerAccountRepository(unitOfWork);

    public IDiscordIdentityLinkRepository DiscordIdentityLinks =>
        discordIdentityLinks ??= new SqliteDiscordIdentityLinkRepository(unitOfWork);

    public IBusinessOperationRepository BusinessOperations =>
        businessOperations ??= new SqliteBusinessOperationRepository(unitOfWork);

    public IOutboxRepository Outbox => outbox ??= new SqliteOutboxRepository(unitOfWork);

    public IInteractionSessionRepository InteractionSessions =>
        interactionSessions ??= new SqliteInteractionSessionRepository(unitOfWork);

    public IBankRepository Banks => banks ??= new SqliteBankRepository(unitOfWork);

    public IBankCustomerRelationshipRepository Relationships =>
        relationships ??= new SqliteBankCustomerRelationshipRepository(unitOfWork);

    public ILedgerAccountRepository LedgerAccounts =>
        ledgerAccounts ??= new SqliteLedgerAccountRepository(unitOfWork);

    public IDepositAccountRepository DepositAccounts =>
        depositAccounts ??= new SqliteDepositAccountRepository(unitOfWork);

    public IAccountProductRepository AccountProducts =>
        accountProducts ??= new SqliteAccountProductRepository(unitOfWork);

    public IBranchRepository Branches => branches ??= new SqliteBranchRepository(unitOfWork);

    public IAccountingPeriodRepository AccountingPeriods =>
        accountingPeriods ??= new SqliteAccountingPeriodRepository(unitOfWork);

    public IAccountingTransactionRepository AccountingTransactions =>
        accountingTransactions ??= new SqliteAccountingTransactionRepository(unitOfWork);

    public IHoldRepository Holds => holds ??= new SqliteHoldRepository(unitOfWork);

    public IPaymentOrderRepository PaymentOrders =>
        paymentOrders ??= new SqlitePaymentOrderRepository(unitOfWork);

    public IEconomyCalendarRepository EconomyCalendars =>
        economyCalendars ??= new SqliteEconomyCalendarRepository(unitOfWork);

    public IFeeScheduleRepository FeeSchedules => feeSchedules ??= new SqliteFeeScheduleRepository(unitOfWork);

    public IFeeWaiverCounterRepository FeeWaiverCounters =>
        feeWaiverCounters ??= new SqliteFeeWaiverCounterRepository(unitOfWork);

    public IFeeAssessmentRepository FeeAssessments =>
        feeAssessments ??= new SqliteFeeAssessmentRepository(unitOfWork);

    public IBankPolicyRepository BankPolicies => bankPolicies ??= new SqliteBankPolicyRepository(unitOfWork);

    public IAccountLimitPreferenceRepository AccountLimitPreferences =>
        accountLimitPreferences ??= new SqliteAccountLimitPreferenceRepository(unitOfWork);

    public ISettlementInstructionRepository SettlementInstructions =>
        settlementInstructions ??= new SqliteSettlementInstructionRepository(unitOfWork);

    public ISettlementParticipationRepository SettlementParticipations =>
        settlementParticipations ??= new SqliteSettlementParticipationRepository(unitOfWork);

    public ICentralBankSettlementAccountRepository CentralBankSettlementAccounts =>
        centralBankSettlementAccounts ??= new SqliteCentralBankSettlementAccountRepository(unitOfWork);

    public IPaymentPreferenceRepository PaymentPreferences =>
        paymentPreferences ??= new SqlitePaymentPreferenceRepository(unitOfWork);

    public IPaymentNetworkRepository PaymentNetworks =>
        paymentNetworks ??= new SqlitePaymentNetworkRepository(unitOfWork);

    public IClearingRepository Clearing => clearing ??= new SqliteClearingRepository(unitOfWork);

    public ISystemOwnerRepository SystemOwners => systemOwners ??= new SqliteSystemOwnerRepository(unitOfWork);

    public IGuildEconomyRepository GuildEconomies =>
        guildEconomies ??= new SqliteGuildEconomyRepository(unitOfWork);
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
