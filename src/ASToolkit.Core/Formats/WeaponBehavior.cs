namespace ASToolkit.Core.Formats;

[Flags]
internal enum WeaponBehavior : uint
{
    None = 0,

    AcquiresTargets = 1 << 0,

    PursuesTargets = 1 << 1
}
