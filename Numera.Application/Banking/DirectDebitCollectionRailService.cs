using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal sealed record DirectDebitCollectionRequest(
    DirectDebitMandateId DirectDebitMandateId,
    string CreditorCollectionReference,
    MoneyMinor Amount,
    UtcTimestamp ScheduledFor);

internal sealed class DirectDebitCollectionRailService
{
    private readonly IIdGenerator idGenerator;

    internal DirectDebitCollectionRailService(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.idGenerator = idGenerator;
    }

    internal Result<DirectDebitCollection> Request(
        IBankingUnitOfWork unitOfWork,
        DirectDebitCollectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(request);

        if (unitOfWork.PaymentManagement.FindMandate(request.DirectDebitMandateId) is not { } mandate)
        {
            return Result<DirectDebitCollection>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DirectDebitMandateNotFound);
        }

        if (!mandate.IsCollectable)
        {
            return Result<DirectDebitCollection>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DirectDebitMandateStateInvalid);
        }

        if (!request.Amount.IsPositive || request.Amount > mandate.SingleCollectionLimit)
        {
            return Result<DirectDebitCollection>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DirectDebitCollectionAmountInvalid);
        }

        if (unitOfWork.PaymentManagement.FindCollectionByReference(
                mandate.Id, request.CreditorCollectionReference) is { } duplicate)
        {
            return duplicate.Status == DirectDebitCollectionStatus.Pending
                ? Result<DirectDebitCollection>.Success(duplicate)
                : Result<DirectDebitCollection>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.DirectDebitCollectionReferenceDuplicated);
        }

        DirectDebitCollection collection;

        try
        {
            collection = DirectDebitCollection.Request(
                DirectDebitCollectionId.FromValue(idGenerator.NextId()),
                mandate.Id,
                request.CreditorCollectionReference,
                request.Amount,
                request.ScheduledFor);
        }
        catch (InvariantViolationException)
        {
            return Result<DirectDebitCollection>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DirectDebitCollectionAmountInvalid);
        }

        unitOfWork.PaymentManagement.AddCollection(collection);

        return Result<DirectDebitCollection>.Success(collection);
    }
}
