using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

// Holds the sprites the readers of one level look up and the events they collect
internal sealed class ScriptContext
{
    private const SpriteClass SceneryClass = (SpriteClass)8;
    private const int MaxLinkDepth = 4;

    private readonly Dictionary<KnownObject, List<MergedMapSprite>> _spritesById;
    private readonly Dictionary<string, List<MergedMapSprite>> _spritesByName = new(StringComparer.OrdinalIgnoreCase);

    public ScriptContext(MapResourceBuilder map, ObjectTable objects, QuestBook quests)
    {
        Map = map;
        Objects = objects;
        Quests = quests;
        _spritesById = map.Sprites.GroupBy(sprite => sprite.Id).ToDictionary(group => group.Key, group => group.ToList());
        foreach (var sprite in map.Sprites)
        {
            if (!SpriteProgram.TryParse(sprite.Payload, objects.IsAlienShooter2, out var program) || !program.HasName)
            {
                continue;
            }

            foreach (string name in GetNames(program.GetName()))
            {
                if (!_spritesByName.TryGetValue(name, out var named))
                {
                    named = [];
                    _spritesByName[name] = named;
                }

                named.Add(sprite);
            }
        }
    }

    public MapResourceBuilder Map { get; }

    public ObjectTable Objects { get; }

    public QuestBook Quests { get; }

    public List<ScriptMarker> Markers { get; } = [];

    public List<(Quest Quest, Vector3 Position, float Radius)> QuestStarts { get; } = [];

    public List<(Vector3 Position, float Radius, (int Line, int Sub)? Quest, string Label)> Exits { get; } = [];

    public HashSet<(int Line, int Sub)> WaitedQuests { get; } = [];

    public List<(string Name, int Value, Vector3 Position, float Radius)> VariableSets { get; } = [];

    public List<(Quest Quest, string Name, int Value)> VariableQuestStarts { get; } = [];

    public List<(uint Handle, string Name, int Value)> VariableOpenings { get; } = [];

    public List<(uint Handle, Vector3 Center, float Radius, (int Line, int Sub)? Quest)> AreaOpenings { get; } = [];

    public Dictionary<uint, List<(int Line, int Sub)?>> Openings { get; } = [];

    public List<((int Line, int Sub) Quest, (int Line, int Sub) After)> FollowingQuests { get; } = [];

    public List<UnitControl> Controls { get; } = [];

    public List<((int Line, int Sub) Quest, Vector3 Destination)> Skips { get; } = [];

    public List<(int Order, Vector3 Position, float Radius)> Downloads { get; } = [];

    // Records a sprite the scripts open with the quest the opening waits for
    public void Open(uint handle, (int Line, int Sub)? quest)
    {
        if (!Openings.TryGetValue(handle, out var quests))
        {
            quests = [];
            Openings[handle] = quests;
        }

        quests.Add(quest);
    }

    // Returns the TimeLine at which a quest begins, following the quests a stack starts once another one begins
    public int GetBeginTimeLine((int Line, int Sub) quest, int depth = 0)
    {
        int timeLine = Quests.GetQuest(quest.Line, quest.Sub)?.TimeLine ?? 0;
        if (depth >= MaxLinkDepth)
        {
            return timeLine;
        }

        foreach (var follow in FollowingQuests.Where(follow => follow.Quest == quest))
        {
            timeLine = Math.Min(timeLine, GetBeginTimeLine(follow.After, depth + 1));
        }

        return timeLine;
    }

    public static string[] GetNames(string name) => name.Split([' ', '[', ']'], StringSplitOptions.RemoveEmptyEntries);

    public IReadOnlyList<MergedMapSprite> GetSprites(KnownObject id) => _spritesById.TryGetValue(id, out var sprites) ? sprites : [];

    public IReadOnlyList<MergedMapSprite> GetNamedSprites(string name) => _spritesByName.TryGetValue(name, out var sprites) ? sprites : [];

    // Returns where the stacks of a sprite last move it to, or where the map places it
    public Vector3 GetPlace(MergedMapSprite sprite)
    {
        var place = sprite.Position;
        if (!SpriteProgram.TryParse(sprite.Payload, Objects.IsAlienShooter2, out var program))
        {
            return place;
        }

        for (int stackIndex = 0; stackIndex < program.StackCount; stackIndex++)
        {
            var stack = program.GetStack(stackIndex);
            for (int index = stack.Length - 1; index >= 0; index--)
            {
                if (stack[index].Act == (int)ActionCode.ChangeCoor)
                {
                    place = new Vector3(stack[index].Var1, stack[index].Var2, stack[index].Var3);
                }
            }
        }

        return place;
    }

    // Returns the places the scripts set a variable to a value from inside a trigger area
    public IEnumerable<(string Name, int Value, Vector3 Position, float Radius)> GetVariableSetters(string name, int value) =>
        VariableSets.Where(set => set.Name == name && set.Value == value && set.Radius > 0);

    // Returns the description of opening a sprite
    public string GetOpeningLabel(uint handle) => $"open {GetObjectName((int)Map.Sprites.First(sprite => sprite.Handle == handle).Id)}";

    public MergedMapSprite? FindNearest(KnownObject id, Vector2 point, uint excluded) =>
        GetSprites(id).Where(sprite => sprite.Handle != excluded).MinBy(sprite => Vector2.DistanceSquared(new Vector2(sprite.Position.X, sprite.Position.Y), point));

    public string GetObjectName(int id)
    {
        if (!Objects.TryFind((KnownObject)id, out _))
        {
            return $"object {id}";
        }

        string name = Objects.GetName((KnownObject)id);
        int colon = name.LastIndexOf(':');
        return $"{name[(colon + 1)..].Trim()} ({id})";
    }

    // Tells whether the sprite stands in the way of the player like a wall or a closed door
    public bool IsWall(uint handle)
    {
        var sprite = Map.Sprites.FirstOrDefault(candidate => candidate.Handle == handle);
        if (sprite is null || !Objects.TryFind(sprite.Id, out var obj))
        {
            return false;
        }

        bool destructible = obj is { IsHittable: true, MaxHp: > 0 };
        return Blocks(sprite.Id) && !destructible && !(obj.SpriteType.HasFlag(SpriteType.Dummy) && !obj.IsSolid) && !obj.SpriteType.HasFlag(SpriteType.Unit)
            && !obj.SpriteType.HasFlag(SpriteType.Avia) && !obj.SpriteType.HasFlag(SpriteType.Cannon);
    }

    // Tells whether an object or, in Alien Shooter 2, one of its linked objects is solid or a box the player bumps into
    public bool Blocks(KnownObject id)
    {
        if (!Objects.TryFind(id, out var obj))
        {
            return false;
        }

        if (!Objects.IsAlienShooter2)
        {
            return obj.IsSolid;
        }

        for (int depth = 0; depth < MaxLinkDepth; depth++)
        {
            if (obj.IsSolid || (obj.SpriteClass != SceneryClass && Objects.TryFind(KnownObject.SequelPlayer, out var player) && (obj.MoveMask & player.MoveMask) != 0))
            {
                return true;
            }

            if (obj.Link == 0 || !Objects.TryFind(obj.Link, out obj))
            {
                return false;
            }
        }

        return false;
    }
}
