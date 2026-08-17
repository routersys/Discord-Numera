namespace Numera.Host;

internal static class Program
{
    private static Task<int> Main(string[] args) => NumeraHost.RunAsync(args);
}
