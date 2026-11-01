using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the per-level curves of a campaign from the surveys of its levels and the game's money table.
/// </summary>
public sealed class CampaignMeasurer
{
    /// <summary>
    /// Represents the money a cash pickup holds.
    /// </summary>
    public const int CashValue = 50;

    private readonly IReadOnlyList<int> _endMoney;

    /// <summary>
    /// Initializes a new instance of the <see cref="CampaignMeasurer"/> class.
    /// </summary>
    /// <param name="endMoney">The money table of the campaign, indexed by the number of the level that ends.</param>
    public CampaignMeasurer(IReadOnlyList<int> endMoney) => _endMoney = endMoney;

    /// <summary>
    /// Returns the measurements of a campaign's levels, given in order from level 1.
    /// </summary>
    public CampaignMeasurement[] Measure(IReadOnlyList<LevelSurvey> levels)
    {
        var seenKinds = new HashSet<KnownObject>();
        var seenWeapons = new HashSet<KnownObject>();
        var measurements = new CampaignMeasurement[levels.Count];
        for (int index = 0; index < levels.Count; index++)
        {
            var survey = levels[index];

            var fights = survey.Encounters
                .Where(encounter => encounter.HitPoints > 0)
                .ToArray();

            int hitPoints = fights.Sum(fight => fight.HitPoints);

            int spawned = fights
                .Where(fight => fight.Kind is EncounterKind.Ambush or EncounterKind.Spawner or EncounterKind.EndlessGenerators)
                .Sum(fight => fight.HitPoints);

            var kinds = fights
                .SelectMany(fight => fight.Monsters)
                .Select(monster => monster.Id)
                .Where(id => KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id))
                .Distinct()
                .Order()
                .ToArray();

            var weapons = survey.Items
                .Where(item => item.Id.IsWeapon)
                .Select(item => item.Id)
                .Distinct()
                .Order()
                .ToArray();

            var newWeapons = weapons
                .Where(seenWeapons.Add)
                .ToArray();

            measurements[index] = new CampaignMeasurement
            {
                Level = index + 1,
                Map = survey.Name,
                Theme = survey.Setting,
                GateStyle = survey.Style,
                HitPoints = hitPoints,
                SpawnedShare = hitPoints == 0 ? 0 : spawned / (float)hitPoints,
                MonsterKinds = kinds,
                NewMonsterKinds = kinds.Where(seenKinds.Add).ToArray(),
                FloorPixels = survey.Places.Sum(place => place.AreaPixels),
                TourPixels = survey.Signature.TourPixels,
                Sections = new SectionMeasurer(survey).Measure().Length,
                Pickups = survey.Items.Where(item => item.Category is not null).GroupBy(item => item.Category!.Value).ToDictionary(group => group.Key, group => group.Count()),
                Items = survey.Items.Where(item => item.Id.IsPickup).CountBy(item => item.Id).ToDictionary(),
                Money = CashValue * survey.Items.Count(item => item.Id == KnownObject.Money),
                Weapons = weapons,
                NewWeapons = newWeapons,
                NewWeaponsBeforeAmmo = newWeapons.Count(weapon => FindFirstRoute(survey, weapon) < FindFirstRoute(survey, weapon.WeaponAmmo!.Value)),
                EndMoney = index + 1 < levels.Count && index + 1 < _endMoney.Count ? _endMoney[index + 1] : 0
            };
        }

        return measurements;
    }

    private static int FindFirstRoute(LevelSurvey survey, KnownObject id) => survey.Items.Where(item => item.Id == id).Select(item => item.Route).DefaultIfEmpty(int.MaxValue).Min();
}
