namespace Numera.Host.Console;

public enum ShellExitReason
{
    ShutdownRequested = 1,
    InputClosed = 2,
    Cancelled = 3,
}

public sealed record ShellSession(ShellExitReason Reason, int ExecutedCount, int FailedCount);

internal sealed class BootstrapShell
{
    private readonly ConsoleCommandExecutor executor;
    private readonly TextReader input;
    private readonly TextWriter output;

    internal BootstrapShell(ConsoleCommandExecutor executor, TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        this.executor = executor;
        this.input = input;
        this.output = output;
    }

    internal ShellSession Run(CancellationToken cancellationToken)
    {
        int executed = 0;
        int failed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            output.Write(ConsoleCommandLine.Prompt);

            if (input.ReadLine() is not { } line)
            {
                return new ShellSession(ShellExitReason.InputClosed, executed, failed);
            }

            ConsoleCommand command = ConsoleCommandLine.Parse(line);

            if (command.Kind == ConsoleCommandKind.Shutdown)
            {
                return new ShellSession(ShellExitReason.ShutdownRequested, executed, failed);
            }

            ConsoleCommandResult result = executor.Execute(command);
            executed++;

            if (!result.IsSuccess)
            {
                failed++;
            }

            foreach (string text in result.Lines)
            {
                output.WriteLine(text);
            }
        }

        return new ShellSession(ShellExitReason.Cancelled, executed, failed);
    }
}
