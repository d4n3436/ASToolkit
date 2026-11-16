using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Provides the reading of level scripts.
/// </summary>
public static class LevelScriptReader
{
    private const string MapsFolder = "Maps";

    /// <summary>
    /// Reads the facts of a level script in a game folder's Maps folder, or returns empty facts when the file does not exist.
    /// </summary>
    public static LevelScriptSummary Read(string path)
    {
        if (!File.Exists(path))
            return new LevelScriptSummary();

        string gameFolder = FindGameFolder(path);

        return Parse(File.ReadAllBytes(path), gameFolder, Path.GetRelativePath(gameFolder, path).Replace('/', '\\'));
    }

    /// <summary>
    /// Reads the facts of a level script from its bytes, opening its includes from a game folder.
    /// </summary>
    public static LevelScriptSummary Parse(ReadOnlyMemory<byte> data, string gameFolder, string name = "level.lgc")
    {
        var script = new Script();
        script.Load(new MarkedScriptSource(new ScriptFolder(gameFolder), data), name);

        return new CompiledLevel(script).Read();
    }

    /// <summary>
    /// Reads the facts of an Alien Shooter 2 level script from its text, or returns empty facts when the file does not exist.
    /// </summary>
    public static LevelScriptSummary ReadSequel(string path)
    {
        if (!File.Exists(path))
            return new LevelScriptSummary();

        return LevelScriptBlockReader.ReadSummary(GameTextEncoding.Cyrillic.GetString(File.ReadAllBytes(path)));
    }

    private static string FindGameFolder(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? folder = Path.GetDirectoryName(full); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            string? parent = Path.GetDirectoryName(folder);
            if (parent is not null && string.Equals(Path.GetFileName(folder), MapsFolder, StringComparison.OrdinalIgnoreCase))
                return parent;
        }

