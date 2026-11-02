using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the fights, dynamite sites, charges, pickups, checkpoint and generators of an original level by the role and zone of their rooms.
/// </summary>
public sealed class EncounterProfile
{
    private const float PileReach = 45;

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
    /// Gets how the original lays the pickups of each category.
    /// </summary>
    public required IReadOnlyDictionary<ItemCategory, PickupStyle> PickupStyles { get; init; }

    /// <summary>
    /// Gets how many monsters the original's largest spawner releases, 0 when the original has no spawner.
    /// </summary>
    public required int LargestSpawner { get; init; }

    /// <summary>
    /// Gets how many endless generators the original holds.
    /// </summary>
    public required int EndlessGenerators { get; init; }

    /// <summary>
    /// Gets the trigger radius of each spawner of the original.
    /// </summary>
    public required IReadOnlyList<int> TriggerRadii { get; init; }


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
            EndlessGenerators = analysis.Elements.Count(element => element.Kind == ElementKind.EndlessGenerator),
            LargestSpawner = analysis.Elements.Where(element => element.Program is not null).Select(element => element.Program!.Monsters.Count()).DefaultIfEmpty(0).Max(),
            TriggerRadii = analysis.Elements.Where(element => element.Program?.Trigger is not null).Select(element => element.Program!.Trigger!.Value.Radius).ToArray(),
            PickupStyles = Enum.GetValues<ItemCategory>().ToDictionary(category => category, category => MeasurePickups(survey, analysis.Model, category))
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

    // Group the loose pickups of a category into piles and read each pile's walls at its first pickup
    private static PickupStyle MeasurePickups(LevelSurvey survey, LevelModel model, ItemCategory category)
    {
        var items = survey.Items.Where(item => item.Category == category && item.Id != KnownObject.Dynamite).ToArray();
        var piles = items.Where(item => !item.InCrate).Select(item => new PointF(item.Position.X, item.Position.Y)).ToArray().Cluster(PileReach);
        var spots = piles.Select(pile =>
        {
            var cell = model.Lattice.GetCell(pile[0].X, pile[0].Y);
            var sides = model.Edges.GetWalledSides(cell, model.Cells.Contains, static kind => kind.IsClosed);
            return sides.IsCorner ? PartSpot.Corner : sides != WallSides.None ? PartSpot.Wall : PartSpot.Center;
        }).ToArray();
        
        return new PickupStyle(items.Length == 0 ? 0 : items.Count(item => item.InCrate) / (float)items.Length, piles.Select(pile => pile.Count).ToArray(), spots);
    }
}
