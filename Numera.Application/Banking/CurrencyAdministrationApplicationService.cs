using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CreateCurrencyCommand(
    AuthorizationContext Actor,
    AccountingBookId IssuanceAccountingBookId,
    string Name,
    string Code,
    string Symbol,
    string DisplayPattern,
    int MinorUnitDigits,
    long? BaseMoneySupplyCapMinor,
    long GenesisAmountMinor,
    string ReasonCode,
    string IdempotencyToken,
    EconomyScopeId? TargetEconomyScopeId = null);

public sealed record IssueCurrencyCommand(
    AuthorizationContext Actor,
    CurrencyId CurrencyId,
    LedgerAccountId DestinationLedgerAccountId,
    long AmountMinor,
    string ReasonCode,
    string IdempotencyToken);

public sealed record BurnCurrencyCommand(
    AuthorizationContext Actor,
    CurrencyId CurrencyId,
    LedgerAccountId SourceLedgerAccountId,
    long AmountMinor,
    string ReasonCode,
    string IdempotencyToken);

public sealed record CurrencyView(
    CurrencyId Id,
    EconomyScopeId EconomyScopeId,
    string Code,
    string Name,
    int MinorUnitDigits,
    CurrencyStatus Status,
    MoneyMinor BaseMoneySupply,
    MoneyMinor? BaseMoneySupplyCap);

public sealed record CurrencySupplyView(
    CurrencyId Id,
    CurrencySupplyOperationKind OperationKind,
    MoneyMinor Amount,
    MoneyMinor BaseMoneySupply,
    MoneyMinor? BaseMoneySupplyCap);

public interface ICurrencyAdministrationApplicationService
{
    Task<Result<CurrencyView>> CreateCurrencyAsync(
        CreateCurrencyCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencySupplyView>> IssueAsync(
        IssueCurrencyCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencySupplyView>> BurnAsync(
        BurnCurrencyCommand command,
        CancellationToken cancellationToken);
}

public sealed class CurrencyAdministrationApplicationService : ICurrencyAdministrationApplicationService
{
    public const string CreateOperationType = "CURRENCY_CREATE";
    public const string IssueOperationType = "CURRENCY_ISSUE";
    public const string BurnOperationType = "CURRENCY_BURN";
    public const string GenesisTransactionType = "CURRENCY_GENESIS";
    public const string IssueTransactionType = "CURRENCY_ISSUE";
    public const string BurnTransactionType = "CURRENCY_BURN";
    public const string DescriptionCode = "CURRENCY_SUPPLY";
    public const string CreatedEventType = "CURRENCY_CREATED";
    public const string SupplyChangedEventType = "CURRENCY_SUPPLY_CHANGED";
    public const string IssuanceControlCodePrefix = "2900";
    public const string IssuancePostingCodePrefix = "2901";
    public const string TreasuryControlCodePrefix = "1900";
    public const string TreasuryPostingCodePrefix = "1901";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public CurrencyAdministrationApplicationService(
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

    public Task<Result<CurrencyView>> CreateCurrencyAsync(
        CreateCurrencyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IdempotencyKey.TryCreate(CreateOperationType, command.IdempotencyToken, out IdempotencyKey key))
        {
            return Task.FromResult(Result<CurrencyView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyMetadataInvalid,
                nameof(command.IdempotencyToken)));
        }

        return writeGateway.ExecuteAsync(unitOfWork => Create(unitOfWork, command, key), cancellationToken);
    }

    public Task<Result<CurrencySupplyView>> IssueAsync(
        IssueCurrencyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IdempotencyKey.TryCreate(IssueOperationType, command.IdempotencyToken, out IdempotencyKey key))
        {
            return Task.FromResult(Result<CurrencySupplyView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyMetadataInvalid,
                nameof(command.IdempotencyToken)));
        }

        SupplyChangeRequest request = new(
            command.Actor,
            command.CurrencyId,
            command.DestinationLedgerAccountId,
            command.AmountMinor,
            command.ReasonCode,
            CurrencySupplyOperationKind.Issue,
            IssueOperationType,
            IssueTransactionType,
            key);

