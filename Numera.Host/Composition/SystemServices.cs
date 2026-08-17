using Numera.Application.Common;
using Numera.Domain.Common;

namespace Numera.Host.Composition;

internal sealed class SystemClock : IClock
{
    private readonly TimeProvider timeProvider;

    internal SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public UtcTimestamp Now() => UtcTimestamp.FromDateTimeOffset(timeProvider.GetUtcNow());
}

internal static class IdentifierFailure
{
    internal const string GenerationFailed = "A version 7 identifier could not be written as 16 big-endian bytes.";
}

internal sealed class UuidVersion7IdGenerator : IIdGenerator
{
    internal const int ByteLength = 16;

    public EntityIdValue NextId()
    {
        Span<byte> buffer = stackalloc byte[ByteLength];

        if (!Guid.CreateVersion7().TryWriteBytes(buffer, bigEndian: true, out int written) || written != ByteLength)
        {
            throw new InvalidOperationException(IdentifierFailure.GenerationFailed);
        }

        return EntityIdValue.FromBytes(buffer);
    }
}
