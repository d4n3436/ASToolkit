namespace ASToolkit.Cli.Commands;

internal abstract class Command<TSettings> where TSettings : GameSettings
{
    public abstract int Execute(TSettings settings, CancellationToken cancellationToken);
}