        return writeGateway.ExecuteAsync(unitOfWork => ChangeSupply(unitOfWork, request), cancellationToken);
    }

    public Task<Result<CurrencySupplyView>> BurnAsync(
        BurnCurrencyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IdempotencyKey.TryCreate(BurnOperationType, command.IdempotencyToken, out IdempotencyKey key))
        {
            return Task.FromResult(Result<CurrencySupplyView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyMetadataInvalid,
                nameof(command.IdempotencyToken)));
        }

        SupplyChangeRequest request = new(
            command.Actor,
            command.CurrencyId,
            command.SourceLedgerAccountId,
            command.AmountMinor,
            command.ReasonCode,
            CurrencySupplyOperationKind.Burn,
            BurnOperationType,
            BurnTransactionType,
            key);

        return writeGateway.ExecuteAsync(unitOfWork => ChangeSupply(unitOfWork, request), cancellationToken);
    }

    private readonly record struct SupplyChangeRequest(
        AuthorizationContext Actor,
        CurrencyId CurrencyId,
        LedgerAccountId CounterpartyLedgerAccountId,
        long AmountMinor,
        string ReasonCode,
        CurrencySupplyOperationKind Kind,
        string OperationType,
        string TransactionType,
        IdempotencyKey IdempotencyKey);

    private readonly record struct CanonicalCurrencyAccounts(
        LedgerAccount IssuanceLiability,
        LedgerAccount Treasury);

    private Result<CurrencyView> Create(
        IBankingUnitOfWork unitOfWork,
        CreateCurrencyCommand command,
        IdempotencyKey key)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(
            unitOfWork, command.Actor, command.TargetEconomyScopeId);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyView>.Failure(scope.Error!);
        }

        EconomyScopeId economyScopeId = scope.Value;

        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, command.Actor, economyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<CurrencyView>.Failure(authorized.Error!);
        }

        if (!unitOfWork.Currencies.EconomyIsActive(economyScopeId))
        {
            return Result<CurrencyView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.EconomyScopeNotFound);
        }

        if (unitOfWork.Currencies.FindCurrent(economyScopeId) is { } current)
        {
            return unitOfWork.BusinessOperations.Find(key) is not null
                ? Result<CurrencyView>.Success(Describe(unitOfWork, current))
                : Result<CurrencyView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.CurrencyAlreadyExists);
        }

        Result validated = ValidateCreateInput(command);
        if (!validated.IsSuccess)
        {
            return Result<CurrencyView>.Failure(validated.Error!);
        }

        if (!unitOfWork.Currencies.AccountingBookIsOpen(command.IssuanceAccountingBookId))
        {
            return Result<CurrencyView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CurrencyIssuanceAccountUnavailable);
        }

        UtcTimestamp now = clock.Now();

        Currency currency = Currency.Create(
            CurrencyId.FromValue(idGenerator.NextId()),
            economyScopeId,
            MinorUnitDigits.FromInt32(command.MinorUnitDigits),
            command.BaseMoneySupplyCapMinor is { } cap ? MoneyMinor.FromMinor(cap) : null,
            now);

        unitOfWork.Currencies.Add(currency);

        unitOfWork.Currencies.AddMetadataVersion(CurrencyMetadataVersion.Create(
            CurrencyMetadataVersionId.FromValue(idGenerator.NextId()),
            currency.Id,
            command.Name,
            command.Code,
            command.Symbol,
            command.DisplayPattern,
            now,
            effectiveTo: null,
            VersionedEntity.InitialVersion));

        Result<CanonicalCurrencyAccounts> accounts = CreateCanonicalAccounts(
            unitOfWork, command.IssuanceAccountingBookId, currency, command.Code, now);

        if (!accounts.IsSuccess)
        {
            return Result<CurrencyView>.Failure(accounts.Error!);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            CreateOperationType,
            economyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            key,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        MoneyMinor genesis = MoneyMinor.FromMinor(command.GenesisAmountMinor);

        if (genesis.IsPositive)
        {
            if (currency.ExceedsSupplyCap(genesis))
            {
                return Result<CurrencyView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.CurrencySupplyCapExceeded);
            }

            Result minted = Mint(
                unitOfWork,
                command.IssuanceAccountingBookId,
                accounts.Value,
                operation,
                currency,
                genesis,
                CurrencySupplyOperationKind.Genesis,
                GenesisTransactionType,
                command.ReasonCode,
                now);

            if (!minted.IsSuccess)
            {
                return Result<CurrencyView>.Failure(minted.Error!);
            }
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            CreatedEventType,
            $$"""{"currency_id":"{{currency.Id.Value}}","code":"{{command.Code}}"}""",
            now));

        return Result<CurrencyView>.Success(new CurrencyView(
            currency.Id,
            currency.EconomyScopeId,
            command.Code,
            command.Name,
            currency.MinorUnitDigits.Value,
            currency.Status,
            genesis,
            currency.BaseMoneySupplyCap));
    }

    private Result<CurrencySupplyView> ChangeSupply(
        IBankingUnitOfWork unitOfWork,
        SupplyChangeRequest request)
    {
        if (unitOfWork.Currencies.Find(request.CurrencyId) is not { } currency)
        {
            return Result<CurrencySupplyView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, request.Actor, currency.EconomyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<CurrencySupplyView>.Failure(authorized.Error!);
        }

        if (unitOfWork.BusinessOperations.Find(request.IdempotencyKey) is not null)
        {
            return Result<CurrencySupplyView>.Success(new CurrencySupplyView(
                currency.Id,
                request.Kind,
                MoneyMinor.FromMinor(request.AmountMinor),
                unitOfWork.Currencies.SumSupply(currency.Id).BaseMoneySupply,
                currency.BaseMoneySupplyCap));
        }

        if (!currency.AcceptsSupplyChange)
        {
            return Result<CurrencySupplyView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyNotIssuable);
        }

        if (request.AmountMinor <= 0)
        {
            return Result<CurrencySupplyView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(request.AmountMinor));
        }

        if (!CurrencySupplyOperation.IsReasonCodeValid(request.ReasonCode))
        {
            return Result<CurrencySupplyView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyReasonCodeInvalid,
                nameof(request.ReasonCode));
        }

        if (unitOfWork.Currencies.FindIssuanceLiabilityAccount(currency.Id) is not { } issuance)
        {
            return Result<CurrencySupplyView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CurrencyIssuanceAccountUnavailable);
        }

        if (unitOfWork.LedgerAccounts.Find(request.CounterpartyLedgerAccountId) is not { } counterparty ||
            !counterparty.AcceptsPosting ||
            counterparty.CurrencyId != currency.Id ||
            counterparty.BookId != issuance.BookId ||
            counterparty.AccountingType != AccountingType.Asset)
        {
            return Result<CurrencySupplyView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencySupplyAccountInvalid);
        }

        MoneyMinor amount = MoneyMinor.FromMinor(request.AmountMinor);
        MoneyMinor supply = unitOfWork.Currencies.SumSupply(currency.Id).BaseMoneySupply;

        Result<MoneyMinor> projected = ProjectSupply(unitOfWork, currency, counterparty, supply, amount, request.Kind);
        if (!projected.IsSuccess)
        {
            return Result<CurrencySupplyView>.Failure(projected.Error!);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            request.OperationType,
            currency.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            request.IdempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result posted = Mint(
            unitOfWork,
            issuance.BookId,
            new CanonicalCurrencyAccounts(issuance, counterparty),
            operation,
            currency,
            amount,
            request.Kind,
            request.TransactionType,
            request.ReasonCode,
            now);

        if (!posted.IsSuccess)
        {
            return Result<CurrencySupplyView>.Failure(posted.Error!);
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            SupplyChangedEventType,
            $$"""{"currency_id":"{{currency.Id.Value}}","kind":"{{request.Kind.ToToken()}}"}""",
            now));

        return Result<CurrencySupplyView>.Success(new CurrencySupplyView(
            currency.Id, request.Kind, amount, projected.Value, currency.BaseMoneySupplyCap));
    }

    private static Result<MoneyMinor> ProjectSupply(
        IBankingUnitOfWork unitOfWork,
        Currency currency,
        LedgerAccount counterparty,
        MoneyMinor supply,
        MoneyMinor amount,
        CurrencySupplyOperationKind kind)
    {
        if (kind == CurrencySupplyOperationKind.Burn)
        {
            if (amount > supply)
            {
                return Result<MoneyMinor>.Failure(
                    ErrorCategory.InsufficientFunds, BankingErrorCodes.CurrencySupplyInsufficient);
            }

            LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(counterparty.Id)
                ?? LedgerBalance.Empty;

            return balance.PostedBalance < amount
                ? Result<MoneyMinor>.Failure(
                    ErrorCategory.InsufficientFunds, BankingErrorCodes.CurrencySupplyInsufficient)
                : Result<MoneyMinor>.Success(currency.ProjectSupplyAfterBurn(supply, amount));
        }

        MoneyMinor projected = currency.ProjectSupplyAfterIssue(supply, amount);

        return currency.ExceedsSupplyCap(projected)
            ? Result<MoneyMinor>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencySupplyCapExceeded)
            : Result<MoneyMinor>.Success(projected);
    }

    private Result Mint(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        CanonicalCurrencyAccounts accounts,
        BusinessOperation operation,
        Currency currency,
        MoneyMinor amount,
        CurrencySupplyOperationKind kind,
        string transactionType,
        string reasonCode,
        UtcTimestamp now)
    {
        bool minting = kind is CurrencySupplyOperationKind.Genesis or CurrencySupplyOperationKind.Issue;

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(
            accounts.Treasury, minting ? EntrySide.Debit : EntrySide.Credit, amount));
        posting.Add(PostingLine.Institutional(
            accounts.IssuanceLiability, minting ? EntrySide.Credit : EntrySide.Debit, amount));

        Result posted = Post(unitOfWork, bookId, posting, operation, currency.Id, transactionType, now);

        if (!posted.IsSuccess)
        {
            return posted;
        }

        unitOfWork.Currencies.AddSupplyOperation(CurrencySupplyOperation.Create(
            CurrencySupplyOperationId.FromValue(idGenerator.NextId()),
            currency.Id,
            operation.Id,
            kind,
            amount,
            minting ? null : accounts.Treasury.Id,
            minting ? accounts.Treasury.Id : null,
            reasonCode,
            now));

        return Result.Success();
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        LedgerPostingBuilder posting,
        BusinessOperation operation,
        CurrencyId currencyId,
        string transactionType,
        UtcTimestamp now)
    {
        BusinessDate businessDate = BusinessDateOf(now);

        if (unitOfWork.AccountingPeriods.FindOpen(bookId, businessDate) is not { } period)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bookId,
                operation.Id,
                currencyId,
                businessDate,
                now,
                now,
                transactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private Result<CanonicalCurrencyAccounts> CreateCanonicalAccounts(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        Currency currency,
        string code,
        UtcTimestamp now)
    {
        string issuanceControlCode = $"{IssuanceControlCodePrefix}-{code}";
        string issuancePostingCode = $"{IssuancePostingCodePrefix}-{code}";
        string treasuryControlCode = $"{TreasuryControlCodePrefix}-{code}";
        string treasuryPostingCode = $"{TreasuryPostingCodePrefix}-{code}";

        foreach (string accountCode in
            new[] { issuanceControlCode, issuancePostingCode, treasuryControlCode, treasuryPostingCode })
        {
            if (unitOfWork.LedgerAccounts.FindByCode(bookId, accountCode) is not null)
            {
                return Result<CanonicalCurrencyAccounts>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.CurrencyAlreadyExists);
            }
        }

        LedgerAccount issuanceControl = LedgerAccount.CreateControl(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bookId,
            parentAccountId: null,
            issuanceControlCode,
            LedgerAccountKind.BaseMoneyIssuanceLiability,
            currency.Id);

        LedgerAccount issuancePosting = LedgerAccount.CreatePosting(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bookId,
            issuanceControl.Id,
            issuancePostingCode,
            LedgerAccountKind.BaseMoneyIssuanceLiability,
            currency.Id,
            LedgerOwnerReferenceType.None,
            EntityIdValue.Empty);

        LedgerAccount treasuryControl = LedgerAccount.CreateControl(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bookId,
            parentAccountId: null,
            treasuryControlCode,
            LedgerAccountKind.CashAsset,
            currency.Id);

        LedgerAccount treasuryPosting = LedgerAccount.CreatePosting(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bookId,
            treasuryControl.Id,
            treasuryPostingCode,
            LedgerAccountKind.CashAsset,
            currency.Id,
            LedgerOwnerReferenceType.None,
            EntityIdValue.Empty);

        unitOfWork.LedgerAccounts.Add(issuanceControl);
        unitOfWork.LedgerAccounts.Add(issuancePosting);
        unitOfWork.LedgerAccounts.Add(treasuryControl);
        unitOfWork.LedgerAccounts.Add(treasuryPosting);
        unitOfWork.LedgerAccounts.UpsertProjection(issuancePosting.Id, LedgerBalance.Empty, now);
        unitOfWork.LedgerAccounts.UpsertProjection(treasuryPosting.Id, LedgerBalance.Empty, now);

        return Result<CanonicalCurrencyAccounts>.Success(
            new CanonicalCurrencyAccounts(issuancePosting, treasuryPosting));
    }

    private static Result ValidateCreateInput(CreateCurrencyCommand command)
    {
        if (!CurrencyMetadataVersion.IsTextValid(command.Name, CurrencyMetadataVersion.MaximumNameLength))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMetadataInvalid, nameof(command.Name));
        }

        if (!CurrencyMetadataVersion.IsTextValid(command.Code, CurrencyMetadataVersion.MaximumCodeLength))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMetadataInvalid, nameof(command.Code));
        }

        if (!CurrencyMetadataVersion.IsTextValid(command.Symbol, CurrencyMetadataVersion.MaximumSymbolLength))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMetadataInvalid, nameof(command.Symbol));
        }

        if (!CurrencyMetadataVersion.IsTextValid(
            command.DisplayPattern, CurrencyMetadataVersion.MaximumDisplayPatternLength))
        {
            return Result.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyMetadataInvalid,
                nameof(command.DisplayPattern));
        }

        if (command.MinorUnitDigits is < MinorUnitDigits.Minimum or > MinorUnitDigits.Maximum)
        {
            return Result.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyMetadataInvalid,
                nameof(command.MinorUnitDigits));
        }

        if (command.BaseMoneySupplyCapMinor is < 0 || command.GenesisAmountMinor < 0)
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(command.GenesisAmountMinor));
        }

        return command.GenesisAmountMinor > 0 && !CurrencySupplyOperation.IsReasonCodeValid(command.ReasonCode)
            ? Result.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyReasonCodeInvalid,
                nameof(command.ReasonCode))
            : Result.Success();
    }

    private static CurrencyView Describe(IBankingUnitOfWork unitOfWork, Currency currency)
    {
        CurrencyMetadataVersion? metadata = unitOfWork.Currencies.FindCurrentMetadata(currency.Id);

        return new CurrencyView(
            currency.Id,
            currency.EconomyScopeId,
            metadata?.Code ?? string.Empty,
            metadata?.Name ?? string.Empty,
            currency.MinorUnitDigits.Value,
            currency.Status,
            unitOfWork.Currencies.SumSupply(currency.Id).BaseMoneySupply,
            currency.BaseMoneySupplyCap);
    }

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);
}
