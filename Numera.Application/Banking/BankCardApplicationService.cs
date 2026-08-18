using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record GetBankCardQuery(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId);

public sealed record RenderBankCardCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId);

public sealed record IssueBankCardCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    BankCardForm Form,
    IdempotencyKey IdempotencyKey);

public sealed record ReplaceBankCardCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    IdempotencyKey IdempotencyKey);

public sealed record SetBankCardLockCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    bool Locked);

public sealed record SetCashCardLockCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    bool Locked);

public sealed record SetDebitCardLockCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    bool Locked);

public sealed record BankCardView(
    BankCardId BankCardId,
    string InstitutionCode,
    BankCardForm Form,
    BankCardStatus Status,
    CashCardStatus? CashCardStatus,
    DebitCardStatus? DebitCardStatus,
    string DisplayIdentifier,
    long? ExpiresAt);

public sealed record BankCardRenderModel(
    string InstitutionCode,
    string BankName,
    string CustomerDisplayName,
    BankCardForm Form,
    string DisplayIdentifier,
    string? DebitDisplayNumber,
    long? ExpiresAt);

public sealed record BankCardImage(string FileName, int Width, int Height, byte[] Content);

public interface IBankCardApplicationService
{
    Task<Result<BankCardView>> GetBankCardAsync(GetBankCardQuery query, CancellationToken cancellationToken);

    Task<Result<BankCardView>> IssueBankCardAsync(
        IssueBankCardCommand command,
        CancellationToken cancellationToken);

    Task<Result<BankCardView>> ReplaceBankCardAsync(
        ReplaceBankCardCommand command,
        CancellationToken cancellationToken);

    Task<Result> SetBankCardLockAsync(
        SetBankCardLockCommand command,
        CancellationToken cancellationToken);

    Task<Result> SetCashCardLockAsync(
        SetCashCardLockCommand command,
        CancellationToken cancellationToken);

    Task<Result> SetDebitCardLockAsync(
        SetDebitCardLockCommand command,
        CancellationToken cancellationToken);

    Task<Result<BankCardImage>> RenderBankCardAsync(
        RenderBankCardCommand command,
        CancellationToken cancellationToken);
}

public sealed class BankCardApplicationService : IBankCardApplicationService
{
    public const int DebitValidityMonths = 60;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;
    private readonly IBankCardImageRenderer imageRenderer;

