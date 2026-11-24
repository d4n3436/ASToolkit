namespace ASToolkit.Engine.IO;

public static partial class CRuntime
{
    private const double FtolLimit = 9223372036854775808.0;

    [EngineRoutine(0x0046b9ac)]
    internal static int Ftol(double value) => Math.Abs(value) < FtolLimit ? (int)(long)value : 0;
}
