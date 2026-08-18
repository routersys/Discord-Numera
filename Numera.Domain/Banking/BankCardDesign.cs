using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum CardFaceMode
{
    Numbered = 1,
    Numberless = 2,
}

public enum BankCardDesignVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public enum CardFontWeight
{
    SemiBold = 1,
    Bold = 2,
}

public enum CardTextAlignment
{
    Left = 1,
    Center = 2,
    Right = 3,
}

public enum CardDisplayToken
{
    BankName = 1,
    BankCode = 2,
    CustomerDisplayName = 3,
    CardNumber = 4,
    CardLast4 = 5,
    CardExpiry = 6,
    CurrencyName = 7,
    CurrencyCode = 8,
    AccountMaskedNumber = 9,
}

public static class CardCanvas
{
    public const int Width = 1026;

    public const int Height = 647;

    public const int CornerRadius = 38;

    public const int SafeMargin = 72;

    public const int ScrimPadding = 16;

    public const int ScrimCornerRadius = 20;

    public const int MinimumFontSize = 16;

    public const int MaximumFontSize = 72;

    public const int MaximumTextSlots = 8;
}

public sealed record BankCardDesignTextSlot(
    BankCardDesignTextSlotId Id,
    BankCardDesignVersionId DesignVersionId,
    int SlotIndex,
    CardDisplayToken Token,
    int X,
    int Y,
    int Width,
    int Height,
    int FontSizePx,
    int MinimumFontSizePx,
    CardFontWeight FontWeight,
    CardTextAlignment HorizontalAlignment,
    bool LargeText,
    int? FixedTextRgb)
{
    public static BankCardDesignTextSlot Create(
        BankCardDesignTextSlotId id,
        BankCardDesignVersionId designVersionId,
        int slotIndex,
        CardDisplayToken token,
        int x,
        int y,
        int width,
        int height,
        int fontSizePx,
        int minimumFontSizePx,
        CardFontWeight fontWeight,
        CardTextAlignment horizontalAlignment,
        bool largeText,
        int? fixedTextRgb)
    {
        if (slotIndex is < 0 or >= CardCanvas.MaximumTextSlots)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignSlotIndexInvalid);
        }

        if (width <= 0 || height <= 0 || x < 0 || y < 0
            || x + width > CardCanvas.Width || y + height > CardCanvas.Height)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignSlotRectInvalid);
        }

        if (fontSizePx is < CardCanvas.MinimumFontSize or > CardCanvas.MaximumFontSize
            || minimumFontSizePx is < CardCanvas.MinimumFontSize or > CardCanvas.MaximumFontSize
            || minimumFontSizePx > fontSizePx)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignFontSizeInvalid);
        }

        if (fixedTextRgb is { } rgb && rgb is < 0 or > 0xFFFFFF)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignColorInvalid);
        }

        return new BankCardDesignTextSlot(
            id,
            designVersionId,
            slotIndex,
            token,
            x,
            y,
            width,
            height,
            fontSizePx,
            minimumFontSizePx,
            fontWeight,
            horizontalAlignment,
            largeText,
            fixedTextRgb);
    }
}

public sealed class BankCardDesignTemplateVersion : VersionedEntity
{
    private static readonly StateTransitionTable<BankCardDesignVersionStatus> Transitions =
        StateTransitionTable<BankCardDesignVersionStatus>
            .Create(InvariantViolationCode.CardDesignTransitionInvalid)
            .AllowCreation(BankCardDesignVersionStatus.Draft)
            .Allow(BankCardDesignVersionStatus.Draft, BankCardDesignVersionStatus.Published)
            .Allow(BankCardDesignVersionStatus.Published, BankCardDesignVersionStatus.Retired)
            .Build();

    private readonly List<BankCardDesignTextSlot> slots;

    private BankCardDesignTemplateVersion(
        BankCardDesignVersionId id,
        BankId bankId,
        CardFaceMode faceMode,
        BankCardDesignVersionStatus status,
        int backgroundRgb,
        IReadOnlyList<BankCardDesignTextSlot> slots,
        UtcTimestamp createdAt,
        UtcTimestamp? publishedAt,
        UtcTimestamp? retiredAt,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        FaceMode = faceMode;
        Status = status;
        BackgroundRgb = backgroundRgb;
        this.slots = [.. slots];
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        RetiredAt = retiredAt;
    }

    public BankCardDesignVersionId Id { get; }

    public BankId BankId { get; }

    public CardFaceMode FaceMode { get; }

    public BankCardDesignVersionStatus Status { get; private set; }

    public int BackgroundRgb { get; }

    public IReadOnlyList<BankCardDesignTextSlot> Slots => slots;

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? PublishedAt { get; private set; }

    public UtcTimestamp? RetiredAt { get; private set; }

