using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

// Reads the actions of one stack of a sprite into script markers and the openings, quest starts and variables the level logic uses
internal sealed class StackReader
{
    private const int VariableWaitAct = 79;
    private const int LastAnimationAct = 16;
    private const int SelfTarget = -1;
    private const int MusicVariable = 1;
    private const int ExperienceVariable = 2;
    private const int MoneyVariable = 3;
    private const int ItemVariable = 4;
    private const int KillObjectsVariable = 5;
    private const int ShopVariable = 6;
    private const int WinVariable = 8;
    private const int InstantWinVariable = 9;
    private const int FirstTeleportEvent = 11;
    private const int LastTeleportEvent = 13;
    private const int ControlEvent = 15;
    private const int DownloadEvent = 16;

    private readonly MergedMapSprite _sprite;
    private readonly SpriteProgram _program;
    private readonly ScriptContext _context;
    private readonly Vector3 _source;
    private readonly List<(ScriptMarkerKind Kind, string Text)> _effects = [];
    private (Vector3 Center, float Radius)? _area;
    private string? _condition;
    private (int Line, int Sub)? _quest;
    private (string Name, int Value)? _variable;
    private Vector3? _target;

    public StackReader(MergedMapSprite sprite, SpriteProgram program, ScriptContext context)
    {
        _sprite = sprite;
        _program = program;
        _context = context;
        _source = sprite.Position;
    }

    private Vector3 Place => _area?.Center ?? _source;

    private float Radius => _area?.Radius ?? 0;

    public void Read(ReadOnlySpan<MapAction> stack)
    {
        Wait(null, null, null);
        for (int index = stack.Length - 1; index >= 0; index--)
        {
            if (!ReadAction(stack[index]))
            {
                break;
            }
        }

        Flush();
    }

    private bool ReadAction(MapAction action)
    {
        switch ((ActionCode)action.Act)
        {
            case ActionCode.FlagmanTrigger:
                Flush();
                _area = (new Vector3(action.Var1, action.Var2, _context.Objects.IsAlienShooter2 ? _sprite.Position.Z : 0), action.Var3);
                return true;
            case ActionCode.WhileNotQuest:
                _context.WaitedQuests.Add((action.Var1, action.Var2));
                Wait($"once quest {action.Var1}_{action.Var2} is active", (action.Var1, action.Var2), null);
                return true;
            case ActionCode.StopStack:
            case ActionCode.GotoStack:
                return false;
            case ActionCode.BuildUnit:
                AddEffect(ScriptMarkerKind.Spawn, $"build {_context.GetObjectName(action.Var1)}");
                return true;
            case ActionCode.SetQuest:
                AddQuestStart(action.Var1, action.Var2);
                return true;
            case ActionCode.SetDialog:
            case ActionCode.SetFlagmanDialog:
                AddEffect(ScriptMarkerKind.Dialog, $"show dialog {action.Var1}: {_context.Quests.GetDialog(action.Var1)}".TrimEnd(' ', ':'));
                return true;
            case ActionCode.ScriptVar:
                AddVariableEffect(action.Var1, action.Var2, action.Var3);
                return true;
            case ActionCode.ScriptEvent when action.Var1 == ControlEvent:
                AddEffect(ScriptMarkerKind.Control, $"give the player control of {_context.GetObjectName(action.Var2)}");
                _context.Controls.Add(new UnitControl(Place, Radius, (KnownObject)action.Var2));
                return true;
            case ActionCode.ScriptEvent when action.Var1 == DownloadEvent && _area is not null:
                AddEffect(ScriptMarkerKind.Other, "download the data of the computer");
                _context.Downloads.Add((int.TryParse(_program.HasName ? _program.GetName() : "", out int order) ? order : int.MaxValue, Place, _area.Value.Radius));
                return true;
            case ActionCode.ScriptEvent:
                AddEffect(action.Var1 is >= FirstTeleportEvent and <= LastTeleportEvent ? ScriptMarkerKind.Teleport : ScriptMarkerKind.Other,
                    action.Var1 is >= FirstTeleportEvent and <= LastTeleportEvent ? "teleport the player" : $"call ScriptEvent{action.Var1}");
                return true;
            case ActionCode.SetGoalCoor:
                _target = new Vector3(action.Var1, action.Var2, action.Var3);
                AddEffect(ScriptMarkerKind.Teleport, $"lead to ({action.Var1}, {action.Var2})");
                return true;
            case ActionCode.DestroyUnit:
                AddDestroyEffect(action);
                return true;
            case ActionCode.ChangeVid:
                AddChangeEffect((KnownObject)action.Var1);
                return true;
            case ActionCode.SetInvisible:
                AddEffect(ScriptMarkerKind.Other, action.Var1 != 0 ? "hide the sprite" : "show the sprite");
                return true;
            case ActionCode.SetArmy:
                AddEffect(ScriptMarkerKind.Other, $"join army {action.Var1}");
                return true;
            default:
                if (action.Act == VariableWaitAct)
                {
                    Wait($"once Action{action.Var1} is {action.Var2}", null, ($"Action{action.Var1}", action.Var2));
                }
                else if (action.Act is >= 0 and <= LastAnimationAct)
                {
                    AddAnimationEffect(action);
                }

                return true;
        }
    }

