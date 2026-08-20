using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CreateDepositInsuranceFundCommand(
    AuthorizationContext Actor,
    CurrencyId CurrencyId,
    PartyId OwnerPartyId,
    AccountingBookId AccountingBookId,
    LedgerAccountId CentralBankSettlementLiabilityLedgerAccountId,
    LedgerAccountId LiquidAssetLedgerAccountId,
    LedgerAccountId PremiumRevenueLedgerAccountId,
    LedgerAccountId ClaimExpenseLedgerAccountId);

public sealed record StartDepositInsuranceSchemeDraftCommand(
    AuthorizationContext Actor,
    CurrencyId CurrencyId,
    string ProtectionClassCode,
    DepositInsuranceFundId FundId,
    long CoverageLimitMinor,
    long EnrollmentFeeMinor);

public sealed record UpdateDepositInsuranceSchemeDraftCommand(
    AuthorizationContext Actor,
    DepositInsuranceSchemeId SchemeId,
    DepositInsuranceFundId FundId,
    long CoverageLimitMinor,
    long EnrollmentFeeMinor);

public sealed record PublishDepositInsuranceSchemeCommand(
    AuthorizationContext Actor,
    DepositInsuranceSchemeId SchemeId);

public sealed record SuspendDepositInsuranceSchemeCommand(
    AuthorizationContext Actor,
    DepositInsuranceSchemeId SchemeId);

public sealed record ResumeDepositInsuranceSchemeCommand(
    AuthorizationContext Actor,
    DepositInsuranceSchemeId SchemeId);

public sealed record RetireDepositInsuranceSchemeCommand(
    AuthorizationContext Actor,
    DepositInsuranceSchemeId SchemeId);

public sealed record DepositInsuranceFundView(
    DepositInsuranceFundId Id,
    CurrencyId CurrencyId,
    DepositInsuranceFundStatus Status);

public sealed record DepositInsuranceSchemeDraftView(
    DepositInsuranceSchemeId Id,
    CurrencyId CurrencyId,
    string ProtectionClassCode,
    DepositInsuranceFundId FundId,
    MoneyMinor CoverageLimit,
    MoneyMinor EnrollmentFee,
    DepositInsuranceSchemeStatus Status);

public sealed record DepositInsuranceSchemeVersionView(
    DepositInsuranceSchemeVersionId Id,
    DepositInsuranceSchemeId SchemeId,
    MoneyMinor CoverageLimit,
    MoneyMinor EnrollmentFee,
    long Version);

public interface IDepositInsuranceAdministrationApplicationService
{
    Task<Result<DepositInsuranceFundView>> CreateFundAsync(
        CreateDepositInsuranceFundCommand command,
        CancellationToken cancellationToken);

    Task<Result<DepositInsuranceSchemeDraftView>> StartDraftAsync(
        StartDepositInsuranceSchemeDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<DepositInsuranceSchemeDraftView>> UpdateDraftAsync(
        UpdateDepositInsuranceSchemeDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<DepositInsuranceSchemeVersionView>> PublishAsync(
        PublishDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken);

    Task<Result> SuspendSchemeAsync(
        SuspendDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken);

    Task<Result> ResumeSchemeAsync(
        ResumeDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken);

    Task<Result> RetireAsync(
        RetireDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken);
}

public sealed class DepositInsuranceAdministrationApplicationService
    : IDepositInsuranceAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public DepositInsuranceAdministrationApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<DepositInsuranceFundView>> CreateFundAsync(
        CreateDepositInsuranceFundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateFund(unitOfWork, command), cancellationToken);
    }

