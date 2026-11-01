namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the checks that go with <see cref="EdgeKind"/>.
/// </summary>
public static class EdgeKindExtensions
{
    extension(EdgeKind kind)
    {
        /// <summary>Gets a value indicating whether the edge stops the player.</summary>
        public bool BlocksWalking => kind is EdgeKind.Wall or EdgeKind.ShutDoor;

        /// <summary>Gets a value indicating whether the edge closes off the floor behind it, as a wall, a shut door or a secret wall does.</summary>
        public bool IsClosed => kind is EdgeKind.Wall or EdgeKind.ShutDoor or EdgeKind.SecretWall;
    }
}