    // Ends what the stack has read so far and starts a wait for a quest or a variable
    private void Wait(string? condition, (int Line, int Sub)? quest, (string Name, int Value)? variable)
    {
        Flush();
        _area = null;
        _condition = condition;
        _quest = quest;
        _variable = variable;
    }

    private void Open(uint handle)
    {
        if (_variable is not null && _quest is null)
        {
            _context.VariableOpenings.Add((handle, _variable.Value.Name, _variable.Value.Value));
        }
        else if (_area is not null && _variable is null && _context.IsWall(handle))
        {
            _context.AreaOpenings.Add((handle, _area.Value.Center, _area.Value.Radius, _quest));
        }
        else
        {
            _context.Open(handle, _quest);
        }
    }

    private void AddQuestStart(int line, int sub)
    {
        var quest = _context.Quests.GetQuest(line, sub);
        string started = quest is null ? $"start quest {line}_{sub}"
            : quest.WinsOnBegin ? $"start quest {quest.Key} and win the level: {quest.Title}" : $"start quest {quest.Key}: {quest.Title}";
        AddEffect(ScriptMarkerKind.Quest, started);
        if (quest is not null)
        {
            if (_area is null && _variable is null && _quest is not null)
            {
                _context.FollowingQuests.Add(((line, sub), _quest.Value));
            }

            _context.QuestStarts.Add((quest, Place, Radius));
            if (_area is null && _variable is not null)
            {
                _context.VariableQuestStarts.Add((quest, _variable.Value.Name, _variable.Value.Value));
            }
        }
    }

    private void AddVariableEffect(int variable, int value, int adds)
    {
        if (adds != 1)
        {
            _context.VariableSets.Add(($"Action{variable}", value, Place, Radius));
        }

        switch (variable)
        {
            case MusicVariable:
                AddEffect(ScriptMarkerKind.Other, $"play music {value}");
                break;
            case ExperienceVariable:
                AddEffect(ScriptMarkerKind.Other, $"give {value} experience");
                break;
            case MoneyVariable:
                AddEffect(ScriptMarkerKind.Other, $"give {value} money");
                break;
            case ItemVariable:
                AddEffect(ScriptMarkerKind.Other, $"give item {value}");
                break;
            case KillObjectsVariable:
                AddEffect(ScriptMarkerKind.Opening, $"remove the objects named h{value}");
                foreach (var named in _context.GetNamedSprites($"h{value}"))
                {
                    Open(named.Handle);
                }

                break;
            case ShopVariable:
                AddEffect(ScriptMarkerKind.Other, "open the shop");
                break;
            case WinVariable:
            case InstantWinVariable:
                AddEffect(ScriptMarkerKind.Exit, "win the level");
                break;
            default:
                AddEffect(ScriptMarkerKind.Other, adds == 1 ? $"add {value} to Action{variable}" : $"set Action{variable} to {value}");
                break;
        }
    }