    public BankCardApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator,
        IBankCardImageRenderer imageRenderer)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(imageRenderer);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
        this.imageRenderer = imageRenderer;
    }

    public Task<Result<BankCardView>> GetBankCardAsync(
        GetBankCardQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => Read(unitOfWork, query.CustomerAccountId, query.DepositAccountId),
            cancellationToken);
    }

    public async Task<Result<BankCardImage>> RenderBankCardAsync(
        RenderBankCardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<BankCardRenderModel> model = await writeGateway
            .ExecuteAsync(unitOfWork => Render(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        if (!model.IsSuccess)
        {
            return Result<BankCardImage>.Failure(model.Error!);
        }

        return imageRenderer.TryRender(model.Value) is { } image
            ? Result<BankCardImage>.Success(image)
            : Result<BankCardImage>.Failure(
                ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.BankCardRendererUnavailable);
    }

    public Task<Result<BankCardView>> IssueBankCardAsync(
        IssueBankCardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Issue(unitOfWork, command), cancellationToken);
    }

    public Task<Result<BankCardView>> ReplaceBankCardAsync(
        ReplaceBankCardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Replace(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> SetBankCardLockAsync(
        SetBankCardLockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Discard(await writeGateway
            .ExecuteAsync(unitOfWork => SetBankCardLock(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false));
    }

    public async Task<Result> SetCashCardLockAsync(
        SetCashCardLockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Discard(await writeGateway
            .ExecuteAsync(unitOfWork => SetCashCardLock(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false));
    }

    public async Task<Result> SetDebitCardLockAsync(
        SetDebitCardLockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Discard(await writeGateway
            .ExecuteAsync(unitOfWork => SetDebitCardLock(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false));
    }

    private static Result Discard(Result<BankCardView> outcome) =>
        outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);

    private Result<BankCardView> Issue(IBankingUnitOfWork unitOfWork, IssueBankCardCommand command)
    {
        Result<DepositAccount> resolved = ResolveAccount(
            unitOfWork, command.CustomerAccountId, command.DepositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCardView>.Failure(resolved.Error!);
        }

        DepositAccount account = resolved.Value;

        if (unitOfWork.BankCards.FindUsableByAccount(account.Id) is not null)
        {
            return Result<BankCardView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankCardAlreadyIssued);
        }

        return IssueCard(unitOfWork, account, command.Form);
    }

    private Result<BankCardView> Replace(IBankingUnitOfWork unitOfWork, ReplaceBankCardCommand command)
    {
        Result<DepositAccount> resolved = ResolveAccount(
            unitOfWork, command.CustomerAccountId, command.DepositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCardView>.Failure(resolved.Error!);
        }

        DepositAccount account = resolved.Value;

        if (unitOfWork.BankCards.FindUsableByAccount(account.Id) is not { } existing)
        {
            return Result<BankCardView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankCardNotFound);
        }

        BankCardForm form = existing.Form;
        UtcTimestamp now = clock.Now();

        if (unitOfWork.BankCards.FindCashCardByBankCard(existing.Id) is { } cash &&
            cash.Status != CashCardStatus.Closed)
        {
            cash.Close(now);
            unitOfWork.BankCards.UpdateCashCard(cash);
        }

        if (unitOfWork.BankCards.FindDebitCardByBankCard(existing.Id) is { } debit &&
            debit.Status != DebitCardStatus.Closed)
        {
            debit.Close(now);
            unitOfWork.BankCards.UpdateDebitCard(debit);
        }

        Result<BankCardView> issued = IssueCard(unitOfWork, account, form);

        if (!issued.IsSuccess)
        {
            return issued;
        }

        existing.Replace(issued.Value.BankCardId);
        unitOfWork.BankCards.Update(existing);

        return issued;
    }

    private Result<BankCardView> IssueCard(
        IBankingUnitOfWork unitOfWork,
        DepositAccount account,
        BankCardForm form)
    {
        UtcTimestamp now = clock.Now();
        BankCardId cardId = BankCardId.FromValue(idGenerator.NextId());
        string identifier = DisplayIdentifier(cardId);

        if (unitOfWork.BankCards.DisplayIdentifierExists(account.BankId, identifier))
        {
            return Result<BankCardView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankCardDisplayIdentifierTaken);
        }

        UtcTimestamp? expiry = form == BankCardForm.CashOnly ? null : Expiry(now);

        BankCard card = BankCard.Issue(
            cardId, account.BankId, account.Id, form, identifier, now, expiry);

        unitOfWork.BankCards.Add(card);

        CashCardStatus? cashStatus = null;
        DebitCardStatus? debitStatus = null;

        if (card.HasCashCapability)
        {
            CashCard cash = CashCard.Issue(
                CashCardId.FromValue(idGenerator.NextId()), card.Id, account.Id, now);

            unitOfWork.BankCards.AddCashCard(cash);
            cashStatus = cash.Status;
        }

        if (card.HasDebitCapability)
        {
            DebitCard debit = DebitCard.Issue(
                DebitCardId.FromValue(idGenerator.NextId()),
                card.Id,
                account.Id,
                DisplayNumber(card.Id),
                now,
                expiry!.Value);

            unitOfWork.BankCards.AddDebitCard(debit);
            debitStatus = debit.Status;
        }

        return Result<BankCardView>.Success(ToView(unitOfWork, card, cashStatus, debitStatus));
    }

    private Result<BankCardView> SetBankCardLock(
        IBankingUnitOfWork unitOfWork,
        SetBankCardLockCommand command)
    {
        Result<BankCard> resolved = ResolveCard(
            unitOfWork, command.CustomerAccountId, command.DepositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCardView>.Failure(resolved.Error!);
        }

        BankCard card = resolved.Value;
        BankCardStatus desired = command.Locked ? BankCardStatus.Locked : BankCardStatus.Active;

        if (card.Status == desired)
        {
            return Result<BankCardView>.Success(ToView(unitOfWork, card));
        }

        if (command.Locked)
        {
            card.Lock();
        }
        else
        {
            card.Unlock();
        }

        unitOfWork.BankCards.Update(card);

        return Result<BankCardView>.Success(ToView(unitOfWork, card));
    }

    private Result<BankCardView> SetCashCardLock(
        IBankingUnitOfWork unitOfWork,
        SetCashCardLockCommand command)
    {
        Result<BankCard> resolved = ResolveCard(
            unitOfWork, command.CustomerAccountId, command.DepositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCardView>.Failure(resolved.Error!);
        }

        BankCard card = resolved.Value;

        if (unitOfWork.BankCards.FindCashCardByBankCard(card.Id) is not { } cash ||
            cash.Status == CashCardStatus.Closed)
        {
            return Result<BankCardView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CashCardNotFound);
        }

        CashCardStatus desired = command.Locked ? CashCardStatus.Locked : CashCardStatus.Active;

        if (cash.Status != desired)
        {
            if (command.Locked)
            {
                cash.Lock();
            }
            else
            {
                cash.Unlock();
            }

            unitOfWork.BankCards.UpdateCashCard(cash);
        }

        return Result<BankCardView>.Success(ToView(unitOfWork, card));
    }

    private Result<BankCardView> SetDebitCardLock(
        IBankingUnitOfWork unitOfWork,
        SetDebitCardLockCommand command)
    {
        Result<BankCard> resolved = ResolveCard(
            unitOfWork, command.CustomerAccountId, command.DepositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCardView>.Failure(resolved.Error!);
        }

        BankCard card = resolved.Value;

        if (unitOfWork.BankCards.FindDebitCardByBankCard(card.Id) is not { } debit ||
            debit.Status == DebitCardStatus.Closed)
        {
            return Result<BankCardView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DebitCardNotFound);
        }

        DebitCardStatus desired = command.Locked ? DebitCardStatus.Locked : DebitCardStatus.Active;

        if (debit.Status != desired)
        {
            if (command.Locked)
            {
                debit.Lock();
            }
            else
            {
                debit.Unlock();
            }

            unitOfWork.BankCards.UpdateDebitCard(debit);
        }

        return Result<BankCardView>.Success(ToView(unitOfWork, card));
    }

    private static Result<BankCardView> Read(
        IBankingUnitOfWork unitOfWork,
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId)
    {
        Result<BankCard> resolved = ResolveCard(unitOfWork, customerAccountId, depositAccountId);

        return resolved.IsSuccess
            ? Result<BankCardView>.Success(ToView(unitOfWork, resolved.Value))
            : Result<BankCardView>.Failure(resolved.Error!);
    }

    private static Result<BankCardRenderModel> Render(
        IBankingUnitOfWork unitOfWork,
        RenderBankCardCommand command)
    {
        Result<BankCard> resolved = ResolveCard(
            unitOfWork, command.CustomerAccountId, command.DepositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCardRenderModel>.Failure(resolved.Error!);
        }

        BankCard card = resolved.Value;
        Bank? bank = unitOfWork.Banks.Find(card.BankId);
        DebitCard? debit = unitOfWork.BankCards.FindDebitCardByBankCard(card.Id);
        CustomerAccount? customer = unitOfWork.CustomerAccounts.Find(command.CustomerAccountId);

        return Result<BankCardRenderModel>.Success(new BankCardRenderModel(
            bank is { } resolvedBank ? resolvedBank.InstitutionCode.Value : string.Empty,
            bank is { } namedBank ? namedBank.Name.Value : string.Empty,
            customer is { } holder ? holder.DisplayName.Value : string.Empty,
            card.Form,
            card.DisplayIdentifier,
            debit is { Status: not DebitCardStatus.Closed } ? debit.DisplayNumber : null,
            card.ExpiresAt?.UnixMilliseconds));
    }

    private static Result<BankCard> ResolveCard(
        IBankingUnitOfWork unitOfWork,
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId)
    {
        Result<DepositAccount> resolved = ResolveAccount(unitOfWork, customerAccountId, depositAccountId);

        if (!resolved.IsSuccess)
        {
            return Result<BankCard>.Failure(resolved.Error!);
        }

        return unitOfWork.BankCards.FindUsableByAccount(resolved.Value.Id) is { } card
            ? Result<BankCard>.Success(card)
            : Result<BankCard>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankCardNotFound);
    }

    private static Result<DepositAccount> ResolveAccount(
        IBankingUnitOfWork unitOfWork,
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId)
    {
        DepositAccount? account = unitOfWork.DepositAccounts.Find(depositAccountId);

        if (account is null || account.CustomerAccountId != customerAccountId)
        {
            ApplicationError error = TargetAccessPolicy.ToError(
                TargetAccess.NotOwned,
                BankingErrorCodes.DepositAccountNotFound,
                BankingErrorCodes.DepositAccountNotOperable);

            return Result<DepositAccount>.Failure(error.Category, error.Code);
        }

        return Result<DepositAccount>.Success(account!);
    }

    private static BankCardView ToView(
        IBankingUnitOfWork unitOfWork,
        BankCard card,
        CashCardStatus? cashStatus = null,
        DebitCardStatus? debitStatus = null)
    {
        CashCardStatus? cash = cashStatus
            ?? unitOfWork.BankCards.FindCashCardByBankCard(card.Id)?.Status;

        DebitCardStatus? debit = debitStatus
            ?? unitOfWork.BankCards.FindDebitCardByBankCard(card.Id)?.Status;

        return new BankCardView(
            card.Id,
            unitOfWork.Banks.Find(card.BankId) is { } bank ? bank.InstitutionCode.Value : string.Empty,
            card.Form,
            card.Status,
            cash,
            debit,
            card.DisplayIdentifier,
            card.ExpiresAt?.UnixMilliseconds);
    }

    private static UtcTimestamp Expiry(UtcTimestamp issuedAt) =>
        UtcTimestamp.FromUnixMilliseconds(
            DateTimeOffset.FromUnixTimeMilliseconds(issuedAt.UnixMilliseconds)
                .AddMonths(DebitValidityMonths)
                .ToUnixTimeMilliseconds());

    private static string DisplayIdentifier(BankCardId id) =>
        id.Value.ToString()[^8..].ToUpperInvariant();

    private static string DisplayNumber(BankCardId id) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"****{id.Value.ToString()[^4..].ToUpperInvariant()}");
}
