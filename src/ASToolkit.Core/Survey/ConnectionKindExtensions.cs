namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the checks that go with <see cref="ConnectionKind"/>.
/// </summary>
public static class ConnectionKindExtensions
{
    extension(ConnectionKind kind)
    {
        /// <summary>Gets a value indicating whether the player walks through the connection as through a door or doorway.</summary>
        public bool IsPassage => kind is ConnectionKind.Doorway or ConnectionKind.Door or ConnectionKind.LockedDoor or ConnectionKind.BrokenWall
            or ConnectionKind.FenceFrame or ConnectionKind.Fence;
    }
}
