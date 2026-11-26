using static ASToolkit.Formats.KnownObject;

namespace ASToolkit.Formats;

/// <summary>
/// Provides the kinds of object IDs that the engine tells apart.
/// </summary>
public static class KnownObjectExtensions
{
    extension(KnownObject id)
    {
        /// <summary>Gets a value indicating whether the ID is an endless monster generator.</summary>
        public bool IsEndlessGenerator => id is >= EndlessGenerator1 and <= RedEndlessGenerator6;

        /// <summary>Gets a value indicating whether the ID is a placed object the campaign scripts drive through its animation hooks.</summary>
        public bool IsScripted => id is DynamiteSite or BreakerOff or SmallDoor or DoubleDoor or Fence or MountedCannon || id.IsEndlessGenerator;
    }
}
