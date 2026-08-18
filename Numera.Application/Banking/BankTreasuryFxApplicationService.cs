using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record ConfigureBankTreasuryFxCurrencyCommand(
    AuthorizationContext Actor,
    BankId BankId,
    CurrencyId CurrencyId,
    LedgerAccountId AssetLedgerAccountId,
    BankTreasuryFxAccountStatus TargetStatus);

public sealed record PlaceBankTreasuryFxOrderCommand(
    AuthorizationContext Actor,
    BankId BankId,
    FxMarketId MarketId,
    FxOrderSide Side,
    FxOrderType OrderType,
    long BaseMinor,
    long? PriceUnits,
    int? MaximumSlippageBps);

public sealed record CancelBankTreasuryFxOrderCommand(
    AuthorizationContext Actor,
    BankId BankId,
    FxOrderId FxOrderId);

public sealed record BankTreasuryFxAccountView(
    BankTreasuryFxAccountId Id,
    BankId BankId,
    CurrencyId CurrencyId,
    LedgerAccountId AssetLedgerAccountId,
    BankTreasuryFxAccountStatus Status);

public interface IBankTreasuryFxApplicationService
{
    Task<Result<BankTreasuryFxAccountView>> ConfigureTreasuryCurrencyAsync(
        ConfigureBankTreasuryFxCurrencyCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxOrderView>> PlaceTreasuryOrderAsync(
        PlaceBankTreasuryFxOrderCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxOrderView>> CancelTreasuryOrderAsync(
        CancelBankTreasuryFxOrderCommand command,
        CancellationToken cancellationToken);
}

public sealed class BankTreasuryFxApplicationService : IBankTreasuryFxApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IIdGenerator idGenerator;

    public BankTreasuryFxApplicationService(
        IBankingWriteGateway writeGateway,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.idGenerator = idGenerator;
    }

    public Task<Result<BankTreasuryFxAccountView>> ConfigureTreasuryCurrencyAsync(
        ConfigureBankTreasuryFxCurrencyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => ConfigureTreasuryCurrency(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxOrderView>> PlaceTreasuryOrderAsync(
        PlaceBankTreasuryFxOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => PlaceTreasuryOrder(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxOrderView>> CancelTreasuryOrderAsync(
        CancelBankTreasuryFxOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => CancelTreasuryOrder(unitOfWork, command), cancellationToken);
    }

    private Result<BankTreasuryFxAccountView> ConfigureTreasuryCurrency(
        IBankingUnitOfWork unitOfWork,
        ConfigureBankTreasuryFxCurrencyCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<BankTreasuryFxAccountView>.Failure(scope.Error!);
        }

        if (unitOfWork.Banks.Find(command.BankId) is not { } bank)
        {
            return Result<BankTreasuryFxAccountView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.LedgerAccounts.Find(command.AssetLedgerAccountId) is not { } asset ||
            asset.BookId != bank.GeneralLedgerBookId ||
            asset.CurrencyId != command.CurrencyId ||
            !asset.PostingAllowed)
        {
            return Result<BankTreasuryFxAccountView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankTreasuryFxAccountInvalid);
        }

        BankTreasuryFxAccountRecord? existing =
            unitOfWork.Fx.FindTreasuryAccount(command.BankId, command.CurrencyId);

        if (existing is null)
        {
            if (command.TargetStatus != BankTreasuryFxAccountStatus.Active)
            {
                return Result<BankTreasuryFxAccountView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.BankTreasuryFxAccountStateInvalid);
            }

            BankTreasuryFxAccountRecord created = new(
                BankTreasuryFxAccountId.FromValue(idGenerator.NextId()),
                command.BankId,
                command.CurrencyId,
                asset.Id,
                BankTreasuryFxAccountStatus.Active,
                VersionedEntity.InitialVersion);

            BankTreasuryFxAccountStatusCatalog.EnsureCreatable(created.Status);
            unitOfWork.Fx.AddTreasuryAccount(created);

            return Result<BankTreasuryFxAccountView>.Success(ToView(created));
        }

        if (existing.AssetLedgerAccountId != asset.Id)
        {
            return Result<BankTreasuryFxAccountView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankTreasuryFxAccountInvalid);
        }

        if (existing.Status != command.TargetStatus)
        {
            if (!BankTreasuryFxAccountStatusCatalog.IsAllowed(existing.Status, command.TargetStatus))
            {
                return Result<BankTreasuryFxAccountView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.BankTreasuryFxAccountStateInvalid);
            }

            BankTreasuryFxAccountStatusCatalog.EnsureTransition(existing.Status, command.TargetStatus);
        }

        BankTreasuryFxAccountRecord updated = existing with
        {
            Status = command.TargetStatus,
            Version = existing.Version + 1,
        };

        unitOfWork.Fx.UpdateTreasuryAccount(updated);

        return Result<BankTreasuryFxAccountView>.Success(ToView(updated));
    }

    private static Result<FxOrderView> PlaceTreasuryOrder(
        IBankingUnitOfWork unitOfWork,
        PlaceBankTreasuryFxOrderCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<FxOrderView>.Failure(scope.Error!);
        }

        if (command.BaseMinor <= 0)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(command.BaseMinor));
        }

        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market ||
            market.Status != FxMarketStatus.Active)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        CurrencyId sourceCurrencyId = command.Side == FxOrderSide.BuyBase
            ? market.QuoteCurrencyId
            : market.BaseCurrencyId;

        if (unitOfWork.Fx.FindTreasuryAccount(command.BankId, sourceCurrencyId) is not
            { Status: BankTreasuryFxAccountStatus.Active })
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankTreasuryFxAccountStateInvalid);
        }

        return Result<FxOrderView>.Failure(
            ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
    }

    private static Result<FxOrderView> CancelTreasuryOrder(
        IBankingUnitOfWork unitOfWork,
        CancelBankTreasuryFxOrderCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<FxOrderView>.Failure(scope.Error!);
        }

        if (unitOfWork.Banks.Find(command.BankId) is null)
        {
            return Result<FxOrderView>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.Fx.FindOrder(command.FxOrderId) is not { } order)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxOrderNotFound);
        }

        if (!order.IsResting)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxOrderNotCancellable);
        }

        return Result<FxOrderView>.Failure(
            ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
    }

    private static BankTreasuryFxAccountView ToView(BankTreasuryFxAccountRecord account) => new(
        account.Id, account.BankId, account.CurrencyId, account.AssetLedgerAccountId, account.Status);
}
