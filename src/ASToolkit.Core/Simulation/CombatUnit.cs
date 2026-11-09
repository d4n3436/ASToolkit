using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class CombatUnit
{
    public CombatUnit(int number, CombatKind kind, Vector2 position, int army, int hp)
    {
        Number = number;
        Kind = kind;
        Position = position;
        Army = army;
        Hp = hp;
    }

    public int Number { get; }

    public CombatKind Kind { get; }

    public int Army { get; }

    public Vector2 Position { get; set; }

    public int Hp { get; set; }

    public bool Awake { get; set; }

    public bool SeesPlayer { get; set; }

    public bool SightChecked { get; set; }

    public int Bucket { get; set; } = -1;

    public int Slot { get; set; }

    public int NextAttack { get; set; }

    public int BlockedUntil { get; set; }

    public int StaggerUntil { get; set; }

    public Vector2[] Route { get; set; } = [];

    public int RouteStep { get; set; }

    public int DiedAt { get; set; } = -1;

    public byte Facing { get; set; }

    public SpriteAnimation Animation { get; set; }

    public bool IsAlive => Hp > 0;

    public UnitState GetState() => new(Number, Kind.Id, new PointF(Position.X, Position.Y), Facing, Animation);
}
