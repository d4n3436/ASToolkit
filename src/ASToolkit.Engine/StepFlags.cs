namespace ASToolkit.Engine;

[Flags]
internal enum StepFlags
{
    None = 0,
    SingleStep = 1 << 0,
    RunInBackground = 1 << 1,
    DrawHashedNumbers = 1 << 2,
    DrawHashedSprites = 1 << 3
}
