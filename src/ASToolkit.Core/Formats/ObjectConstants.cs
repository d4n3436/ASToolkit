using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit, Size = 104)]
internal readonly struct ObjectConstants
{
    [FieldOffset(0)]
    public readonly float ScrollSpeedX;

    [FieldOffset(4)]
    public readonly float ScrollSpeedY;

    [FieldOffset(8)]
    public readonly float Gravity;

    [FieldOffset(12)]
    public readonly float HeavyGravity;

    [FieldOffset(16)]
    public readonly int RepairAmount;

    [FieldOffset(20)]
    public readonly int ResupplySteps;

    [FieldOffset(24)]
    public readonly float MaxTrainSpeed;

    [FieldOffset(36)]
    public readonly int BuildTimeFactor;

    [FieldOffset(52)]
    public readonly int HoverDelay;

    [FieldOffset(80)]
    public readonly uint SelectedGamma;

    [FieldOffset(84)]
    public readonly uint HoveredEnemyGamma;

    [FieldOffset(88)]
    public readonly uint HoveredGamma;

    [FieldOffset(96)]
    public readonly float CrashSpeed;
}