    public static BankCardDesignTemplateVersion CreateDraft(
        BankCardDesignVersionId id,
        BankId bankId,
        CardFaceMode faceMode,
        int backgroundRgb,
        IReadOnlyList<BankCardDesignTextSlot> slots,
        BankCardForm form,
        UtcTimestamp createdAt)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (backgroundRgb is < 0 or > 0xFFFFFF)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignColorInvalid);
        }

        if (slots.Count > CardCanvas.MaximumTextSlots)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignSlotCountInvalid);
        }

        if (slots.Select(static slot => slot.SlotIndex).Distinct().Count() != slots.Count)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignSlotIndexInvalid);
        }

        EnsureRequiredTokens(slots, faceMode, form);

        return new BankCardDesignTemplateVersion(
            id,
            bankId,
            faceMode,
            BankCardDesignVersionStatus.Draft,
            backgroundRgb,
            slots,
            createdAt,
            publishedAt: null,
            retiredAt: null,
            InitialVersion);
    }

    public static BankCardDesignTemplateVersion Rehydrate(
        BankCardDesignVersionId id,
        BankId bankId,
        CardFaceMode faceMode,
        BankCardDesignVersionStatus status,
        int backgroundRgb,
        IReadOnlyList<BankCardDesignTextSlot> slots,
        UtcTimestamp createdAt,
        UtcTimestamp? publishedAt,
        UtcTimestamp? retiredAt,
        long version) =>
        new(id, bankId, faceMode, status, backgroundRgb, slots, createdAt, publishedAt, retiredAt, version);

    public void Publish(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, BankCardDesignVersionStatus.Published);

        Status = BankCardDesignVersionStatus.Published;
        PublishedAt = now;
        AdvanceVersion();
    }

    public void Retire(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, BankCardDesignVersionStatus.Retired);

        Status = BankCardDesignVersionStatus.Retired;
        RetiredAt = now;
        AdvanceVersion();
    }

    private static void EnsureRequiredTokens(
        IReadOnlyList<BankCardDesignTextSlot> slots,
        CardFaceMode faceMode,
        BankCardForm form)
    {
        CardDisplayToken[] present = [.. slots.Select(static slot => slot.Token)];

        if (!present.Contains(CardDisplayToken.BankName)
            || !present.Contains(CardDisplayToken.CustomerDisplayName))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignRequiredTokenMissing);
        }

        bool carriesDebit = form is BankCardForm.DebitOnly or BankCardForm.IntegratedCashDebit;

        if (carriesDebit && faceMode == CardFaceMode.Numbered
            && (!present.Contains(CardDisplayToken.CardNumber)
                || !present.Contains(CardDisplayToken.CardExpiry)))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignRequiredTokenMissing);
        }

        if (!carriesDebit
            && (present.Contains(CardDisplayToken.CardNumber)
                || present.Contains(CardDisplayToken.CardExpiry)))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CardDesignDebitTokenNotAllowed);
        }
    }
}

public static class BankCardDesignCatalog
{
    public static string ToToken(this BankCardDesignVersionStatus status) => status switch
    {
        BankCardDesignVersionStatus.Draft => "DRAFT",
        BankCardDesignVersionStatus.Published => "PUBLISHED",
        BankCardDesignVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this CardFaceMode mode) => mode switch
    {
        CardFaceMode.Numbered => "NUMBERED",
        CardFaceMode.Numberless => "NUMBERLESS",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string ToToken(this CardFontWeight weight) => weight switch
    {
        CardFontWeight.SemiBold => "SEMIBOLD",
        CardFontWeight.Bold => "BOLD",
        _ => throw new ArgumentOutOfRangeException(nameof(weight)),
    };

    public static string ToToken(this CardTextAlignment alignment) => alignment switch
    {
        CardTextAlignment.Left => "LEFT",
        CardTextAlignment.Center => "CENTER",
        CardTextAlignment.Right => "RIGHT",
        _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
    };

    public static string ToToken(this CardDisplayToken token) => token switch
    {
        CardDisplayToken.BankName => "{bank.name}",
        CardDisplayToken.BankCode => "{bank.code}",
        CardDisplayToken.CustomerDisplayName => "{customer.display_name}",
        CardDisplayToken.CardNumber => "{card.number}",
        CardDisplayToken.CardLast4 => "{card.last4}",
        CardDisplayToken.CardExpiry => "{card.expiry}",
        CardDisplayToken.CurrencyName => "{currency.name}",
        CardDisplayToken.CurrencyCode => "{currency.code}",
        CardDisplayToken.AccountMaskedNumber => "{account.masked_number}",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BankCardDesignVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = BankCardDesignVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = BankCardDesignVersionStatus.Published;
                return true;
            case "RETIRED":
                status = BankCardDesignVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BankCardDesignVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BankCardDesignVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CardDesignStatusUnknown);

    public static CardFaceMode ParseFaceModeToken(ReadOnlySpan<char> token) => token switch
    {
        "NUMBERED" => CardFaceMode.Numbered,
        "NUMBERLESS" => CardFaceMode.Numberless,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CardDesignFaceModeUnknown),
    };

    public static CardFontWeight ParseFontWeightToken(ReadOnlySpan<char> token) => token switch
    {
        "SEMIBOLD" => CardFontWeight.SemiBold,
        "BOLD" => CardFontWeight.Bold,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CardDesignFontWeightUnknown),
    };

    public static CardTextAlignment ParseAlignmentToken(ReadOnlySpan<char> token) => token switch
    {
        "LEFT" => CardTextAlignment.Left,
        "CENTER" => CardTextAlignment.Center,
        "RIGHT" => CardTextAlignment.Right,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CardDesignAlignmentUnknown),
    };

    public static CardDisplayToken ParseDisplayToken(ReadOnlySpan<char> token) => token switch
    {
        "{bank.name}" => CardDisplayToken.BankName,
        "{bank.code}" => CardDisplayToken.BankCode,
        "{customer.display_name}" => CardDisplayToken.CustomerDisplayName,
        "{card.number}" => CardDisplayToken.CardNumber,
        "{card.last4}" => CardDisplayToken.CardLast4,
        "{card.expiry}" => CardDisplayToken.CardExpiry,
        "{currency.name}" => CardDisplayToken.CurrencyName,
        "{currency.code}" => CardDisplayToken.CurrencyCode,
        "{account.masked_number}" => CardDisplayToken.AccountMaskedNumber,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CardDesignDisplayTokenUnknown),
    };
}
