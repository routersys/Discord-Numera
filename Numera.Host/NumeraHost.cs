namespace Numera.Host;

internal static class NumeraHost
{
    internal static Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Task.FromResult(0);
    }
}