    public Task<Result<DepositInsuranceSchemeDraftView>> StartDraftAsync(
        StartDepositInsuranceSchemeDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<DepositInsuranceSchemeDraftView>> UpdateDraftAsync(
        UpdateDepositInsuranceSchemeDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<DepositInsuranceSchemeVersionView>> PublishAsync(
        PublishDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Publish(unitOfWork, command), cancellationToken);
    }

    public Task<Result> SuspendSchemeAsync(
        SuspendDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeStateAsync(
            command.Actor, command.SchemeId, DepositInsuranceSchemeStatus.Suspended, cancellationToken);
    }

    public Task<Result> ResumeSchemeAsync(
        ResumeDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeStateAsync(
            command.Actor, command.SchemeId, DepositInsuranceSchemeStatus.Active, cancellationToken);
    }

    public Task<Result> RetireAsync(
        RetireDepositInsuranceSchemeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeStateAsync(
            command.Actor, command.SchemeId, DepositInsuranceSchemeStatus.Retired, cancellationToken);
    }

    private async Task<Result> ChangeStateAsync(
        AuthorizationContext actor,
        DepositInsuranceSchemeId schemeId,
        DepositInsuranceSchemeStatus target,
        CancellationToken cancellationToken)
    {
        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => ChangeState(unitOfWork, actor, schemeId, target), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<DepositInsuranceFundView> CreateFund(
        IBankingUnitOfWork unitOfWork,
        CreateDepositInsuranceFundCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<DepositInsuranceFundView>.Failure(scope.Error!);
        }

        if (unitOfWork.DepositInsurance.FindFundByCurrency(scope.Value, command.CurrencyId) is not null)
        {
            return Result<DepositInsuranceFundView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundAlreadyExists);
        }

        if (unitOfWork.Parties.Find(command.OwnerPartyId) is null)
        {
            return Result<DepositInsuranceFundView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.PartyNotFound);
        }

        LedgerAccountId[] accounts =
        [
            command.CentralBankSettlementLiabilityLedgerAccountId,
            command.LiquidAssetLedgerAccountId,
            command.PremiumRevenueLedgerAccountId,
            command.ClaimExpenseLedgerAccountId,
        ];

        foreach (LedgerAccountId accountId in accounts)
        {
            if (unitOfWork.LedgerAccounts.Find(accountId) is not { } account ||
                account.CurrencyId != command.CurrencyId ||
                !account.PostingAllowed)
            {
                return Result<DepositInsuranceFundView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundAccountInvalid);
            }

            bool central = accountId == command.CentralBankSettlementLiabilityLedgerAccountId;

