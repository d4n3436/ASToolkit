using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents a small room with items behind a breakable wall.
/// </summary>
/// <param name="Progress">The tour progress of its items.</param>
/// <param name="Items">The pickup IDs inside, crate contents included.</param>
public sealed record SecretCloset(float Progress, IReadOnlyList<KnownObject> Items);
