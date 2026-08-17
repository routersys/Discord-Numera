using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal readonly record struct PaymentRoute(
    SettlementMode Mode,
    BeneficiaryPostingPolicy PostingPolicy,
    PaymentNetworkPolicyVersionId? PolicyVersionId)
{
    internal static PaymentRoute Internal => new(
        SettlementMode.Internal, BeneficiaryPostingPolicy.ImmediateAfterAcceptance, PolicyVersionId: null);

    internal string Method => Mode switch
    {
        SettlementMode.Internal => PaymentApplicationService.PaymentMethod,
        SettlementMode.Clearing => PaymentApplicationService.ClearingPaymentMethod,
        _ => PaymentApplicationService.InterbankPaymentMethod,
    };
}

internal static class PaymentRoutePolicy
{
    internal static Result<PaymentRoute> Resolve(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        bool interbank,
        MoneyMinor amount)
    {
        if (!interbank)
        {
            return Result<PaymentRoute>.Success(PaymentRoute.Internal);
        }

        PaymentNetwork? network = unitOfWork.PaymentNetworks.FindRouting(economyScopeId);

        if (network is not { RoutesPayments: true } routing)
        {
            return Result<PaymentRoute>.Success(new PaymentRoute(
                SettlementMode.Rtgs,
                BeneficiaryPostingPolicy.AfterFinalSettlement,
                PolicyVersionId: null));
        }

        PaymentNetworkPolicyVersionId policyVersionId = routing.CurrentPolicyVersionId!.Value;

        if (unitOfWork.PaymentNetworks.FindPolicy(policyVersionId) is not { } policy)
        {
            return Result<PaymentRoute>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PaymentNetworkPolicyUnavailable);
        }

        SettlementMode mode = policy.ResolveSettlementMode(amount);

        return mode == SettlementMode.Rtgs
            ? Result<PaymentRoute>.Success(new PaymentRoute(
                SettlementMode.Rtgs, BeneficiaryPostingPolicy.AfterFinalSettlement, policyVersionId))
            : Result<PaymentRoute>.Success(new PaymentRoute(
                SettlementMode.Clearing,
                policy.BeneficiaryPostingPolicy,
                policyVersionId));
    }

    internal static bool CoversPreCredit(
        IBankingUnitOfWork unitOfWork,
        PaymentNetwork network,
        PaymentNetworkPolicyVersion policy,
        BankId sourceBankId,
        CurrencyId currencyId,
        MoneyMinor amount)
    {
        if (policy.BeneficiaryPostingPolicy != BeneficiaryPostingPolicy.GuaranteedPreCredit)
        {
            return false;
        }

        if (unitOfWork.PaymentNetworks.FindPrefund(network.Id, sourceBankId, currencyId) is not { } prefund)
        {
            return false;
        }

        MoneyMinor exposure = unitOfWork.PaymentOrders.SumUnfinalisedPreCreditExposure(sourceBankId);

        if (exposure.Add(amount).Value > policy.PerBankPrecreditExposureLimit.Value)
        {
            return false;
        }

        MoneyMinor posted =
            (unitOfWork.LedgerAccounts.FindProjection(prefund.PrefundLiabilityLedgerAccountId)
                ?? LedgerBalance.Empty).PostedBalance;

        return prefund.AvailableAmount(posted, exposure).Value >= policy.RequiredPrefund(amount).Value;
    }

    internal static string CycleKeyOf(PaymentNetworkPolicyVersion policy, UtcTimestamp at)
    {
        long interval = policy.ClearingCycleIntervalSeconds
            ?? PaymentNetworkPolicyVersion.MaximumClearingCycleIntervalSeconds;

        long seconds = at.UnixMilliseconds / 1000;

        return (seconds - (seconds % interval)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