            if (central
                ? account.BookId == command.AccountingBookId ||
                    account.Kind != LedgerAccountKind.CentralBankSettlementLiability
                : account.BookId != command.AccountingBookId)
            {
                return Result<DepositInsuranceFundView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundAccountInvalid);
            }
        }

        if (accounts.Distinct().Count() != accounts.Length)
        {
            return Result<DepositInsuranceFundView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundAccountInvalid);
        }

        DepositInsuranceFundRecord fund = new(
            DepositInsuranceFundId.FromValue(idGenerator.NextId()),
            scope.Value,
            command.CurrencyId,
            command.OwnerPartyId,
            command.AccountingBookId,
            command.CentralBankSettlementLiabilityLedgerAccountId,
            command.LiquidAssetLedgerAccountId,
            command.PremiumRevenueLedgerAccountId,
            command.ClaimExpenseLedgerAccountId,
            DepositInsuranceFundStatus.Active,
            clock.Now(),
            VersionedEntity.InitialVersion);

        DepositInsuranceFundStatusCatalog.EnsureCreatable(fund.Status);
        unitOfWork.DepositInsurance.AddFund(fund);

        return Result<DepositInsuranceFundView>.Success(
            new DepositInsuranceFundView(fund.Id, fund.CurrencyId, fund.Status));
    }

    private Result<DepositInsuranceSchemeDraftView> StartDraft(
        IBankingUnitOfWork unitOfWork,
        StartDepositInsuranceSchemeDraftCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(scope.Error!);
        }

        if (!IsProtectionClass(command.ProtectionClassCode))
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.DepositInsuranceProtectionClassInvalid,
                nameof(command.ProtectionClassCode));
        }

        if (command.CoverageLimitMinor <= 0 || command.EnrollmentFeeMinor < 0)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DepositInsuranceCoverageInvalid);
        }

        if (unitOfWork.DepositInsurance.FindFund(command.FundId) is not { } fund ||
            fund.Status != DepositInsuranceFundStatus.Active ||
            fund.CurrencyId != command.CurrencyId)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceFundNotFound);
        }

        if (unitOfWork.DepositInsurance.FindSchemeByClass(
                scope.Value, command.CurrencyId, command.ProtectionClassCode) is not null)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceSchemeAlreadyExists);
        }

        UtcTimestamp now = clock.Now();

        DepositInsuranceSchemeRecord scheme = new(
            DepositInsuranceSchemeId.FromValue(idGenerator.NextId()),
            scope.Value,
            command.CurrencyId,
            command.ProtectionClassCode,
            DepositInsuranceSchemeStatus.Draft,
            null,
            now,
            VersionedEntity.InitialVersion);

        DepositInsuranceSchemeStatusCatalog.EnsureCreatable(scheme.Status);
        unitOfWork.DepositInsurance.AddScheme(scheme);

        DepositInsuranceSchemeVersionRecord version = new(
            DepositInsuranceSchemeVersionId.FromValue(idGenerator.NextId()),
            scheme.Id,
            fund.Id,
            MoneyMinor.FromPositiveMinor(command.CoverageLimitMinor),
            MoneyMinor.FromMinor(command.EnrollmentFeeMinor),
            now,
            VersionedEntity.InitialVersion);

        unitOfWork.DepositInsurance.AddSchemeVersion(version);

        return Result<DepositInsuranceSchemeDraftView>.Success(new DepositInsuranceSchemeDraftView(
            scheme.Id,
            scheme.CurrencyId,
            scheme.ProtectionClassCode,
            fund.Id,
            version.CoverageLimit,
            version.EnrollmentFee,
            scheme.Status));
    }

    private Result<DepositInsuranceSchemeDraftView> UpdateDraft(
        IBankingUnitOfWork unitOfWork,
        UpdateDepositInsuranceSchemeDraftCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(scope.Error!);
        }

        if (command.CoverageLimitMinor <= 0 || command.EnrollmentFeeMinor < 0)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DepositInsuranceCoverageInvalid);
        }

        if (unitOfWork.DepositInsurance.FindScheme(command.SchemeId) is not { } scheme)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeNotFound);
        }

        if (scheme.Status != DepositInsuranceSchemeStatus.Draft)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceSchemeNotDraft);
        }

        if (unitOfWork.DepositInsurance.FindFund(command.FundId) is not { } fund ||
            fund.Status != DepositInsuranceFundStatus.Active ||
            fund.CurrencyId != scheme.CurrencyId)
        {
            return Result<DepositInsuranceSchemeDraftView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceFundNotFound);
        }

        DepositInsuranceSchemeVersionRecord version = new(
            DepositInsuranceSchemeVersionId.FromValue(idGenerator.NextId()),
            scheme.Id,
            fund.Id,
            MoneyMinor.FromPositiveMinor(command.CoverageLimitMinor),
            MoneyMinor.FromMinor(command.EnrollmentFeeMinor),
            clock.Now(),
            unitOfWork.DepositInsurance.NextSchemeVersion(scheme.Id));

        unitOfWork.DepositInsurance.AddSchemeVersion(version);

        return Result<DepositInsuranceSchemeDraftView>.Success(new DepositInsuranceSchemeDraftView(
            scheme.Id,
            scheme.CurrencyId,
            scheme.ProtectionClassCode,
            fund.Id,
            version.CoverageLimit,
            version.EnrollmentFee,
            scheme.Status));
    }

    private static Result<DepositInsuranceSchemeVersionView> Publish(
        IBankingUnitOfWork unitOfWork,
        PublishDepositInsuranceSchemeCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<DepositInsuranceSchemeVersionView>.Failure(scope.Error!);
        }

        if (unitOfWork.DepositInsurance.FindScheme(command.SchemeId) is not { } scheme)
        {
            return Result<DepositInsuranceSchemeVersionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeNotFound);
        }

        if (scheme.Status is not (DepositInsuranceSchemeStatus.Draft
            or DepositInsuranceSchemeStatus.Active))
        {
            return Result<DepositInsuranceSchemeVersionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceSchemeNotDraft);
        }

        long latest = unitOfWork.DepositInsurance.NextSchemeVersion(scheme.Id) - 1;

        if (latest < VersionedEntity.InitialVersion ||
            unitOfWork.DepositInsurance.FindSchemeVersionByNumber(scheme.Id, latest) is not { } version)
        {
            return Result<DepositInsuranceSchemeVersionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeVersionNotFound);
        }

        if (unitOfWork.DepositInsurance.FindFund(version.FundId) is not
            { Status: DepositInsuranceFundStatus.Active })
        {
            return Result<DepositInsuranceSchemeVersionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        if (scheme.Status == DepositInsuranceSchemeStatus.Draft)
        {
            DepositInsuranceSchemeStatusCatalog.EnsureTransition(
                scheme.Status, DepositInsuranceSchemeStatus.Active);
        }

        unitOfWork.DepositInsurance.UpdateScheme(scheme with
        {
            Status = DepositInsuranceSchemeStatus.Active,
            CurrentVersionId = version.Id,
            Version = scheme.Version + 1,
        });

        return Result<DepositInsuranceSchemeVersionView>.Success(new DepositInsuranceSchemeVersionView(
            version.Id, version.SchemeId, version.CoverageLimit, version.EnrollmentFee, version.Version));
    }

    private static Result<bool> ChangeState(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        DepositInsuranceSchemeId schemeId,
        DepositInsuranceSchemeStatus target)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (unitOfWork.DepositInsurance.FindScheme(schemeId) is not { } scheme)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeNotFound);
        }

        if (!DepositInsuranceSchemeStatusCatalog.IsAllowed(scheme.Status, target))
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceSchemeStateInvalid);
        }

        DepositInsuranceSchemeStatusCatalog.EnsureTransition(scheme.Status, target);

        unitOfWork.DepositInsurance.UpdateScheme(scheme with
        {
            Status = target,
            Version = scheme.Version + 1,
        });

        return Result<bool>.Success(true);
    }

    private static bool IsProtectionClass(string value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= 32 &&
        value.All(static character =>
            char.IsAsciiDigit(character) || char.IsAsciiLetterUpper(character) || character == '_');
}

