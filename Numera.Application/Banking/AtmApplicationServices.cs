using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CreateAtmNetworkCommand(AuthorizationContext Actor, string Name);

public sealed record UpdateAtmNetworkCommand(
    AuthorizationContext Actor,
    AtmNetworkId AtmNetworkId,
    string Name,
    AtmNetworkStatus TargetStatus);

public sealed record CreateAtmTerminalCommand(
    AuthorizationContext Actor,
    BankId OwnerBankId,
    string PlacementGuildId,
    AtmNetworkId? AtmNetworkId,
    string DisplayName);

public sealed record UpdateAtmTerminalCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    string DisplayName,
    AtmTerminalStatus TargetStatus,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool BalanceInquiryEnabled,
    bool TransferEnabled);

public sealed record SetAtmPlacementAgreementCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    int RevenueShareBps,
    AtmPlacementAgreementStatus TargetStatus);

public sealed record ConfigureAtmTerminalCurrencyServiceCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    CurrencyId CurrencyId,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool CrossCurrencyWithdrawalEnabled,
    AtmTerminalCurrencyServiceStatus TargetStatus);

public sealed record ConfigureAtmCashCassetteCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    CurrencyDenominationId CurrencyDenominationId,
    string CassetteRole,
    int CassettePriority,
    long CapacityCount);

public sealed record SetAtmNetworkParticipationCommand(
    AuthorizationContext Actor,
    AtmNetworkId AtmNetworkId,
    BankId BankId,
    bool IssuerEnabled,
    bool AcquirerEnabled,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool BalanceInquiryEnabled,
    bool TransferEnabled);

public sealed record ReplenishAtmCashCommand(
    AuthorizationContext Actor,
    AtmCashCassetteId AtmCashCassetteId,
    long Quantity);

public sealed record CollectAtmCashCommand(
    AuthorizationContext Actor,
    AtmCashCassetteId AtmCashCassetteId,
    long Quantity);

public sealed record AtmNetworkView(AtmNetworkId Id, string Name, AtmNetworkStatus Status);

public sealed record AtmTerminalView(
    AtmTerminalId Id,
    BankId OwnerBankId,
    string PlacementGuildId,
    string DisplayName,
    AtmTerminalStatus Status,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool BalanceInquiryEnabled,
    bool TransferEnabled);

public sealed record AtmPlacementAgreementView(
    AtmPlacementAgreementId Id,
    AtmTerminalId AtmTerminalId,
    string PlacementGuildId,
    int RevenueShareBps,
    AtmPlacementAgreementStatus Status);

public sealed record AtmTerminalCurrencyServiceView(
    AtmTerminalId AtmTerminalId,
    CurrencyId CurrencyId,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool CrossCurrencyWithdrawalEnabled,
    AtmTerminalCurrencyServiceStatus Status);

public sealed record AtmCashCassetteView(
    AtmCashCassetteId Id,
    AtmTerminalId AtmTerminalId,
    CurrencyDenominationId CurrencyDenominationId,
    string CassetteRole,
    int CassettePriority,
    long CapacityCount,
    long OnHandCount,
    AtmCashCassetteStatus Status);

public sealed record GetAtmDeploymentQuery(
    AuthorizationContext Actor,
    string NetworkName,
    string TerminalName,
    string InstitutionCode,
    long DenominationValueMinor);

public sealed record AtmDeploymentView(
    EconomyScopeId EconomyScopeId,
    CurrencyId CurrencyId,
    AtmNetworkId? NetworkId,
    AtmNetworkStatus? NetworkStatus,
    AtmTerminalId? TerminalId,
    AtmTerminalStatus? TerminalStatus,
    BankId? BankId,
    CurrencyDenominationId? DenominationId);

public interface IAtmAdministrationApplicationService
{
    Task<Result<AtmDeploymentView>> GetDeploymentAsync(
        GetAtmDeploymentQuery query,
        CancellationToken cancellationToken);

