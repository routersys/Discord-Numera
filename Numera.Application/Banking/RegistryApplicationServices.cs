using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record GetLoanProductsQuery(ulong GuildId, string InstitutionCode);

public sealed record ApplyLoanCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DisbursementDepositAccountId,
    string InstitutionCode,
    string ProductCode,
    long PrincipalMinor);

public sealed record LoanProductItem(string ProductCode, string Name, int AnnualRatePpt);

public sealed record LoanProductPageView(IReadOnlyList<LoanProductItem> Items, string? NextCursor);

public sealed record LoanApplicationView(
    LoanContractId Id,
    LoanContractStatus Status,
    MoneyMinor Principal);

public interface ILoanApplicationService
{
    Task<Result<LoanProductPageView>> GetLoanProductsAsync(
        GetLoanProductsQuery query,
        CancellationToken cancellationToken);

    Task<Result<LoanApplicationView>> ApplyLoanAsync(
        ApplyLoanCommand command,
        CancellationToken cancellationToken);
}

public sealed class LoanApplicationService : ILoanApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public LoanApplicationService(
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

    public Task<Result<LoanProductPageView>> GetLoanProductsAsync(
        GetLoanProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                if (unitOfWork.GuildEconomies.FindEconomyScope(query.GuildId) is not { } scope)
                {
                    return Result<LoanProductPageView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
                }

                if (unitOfWork.Banks.FindByInstitutionCode(scope, query.InstitutionCode) is not { } bank)
                {
                    return Result<LoanProductPageView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
                }

                IReadOnlyList<LoanProductRecord> products =
                    unitOfWork.Governance.ListLoanProducts(bank.Id, PaginationBudget.ListPageSize);

                return Result<LoanProductPageView>.Success(new LoanProductPageView(
                    [
                        .. products.Select(static product => new LoanProductItem(
                            product.ProductCode, product.Name, product.AnnualRatePpt)),
                    ],
                    null));
            },
            cancellationToken);
    }

    public Task<Result<LoanApplicationView>> ApplyLoanAsync(
        ApplyLoanCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => LoanOriginationService.Originate(unitOfWork, clock, idGenerator, command),
            cancellationToken);
    }
}

public sealed record GrantMerchantOperatorCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    ulong TargetDiscordUserId,
    bool ManageCatalog,
    bool ManagePaymentPolicy,
    bool ManageRefunds,
    bool ManageReturns,
    bool ManageSettlementAccount);

public sealed record RevokeMerchantOperatorCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    ulong TargetDiscordUserId);

public sealed record MerchantOperatorGrantView(
    MerchantOperatorGrantId Id,
    MerchantProfileId MerchantProfileId,
    ulong DiscordUserId,
    MerchantOperatorGrantStatus Status);

public interface IMerchantOperatorGrantApplicationService
{
    Task<Result<MerchantOperatorGrantView>> GrantAsync(
        GrantMerchantOperatorCommand command,
        CancellationToken cancellationToken);

    Task<Result> RevokeAsync(
        RevokeMerchantOperatorCommand command,
        CancellationToken cancellationToken);
}

public sealed class MerchantOperatorGrantApplicationService : IMerchantOperatorGrantApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public MerchantOperatorGrantApplicationService(
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

    public Task<Result<MerchantOperatorGrantView>> GrantAsync(
        GrantMerchantOperatorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Grant(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> RevokeAsync(
        RevokeMerchantOperatorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Revoke(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<MerchantOperatorGrantView> Grant(
        IBankingUnitOfWork unitOfWork,
        GrantMerchantOperatorCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<MerchantOperatorGrantView>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindMerchantProfileStatus(command.MerchantProfileId) is not { } status)
        {
            return Result<MerchantOperatorGrantView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        if (status is MerchantProfileStatus.Closing or MerchantProfileStatus.Closed)
        {
            return Result<MerchantOperatorGrantView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProfileNotManageable);
        }

        string target = command.TargetDiscordUserId.ToString(CultureInfo.InvariantCulture);

        if (unitOfWork.Governance.FindActiveMerchantOperatorGrant(
                command.MerchantProfileId, target) is not null)
        {
            return Result<MerchantOperatorGrantView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantOperatorGrantAlreadyActive);
        }

        MerchantOperatorGrantRecord grant = new(
            MerchantOperatorGrantId.FromValue(idGenerator.NextId()),
            command.MerchantProfileId,
            target,
            command.ManageCatalog,
            command.ManagePaymentPolicy,
            command.ManageRefunds,
            command.ManageReturns,
            command.ManageSettlementAccount,
            MerchantOperatorGrantStatus.Active,
            1);

        MerchantOperatorGrantStatusCatalog.EnsureCreatable(grant.Status);
        unitOfWork.Governance.AddMerchantOperatorGrant(grant);

        return Result<MerchantOperatorGrantView>.Success(new MerchantOperatorGrantView(
            grant.Id, grant.MerchantProfileId, command.TargetDiscordUserId, grant.Status));
    }

    private static Result<bool> Revoke(
        IBankingUnitOfWork unitOfWork,
        RevokeMerchantOperatorCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        string target = command.TargetDiscordUserId.ToString(CultureInfo.InvariantCulture);

        if (unitOfWork.Governance.FindActiveMerchantOperatorGrant(
                command.MerchantProfileId, target) is not { } grant)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantOperatorGrantNotFound);
        }

        MerchantOperatorGrantStatusCatalog.EnsureTransition(
            grant.Status, MerchantOperatorGrantStatus.Revoked);

        unitOfWork.Governance.UpdateMerchantOperatorGrant(grant with
        {
            Status = MerchantOperatorGrantStatus.Revoked,
            Version = grant.Version + 1,
        });

        return Result<bool>.Success(true);
    }
}
