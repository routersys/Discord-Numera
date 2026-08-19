using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record AtmInstallationRecoveryReport(int Examined, int Confirmed, int Broken);

public sealed class AtmInstallationRecoveryService
{
    public const int BatchSize = 25;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IAtmInstallationMessageGateway messages;

    public AtmInstallationRecoveryService(
        IBankingWriteGateway writeGateway,
        IAtmInstallationMessageGateway messages)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(messages);

        this.writeGateway = writeGateway;
        this.messages = messages;
    }

    public async Task<AtmInstallationRecoveryReport> ScanAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AtmDiscordInstallationRecord>> listed = await writeGateway
            .ExecuteAsync(Claim, cancellationToken)
            .ConfigureAwait(false);

        if (!listed.IsSuccess)
        {
            return new AtmInstallationRecoveryReport(0, 0, 0);
        }

        int confirmed = 0;
        int broken = 0;

        foreach (AtmDiscordInstallationRecord installation in listed.Value)
        {
            AtmInstallationMessageState state = await messages.ConfirmAsync(
                installation.GuildId,
                installation.ChannelId,
                installation.MessageId,
                installation.InstallationNonce,
                cancellationToken).ConfigureAwait(false);

            if (state == AtmInstallationMessageState.Confirmed)
            {
                confirmed++;
                continue;
            }

            if (state == AtmInstallationMessageState.Unknown)
            {
                continue;
            }

            Result<bool> marked = await writeGateway
                .ExecuteAsync(unitOfWork => Break(unitOfWork, installation.Id), cancellationToken)
                .ConfigureAwait(false);

            if (marked.IsSuccess && marked.Value)
            {
                broken++;
            }
        }

        return new AtmInstallationRecoveryReport(listed.Value.Count, confirmed, broken);
    }

    private static Result<IReadOnlyList<AtmDiscordInstallationRecord>> Claim(
        IBankingUnitOfWork unitOfWork) =>
        Result<IReadOnlyList<AtmDiscordInstallationRecord>>.Success(
            unitOfWork.Cash.ListActiveInstallations(BatchSize));

    private static Result<bool> Break(
        IBankingUnitOfWork unitOfWork,
        AtmDiscordInstallationId id)
    {
        if (unitOfWork.Cash.FindInstallation(id) is not { } installation ||
            !AtmDiscordInstallationStatusCatalog.IsAllowed(
                installation.Status, AtmDiscordInstallationStatus.Broken))
        {
            return Result<bool>.Success(false);
        }

        AtmDiscordInstallationStatusCatalog.EnsureTransition(
            installation.Status, AtmDiscordInstallationStatus.Broken);

        unitOfWork.Cash.UpdateInstallation(installation with
        {
            Status = AtmDiscordInstallationStatus.Broken,
            Version = installation.Version + 1,
        });

        return Result<bool>.Success(true);
    }
}