    Task<Result<AtmNetworkView>> CreateNetworkAsync(
        CreateAtmNetworkCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmNetworkView>> UpdateNetworkAsync(
        UpdateAtmNetworkCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmTerminalView>> CreateTerminalAsync(
        CreateAtmTerminalCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmTerminalView>> UpdateTerminalAsync(
        UpdateAtmTerminalCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmPlacementAgreementView>> SetPlacementAgreementAsync(
        SetAtmPlacementAgreementCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmTerminalCurrencyServiceView>> ConfigureCurrencyServiceAsync(
        ConfigureAtmTerminalCurrencyServiceCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmCashCassetteView>> ConfigureCassetteAsync(
        ConfigureAtmCashCassetteCommand command,
        CancellationToken cancellationToken);

    Task<Result> SetParticipationAsync(
        SetAtmNetworkParticipationCommand command,
        CancellationToken cancellationToken);

    Task<Result> ReplenishAsync(ReplenishAtmCashCommand command, CancellationToken cancellationToken);

    Task<Result> CollectCashAsync(CollectAtmCashCommand command, CancellationToken cancellationToken);
}

public sealed class AtmAdministrationApplicationService : IAtmAdministrationApplicationService
{
    internal const int MaximumCassettesPerTerminal = 8;
    private const string ReplenishOperationType = "ATM_CASH_REPLENISH";
    private const string CollectOperationType = "ATM_CASH_COLLECT";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public AtmAdministrationApplicationService(
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

    public Task<Result<AtmDeploymentView>> GetDeploymentAsync(
        GetAtmDeploymentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => Deployment(unitOfWork, query), cancellationToken);
    }

    private static Result<AtmDeploymentView> Deployment(
        IBankingUnitOfWork unitOfWork,
        GetAtmDeploymentQuery query)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, query.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmDeploymentView>.Failure(scope.Error!);
        }

        if (unitOfWork.Currencies.FindCurrent(scope.Value) is not { } currency)
        {
            return Result<AtmDeploymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        AtmNetworkRecord? network = query.NetworkName.Length > 0
            ? unitOfWork.Cash.FindNetworkByName(query.NetworkName)
            : null;

        AtmTerminalRecord? terminal = query.TerminalName.Length > 0
            ? unitOfWork.Cash
                .ListTerminals(query.Actor.GuildId.ToString(CultureInfo.InvariantCulture), 100)
                .FirstOrDefault(entry => string.Equals(
                    entry.DisplayName, query.TerminalName, StringComparison.Ordinal))
            : null;

        Bank? bank =
            InstitutionCode.TryParse(query.InstitutionCode, out InstitutionCode institutionCode)
                ? unitOfWork.Banks.FindByInstitutionCode(scope.Value, institutionCode.Value)
                : null;

        CurrencyDenominationRecord? denomination = query.DenominationValueMinor > 0
            ? unitOfWork.Cash.FindDenominationByValue(currency.Id, query.DenominationValueMinor)
            : null;

        return Result<AtmDeploymentView>.Success(new AtmDeploymentView(
            scope.Value,
            currency.Id,
            network?.Id,
            network?.Status,
            terminal?.Id,
            terminal?.Status,
            bank?.Id,
            denomination?.Id));
    }

    public Task<Result<AtmNetworkView>> CreateNetworkAsync(
        CreateAtmNetworkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateNetwork(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmNetworkView>> UpdateNetworkAsync(
        UpdateAtmNetworkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateNetwork(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmTerminalView>> CreateTerminalAsync(
        CreateAtmTerminalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateTerminal(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmTerminalView>> UpdateTerminalAsync(
        UpdateAtmTerminalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateTerminal(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmPlacementAgreementView>> SetPlacementAgreementAsync(
        SetAtmPlacementAgreementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => SetPlacementAgreement(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmTerminalCurrencyServiceView>> ConfigureCurrencyServiceAsync(
        ConfigureAtmTerminalCurrencyServiceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => ConfigureCurrencyService(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmCashCassetteView>> ConfigureCassetteAsync(
        ConfigureAtmCashCassetteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => ConfigureCassette(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> SetParticipationAsync(
        SetAtmNetworkParticipationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => SetParticipation(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    public async Task<Result> ReplenishAsync(
        ReplenishAtmCashCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(
                unitOfWork => MoveVaultCash(
                    unitOfWork,
                    command.Actor,
                    command.AtmCashCassetteId,
                    command.Quantity,
                    toCassette: true,
                    ReplenishOperationType),
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    public async Task<Result> CollectCashAsync(
        CollectAtmCashCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(
                unitOfWork => MoveVaultCash(
                    unitOfWork,
                    command.Actor,
                    command.AtmCashCassetteId,
                    command.Quantity,
                    toCassette: false,
                    CollectOperationType),
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<AtmNetworkView> CreateNetwork(
        IBankingUnitOfWork unitOfWork,
        CreateAtmNetworkCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmNetworkView>.Failure(scope.Error!);
        }

        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 64)
        {
            return Result<AtmNetworkView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AtmNetworkNameInvalid, nameof(command.Name));
        }

        if (unitOfWork.Cash.FindNetworkByName(command.Name) is not null)
        {
            return Result<AtmNetworkView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmNetworkAlreadyExists);
        }

        AtmNetworkRecord network = new(
            AtmNetworkId.FromValue(idGenerator.NextId()),
            command.Name,
            AtmNetworkStatus.Active,
            VersionedEntity.InitialVersion);

        AtmNetworkStatusCatalog.EnsureCreatable(network.Status);
        unitOfWork.Cash.AddNetwork(network);

        return Result<AtmNetworkView>.Success(new AtmNetworkView(network.Id, network.Name, network.Status));
    }

    private static Result<AtmNetworkView> UpdateNetwork(
        IBankingUnitOfWork unitOfWork,
        UpdateAtmNetworkCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmNetworkView>.Failure(scope.Error!);
        }

        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 64)
        {
            return Result<AtmNetworkView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AtmNetworkNameInvalid, nameof(command.Name));
        }

        if (unitOfWork.Cash.FindNetwork(command.AtmNetworkId) is not { } network)
        {
            return Result<AtmNetworkView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmNetworkNotFound);
        }

        if (network.Status != command.TargetStatus &&
            !AtmNetworkStatusCatalog.IsAllowed(network.Status, command.TargetStatus))
        {
            return Result<AtmNetworkView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmNetworkStateInvalid);
        }

        if (network.Status != command.TargetStatus)
        {
            AtmNetworkStatusCatalog.EnsureTransition(network.Status, command.TargetStatus);
        }

        AtmNetworkRecord updated = network with
        {
            Name = command.Name,
            Status = command.TargetStatus,
            Version = network.Version + 1,
        };

        unitOfWork.Cash.UpdateNetwork(updated);

        return Result<AtmNetworkView>.Success(new AtmNetworkView(updated.Id, updated.Name, updated.Status));
    }

    private Result<AtmTerminalView> CreateTerminal(
        IBankingUnitOfWork unitOfWork,
        CreateAtmTerminalCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmTerminalView>.Failure(scope.Error!);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 64)
        {
            return Result<AtmTerminalView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AtmTerminalNameInvalid,
                nameof(command.DisplayName));
        }

        if (unitOfWork.Banks.Find(command.OwnerBankId) is null)
        {
            return Result<AtmTerminalView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (command.AtmNetworkId is { } networkId &&
            unitOfWork.Cash.FindNetwork(networkId) is not { Status: AtmNetworkStatus.Active })
        {
            return Result<AtmTerminalView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmNetworkNotFound);
        }

        AtmTerminalRecord terminal = new(
            AtmTerminalId.FromValue(idGenerator.NextId()),
            command.OwnerBankId,
            command.PlacementGuildId,
            null,
            command.AtmNetworkId,
            command.DisplayName,
            AtmTerminalStatus.OutOfService,
            true,
            true,
            true,
            true,
            VersionedEntity.InitialVersion);

        AtmTerminalStatusCatalog.EnsureCreatable(terminal.Status);
        unitOfWork.Cash.AddTerminal(terminal);

        return Result<AtmTerminalView>.Success(ToView(terminal));
    }

    private static Result<AtmTerminalView> UpdateTerminal(
        IBankingUnitOfWork unitOfWork,
        UpdateAtmTerminalCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmTerminalView>.Failure(scope.Error!);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 64)
        {
            return Result<AtmTerminalView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AtmTerminalNameInvalid,
                nameof(command.DisplayName));
        }

        if (unitOfWork.Cash.FindTerminal(command.AtmTerminalId) is not { } terminal)
        {
            return Result<AtmTerminalView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (terminal.Status != command.TargetStatus &&
            !AtmTerminalStatusCatalog.IsAllowed(terminal.Status, command.TargetStatus))
        {
            return Result<AtmTerminalView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmTerminalStateInvalid);
        }

        if (terminal.Status != command.TargetStatus)
        {
            AtmTerminalStatusCatalog.EnsureTransition(terminal.Status, command.TargetStatus);
        }

        AtmTerminalRecord updated = terminal with
        {
            DisplayName = command.DisplayName,
            Status = command.TargetStatus,
            WithdrawalEnabled = command.WithdrawalEnabled,
            DepositEnabled = command.DepositEnabled,
            BalanceInquiryEnabled = command.BalanceInquiryEnabled,
            TransferEnabled = command.TransferEnabled,
            Version = terminal.Version + 1,
        };

        unitOfWork.Cash.UpdateTerminal(updated);

        return Result<AtmTerminalView>.Success(ToView(updated));
    }

    private Result<AtmPlacementAgreementView> SetPlacementAgreement(
        IBankingUnitOfWork unitOfWork,
        SetAtmPlacementAgreementCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmPlacementAgreementView>.Failure(scope.Error!);
        }

        if (command.RevenueShareBps is < 0 or > 10000)
        {
            return Result<AtmPlacementAgreementView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AtmRevenueShareInvalid,
                nameof(command.RevenueShareBps));
        }

        if (unitOfWork.Cash.FindTerminal(command.AtmTerminalId) is not { } terminal)
        {
            return Result<AtmPlacementAgreementView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        UtcTimestamp now = clock.Now();
        AtmPlacementAgreementRecord? existing =
            unitOfWork.Cash.FindPlacementAgreement(terminal.Id);

        if (existing is null)
        {
            if (command.TargetStatus != AtmPlacementAgreementStatus.Pending)
            {
                return Result<AtmPlacementAgreementView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.AtmPlacementAgreementStateInvalid);
            }

            AtmPlacementAgreementRecord created = new(
                AtmPlacementAgreementId.FromValue(idGenerator.NextId()),
                terminal.Id,
                terminal.PlacementGuildId,
                terminal.OwnerBankId,
                null,
                null,
                null,
                now,
                null,
                null,
                command.RevenueShareBps,
                AtmPlacementAgreementStatus.Pending,
                VersionedEntity.InitialVersion);

            AtmPlacementAgreementStatusCatalog.EnsureCreatable(created.Status);
            unitOfWork.Cash.AddPlacementAgreement(created);

            return Result<AtmPlacementAgreementView>.Success(ToView(created));
        }

        if (existing.Status != command.TargetStatus &&
            !AtmPlacementAgreementStatusCatalog.IsAllowed(existing.Status, command.TargetStatus))
        {
            return Result<AtmPlacementAgreementView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmPlacementAgreementStateInvalid);
        }

        if (existing.Status != command.TargetStatus)
        {
            AtmPlacementAgreementStatusCatalog.EnsureTransition(existing.Status, command.TargetStatus);
        }

        AtmPlacementAgreementRecord updated = existing with
        {
            RevenueShareBps = command.RevenueShareBps,
            Status = command.TargetStatus,
            EffectiveTo = command.TargetStatus == AtmPlacementAgreementStatus.Ended ? now : existing.EffectiveTo,
            Version = existing.Version + 1,
        };

        unitOfWork.Cash.UpdatePlacementAgreement(updated);

        return Result<AtmPlacementAgreementView>.Success(ToView(updated));
    }

    private static Result<AtmTerminalCurrencyServiceView> ConfigureCurrencyService(
        IBankingUnitOfWork unitOfWork,
        ConfigureAtmTerminalCurrencyServiceCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmTerminalCurrencyServiceView>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindTerminal(command.AtmTerminalId) is null)
        {
            return Result<AtmTerminalCurrencyServiceView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        AtmTerminalCurrencyServiceRecord? existing =
            unitOfWork.Cash.FindCurrencyService(command.AtmTerminalId, command.CurrencyId);

        if (existing is null)
        {
            if (command.TargetStatus != AtmTerminalCurrencyServiceStatus.Active)
            {
                return Result<AtmTerminalCurrencyServiceView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
            }

            AtmTerminalCurrencyServiceStatusCatalog.EnsureCreatable(
                AtmTerminalCurrencyServiceStatus.Active);
        }
        else if (existing.Status != command.TargetStatus)
        {
            if (!AtmTerminalCurrencyServiceStatusCatalog.IsAllowed(existing.Status, command.TargetStatus))
            {
                return Result<AtmTerminalCurrencyServiceView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
            }

            AtmTerminalCurrencyServiceStatusCatalog.EnsureTransition(existing.Status, command.TargetStatus);
        }

        AtmTerminalCurrencyServiceRecord service = new(
            command.AtmTerminalId,
            command.CurrencyId,
            command.WithdrawalEnabled,
            command.DepositEnabled,
            command.CrossCurrencyWithdrawalEnabled,
            command.TargetStatus,
            existing is null ? VersionedEntity.InitialVersion : existing.Version + 1);

        unitOfWork.Cash.UpsertCurrencyService(service);

        return Result<AtmTerminalCurrencyServiceView>.Success(new AtmTerminalCurrencyServiceView(
            service.AtmTerminalId,
            service.CurrencyId,
            service.WithdrawalEnabled,
            service.DepositEnabled,
            service.CrossCurrencyWithdrawalEnabled,
            service.Status));
    }

    private Result<AtmCashCassetteView> ConfigureCassette(
        IBankingUnitOfWork unitOfWork,
        ConfigureAtmCashCassetteCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmCashCassetteView>.Failure(scope.Error!);
        }

        if (command.CassetteRole is not ("DISPENSE" or "DEPOSIT" or "RECYCLE"))
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AtmCassetteRoleInvalid,
                nameof(command.CassetteRole));
        }

        if (command.CassettePriority is < 0 or > 7)
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AtmCassettePriorityInvalid,
                nameof(command.CassettePriority));
        }

        if (command.CapacityCount <= 0)
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AtmCassetteCapacityInvalid,
                nameof(command.CapacityCount));
        }

        if (unitOfWork.Cash.FindTerminal(command.AtmTerminalId) is not { } terminal)
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (unitOfWork.Cash.FindDenomination(command.CurrencyDenominationId) is not { } denomination ||
            denomination.Status != CurrencyDenominationStatus.Active)
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyDenominationNotFound);
        }

        IReadOnlyList<AtmCashCassetteRecord> cassettes = unitOfWork.Cash.ListCassettes(terminal.Id);

        if (unitOfWork.Cash.FindCassetteByPriority(terminal.Id, command.CassettePriority) is not null)
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmCassetteSlotOccupied);
        }

        if (cassettes.Count >= MaximumCassettesPerTerminal)
        {
            return Result<AtmCashCassetteView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmCassetteLimitReached);
        }

        AtmCashCassetteId cassetteId = AtmCashCassetteId.FromValue(idGenerator.NextId());

        CashHolderRecord holder = new(
            CashHolderId.FromValue(idGenerator.NextId()),
            denomination.CurrencyId,
            "ATM_CASSETTE",
            cassetteId.Value,
            clock.Now());

        unitOfWork.Cash.AddCashHolder(holder);

        AtmCashCassetteRecord cassette = new(
            cassetteId,
            terminal.Id,
            holder.Id,
            denomination.Id,
            command.CassetteRole,
            command.CassettePriority,
            command.CapacityCount,
            AtmCashCassetteStatus.Active,
            VersionedEntity.InitialVersion);

        AtmCashCassetteStatusCatalog.EnsureCreatable(cassette.Status);
        unitOfWork.Cash.AddCassette(cassette);
        unitOfWork.Cash.UpsertCashPosition(
            new CashPositionRecord(holder.Id, denomination.Id, 0, 0, VersionedEntity.InitialVersion));

        return Result<AtmCashCassetteView>.Success(new AtmCashCassetteView(
            cassette.Id,
            cassette.AtmTerminalId,
            cassette.CurrencyDenominationId,
            cassette.CassetteRole,
            cassette.CassettePriority,
            cassette.CapacityCount,
            0,
            cassette.Status));
    }

    private Result<bool> SetParticipation(
        IBankingUnitOfWork unitOfWork,
        SetAtmNetworkParticipationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindNetwork(command.AtmNetworkId) is not { } network ||
            network.Status == AtmNetworkStatus.Retired)
        {
            return Result<bool>.Failure(ErrorCategory.NotFound, BankingErrorCodes.AtmNetworkNotFound);
        }

        if (unitOfWork.Banks.Find(command.BankId) is null)
        {
            return Result<bool>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        UtcTimestamp now = clock.Now();
        AtmNetworkParticipationRecord? existing =
            unitOfWork.Cash.FindParticipation(command.AtmNetworkId, command.BankId, now);

        unitOfWork.Cash.UpsertParticipation(new AtmNetworkParticipationRecord(
            command.AtmNetworkId,
            command.BankId,
            command.IssuerEnabled,
            command.AcquirerEnabled,
            command.WithdrawalEnabled,
            command.DepositEnabled,
            command.BalanceInquiryEnabled,
            command.TransferEnabled,
            now,
            null,
            existing is null ? VersionedEntity.InitialVersion : existing.Version + 1));

        return Result<bool>.Success(true);
    }

    private Result<bool> MoveVaultCash(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        AtmCashCassetteId cassetteId,
        long quantity,
        bool toCassette,
        string operationType)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (quantity <= 0)
        {
            return Result<bool>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CashQuantityInvalid, nameof(quantity));
        }

        if (unitOfWork.Cash.FindCassette(cassetteId) is not { } cassette ||
            cassette.Status != AtmCashCassetteStatus.Active)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmCashCassetteNotFound);
        }

        if (unitOfWork.Cash.FindTerminal(cassette.AtmTerminalId) is not { } terminal)
        {
            return Result<bool>.Failure(ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (unitOfWork.Cash.FindDenomination(cassette.CurrencyDenominationId) is not { } denomination)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyDenominationNotFound);
        }

        if (unitOfWork.Cash.FindCashVault(terminal.OwnerBankId, denomination.CurrencyId) is not { } vault ||
            vault.Status != BankCashVaultStatus.Active)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankCashVaultNotFound);
        }

        CashPositionRecord vaultPosition =
            unitOfWork.Cash.FindCashPosition(vault.CashHolderId, denomination.Id)
            ?? new CashPositionRecord(
                vault.CashHolderId, denomination.Id, 0, 0, VersionedEntity.InitialVersion);

        CashPositionRecord cassettePosition =
            unitOfWork.Cash.FindCashPosition(cassette.CashHolderId, denomination.Id)
            ?? new CashPositionRecord(
                cassette.CashHolderId, denomination.Id, 0, 0, VersionedEntity.InitialVersion);

        CashHolderId from = toCassette ? vault.CashHolderId : cassette.CashHolderId;
        CashHolderId to = toCassette ? cassette.CashHolderId : vault.CashHolderId;
        CashPositionRecord source = toCassette ? vaultPosition : cassettePosition;
        CashPositionRecord destination = toCassette ? cassettePosition : vaultPosition;

        if (source.OnHandCount - source.ReservedCount < quantity)
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CashVaultInsufficient);
        }

        if (toCassette && destination.OnHandCount + quantity > cassette.CapacityCount)
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmCassetteCapacityExceeded);
        }

        UtcTimestamp now = clock.Now();
        BusinessOperationId operationId = BusinessOperationId.FromValue(idGenerator.NextId());
        string discriminator = string.Create(
            CultureInfo.InvariantCulture,
            $"{cassette.Id.Value}:{now.UnixMilliseconds}");

        unitOfWork.BusinessOperations.Add(Numera.Domain.Accounting.BusinessOperation.Start(
            operationId,
            operationType,
            scope.Value,
            null,
            cassette.Id.Value,
            Numera.Domain.Accounting.IdempotencyKey.Create(operationType, discriminator),
            now));

        unitOfWork.Cash.UpsertCashPosition(source with
        {
            OnHandCount = source.OnHandCount - quantity,
            Version = source.Version + 1,
        });

        unitOfWork.Cash.UpsertCashPosition(destination with
        {
            OnHandCount = destination.OnHandCount + quantity,
            Version = destination.Version + 1,
        });

        unitOfWork.Cash.AddCashMovement(new CashMovementRecord(
            CashMovementId.FromValue(idGenerator.NextId()),
            operationId,
            denomination.Id,
            from,
            to,
            quantity,
            MoneyMinor.FromIntermediate(checked(denomination.ValueMinor * (Int128)quantity)),
            "TRANSFER",
            now));

        return Result<bool>.Success(true);
    }

    private static AtmTerminalView ToView(AtmTerminalRecord terminal) => new(
        terminal.Id,
        terminal.OwnerBankId,
        terminal.PlacementGuildId,
        terminal.DisplayName,
        terminal.Status,
        terminal.WithdrawalEnabled,
        terminal.DepositEnabled,
        terminal.BalanceInquiryEnabled,
        terminal.TransferEnabled);

    private static AtmPlacementAgreementView ToView(AtmPlacementAgreementRecord agreement) => new(
        agreement.Id,
        agreement.AtmTerminalId,
        agreement.PlacementGuildId,
        agreement.RevenueShareBps,
        agreement.Status);
}
