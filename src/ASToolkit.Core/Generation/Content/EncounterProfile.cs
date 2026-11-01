using ASToolkit.Core.Formats;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the fights, dynamite sites, charges, pickups, checkpoint and generators of an original level by the role and zone of their rooms.
/// </summary>
public sealed class EncounterProfile
{
    private EncounterProfile()
    {
    }

    /// <summary>
    /// Gets the fights, standing monsters grouped by room and each spawner on its own.
    /// </summary>
    public required IReadOnlyList<Encounter> Encounters { get; init; }

    /// <summary>
    /// Gets a value indicating whether the level has rooms behind a gate.
    /// </summary>
    public required bool IsGated { get; init; }

    /// <summary>
    /// Gets the rooms that hold dynamite sites, in tour order.
    /// </summary>
    public required IReadOnlyList<ObjectiveRoom> SiteRooms { get; init; }

    /// <summary>
    /// Gets the rooms that hold dynamite charges, in tour order.
    /// </summary>
    public required IReadOnlyList<ObjectiveRoom> ChargeRooms { get; init; }

    /// <summary>
    /// Gets the rooms outside secret closets that hold loose pickups, by item category, in tour order.
    /// </summary>
    public required IReadOnlyDictionary<ItemCategory, IReadOnlyList<ObjectiveRoom>> PickupRooms { get; init; }

    /// <summary>
    /// Gets the room that holds the checkpoint, or <see langword="null"/> for a level without one.
    /// </summary>
    public required ObjectiveRoom? CheckpointRoom { get; init; }

    /// <summary>
    /// Gets the shares of the tour walked before each generator, in tour order.
    /// </summary>
    public required IReadOnlyList<float> GeneratorProgress { get; init; }

    /// <summary>
    /// Measures the fights, sites, charges, pickups, checkpoint and generators of a surveyed level from its tour.
    /// </summary>
    public static EncounterProfile Measure(LevelSurvey survey, RouteAnalysis analysis, ObjectTable objects)
    {
        var roles = RoomRoleMap.Measure(survey);
        var places = survey.Places.ToDictionary(place => place.Id);
        
        var standing = analysis.Elements
            .Where(element => element.Kind == ElementKind.Monster)
            .GroupBy(element => survey.FindPlace(element.X, element.Y))
            .Select(group => (Place: group.Key, HitPoints: group.Sum(element => HitPoints(element.Id)), Progress: group.Min(element => element.Progress), Trigger: group.Key))
            .ToArray();
        
        var spawned = analysis.Elements.Where(element => element is { Kind: ElementKind.Spawner, Program.Trigger: not null })
            .Select(element => (Place: survey.FindPlace(element.X, element.Y), HitPoints: element.Program!.Monsters.Sum(HitPoints), element.Progress,
                Trigger: survey.FindPlace(element.Program.Trigger!.Value.X, element.Program.Trigger.Value.Y)))
            .ToArray();
        
        float total = Math.Max(1, standing.Sum(fight => fight.HitPoints) + spawned.Sum(fight => fight.HitPoints));

        return new EncounterProfile
        {
            Encounters = standing.Select(fight => Create(fight, false)).Concat(spawned.Select(fight => Create(fight, true))).Where(encounter => encounter.Share > 0).ToArray(),
            IsGated = survey.Places.Any(place => place.Zone is not (GateZone.Open or GateZone.Secret)),
            SiteRooms = MeasureRooms(element => element.Kind == ElementKind.DynamiteSite),
            ChargeRooms = MeasureRooms(element => element.Id == KnownObject.Dynamite),
            PickupRooms = Enum.GetValues<ItemCategory>().ToDictionary(category => category, IReadOnlyList<ObjectiveRoom> (category) => MeasureRooms(element =>
                element is { Kind: ElementKind.Pickup } && element.Category == category && element.Id != KnownObject.Dynamite && Read(survey.FindPlace(element.X, element.Y)).Zone != GateZone.Secret)),
            CheckpointRoom = MeasureCheckpoint(),
            GeneratorProgress = survey.Landmarks.Where(landmark => landmark.Kind == LandmarkKind.Generator).Select(landmark => landmark.Route / Math.Max(1f, survey.Signature.TourPixels)).Order().ToArray()
        };

        (RoomRole Role, GateZone Zone) Read(int place) => places.TryGetValue(place, out var found) ? (roles.GetRole(found), found.Zone) : (RoomRole.Outside, GateZone.Open);
        
        int HitPoints(KnownObject id) => objects.TryFind(id, out var obj) ? obj.MaxHp : 0;

        ObjectiveRoom? MeasureCheckpoint() => analysis.Checkpoints.Select(checkpoint =>
        {
            var (role, zone) = Read(survey.FindPlace(checkpoint.X, checkpoint.Y));
            return new ObjectiveRoom(role, zone, 1, checkpoint.Progress);
        }).FirstOrDefault();

        ObjectiveRoom[] MeasureRooms(Func<RouteElement, bool> isWanted) => analysis.Elements
            .Where(isWanted)
            .GroupBy(element => survey.FindPlace(element.X, element.Y))
            .Select(group => new ObjectiveRoom(Read(group.Key).Role, Read(group.Key).Zone, group.Count(), group.Min(element => element.Progress)))
            .OrderBy(room => room.Progress)
            .ToArray();

        Encounter Create((int Place, int HitPoints, float Progress, int Trigger) fight, bool isSpawned)
        {
            var (role, zone) = Read(fight.Place);
            var (triggerRole, triggerZone) = Read(fight.Trigger);
            bool nextDoor = survey.Connections.Any(connection => (connection.A, connection.B) == (fight.Place, fight.Trigger) || (connection.A, connection.B) == (fight.Trigger, fight.Place));
            return new Encounter(role, zone, isSpawned, fight.HitPoints / total, fight.Progress, triggerRole, triggerZone, fight.Trigger == fight.Place, nextDoor);
        }
    }
}
