using Numera.Domain.Common;

namespace Numera.Application.Common;

public interface IClock
{
    UtcTimestamp Now();
}

public interface IIdGenerator
{
    EntityIdValue NextId();
}

public interface IIdempotencyKeyFactory
{
    string Create(string scope, ReadOnlySpan<char> discriminator);
}