        return Path.GetDirectoryName(full) ?? ".";
    }

    private sealed class CompiledLevel
    {
        private const int ChangeAnimationAction = 61;
        private const int CountData = 28;
        private const int KeypadOn = 423;
        private const int KeypadOff = 424;
        private readonly Script _script;
        private readonly ReadOnlyMemory<ScriptInstruction> _code;
        private readonly Range _own;
        private readonly Callee _flagman;
        private readonly Callee _sizeTo;
        private readonly Callee _lightOff;
        private readonly Callee _getX;
        private readonly Callee _getUnitVid;
        private readonly Callee _action;
        private readonly Callee _getVidData;
        private readonly Callee _genocide;
        private readonly Callee _createSprite;
        private readonly Callee _earthQuake;
        private readonly Callee _win;
        private readonly Callee _campaignTact;
        private readonly int _startX;
        private readonly int _startY;
        private readonly int _doorsOpen;

        public CompiledLevel(Script script)
        {
            _script = script;
            _own = MarkedScriptSource.GetRootCode(script);
            _code = Decode(_own.Start.Value, _own.End.Value);
            _flagman = GetCallee("Flagman"u8);
            _sizeTo = GetCallee("SizeTo"u8);
            _lightOff = GetCallee("LightOff"u8);
            _getX = GetCallee("GetX"u8);
            _getUnitVid = GetCallee("GetUnitVid"u8);
            _action = GetCallee("Action"u8);
            _getVidData = GetCallee("GetVidData"u8);
            _genocide = GetCallee("Genocide"u8);
            _createSprite = GetCallee("CreateSprite"u8);
            _earthQuake = GetCallee("EarthQuake"u8);
            _win = GetCallee("Win"u8);
            _campaignTact = GetCallee("CompaignGameTact"u8);
            _startX = GetVariable("StartPlayerX"u8);
            _startY = GetVariable("StartPlayerY"u8);
            _doorsOpen = GetVariable("DoorsIsOpen"u8);
        }

        public LevelScriptSummary Read()
        {
            var triggers = new List<ScriptTrigger>();
            var checkpoints = new List<TriggerArea>();
            TriggerArea? blackout = null;
            uint? blackoutGamma = null;
            TriggerArea? exit = null;
            var code = _code.Span;
            for (int index = 0; index < code.Length; index++)
            {
                if (!IsDistanceCheck(code, index, out var area, out var body))
                    continue;

                int? color = FindFirstArgument(body, _lightOff);
                uint? lightOff = color is null ? null : unchecked((uint)color.Value);
                if (lightOff is not null)
                {
                    blackout = area;
                    blackoutGamma = lightOff;
                }

                if (UsesVariable(body, _startX))
                {
                    checkpoints.Add(area);
                }

                bool wins = Calls(body, _win);
                if (wins)
                {
                    exit = area;
                }

                triggers.Add(ReadTrigger(area, body, lightOff, wins));
            }

            return new LevelScriptSummary
            {
                ClearsMonsters = Calls(code, _campaignTact),
                DoorsLocked = LocksDoorsAtStart(code),
                Blackout = blackout,
                BlackoutGamma = blackoutGamma,
                Checkpoints = checkpoints,
                WinsAfterSites = ChecksSites(code),
                Exit = exit,
                Triggers = triggers,
                Handlers = ReadHandlers(),
                CrowdRules = ReadCrowdRules(code)
            };
        }

        private ReadOnlyMemory<ScriptInstruction> Decode(int start, int end)
        {
            var code = _script.Code;
            var instructions = new List<ScriptInstruction>();
            for (int offset = start; offset < end;)
            {
                var instruction = ScriptInstruction.Read(code, offset);
                instructions.Add(instruction);
                offset = instruction.End;
            }

            return instructions.ToArray();
        }

        private Callee GetCallee(ReadOnlySpan<byte> name)
        {
            int found = _script.FindObject(name);
            if (found < 0)
                return Callee.None;

            var entry = _script.Objects[found];

            return entry.Kind switch
            {
                ScriptObjectKind.Extern => new Callee(entry.Address, -1, entry.FirstVariable, entry.Count),
                ScriptObjectKind.Function => new Callee(-1, entry.Address, entry.FirstVariable, entry.Count),
                _ => Callee.None
            };
        }

        private int GetVariable(ReadOnlySpan<byte> name)
        {
            int found = _script.FindObject(name);

            return found >= 0 && _script.Objects[found].Kind == ScriptObjectKind.Variable ? _script.Objects[found].Address : -1;
        }

        private static bool IsNumber(ReadOnlySpan<ScriptInstruction> code, int index) =>
            (uint)index < (uint)code.Length && code[index].Opcode == ScriptOpcode.PushNumber;

        private static bool IsNumber(ReadOnlySpan<ScriptInstruction> code, int index, int value) => IsNumber(code, index) && code[index].Operand == value;

        private static bool IsExtern(ReadOnlySpan<ScriptInstruction> code, int index, Callee callee) =>
            callee.Id >= 0 && (uint)index < (uint)code.Length && code[index].IsExternCall && (int)code[index].Opcode == callee.Id;

        private static bool Is(ReadOnlySpan<ScriptInstruction> code, int index, ScriptOpcode opcode) =>
            (uint)index < (uint)code.Length && code[index].Opcode == opcode;

        private static int SkipDefaults(ReadOnlySpan<ScriptInstruction> code, int index, Callee callee)
        {
            while (Is(code, index, ScriptOpcode.Load) && code[index].Operand >= callee.FirstVariable && code[index].Operand < callee.FirstVariable + callee.Count)
            {
                index++;
            }

            return index;
        }

        private static bool IsExternCall(ReadOnlySpan<ScriptInstruction> code, int index, Callee callee) => IsExtern(code, SkipDefaults(code, index, callee), callee);

        private bool IsDistanceCheck(ReadOnlySpan<ScriptInstruction> code, int index, out TriggerArea area, out ReadOnlySpan<ScriptInstruction> body)
        {
            area = default;
            body = default;
            if (!IsNumber(code, index, 0) || !IsExtern(code, index + 1, _flagman) || !IsNumber(code, index + 2) || !IsNumber(code, index + 3))
                return false;

            int call = SkipDefaults(code, index + 4, _sizeTo);
            if (!IsExtern(code, call, _sizeTo) || !IsNumber(code, call + 1) || code[call + 1].Operand < 0 || !Is(code, call + 2, ScriptOpcode.Less) ||
                !Is(code, call + 3, ScriptOpcode.JumpIfFalseOnce))
                return false;

            area = new TriggerArea(new Point(code[index + 2].Operand, code[index + 3].Operand), code[call + 1].Operand);
            body = GetBody(code, call + 3);

            return true;
        }

        private static ReadOnlySpan<ScriptInstruction> GetBody(ReadOnlySpan<ScriptInstruction> code, int jump)
        {
            int target = code[jump].Target;
            int end = jump + 1;
            while (end < code.Length && code[end].Offset < target)
            {
                end++;
            }

            return code[(jump + 1)..end];
        }

        private static int? FindFirstArgument(ReadOnlySpan<ScriptInstruction> body, Callee callee)
        {
            for (int index = 0; index < body.Length; index++)
            {
                if (IsNumber(body, index) && IsExternCall(body, index + 1, callee))
                    return body[index].Operand;

                if (callee.Address < 0 || body[index].Opcode != ScriptOpcode.Call || body[index].Operand != callee.Address)
                    continue;

                for (int argument = index - 1; argument > 0; argument--)
                {
                    if (body[argument].Opcode == ScriptOpcode.Assign && body[argument].Operand == callee.FirstVariable)
                        return IsNumber(body, argument - 1) ? body[argument - 1].Operand : null;
                }
            }

            return null;
        }

        private static bool UsesVariable(ReadOnlySpan<ScriptInstruction> body, int variable)
        {
            foreach (var instruction in body)
            {
                if (instruction.Opcode is >= ScriptOpcode.PostIncrement and <= ScriptOpcode.CompoundAssign && instruction.Operand == variable)
                    return true;
            }

            return false;
        }

        private static bool Calls(ReadOnlySpan<ScriptInstruction> code, Callee callee)
        {
            foreach (var instruction in code)
            {
                if (callee.Address >= 0 && instruction.Opcode == ScriptOpcode.Call && instruction.Operand == callee.Address)
                    return true;
            }

            return false;
        }

        private ScriptTrigger ReadTrigger(TriggerArea area, ReadOnlySpan<ScriptInstruction> body, uint? lightOff, bool wins)
        {
            int? fixedX = FindAssignedNumber(body, _startX);
            int? fixedY = FindAssignedNumber(body, _startY);
            var respawn = fixedX is not null && fixedY is not null ? ScriptRespawn.Fixed : RespawnsAtPlayer(body) ? ScriptRespawn.Player : ScriptRespawn.None;
            var point = respawn == ScriptRespawn.Fixed ? new Point(fixedX!.Value, fixedY!.Value) : Point.Empty;

            return new ScriptTrigger(area, respawn, point, AssignsNumber(body, _doorsOpen, 0), lightOff, Math.Max(FindFirstArgument(body, _earthQuake) ?? 0, 0),
                SwapsKeypads(body), wins, ReadKills(body));
        }

        private static int? FindAssignedNumber(ReadOnlySpan<ScriptInstruction> body, int variable)
        {
            for (int index = 1; index < body.Length; index++)
            {
                if (variable >= 0 && body[index].Opcode == ScriptOpcode.Assign && body[index].Operand == variable && IsNumber(body, index - 1))
                    return body[index - 1].Operand;
            }

            return null;
        }

        private static bool AssignsNumber(ReadOnlySpan<ScriptInstruction> body, int variable, int value)
        {
            for (int index = 1; index < body.Length; index++)
            {
                if (variable >= 0 && body[index].Opcode == ScriptOpcode.Assign && body[index].Operand == variable && IsNumber(body, index - 1, value))
                    return true;
            }

            return false;
        }

        private bool RespawnsAtPlayer(ReadOnlySpan<ScriptInstruction> body)
        {
            for (int index = 2; index < body.Length; index++)
            {
                if (body[index].Opcode == ScriptOpcode.Assign && body[index].Operand == _startX && IsExtern(body, index - 1, _getX) && IsExtern(body, index - 2, _flagman))
                    return true;
            }

            return false;
        }

        private int MatchUnitTest(ReadOnlySpan<ScriptInstruction> code, int index, out int vid)
        {
            vid = 0;
            if (!Is(code, index, ScriptOpcode.Load) || !IsExtern(code, index + 1, _getUnitVid) || !IsNumber(code, index + 2) || !Is(code, index + 3, ScriptOpcode.Equal) ||
                !Is(code, index + 4, ScriptOpcode.JumpIfFalse))
                return -1;

            vid = code[index + 2].Operand;

            return index + 5;
        }

        private bool SwapsKeypads(ReadOnlySpan<ScriptInstruction> body)
        {
            for (int index = 0; index < body.Length; index++)
            {
                int first = MatchUnitTest(body, index, out int vid);
                if (first >= 0 && vid == KeypadOn && IsNumber(body, first, KeypadOff))
                    return true;
            }

            return false;
        }

        private KnownObject[] ReadKills(ReadOnlySpan<ScriptInstruction> body)
        {
            var kills = new List<KnownObject>();
            for (int index = 0; index < body.Length; index++)
            {
                int first = MatchUnitTest(body, index, out int vid);
                if (first >= 0 && Is(body, first, ScriptOpcode.Load) && body[first].Operand == body[index].Operand && IsNumber(body, first + 1, ChangeAnimationAction) &&
                    IsNumber(body, first + 2, (int)SpriteAnimation.Death) && IsExternCall(body, first + 3, _action))
                {
                    kills.Add((KnownObject)vid);
                }
            }

            return kills.ToArray();
        }

        private bool LocksDoorsAtStart(ReadOnlySpan<ScriptInstruction> code)
        {
            for (int index = 0; index + 1 < code.Length; index++)
            {
                if (IsNumber(code, index, 1) && Is(code, index + 1, ScriptOpcode.JumpIfFalseOnce))
                    return AssignsNumber(GetBody(code, index + 1), _doorsOpen, 0);
            }

            return false;
        }

        private bool ChecksSites(ReadOnlySpan<ScriptInstruction> code)
        {
            for (int index = 0; index + 2 < code.Length; index++)
            {
                if (IsNumber(code, index, (int)KnownObject.DynamiteSite) && IsNumber(code, index + 1) && IsExternCall(code, index + 2, _getVidData))
                    return true;
            }

            return false;
        }

        private ScriptHandler[] ReadHandlers()
        {
            var handlers = new List<ScriptHandler>();
            var objects = _script.Objects;
            for (int index = 0; index < objects.Length; index++)
            {
                var entry = objects[index];
                var name = entry.Name.Span;
                int separator = name.IndexOf((byte)'_');
                if (entry.Kind != ScriptObjectKind.Function || entry.Count != 1 || !IsOwn(entry.Address) || separator < 2 || name[0] != 'F' ||
                    !int.TryParse(name[1..separator], out int id) || !(name[(separator + 1)..].SequenceEqual("15"u8) || name[(separator + 1)..].SequenceEqual("16"u8)))
                    continue;

                var body = InlineCalls(index).Span;
                var destroys = new List<KnownObject>();
                for (int at = 0; at + 1 < body.Length; at++)
                {
                    if (IsNumber(body, at) && IsExternCall(body, at + 1, _genocide))
                    {
                        destroys.Add((KnownObject)body[at].Operand);
                    }
                }

                var state = name[(separator + 1)..].SequenceEqual("15"u8) ? SpriteAnimation.Death : SpriteAnimation.Death2;
                handlers.Add(new ScriptHandler((KnownObject)id, state, Calls(body, _win), destroys.ToArray(), ReadKills(body)));
            }

            return handlers.ToArray();
        }

        private bool IsOwn(int offset) => offset >= _own.Start.Value && offset < _own.End.Value;

        private ReadOnlyMemory<ScriptInstruction> InlineCalls(int function)
        {
            var own = Decode(_script.Objects[function].Address, _script.GetFunctionEnd(function));
            var body = new List<ScriptInstruction>(own.Length);
            body.AddRange(own.Span);
            var objects = _script.Objects;
            foreach (var instruction in own.Span)
            {
                if (instruction.Opcode != ScriptOpcode.Call || !IsOwn(instruction.Operand))
                    continue;

                for (int index = 0; index < objects.Length; index++)
                {
                    if (objects[index].Kind == ScriptObjectKind.Function && objects[index].Address == instruction.Operand && objects[index].Count == 0)
                    {
                        body.AddRange(Decode(objects[index].Address, _script.GetFunctionEnd(index)).Span);
                    }
                }
            }

            return body.ToArray();
        }

        private ScriptCrowdRule[] ReadCrowdRules(ReadOnlySpan<ScriptInstruction> code)
        {
            var rules = new List<ScriptCrowdRule>();
            for (int index = 0; index < code.Length; index++)
            {
                if (!IsNumber(code, index) || !IsNumber(code, index + 1, CountData))
                    continue;

                int call = SkipDefaults(code, index + 2, _getVidData);
                if (!IsExtern(code, call, _getVidData) || !IsNumber(code, call + 1) || !Is(code, call + 2, ScriptOpcode.Greater) || !Is(code, call + 3, ScriptOpcode.JumpIfFalse))
                    continue;

                var body = GetBody(code, call + 3);
                if (body.IsEmpty || body[^1].Opcode != ScriptOpcode.Jump)
                    continue;

                var points = new List<Vector3>();
                KnownObject placed = 0;
                for (int at = 0; at + 4 < body.Length; at++)
                {
                    if (!IsNumber(body, at) || !IsNumber(body, at + 1) || !IsNumber(body, at + 2) || !IsNumber(body, at + 3) || Is(body, at - 1, ScriptOpcode.PushNumber) ||
                        SkipDefaults(body, at + 4, _createSprite) != at + 6 || !IsExtern(body, at + 6, _createSprite))
                        continue;

                    if (points.Count == 0)
                    {
                        placed = (KnownObject)body[at].Operand;
                    }

                    points.Add(new Vector3(body[at + 1].Operand, body[at + 2].Operand, body[at + 3].Operand));
                }

                if (points.Count > 0)
                {
                    rules.Add(new ScriptCrowdRule((KnownObject)code[index].Operand, code[call + 1].Operand, placed, points.ToArray()));
                }
            }

            return rules.ToArray();
        }

        private readonly record struct Callee(int Id, int Address, int FirstVariable, int Count)
        {
            public static Callee None { get; } = new(-1, -1, 0, 0);
        }
    }
}
