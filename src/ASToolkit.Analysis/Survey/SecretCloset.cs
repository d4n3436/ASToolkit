using ASToolkit.Formats;

namespace ASToolkit.Analysis.Survey;

/// <summary>
/// Represents a small room with items behind a breakable wall.
/// </summary>
/// <param name="Progress">The tour progress of the closet's items.</param>
/// <param name="Items">The object IDs of the pickups inside, crate contents included.</param>
public sealed record SecretCloset(float Progress, IReadOnlyList<KnownObject> Items);
