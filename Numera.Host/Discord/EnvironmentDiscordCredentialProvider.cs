using Numera.Application.Common;
using Numera.Discord.Abstractions;

namespace Numera.Host.Discord;

internal sealed class DiscordCredentialMissingException : Exception
{
    internal DiscordCredentialMissingException()
        : base(BankingErrorCodes.DiscordCredentialMissing)
    {
    }

    internal string Code => BankingErrorCodes.DiscordCredentialMissing;
}

internal sealed class EnvironmentDiscordCredentialProvider : IDiscordCredentialProvider
{
    internal const string EnvironmentVariable = "DISCORD_TOKEN";

    private string? token;

    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (token is not null)
        {
            return ValueTask.FromResult(token);
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            token = fromEnvironment.Trim();

            return ValueTask.FromResult(token);
        }

        if (System.Console.IsInputRedirected)
        {
            throw new DiscordCredentialMissingException();
        }

        string entered = HiddenConsoleInput.Read();

        if (entered.Length == 0)
        {
            throw new DiscordCredentialMissingException();
        }

        token = entered;

        return ValueTask.FromResult(token);
    }

    public void Clear() => token = null;
}

internal static class HiddenConsoleInput
{
    internal static string Read()
    {
        System.Text.StringBuilder builder = new();

        while (true)
        {
            ConsoleKeyInfo key = System.Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
    }
}
