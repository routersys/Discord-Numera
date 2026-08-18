using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum SavedBeneficiaryStatus
{
    Active = 1,
    Hidden = 2,
    Invalid = 3,
}

public sealed class SavedBeneficiary : VersionedEntity
{
    public const int MaximumDisplayNameLength = 64;

    private static readonly StateTransitionTable<SavedBeneficiaryStatus> Transitions =
        StateTransitionTable<SavedBeneficiaryStatus>
            .Create(InvariantViolationCode.SavedBeneficiaryTransitionInvalid)
            .AllowCreation(SavedBeneficiaryStatus.Active)
            .Allow(SavedBeneficiaryStatus.Active, SavedBeneficiaryStatus.Hidden, SavedBeneficiaryStatus.Invalid)
            .Allow(SavedBeneficiaryStatus.Hidden, SavedBeneficiaryStatus.Active, SavedBeneficiaryStatus.Invalid)
            .Build();

    private SavedBeneficiary(
        SavedBeneficiaryId id,
        CustomerAccountId customerAccountId,
        DepositAccountId destinationDepositAccountId,
        string displayName,
        string institutionCodeSnapshot,
        string branchCodeSnapshot,
        string accountNumberSnapshot,
        SavedBeneficiaryStatus status,
        UtcTimestamp createdAt,
        long version)
        : base(version)
    {
        Id = id;
        CustomerAccountId = customerAccountId;
        DestinationDepositAccountId = destinationDepositAccountId;
        DisplayName = displayName;
        InstitutionCodeSnapshot = institutionCodeSnapshot;
        BranchCodeSnapshot = branchCodeSnapshot;
        AccountNumberSnapshot = accountNumberSnapshot;
        Status = status;
        CreatedAt = createdAt;
    }

    public SavedBeneficiaryId Id { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public DepositAccountId DestinationDepositAccountId { get; }

    public string DisplayName { get; }

    public string InstitutionCodeSnapshot { get; }

    public string BranchCodeSnapshot { get; }

    public string AccountNumberSnapshot { get; }

    public SavedBeneficiaryStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public static SavedBeneficiary Save(
        SavedBeneficiaryId id,
        CustomerAccountId customerAccountId,
        DepositAccountId destinationDepositAccountId,
        string displayName,
        string institutionCodeSnapshot,
        string branchCodeSnapshot,
        string accountNumberSnapshot,
        UtcTimestamp createdAt)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumDisplayNameLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.SavedBeneficiaryNameInvalid);
        }

        return new SavedBeneficiary(
            id,
            customerAccountId,
            destinationDepositAccountId,
            displayName,
            institutionCodeSnapshot,
            branchCodeSnapshot,
            accountNumberSnapshot,
            SavedBeneficiaryStatus.Active,
            createdAt,
            InitialVersion);
    }

    public static SavedBeneficiary Rehydrate(
        SavedBeneficiaryId id,
        CustomerAccountId customerAccountId,
        DepositAccountId destinationDepositAccountId,
        string displayName,
        string institutionCodeSnapshot,
        string branchCodeSnapshot,
        string accountNumberSnapshot,
        SavedBeneficiaryStatus status,
        UtcTimestamp createdAt,
        long version) =>
        new(
            id,
            customerAccountId,
            destinationDepositAccountId,
            displayName,
            institutionCodeSnapshot,
            branchCodeSnapshot,
            accountNumberSnapshot,
            status,
            createdAt,
            version);

    public void Hide()
    {
        Transitions.EnsureAllowed(Status, SavedBeneficiaryStatus.Hidden);

        Status = SavedBeneficiaryStatus.Hidden;
        AdvanceVersion();
    }

    public void Restore()
    {
        Transitions.EnsureAllowed(Status, SavedBeneficiaryStatus.Active);

        Status = SavedBeneficiaryStatus.Active;
        AdvanceVersion();
    }

    public void Invalidate()
    {
        Transitions.EnsureAllowed(Status, SavedBeneficiaryStatus.Invalid);

        Status = SavedBeneficiaryStatus.Invalid;
        AdvanceVersion();
    }
}

public static class SavedBeneficiaryCatalog
{
    public static string ToToken(this SavedBeneficiaryStatus status) => status switch
    {
        SavedBeneficiaryStatus.Active => "ACTIVE",
        SavedBeneficiaryStatus.Hidden => "HIDDEN",
        SavedBeneficiaryStatus.Invalid => "INVALID",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out SavedBeneficiaryStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = SavedBeneficiaryStatus.Active;
                return true;
            case "HIDDEN":
                status = SavedBeneficiaryStatus.Hidden;
                return true;
            case "INVALID":
                status = SavedBeneficiaryStatus.Invalid;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static SavedBeneficiaryStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out SavedBeneficiaryStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.SavedBeneficiaryStatusUnknown);
}