public sealed record GetDepositInsuranceOptionsQuery(
    AuthorizationContext Actor,
    DepositAccountId DepositAccountId);

public sealed record EnrollDepositInsuranceCommand(
    AuthorizationContext Actor,
    DepositAccountId DepositAccountId,
    string ProtectionClassCode,
    string IdempotencyToken);

public sealed record CancelDepositInsuranceCommand(
    AuthorizationContext Actor,
    DepositAccountId DepositAccountId);

public sealed record GetDepositInsuranceClaimsQuery(AuthorizationContext Actor, string? Cursor);

public sealed record GetInsuranceSettlementWalletQuery(
    AuthorizationContext Actor,
    CurrencyId CurrencyId);

public sealed record TransferInsuranceSettlementWalletCommand(
    AuthorizationContext Actor,
    InsuranceSettlementWalletId InsuranceSettlementWalletId,
    DepositAccountId DestinationDepositAccountId,
    long AmountMinor,
    string IdempotencyToken);

public sealed record DepositInsuranceOptionItem(
    DepositInsuranceSchemeId SchemeId,
    string ProtectionClassCode,
    MoneyMinor CoverageLimit,
    MoneyMinor EnrollmentFee,
    DepositInsuranceSchemeStatus Status);

public sealed record DepositInsuranceOptionsView(
    DepositAccountId DepositAccountId,
    CurrencyId CurrencyId,
    bool Enrolled,
    IReadOnlyList<DepositInsuranceOptionItem> Options);

public sealed record DepositInsuranceEnrollmentView(
    DepositInsuranceEnrollmentId Id,
    DepositAccountId DepositAccountId,
    string ProtectionClassCode,
    MoneyMinor CoverageLimit,
    DepositInsuranceEnrollmentStatus Status);

