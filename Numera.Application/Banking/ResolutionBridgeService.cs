using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

internal sealed class ResolutionBridgeService
{
    private const string ReserveAccountCode = "1100";
    private const string FeeRevenueAccountCode = "4300";
    private const string EstateAccountCode = "5900";
    private const string BranchCode = "001";
    private const string PeriodKey = "RESOLUTION";

    private readonly IIdGenerator idGenerator;

    internal ResolutionBridgeService(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.idGenerator = idGenerator;
    }

    internal Result<Bank> Establish(
        IBankingUnitOfWork unitOfWork,
        ResolutionCaseRecord resolution,
        Bank failing,
        UtcTimestamp now)
    {
        if (unitOfWork.LedgerAccounts.FindByCode(
                failing.GeneralLedgerBookId, AccountOpeningWorkflow.DemandDepositControlCode)
            is not { } failingControl)
        {
            return Result<Bank>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.ControlAccountUnavailable);
        }

        if (failing.CurrentPolicyVersionId is not { } policyVersionId ||
            failing.CurrentFeeScheduleVersionId is not { } feeScheduleVersionId)
        {
            return Result<Bank>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankPolicyUnavailable);
        }

        Party party = Party.Create(
            PartyId.FromValue(idGenerator.NextId()),
            PartyType.Bank,
            DisplayName.Parse(BridgeName(resolution.Id)),
            now);

        AccountingBookId bookId = AccountingBookId.FromValue(idGenerator.NextId());

        Bank bridge = Bank.EstablishBridge(
            BankId.FromValue(idGenerator.NextId()),
            failing.EconomyScopeId,
            party.Id,
            InstitutionCode.Parse(BridgeCode(unitOfWork, failing.EconomyScopeId)),
            BankName.Parse(BridgeName(resolution.Id)),
            bookId,
            resolution.Id,
            policyVersionId,
            feeScheduleVersionId,
            now);

        unitOfWork.Parties.Add(party);
        unitOfWork.BankAdministration.AddAccountingBook(bookId, party.Id, now);
        unitOfWork.BankAdministration.AddBank(bridge);

        BranchId branchId = BranchId.FromValue(idGenerator.NextId());

        unitOfWork.BankAdministration.AddBranch(branchId, bridge.Id, BranchCode, BridgeName(resolution.Id), now);

        unitOfWork.LedgerAccounts.Add(LedgerAccount.CreateControl(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bookId,
            parentAccountId: null,
            AccountOpeningWorkflow.DemandDepositControlCode,
            LedgerAccountKind.DemandDepositControl,
            failingControl.CurrencyId));

        Add(unitOfWork, bridge, failingControl.CurrencyId, ReserveAccountCode,
            LedgerAccountKind.CentralBankReserveAsset);
        Add(unitOfWork, bridge, failingControl.CurrencyId, FeeRevenueAccountCode,
            LedgerAccountKind.FeeRevenue);
        Add(unitOfWork, bridge, failingControl.CurrencyId, EstateAccountCode,
            LedgerAccountKind.ResolutionLossExpense);

        AccountProductId productId = AccountProductId.FromValue(idGenerator.NextId());

        unitOfWork.BankAdministration.AddAccountProduct(
            productId, bridge.Id, "BRIDGE01", "承継普通預金", now);
        unitOfWork.BankAdministration.AddAccountProductVersion(
            AccountProductVersionId.FromValue(idGenerator.NextId()), productId, MoneyMinor.Zero, now);

        unitOfWork.AccountingPeriods.Open(
            AccountingPeriodId.FromValue(idGenerator.NextId()),
            bookId,
            PeriodKey,
            BusinessDate.FromDayNumber(DateOnly.MinValue.DayNumber),
            BusinessDate.FromDayNumber(DateOnly.MaxValue.DayNumber));

        return Result<Bank>.Success(bridge);
    }

    private void Add(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId,
        string accountCode,
        LedgerAccountKind kind) =>
        unitOfWork.LedgerAccounts.Add(LedgerAccount.CreatePosting(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bank.GeneralLedgerBookId,
            parentAccountId: null,
            accountCode,
            kind,
            currencyId,
            LedgerOwnerReferenceType.None,
            bank.Id.Value));

    private static string BridgeName(ResolutionCaseId id) => string.Create(
        CultureInfo.InvariantCulture, $"承継銀行{id.Value.ToString()[..4]}");

    private static string BridgeCode(IBankingUnitOfWork unitOfWork, EconomyScopeId economyScopeId)
    {
        for (int sequence = 1; sequence <= 9999; sequence++)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"BRG{sequence:D4}");

            if (unitOfWork.Banks.FindByInstitutionCode(economyScopeId, candidate) is null)
            {
                return candidate;
            }
        }

        return "BRG9999";
    }
}
