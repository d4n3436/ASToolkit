namespace ASToolkit.Core.Rendering;

/// <summary>
/// Specifies the role of a helper (hidden) sprite.
/// </summary>
public enum HelperKind
{
    /// <summary>
    /// A helper of no other kind.
    /// </summary>
    Other,

    /// <summary>
    /// A sprite whose object or sprite file is missing.
    /// </summary>
    Missing,

    /// <summary>
    /// A block that stops movement or holds things up.
    /// </summary>
    Blocker,

    /// <summary>
    /// A dummy that runs a script.
    /// </summary>
    Trigger,

    /// <summary>
    /// A monster generator.
    /// </summary>
    Spawner
}
