using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Represents a level's mission before any geometry is laid out.
/// </summary>
/// <param name="Progression">The phases, keys, locks, flow, triggers and seals of the mission.</param>
/// <param name="Links">The links between the mission's rooms.</param>
/// <param name="Roles">The role of each room of the mission.</param>
public sealed record MissionGraph(LevelProgression Progression, IReadOnlyList<MissionLink> Links, IReadOnlyDictionary<int, MissionRole> Roles);