public sealed record DepositInsuranceClaimItem(
    DepositInsuranceClaimId Id,
    BankId BankId,
    CurrencyId CurrencyId,
    MoneyMinor Insured,
    MoneyMinor Paid,
    DepositInsuranceClaimStatus Status);

public sealed record DepositInsuranceClaimPageView(
    IReadOnlyList<DepositInsuranceClaimItem> Items,
    string? NextCursor);

public sealed record InsuranceSettlementWalletView(
    InsuranceSettlementWalletId Id,
    CurrencyId CurrencyId,
    MoneyMinor Balance,
    InsuranceSettlementWalletStatus Status);

public sealed record InsuranceSettlementWalletPayoutView(
    InsuranceSettlementWalletPayoutId Id,
    InsuranceSettlementWalletId InsuranceSettlementWalletId,
    DepositAccountId DestinationDepositAccountId,
    MoneyMinor Amount);

public interface IDepositInsuranceApplicationService
{
    Task<Result<DepositInsuranceOptionsView>> GetOptionsAsync(
        GetDepositInsuranceOptionsQuery query,
        CancellationToken cancellationToken);

    Task<Result<DepositInsuranceEnrollmentView>> EnrollAsync(
        EnrollDepositInsuranceCommand command,
        CancellationToken cancellationToken);

    Task<Result> CancelAsync(
        CancelDepositInsuranceCommand command,
        CancellationToken cancellationToken);

    Task<Result<DepositInsuranceClaimPageView>> GetClaimsAsync(
        GetDepositInsuranceClaimsQuery query,
        CancellationToken cancellationToken);

    Task<Result<InsuranceSettlementWalletView>> GetSettlementWalletAsync(
        GetInsuranceSettlementWalletQuery query,
        CancellationToken cancellationToken);

    Task<Result<InsuranceSettlementWalletPayoutView>> TransferSettlementWalletAsync(
        TransferInsuranceSettlementWalletCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class DepositInsuranceApplicationService : IDepositInsuranceApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public DepositInsuranceApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<DepositInsuranceOptionsView>> GetOptionsAsync(
        GetDepositInsuranceOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => GetOptions(unitOfWork, query), cancellationToken);
    }

    public Task<Result<DepositInsuranceEnrollmentView>> EnrollAsync(
        EnrollDepositInsuranceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Enroll(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> CancelAsync(
        CancelDepositInsuranceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Cancel(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    public Task<Result<DepositInsuranceClaimPageView>> GetClaimsAsync(
        GetDepositInsuranceClaimsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => GetClaims(unitOfWork, query), cancellationToken);
    }

    public Task<Result<InsuranceSettlementWalletView>> GetSettlementWalletAsync(
        GetInsuranceSettlementWalletQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => GetWallet(unitOfWork, query), cancellationToken);
    }

    public Task<Result<InsuranceSettlementWalletPayoutView>> TransferSettlementWalletAsync(
        TransferInsuranceSettlementWalletCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Transfer(unitOfWork, command), cancellationToken);
    }

    private static Result<DepositInsuranceOptionsView> GetOptions(
        IBankingUnitOfWork unitOfWork,
        GetDepositInsuranceOptionsQuery query)
    {
        Result<DepositAccount> owned = ResolveOwnedAccount(
            unitOfWork, query.Actor, query.DepositAccountId);

        if (!owned.IsSuccess)
        {
            return Result<DepositInsuranceOptionsView>.Failure(owned.Error!);
        }

        DepositAccount account = owned.Value;

        if (unitOfWork.Banks.Find(account.BankId) is not { } bank)
        {
            return Result<DepositInsuranceOptionsView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        List<DepositInsuranceOptionItem> options = [];

        foreach (DepositInsuranceSchemeRecord scheme in unitOfWork.DepositInsurance.ListSchemes(
            bank.EconomyScopeId, account.CurrencyId, PaginationBudget.ListPageSize))
        {
            if (scheme.CurrentVersionId is not { } versionId ||
                unitOfWork.DepositInsurance.FindSchemeVersion(versionId) is not { } version)
            {
                continue;
            }

            options.Add(new DepositInsuranceOptionItem(
                scheme.Id,
                scheme.ProtectionClassCode,
                version.CoverageLimit,
                version.EnrollmentFee,
                scheme.Status));
        }

        return Result<DepositInsuranceOptionsView>.Success(new DepositInsuranceOptionsView(
            account.Id,
            account.CurrencyId,
            unitOfWork.DepositInsurance.FindActiveEnrollment(account.Id) is not null,
            options));
    }

    private Result<DepositInsuranceEnrollmentView> Enroll(
        IBankingUnitOfWork unitOfWork,
        EnrollDepositInsuranceCommand command)
    {
        Result<DepositAccount> owned = ResolveOwnedAccount(
            unitOfWork, command.Actor, command.DepositAccountId);

        if (!owned.IsSuccess)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(owned.Error!);
        }

        DepositAccount account = owned.Value;

        if (account.Status != DepositAccountStatus.Active)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (unitOfWork.DepositInsurance.FindActiveEnrollment(account.Id) is not null)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceAlreadyEnrolled);
        }

