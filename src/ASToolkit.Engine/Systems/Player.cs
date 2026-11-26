using System.Globalization;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal class Player
{
    private const int StartMoney = 1000;
    private const int UnitKeyLength = 16;

    private readonly SpriteList _sprites = new();

    [EngineRoutine(0x00413060)]
    public Player(GameSession session, int index)
    {
        Session = session;
        Map = session.Map!;
        Index = index;
        Money = StartMoney;
    }

    [EngineRoutine(0x0043a8b0)]
    public int Money { get; set; }

    public int Index { get; }

    public Sprite? Flagman { get; private set; }

    public Sprite? Hovered { get; set; }

    [EngineRoutine(0x0043a8c0)]
    public bool IsCleverEnemyAttack { get; set; }

    public GameSession Session { get; }

    public Map Map { get; }

    [EngineRoutine(0x00413140)]
    public void SetFlagman(Sprite? sprite)
    {
        sprite?.AddRef();

        if (Flagman is not null)
        {
            Map.Release(Flagman);
        }

        Flagman = sprite;
    }

    [EngineRoutine(0x004131b0)]
    public virtual void PutMessage(ReadOnlyMemory<byte> text, Vector2 target)
    {
    }

    [EngineRoutine(0x00413280)]
    public virtual void Forget(Sprite removed)
    {
        _sprites.Delete(removed);

        if (Flagman == removed)
        {
            Map.Release(removed);
            Flagman = null;
        }

        if (Hovered == removed)
        {
            Map.Release(removed);
            Hovered = null;
        }
    }

    [EngineRoutine(0x00413360)]
    public void Reset()
    {
        if (Flagman is not null)
        {
            Map.Release(Flagman);
        }

        Flagman = null;

        if (Hovered is not null)
        {
            Map.Release(Hovered);
        }

        Hovered = null;
        Money = StartMoney;
    }

    [EngineRoutine(0x00413440)]
    public void Load(ref Resource reader)
    {
        Map.TryReadPointer(ref reader, out var flagman);
        SetFlagman(flagman);
    }

    [EngineRoutine(0x004134c0)]
    public ReadOnlyMemory<byte> GetMouseTipsString()
    {
        if (Hovered is null)
            return default;

        Span<byte> key = stackalloc byte[UnitKeyLength];
        ((int)Hovered.Vid.Id).TryFormat(key, out int written, provider: CultureInfo.InvariantCulture);

        return Map.Session.Strings?.GetString("Units"u8, key[..written]) ?? default;
    }

    public virtual void Control(Input input)
    {
    }
}
