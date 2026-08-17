using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Common;

namespace Numera.Discord.Sessions;

public enum SessionVerification
{
    Accepted = 0,
    NotFound = 1,
    NotActive = 2,
    Expired = 3,
    UserMismatch = 4,
    GuildMismatch = 5,
    ScopeMismatch = 6,
    StateMismatch = 7,
    VersionMismatch = 8,
}

public sealed record InteractionSessionTicket(InteractionSessionId Id, string RawToken);

public sealed record InteractionSessionSnapshot(
    InteractionSessionId Id,
    string FlowType,
    string State,
    string PayloadJson,
    long StateVersion);

public sealed record OpenInteractionSessionRequest(
    ulong DiscordUserId,
    ulong GuildId,
    EconomyScopeId EconomyScopeId,
    string FlowType,
    string State,
    string PayloadJson);

public sealed record ConsumeInteractionSessionRequest(
    string RawToken,
    ulong DiscordUserId,
    ulong GuildId,
    EconomyScopeId EconomyScopeId,
    string ExpectedState,
    long ExpectedStateVersion);

public sealed class InteractionSessionService
{
    public const int RawTokenByteLength = 32;
    public const int RawTokenTextLength = 43;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;
    private readonly TimeSpan lifetime;

    public InteractionSessionService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
        this.lifetime = lifetime ?? TimeSpan.FromMinutes(InteractionSession.DefaultLifetimeMinutes);
    }

    public static string CreateRawToken()
    {
        Span<byte> buffer = stackalloc byte[RawTokenByteLength];
        RandomNumberGenerator.Fill(buffer);
        return Base64Url.EncodeToString(buffer);
    }

    public static bool TryComputeTokenHash(string rawToken, out byte[] hash)
    {
        hash = [];

        if (string.IsNullOrEmpty(rawToken) || rawToken.Length != RawTokenTextLength)
        {
            return false;
        }

        foreach (char character in rawToken)
        {
            bool permitted = character is (>= 'A' and <= 'Z')
                or (>= 'a' and <= 'z')
                or (>= '0' and <= '9')
                or '-' or '_';

            if (!permitted)
            {
                return false;
            }
        }

        Span<byte> decoded = stackalloc byte[RawTokenByteLength];
        if (!Base64Url.TryDecodeFromChars(rawToken, decoded, out int written) || written != RawTokenByteLength)
        {
            return false;
        }

        hash = SHA256.HashData(decoded);
        return true;
    }

    public static SessionVerification Verify(
        InteractionSession? session,
        ConsumeInteractionSessionRequest request,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session is null)
        {
            return SessionVerification.NotFound;
        }

        if (!session.IsActive)
        {
            return SessionVerification.NotActive;
        }

        if (session.HasExpired(now))
        {
            return SessionVerification.Expired;
        }

        if (!string.Equals(session.DiscordUserId, Text(request.DiscordUserId), StringComparison.Ordinal))
        {
            return SessionVerification.UserMismatch;
        }

        if (!string.Equals(session.GuildId, Text(request.GuildId), StringComparison.Ordinal))
        {
            return SessionVerification.GuildMismatch;
        }

        if (session.EconomyScopeId != request.EconomyScopeId)
        {
            return SessionVerification.ScopeMismatch;
        }

        if (!string.Equals(session.State, request.ExpectedState, StringComparison.Ordinal))
        {
            return SessionVerification.StateMismatch;
        }

        return session.StateVersion == request.ExpectedStateVersion
            ? SessionVerification.Accepted
            : SessionVerification.VersionMismatch;
    }

    public static ApplicationError ToError(SessionVerification verification) => verification switch
    {
        SessionVerification.Expired or SessionVerification.NotActive => ApplicationError.Create(
            ErrorCategory.OperationExpired, BankingErrorCodes.SessionExpired),
        SessionVerification.StateMismatch or SessionVerification.VersionMismatch => ApplicationError.Create(
            ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification),
        _ => ApplicationError.Create(ErrorCategory.Forbidden, BankingErrorCodes.SessionInvalid),
    };

    public Task<Result<InteractionSessionTicket>> OpenAsync(
        OpenInteractionSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string rawToken = CreateRawToken();
        if (!TryComputeTokenHash(rawToken, out byte[] tokenHash))
        {
            return Task.FromResult(Result<InteractionSessionTicket>.Failure(
                ErrorCategory.Unexpected, ErrorCodeFormat.Compose(ErrorCategory.Unexpected, 1)));
        }

        UtcTimestamp now = clock.Now();
        UtcTimestamp expiresAt = now.Add(lifetime);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                string owner = Text(request.DiscordUserId);
                IReadOnlyList<InteractionSession> active = unitOfWork.InteractionSessions.ListActiveByUser(owner);

                for (int index = 0; index <= active.Count - InteractionSession.MaximumActivePerUser; index++)
                {
                    InteractionSession oldest = active[index];
                    oldest.Supersede(now);
                    unitOfWork.InteractionSessions.Update(oldest);
                }

                InteractionSession session = InteractionSession.Open(
                    InteractionSessionId.FromValue(idGenerator.NextId()),
                    owner,
                    Text(request.GuildId),
                    request.EconomyScopeId,
                    request.FlowType,
                    request.State,
                    tokenHash,
                    request.PayloadJson,
                    now,
                    expiresAt);

                unitOfWork.InteractionSessions.Add(session);

                return Result<InteractionSessionTicket>.Success(
                    new InteractionSessionTicket(session.Id, rawToken));
            },
            cancellationToken);
    }

    public Task<Result<InteractionSessionSnapshot>> ConsumeAsync(
        ConsumeInteractionSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryComputeTokenHash(request.RawToken, out byte[] tokenHash))
        {
            return Task.FromResult(Result<InteractionSessionSnapshot>.Failure(
                ToError(SessionVerification.NotFound)));
        }

        UtcTimestamp now = clock.Now();

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                InteractionSession? session = unitOfWork.InteractionSessions.FindByTokenHash(tokenHash);
                SessionVerification verification = Verify(session, request, now);

                if (verification != SessionVerification.Accepted)
                {
                    return Result<InteractionSessionSnapshot>.Failure(ToError(verification));
                }

                return Result<InteractionSessionSnapshot>.Success(new InteractionSessionSnapshot(
                    session!.Id, session.FlowType, session.State, session.PayloadJson, session.StateVersion));
            },
            cancellationToken);
    }

    public Task<Result<InteractionSessionSnapshot>> AdvanceAsync(
        ConsumeInteractionSessionRequest request,
        string nextState,
        string nextPayloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextState);
        ArgumentNullException.ThrowIfNull(nextPayloadJson);

        if (!TryComputeTokenHash(request.RawToken, out byte[] tokenHash))
        {
            return Task.FromResult(Result<InteractionSessionSnapshot>.Failure(
                ToError(SessionVerification.NotFound)));
        }

        UtcTimestamp now = clock.Now();

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                InteractionSession? session = unitOfWork.InteractionSessions.FindByTokenHash(tokenHash);
                SessionVerification verification = Verify(session, request, now);

                if (verification != SessionVerification.Accepted)
                {
                    return Result<InteractionSessionSnapshot>.Failure(ToError(verification));
                }

                session!.Advance(nextState, nextPayloadJson);
                unitOfWork.InteractionSessions.Update(session);

                return Result<InteractionSessionSnapshot>.Success(new InteractionSessionSnapshot(
                    session.Id, session.FlowType, session.State, session.PayloadJson, session.StateVersion));
            },
            cancellationToken);
    }

    public Task<Result<InteractionSessionSnapshot>> CompleteAsync(
        ConsumeInteractionSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryComputeTokenHash(request.RawToken, out byte[] tokenHash))
        {
            return Task.FromResult(Result<InteractionSessionSnapshot>.Failure(
                ToError(SessionVerification.NotFound)));
        }

        UtcTimestamp now = clock.Now();

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                InteractionSession? session = unitOfWork.InteractionSessions.FindByTokenHash(tokenHash);
                SessionVerification verification = Verify(session, request, now);

                if (verification != SessionVerification.Accepted)
                {
                    return Result<InteractionSessionSnapshot>.Failure(ToError(verification));
                }

                session!.Complete(now);
                unitOfWork.InteractionSessions.Update(session);

                return Result<InteractionSessionSnapshot>.Success(new InteractionSessionSnapshot(
                    session.Id, session.FlowType, session.State, session.PayloadJson, session.StateVersion));
            },
            cancellationToken);
    }

    public Task<Result<int>> ExpireStaleAsync(int batchSize, CancellationToken cancellationToken)
    {
        UtcTimestamp now = clock.Now();

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                IReadOnlyList<InteractionSession> stale =
                    unitOfWork.InteractionSessions.ListExpired(now, batchSize);

                foreach (InteractionSession session in stale)
                {
                    session.Expire(now);
                    unitOfWork.InteractionSessions.Update(session);
                }

                return Result<int>.Success(stale.Count);
            },
            cancellationToken);
    }

    public Task<Result<int>> PurgeTerminalAsync(
        TimeSpan retention,
        int batchSize,
        CancellationToken cancellationToken)
    {
        UtcTimestamp threshold = clock.Now().Add(-retention);

        return writeGateway.ExecuteAsync(
            unitOfWork => Result<int>.Success(
                unitOfWork.InteractionSessions.PurgeTerminal(threshold, batchSize)),
            cancellationToken);
    }

    private static string Text(ulong value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
