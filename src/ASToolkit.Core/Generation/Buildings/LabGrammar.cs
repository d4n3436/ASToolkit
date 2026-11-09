using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the underground lab, a hub hall lined with pods followed by the wings its gate program draws.
/// </summary>
internal sealed class LabGrammar : BuildingGrammar
{
    private const float SmallSide = 6;
    private const float LabNeckShare = 0.38f;

    public LabGrammar(BuildingRequest request, Random rng) : base(request, rng)
    {
        RoomSide = SmallSide * Random.Uniform(0.9f, 1.1f) * (request.Rooms <= 12 ? 1.2f : 1.0f);
        HallWidth = Random.Choice((int[])[5, 5, 6, 6]);
        NeckShare = LabNeckShare;
        IsPacked = request is { Gate: LevelStyle.LockedDoors, IsIntro: false } && Random.Next() < 0.5f;
    }

    public override void Build()
    {
        int rooms = Math.Max(4, Request.Rooms);
        switch (Request.Gate)
        {
            case LevelStyle.LockedDoors or LevelStyle.Fenced:
                BuildLockedOffices(rooms);
                break;
            case LevelStyle.Blackout:
                BuildDarkOffices(rooms);
                break;
            default:
                BuildSequence(rooms);
                break;
        }

    }

    public override void PlaceSideRooms()
    {
        new RoomShaper(Draft).ShapeHalls();
        GrowCrossPassages();
        new SideRoomPlacer(Draft, BackTypes).PlaceSecretRoom();
        new SideRoomPlacer(Draft, BackTypes).PlaceClosets();
    }
}