    private void AddDestroyEffect(MapAction action)
    {
        if (action.Var1 == SelfTarget)
        {
            bool solid = _context.Blocks(_sprite.Id);
            AddEffect(solid ? ScriptMarkerKind.Opening : ScriptMarkerKind.Other, "remove itself");
            if (solid)
            {
                Open(_sprite.Handle);
            }

            return;
        }

        var target = _context.FindNearest((KnownObject)action.Var1, new Vector2(action.Var2, action.Var3), 0);
        bool opens = _context.Blocks((KnownObject)action.Var1);
        AddEffect(opens ? ScriptMarkerKind.Opening : ScriptMarkerKind.Other, $"destroy {_context.GetObjectName(action.Var1)} near ({action.Var2}, {action.Var3})");
        if (opens && target is not null)
        {
            Open(target.Handle);
        }
    }

    private void AddChangeEffect(KnownObject to)
    {
        var kind = (_context.Blocks(_sprite.Id), _context.Blocks(to)) switch
        {
            (true, false) => ScriptMarkerKind.Opening,
            (false, true) => ScriptMarkerKind.Closing,
            _ => ScriptMarkerKind.Other
        };
        AddEffect(kind, $"turn into {_context.GetObjectName((int)to)}");
        if (kind == ScriptMarkerKind.Opening)
        {
            Open(_sprite.Handle);
        }
    }

    private void AddAnimationEffect(MapAction action)
    {
        var animation = (SpriteAnimation)action.Act;
        bool other = action.Var1 > 0 && _context.Objects.TryFind((KnownObject)action.Var1, out _);
        bool nearby = action is { Var2: 0, Var3: 0 };
        var target = !other ? _sprite
            : _context.FindNearest((KnownObject)action.Var1, nearby ? new Vector2(_sprite.Position.X, _sprite.Position.Y) : new Vector2(action.Var2, action.Var3),
                _sprite.Handle);
        string subject = !other ? "" : nearby ? $" {_context.GetObjectName(action.Var1)} nearby" : $" {_context.GetObjectName(action.Var1)} near ({action.Var2}, {action.Var3})";
        bool dies = animation is SpriteAnimation.Death or SpriteAnimation.Death2;
        var targetId = other ? (KnownObject)action.Var1 : _sprite.Id;
        if (targetId == KnownObject.RespawnPost && dies)
        {
            AddEffect(ScriptMarkerKind.Checkpoint, "set the respawn point");
        }
        else if ((dies || animation == SpriteAnimation.Open) && _context.Blocks(targetId))
        {
            AddEffect(ScriptMarkerKind.Opening, (animation == SpriteAnimation.Open ? "open" : "break") + subject);
            if (target is not null)
            {
                Open(target.Handle);
            }
        }
        else
        {
            AddEffect(ScriptMarkerKind.Other, $"play {animation}" + (other ? $" on{subject}" : ""));
        }
    }

    private void AddEffect(ScriptMarkerKind kind, string text) => _effects.Add((kind, text));

    private void Flush()
    {
        if (_effects.Count == 0)
        {
            return;
        }

        var kind = _effects.Max(effect => effect.Kind);
        string objectName = _context.GetObjectName((int)_sprite.Id);
        string owner = _program.HasName ? $"{objectName} «{_program.GetName()}»" : objectName;
        var parts = new List<string> { owner };
        if (_condition is not null)
        {
            parts.Add(_condition);
        }

        if (_area is not null)
        {
            parts.Add($"when the player comes within {_area.Value.Radius} px");
        }

        string effects = string.Join("; ", _effects.GroupBy(effect => effect.Text).Select(group => group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key));
        string label = $"{string.Join(", ", parts)}: {effects}";
        _context.Markers.Add(new ScriptMarker(kind, Place, Radius, _source, label) { Target = _target });
        if (kind == ScriptMarkerKind.Exit)
        {
            _context.Exits.Add((Place, Radius, _quest, label));
        }

        _effects.Clear();
        _target = null;
    }
}