        if (unitOfWork.Banks.Find(account.BankId) is not { } bank)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.DepositInsurance.FindSchemeByClass(
                bank.EconomyScopeId, account.CurrencyId, command.ProtectionClassCode) is not { } scheme)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeNotFound);
        }

        if (scheme.Status != DepositInsuranceSchemeStatus.Active ||
            scheme.CurrentVersionId is not { } versionId)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceSchemeStateInvalid);
        }

        if (unitOfWork.DepositInsurance.FindSchemeVersion(versionId) is not { } version)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeVersionNotFound);
        }

        if (unitOfWork.DepositInsurance.FindFund(version.FundId) is not
            { Status: DepositInsuranceFundStatus.Active } fund)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        if (unitOfWork.LedgerAccounts.Find(fund.LiquidAssetLedgerAccountId) is not { } liquidAsset)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        Int128 available =
            (Int128)(unitOfWork.LedgerAccounts.FindProjection(liquidAsset.Id) ?? LedgerBalance.Empty)
                .PostedBalance.Value
            - unitOfWork.DepositInsurance.SumActiveReservationRemaining(fund.Id)
            - unitOfWork.DepositInsurance.SumOutstandingWalletLiability(fund.Id);

        if (available < version.CoverageLimit.Value)
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundCapacityInsufficient);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            EnrollOperationType,
            fund.EconomyScopeId,
            null,
            idGenerator.NextId(),
            IdempotencyKey.Create(EnrollOperationType, command.IdempotencyToken),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        if (version.EnrollmentFee.IsPositive &&
            unitOfWork.Banks.Find(account.BankId) is not { Status: BankStatus.Operating })
        {
            return Result<DepositInsuranceEnrollmentView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        Bank issuingBank = unitOfWork.Banks.Find(account.BankId)!;

        DepositInsurancePremiumPaymentId? premiumPaymentId = null;

        if (version.EnrollmentFee.IsPositive)
        {
            Result<DepositInsurancePremiumPaymentId> premium = PostPremium(
                unitOfWork,
                operation,
                fund,
                account,
                issuingBank,
                version.EnrollmentFee,
                BusinessDateOf(now),
                now);

            if (!premium.IsSuccess)
            {
                return Result<DepositInsuranceEnrollmentView>.Failure(premium.Error!);
            }

            premiumPaymentId = premium.Value;
        }

        DepositInsuranceEnrollmentRecord enrollment = new(
            DepositInsuranceEnrollmentId.FromValue(idGenerator.NextId()),
            account.Id,
            account.CustomerAccountId,
            account.BankId,
            scheme.ProtectionClassCode,
            version.Id,
            version.CoverageLimit,
            version.EnrollmentFee,
            premiumPaymentId,
            DepositInsuranceEnrollmentStatus.Active,
            now,
            null,
            VersionedEntity.InitialVersion);

        DepositInsuranceEnrollmentStatusCatalog.EnsureCreatable(enrollment.Status);
        unitOfWork.DepositInsurance.AddEnrollment(enrollment);

        DepositInsuranceReservationRecord reservation = new(
            DepositInsuranceReservationId.FromValue(idGenerator.NextId()),
            enrollment.Id,
            fund.Id,
            version.CoverageLimit,
            MoneyMinor.Zero,
            MoneyMinor.Zero,
            DepositInsuranceReservationStatus.Active,
            now,
            null,
            VersionedEntity.InitialVersion);

        DepositInsuranceReservationStatusCatalog.EnsureCreatable(reservation.Status);
        unitOfWork.DepositInsurance.AddReservation(reservation);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            EnrolledEventType,
            EnrollmentPayload(enrollment.Id),
            now));

        unitOfWork.OperationResults.Add(new OperationResultRecord(
            OperationResultId.FromValue(idGenerator.NextId()),
            operation.Id,
            EnrollOperationType,
            EnrollmentPayload(enrollment.Id),
            now));

        return Result<DepositInsuranceEnrollmentView>.Success(new DepositInsuranceEnrollmentView(
            enrollment.Id,
            enrollment.DepositAccountId,
            enrollment.ProtectionClassCode,
            enrollment.CoverageLimitSnapshot,
            enrollment.Status));
    }

    private Result<bool> Cancel(
        IBankingUnitOfWork unitOfWork,
        CancelDepositInsuranceCommand command)
    {
        Result<DepositAccount> owned = ResolveOwnedAccount(
            unitOfWork, command.Actor, command.DepositAccountId);

        if (!owned.IsSuccess)
        {
            return Result<bool>.Failure(owned.Error!);
        }

        if (unitOfWork.DepositInsurance.FindActiveEnrollment(
                owned.Value.Id) is not { } enrollment)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceEnrollmentNotFound);
        }

        if (unitOfWork.DepositInsurance.FindReservation(enrollment.Id) is not { } reservation)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceReservationNotFound);
        }

        UtcTimestamp now = clock.Now();

        DepositInsuranceEnrollmentStatusCatalog.EnsureTransition(
            enrollment.Status, DepositInsuranceEnrollmentStatus.Cancelled);
        DepositInsuranceReservationStatusCatalog.EnsureTransition(
            reservation.Status, DepositInsuranceReservationStatus.Settled);

        unitOfWork.DepositInsurance.UpdateReservation(reservation with
        {
            Released = reservation.Reserved.Subtract(reservation.Consumed),
            Status = DepositInsuranceReservationStatus.Settled,
            TerminalAt = now,
            Version = reservation.Version + 1,
        });

        unitOfWork.DepositInsurance.UpdateEnrollment(enrollment with
        {
            Status = DepositInsuranceEnrollmentStatus.Cancelled,
            TerminalAt = now,
            Version = enrollment.Version + 1,
        });

        return Result<bool>.Success(true);
    }

    private static Result<DepositInsuranceClaimPageView> GetClaims(
        IBankingUnitOfWork unitOfWork,
        GetDepositInsuranceClaimsQuery query)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, query.Actor) is not { } customer)
        {
            return Result<DepositInsuranceClaimPageView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        DepositInsuranceClaimId? after = null;

        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!EntityIdValue.TryParse(query.Cursor, out EntityIdValue parsed))
            {
                return Result<DepositInsuranceClaimPageView>.Failure(
                    ErrorCategory.Validation, BankingErrorCodes.PageCursorInvalid, nameof(query.Cursor));
            }

            after = DepositInsuranceClaimId.FromValue(parsed);
        }

        IReadOnlyList<DepositInsuranceClaimRecord> claims = unitOfWork.DepositInsurance.ListClaims(
            customer.Id, after, PaginationBudget.Fetch(PaginationBudget.HistoryPageSize));

        bool hasMore = claims.Count > PaginationBudget.HistoryPageSize;
        List<DepositInsuranceClaimItem> items =
        [
            .. (hasMore ? claims.Take(PaginationBudget.HistoryPageSize) : claims)
                .Select(static claim => new DepositInsuranceClaimItem(
                    claim.Id, claim.BankId, claim.CurrencyId, claim.Insured, claim.Paid, claim.Status)),
        ];

        return Result<DepositInsuranceClaimPageView>.Success(new DepositInsuranceClaimPageView(
            items,
            hasMore && items.Count > 0 ? items[^1].Id.Value.ToString() : null));
    }

    private static Result<InsuranceSettlementWalletView> GetWallet(
        IBankingUnitOfWork unitOfWork,
        GetInsuranceSettlementWalletQuery query)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, query.Actor) is not { } customer)
        {
            return Result<InsuranceSettlementWalletView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.DepositInsurance.FindSettlementWallet(
                customer.Id, query.CurrencyId) is not { } wallet)
        {
            return Result<InsuranceSettlementWalletView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.InsuranceSettlementWalletNotFound);
        }

        LedgerBalance balance =
            unitOfWork.LedgerAccounts.FindProjection(wallet.LiabilityLedgerAccountId)
            ?? LedgerBalance.Empty;

        return Result<InsuranceSettlementWalletView>.Success(new InsuranceSettlementWalletView(
            wallet.Id, wallet.CurrencyId, balance.AvailableBalance, wallet.Status));
    }

    private Result<InsuranceSettlementWalletPayoutView> Transfer(
        IBankingUnitOfWork unitOfWork,
        TransferInsuranceSettlementWalletCommand command)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, command.Actor) is not { } customer)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (command.AmountMinor <= 0)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(command.AmountMinor));
        }

        if (unitOfWork.DepositAccounts.Find(command.DestinationDepositAccountId) is not { } destination ||
            destination.CustomerAccountId != customer.Id)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (unitOfWork.DepositInsurance.FindSettlementWallet(
                customer.Id, destination.CurrencyId) is not { } wallet ||
            wallet.Id != command.InsuranceSettlementWalletId)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.InsuranceSettlementWalletNotFound);
        }

        if (unitOfWork.DepositInsurance.FindFund(wallet.FundId) is not
            { Status: DepositInsuranceFundStatus.Active } fund)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        if (unitOfWork.Banks.Find(destination.BankId) is not
            { Status: BankStatus.Operating } destinationBank)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (destination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            PayoutOperationType,
            fund.EconomyScopeId,
            null,
            idGenerator.NextId(),
            IdempotencyKey.Create(PayoutOperationType, command.IdempotencyToken),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result posted = PayoutWallet(
            unitOfWork,
            operation,
            fund,
            wallet,
            destination,
            destinationBank,
            MoneyMinor.FromMinor(command.AmountMinor),
            BusinessDateOf(now),
            now);

        if (!posted.IsSuccess)
        {
            return Result<InsuranceSettlementWalletPayoutView>.Failure(posted.Error!);
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            PaidOutEventType,
            EnrollmentPayload(wallet.Id.Value),
            now));

        return Result<InsuranceSettlementWalletPayoutView>.Success(
            new InsuranceSettlementWalletPayoutView(
                InsuranceSettlementWalletPayoutId.FromValue(idGenerator.NextId()),
                wallet.Id,
                destination.Id,
                MoneyMinor.FromMinor(command.AmountMinor)));
    }

    internal const string EnrollOperationType = "DEPOSIT_INSURANCE_ENROLL";

    internal const string PayoutOperationType = "DEPOSIT_INSURANCE_PAYOUT";

    internal const string EnrolledEventType = "DEPOSIT_INSURANCE_ENROLLED";

    internal const string PaidOutEventType = "DEPOSIT_INSURANCE_PAID_OUT";

    private static string EnrollmentPayload(DepositInsuranceEnrollmentId id) =>
        EnrollmentPayload(id.Value);

    private static string EnrollmentPayload(Numera.Domain.Common.EntityIdValue id) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $$"""{"id":"{{id}}"}""");

    private static Result<DepositAccount> ResolveOwnedAccount(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        DepositAccountId depositAccountId)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, actor) is not { } customer)
        {
            return Result<DepositAccount>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        return unitOfWork.DepositAccounts.Find(depositAccountId) is { } account &&
            account.CustomerAccountId == customer.Id
            ? Result<DepositAccount>.Success(account)
            : Result<DepositAccount>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
    }
}
