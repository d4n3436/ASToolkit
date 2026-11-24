using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Provides methods for reading level scripts.
/// </summary>
public static class LevelScriptReader
{
    private const string MapsFolder = "Maps";

    /// <summary>
    /// Reads the facts of the level script at a path in a game folder's Maps folder.
    /// </summary>
    /// <param name="path">The path of the level script.</param>
    /// <param name="version">The version of the script language the level script is written in.</param>
    /// <returns>The facts of the level script, or empty facts if the file doesn't exist.</returns>
    public static LevelScriptSummary Read(string path, ScriptVersion version) =>
        File.Exists(path) ? new CompiledLevel(Compile(path, version)).Read() : new LevelScriptSummary();

    /// <summary>
    /// Reads the facts of a level script from its bytes and opens its includes from a game folder.
    /// </summary>
    /// <param name="data">The bytes of the level script.</param>
    /// <param name="gameFolder">The game folder the includes are opened from.</param>
    /// <param name="version">The version of the script language the level script is written in.</param>
    /// <param name="name">The path the level script is compiled under.</param>
    /// <returns>The facts of the level script.</returns>
    public static LevelScriptSummary Parse(ReadOnlyMemory<byte> data, string gameFolder, ScriptVersion version, string name = "level.lgc") =>
        new CompiledLevel(Compile(data, gameFolder, name, version)).Read();

    private static Script Compile(string path, ScriptVersion version)
    {
        string gameFolder = FindGameFolder(path);

        return Compile(File.ReadAllBytes(path), gameFolder, Path.GetRelativePath(gameFolder, path).Replace('/', '\\'), version);
    }

    private static Script Compile(ReadOnlyMemory<byte> data, string gameFolder, string name, ScriptVersion version)
    {
        var script = new Script(version);
        script.Load(new MarkedScriptSource(new GameFolder(gameFolder), data), name);

        return script;
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
        private const int AlienShooter2GetQuestsState = 48;
        private const int AlienShooter2MissionFailed = 4;
        private const int AlienShooter2SetDeathTimer = 113;
        private const int SpaceKey = ' ';
        private readonly Script _script;
        private readonly ReadOnlyMemory<ScriptInstruction> _code;
        private readonly Range _own;
        private readonly ScriptCallee _flagman;
        private readonly ScriptCallee _sizeTo;
        private readonly ScriptCallee _lightOff;
        private readonly ScriptCallee _getX;
        private readonly ScriptCallee _getUnitVid;
        private readonly ScriptCallee _action;
        private readonly ScriptCallee _getVidData;
        private readonly ScriptCallee _genocide;
        private readonly ScriptCallee _createSprite;
        private readonly ScriptCallee _earthQuake;
        private readonly ScriptCallee _win;
        private readonly ScriptCallee _campaignTact;
        private readonly ScriptCallee _getKey;
        private readonly ScriptCallee _failCurrentQuest;
        private readonly ScriptCallee[] _wins;
        private readonly ScriptCallee[] _lights;
        private readonly Dictionary<int, string> _variableNames = [];
        private readonly int _startX;
        private readonly int _startY;
        private readonly int _doorsOpen;
        private readonly int _gameState;

        public CompiledLevel(Script script)
        {
            _script = script;
            _own = MarkedScriptSource.GetRootCode(script);
            _code = Decode(_own.Start.Value, _own.End.Value);
            _flagman = ScriptCallee.Find(_script, "Flagman"u8);
            _sizeTo = ScriptCallee.Find(_script, "SizeTo"u8);
            _lightOff = ScriptCallee.Find(_script, "LightOff"u8);
            _getX = ScriptCallee.Find(_script, "GetX"u8);
            _getUnitVid = ScriptCallee.Find(_script, "GetUnitVid"u8);
            _action = ScriptCallee.Find(_script, "Action"u8);
            _getVidData = ScriptCallee.Find(_script, "GetVidData"u8);
            _genocide = ScriptCallee.Find(_script, "Genocide"u8);
            _createSprite = ScriptCallee.Find(_script, "CreateSprite"u8);
            _earthQuake = ScriptCallee.Find(_script, "EarthQuake"u8);
            _win = ScriptCallee.Find(_script, "Win"u8);
            _campaignTact = ScriptCallee.Find(_script, "CompaignGameTact"u8);
            _startX = GetVariable("StartPlayerX"u8);
            _startY = GetVariable("StartPlayerY"u8);
            _doorsOpen = GetVariable("DoorsIsOpen"u8);
            _gameState = GetVariable("GameState"u8);
            _getKey = ScriptCallee.Find(_script, "GetKey"u8);
            _failCurrentQuest = ScriptCallee.Find(_script, "FailCurQuest"u8);
            _wins = [_win, ScriptCallee.Find(_script, "InstantWin"u8), ScriptCallee.Find(_script, "GoToNextMap"u8)];
            _lights = [ScriptCallee.Find(_script, "ChangeLight"u8), _lightOff, ScriptCallee.Find(_script, "SetGamma"u8)];
            foreach (var (entryName, entry) in script.Objects.Items)
            {
                if (entry.Kind == ScriptObjectKind.Variable)
                {
                    _variableNames[entry.Address] = GameTextEncoding.Cyrillic.GetString(entryName.Span);
                }
            }
        }

        public LevelScriptSummary Read()
        {
            var triggers = new List<ScriptTrigger>();
            var checkpoints = new List<TriggerArea>();
            TriggerArea? blackout = null;
            TriggerArea? exit = null;
            var code = _code.Span;
            for (int i = 0; i < code.Length; i++)
            {
                if (!IsDistanceCheck(code, i, out var area, out var body))
                    continue;

                int? color = FindFirstArgument(body, _lightOff);
                uint? lightOff = color is null ? null : unchecked((uint)color.Value);
                if (lightOff is not null)
                {
                    blackout = area;
                }

                if (UsesVariable(body, _startX))
                {
                    checkpoints.Add(area);
                }

                bool shouldWin = Calls(body, _win);
                if (shouldWin)
                {
                    exit = area;
                }

                triggers.Add(ReadTrigger(area, body, lightOff, shouldWin));
            }

            return new LevelScriptSummary
            {
                ShouldClearMonsters = Calls(code, _campaignTact),
                DoorsLocked = LocksDoorsAtStart(code),
                Blackout = blackout,
                Checkpoints = checkpoints,
                IsWonAfterSites = ChecksSites(code),
                Exit = exit,
                Triggers = triggers,
                Handlers = ReadHandlers(),
                CrowdRules = ReadCrowdRules(code),
                Blocks = _script.Version == ScriptVersion.AlienShooter2 ? ReadBlocks() : []
            };
        }

        private ReadOnlyMemory<ScriptInstruction> Decode(int start, int end)
        {
            var code = _script.Code;
            var instructions = new List<ScriptInstruction>();
            for (int offset = start; offset < end;)
            {
                var instruction = ScriptInstruction.Read(code, offset, _script.Version);
                instructions.Add(instruction);
                offset = instruction.End;
            }

            return instructions.ToArray();
        }


        private int GetVariable(ReadOnlySpan<byte> name)
        {
            int found = _script.Objects.Location(name);

            return found >= 0 && _script.Objects[found].Kind == ScriptObjectKind.Variable ? _script.Objects[found].Address : -1;
        }

        private static bool IsNumber(ReadOnlySpan<ScriptInstruction> code, int index) =>
            index >= 0 && index < code.Length && code[index].Opcode == ScriptOpcode.PushNumber;

        private static bool IsNumber(ReadOnlySpan<ScriptInstruction> code, int index, int value) => IsNumber(code, index) && code[index].Operand == value;


        private static bool HasOpcode(ReadOnlySpan<ScriptInstruction> code, int index, ScriptOpcode opcode) =>
            index >= 0 && index < code.Length && code[index].Opcode == opcode;



        private bool IsDistanceCheck(ReadOnlySpan<ScriptInstruction> code, int index, out TriggerArea area, out ReadOnlySpan<ScriptInstruction> body)
        {
            body = default;
            if (!IsPlayerWithin(code, index, out area, out int end) || !HasOpcode(code, end, ScriptOpcode.JumpIfFalseOnce))
                return false;

            body = GetBody(code, end);

            return true;
        }

        private bool IsPlayer(ReadOnlySpan<ScriptInstruction> code, int index) => IsNumber(code, index, 0) && _flagman.IsExternAt(code, index + 1);

        private bool IsPlayerWithin(ReadOnlySpan<ScriptInstruction> code, int index, out TriggerArea area, out int end)
        {
            area = default;
            end = -1;
            if (!IsPlayer(code, index) || !IsNumber(code, index + 2) || !IsNumber(code, index + 3))
                return false;

            int call = _sizeTo.SkipDefaults(code, index + 4);
            if (!_sizeTo.IsExternAt(code, call) || !IsNumber(code, call + 1) || code[call + 1].Operand < 0 || !HasOpcode(code, call + 2, ScriptOpcode.Less))
                return false;

            area = new TriggerArea(new Point(code[index + 2].Operand, code[index + 3].Operand), code[call + 1].Operand);
            end = call + 3;

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

        private static int? FindFirstArgument(ReadOnlySpan<ScriptInstruction> body, ScriptCallee callee)
        {
            for (int i = 0; i < body.Length; i++)
            {
                if (IsNumber(body, i) && callee.IsExternCallAt(body, i + 1))
                    return body[i].Operand;

                if (callee.CallOperand < 0 || body[i].Opcode != ScriptOpcode.Call || body[i].Operand != callee.CallOperand)
                    continue;

                for (int argument = i - 1; argument > 0; argument--)
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

        private static bool Calls(ReadOnlySpan<ScriptInstruction> code, ScriptCallee callee)
        {
            foreach (var instruction in code)
            {
                if (callee.CallOperand >= 0 && instruction.Opcode == ScriptOpcode.Call && instruction.Operand == callee.CallOperand)
                    return true;
            }

            return false;
        }

        private ScriptTrigger ReadTrigger(TriggerArea area, ReadOnlySpan<ScriptInstruction> body, uint? lightOff, bool shouldWin)
        {
            int? fixedX = FindAssignedNumber(body, _startX);
            int? fixedY = FindAssignedNumber(body, _startY);
            var respawn = fixedX is not null && fixedY is not null ? ScriptRespawn.Fixed : RespawnsAtPlayer(body) ? ScriptRespawn.Player : ScriptRespawn.None;
            var point = respawn == ScriptRespawn.Fixed ? new Point(fixedX!.Value, fixedY!.Value) : Point.Empty;

            return new ScriptTrigger(area, respawn, point, AssignsNumber(body, _doorsOpen, 0), lightOff, Math.Max(FindFirstArgument(body, _earthQuake) ?? 0, 0),
                ShouldSwapKeypads(body), shouldWin, ReadKills(body));
        }

        private static int? FindAssignedNumber(ReadOnlySpan<ScriptInstruction> body, int variable)
        {
            for (int i = 1; i < body.Length; i++)
            {
                if (variable >= 0 && body[i].Opcode == ScriptOpcode.Assign && body[i].Operand == variable && IsNumber(body, i - 1))
                    return body[i - 1].Operand;
            }

            return null;
        }

        private static bool AssignsNumber(ReadOnlySpan<ScriptInstruction> body, int variable, int value)
        {
            for (int i = 1; i < body.Length; i++)
            {
                if (variable >= 0 && body[i].Opcode == ScriptOpcode.Assign && body[i].Operand == variable && IsNumber(body, i - 1, value))
                    return true;
            }

            return false;
        }

        private bool RespawnsAtPlayer(ReadOnlySpan<ScriptInstruction> body)
        {
            for (int i = 2; i < body.Length; i++)
            {
                if (body[i].Opcode == ScriptOpcode.Assign && body[i].Operand == _startX && _getX.IsExternAt(body, i - 1) && _flagman.IsExternAt(body, i - 2))
                    return true;
            }

            return false;
        }

        private int MatchUnitTest(ReadOnlySpan<ScriptInstruction> code, int index, out int vid)
        {
            vid = 0;
            if (!HasOpcode(code, index, ScriptOpcode.Load) || !_getUnitVid.IsExternAt(code, index + 1) || !IsNumber(code, index + 2) || !HasOpcode(code, index + 3, ScriptOpcode.Equal) ||
                !HasOpcode(code, index + 4, ScriptOpcode.JumpIfFalse))
                return -1;

            vid = code[index + 2].Operand;

            return index + 5;
        }

        private bool ShouldSwapKeypads(ReadOnlySpan<ScriptInstruction> body)
        {
            for (int i = 0; i < body.Length; i++)
            {
                int first = MatchUnitTest(body, i, out int vid);
                if (first >= 0 && vid == (int)KnownObject.KeypadOn && IsNumber(body, first, (int)KnownObject.KeypadOff))
                    return true;
            }

            return false;
        }

        private KnownObject[] ReadKills(ReadOnlySpan<ScriptInstruction> body)
        {
            var kills = new List<KnownObject>();
            for (int i = 0; i < body.Length; i++)
            {
                int first = MatchUnitTest(body, i, out int vid);
                if (first >= 0 && HasOpcode(body, first, ScriptOpcode.Load) && body[first].Operand == body[i].Operand && IsNumber(body, first + 1, (int)ActionCode.ChangeAnimation) &&
                    IsNumber(body, first + 2, (int)SpriteAnimation.Death) && _action.IsExternCallAt(body, first + 3))
                {
                    kills.Add((KnownObject)vid);
                }
            }

            return kills.ToArray();
        }

        private bool LocksDoorsAtStart(ReadOnlySpan<ScriptInstruction> code)
        {
            for (int i = 0; i + 1 < code.Length; i++)
            {
                if (IsNumber(code, i, 1) && HasOpcode(code, i + 1, ScriptOpcode.JumpIfFalseOnce))
                    return AssignsNumber(GetBody(code, i + 1), _doorsOpen, 0);
            }

            return false;
        }

        private bool ChecksSites(ReadOnlySpan<ScriptInstruction> code)
        {
            for (int i = 0; i + 2 < code.Length; i++)
            {
                if (IsNumber(code, i, (int)KnownObject.DynamiteSite) && IsNumber(code, i + 1) && _getVidData.IsExternCallAt(code, i + 2))
                    return true;
            }

            return false;
        }

        private ScriptHandler[] ReadHandlers()
        {
            var handlers = new List<ScriptHandler>();
            var objects = _script.Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                var entry = objects[i];
                var name = objects.Name(i).Span;
                int separator = name.IndexOf((byte)'_');
                if (entry.Kind != ScriptObjectKind.Function || entry.Count != 1 || !IsOwn(entry.Address) || separator < 2 || name[0] != 'F' ||
                    !int.TryParse(name[1..separator], out int id) || !(name[(separator + 1)..].SequenceEqual("15"u8) || name[(separator + 1)..].SequenceEqual("16"u8)))
                    continue;

                var body = InlineCalls(i).Span;
                var destroys = new List<KnownObject>();
                for (int at = 0; at + 1 < body.Length; at++)
                {
                    if (IsNumber(body, at) && _genocide.IsExternCallAt(body, at + 1))
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
                if (instruction.Opcode != ScriptOpcode.Call)
                    continue;

                int i = _script.FindCalledFunction(instruction.Operand);
                if (i >= 0 && IsOwn(objects[i].Address) && objects[i].Count == 0)
                {
                    body.AddRange(Decode(objects[i].Address, _script.GetFunctionEnd(i)).Span);
                }
            }

            return body.ToArray();
        }

        private ScriptBlock[] ReadBlocks()
        {
            var blocks = new List<ScriptBlock>();
            AddBlocks(blocks, [], 0, _code.Length, FindIfs());

            return blocks.ToArray();
        }

        private static bool EndsStatement(ScriptOpcode opcode) =>
            opcode is ScriptOpcode.EndStatement or ScriptOpcode.JumpIfFalse or ScriptOpcode.JumpIfFalseOnce or ScriptOpcode.Jump or ScriptOpcode.Return;

        private List<IfStatement> FindIfs()
        {
            var code = _code.Span;
            var ifs = new List<IfStatement>();
            for (int jump = 0; jump < code.Length; jump++)
            {
                if (code[jump].Opcode is not (ScriptOpcode.JumpIfFalse or ScriptOpcode.JumpIfFalseOnce) || code[jump].Target <= code[jump].Offset ||
                    HasOpcode(code, jump + 1, ScriptOpcode.Jump))
                    continue;

                int start = jump;
                while (start > 0 && !EndsStatement(code[start - 1].Opcode))
                {
                    start--;
                }

                int end = jump + 1;
                while (end < code.Length && code[end].Offset < code[jump].Target)
                {
                    end++;
                }

                int bodyEnd = end;
                if (end - 1 > jump && code[end - 1].Opcode == ScriptOpcode.Jump)
                {
                    if (code[end - 1].Target == code[start].Offset)
                        continue;

                    bodyEnd--;
                }

                ifs.Add(new IfStatement(start, jump, bodyEnd, end));
            }

            ifs.Sort((first, second) => first.Start.CompareTo(second.Start));

            return ifs;
        }

        private void AddBlocks(List<ScriptBlock> blocks, ScriptCondition[] conditions, int from, int to, List<IfStatement> ifs)
        {
            var code = _code.Span;
            var own = new List<ScriptInstruction>();
            int i = from;
            foreach (var statement in ifs)
            {
                if (statement.Start < i || statement.Start >= to)
                    continue;

                own.AddRange(code[i..statement.Start]);
                AddBlocks(blocks, conditions.Append(ReadCondition(code[statement.Start..statement.Jump])).ToArray(), statement.Jump + 1, statement.BodyEnd, ifs);
                i = statement.End;
            }

            own.AddRange(code[i..to]);
            blocks.Add(ReadBlock(conditions, own.ToArray()));
        }

        private ScriptCondition ReadCondition(ReadOnlySpan<ScriptInstruction> condition)
        {
            TriggerArea? area = null;
            (int Line, int Sub)? quest = null;
            string? variable = null;
            int value = 0;
            bool waitForSpace = false;
            for (int i = 0; i < condition.Length; i++)
            {
                if (area is null && IsPlayerWithin(condition, i, out var within, out _))
                {
                    area = within;
                }

                if (quest is null && IsNumber(condition, i, AlienShooter2GetQuestsState) && IsNumber(condition, i + 1) && IsNumber(condition, i + 2) &&
                    condition[i + 1].Operand >= 0 && condition[i + 2].Operand >= 0)
                {
                    quest = (condition[i + 1].Operand + 1, condition[i + 2].Operand);
                }

                if (variable is null && HasOpcode(condition, i, ScriptOpcode.Load) && IsNumber(condition, i + 1) && condition[i + 1].Operand >= 0 &&
                    HasOpcode(condition, i + 2, ScriptOpcode.Equal) && _variableNames.TryGetValue(condition[i].Operand, out string? compared))
                {
                    variable = compared;
                    value = condition[i + 1].Operand;
                }

                waitForSpace |= _getKey.IsCallAt(condition, i) && IsNumber(condition, i + 1, SpaceKey) && HasOpcode(condition, i + 2, ScriptOpcode.Equal);
            }

            string? flag = HasOpcode(condition, 0, ScriptOpcode.Load) && (condition.Length == 1 || HasOpcode(condition, 1, ScriptOpcode.AndJump)) &&
                _variableNames.TryGetValue(condition[0].Operand, out string? name) ? name : null;

            return new ScriptCondition(area, quest, variable, value, flag, waitForSpace);
        }

        private ScriptBlock ReadBlock(ScriptCondition[] conditions, ReadOnlySpan<ScriptInstruction> code)
        {
            var destroys = new List<KnownObject>();
            var assignments = new List<ScriptAssignment>();
            bool shouldLoseLevel = _failCurrentQuest.IsCalledIn(code);
            Vector3? move = null;
            bool shouldSetDeathTimer = false;
            for (int i = 0; i < code.Length; i++)
            {
                if (IsNumber(code, i) && _genocide.IsExternCallAt(code, i + 1))
                {
                    destroys.Add((KnownObject)code[i].Operand);
                }

                shouldLoseLevel |= IsNumber(code, i, AlienShooter2MissionFailed) && HasOpcode(code, i + 1, ScriptOpcode.Assign) && code[i + 1].Operand == _gameState;
                if (move is null && IsPlayer(code, i) && IsNumber(code, i + 2, (int)ActionCode.ChangeCoor) && IsNumber(code, i + 3) && IsNumber(code, i + 4) &&
                    IsNumber(code, i + 5))
                {
                    move = new Vector3(code[i + 3].Operand, code[i + 4].Operand, code[i + 5].Operand);
                }

                shouldSetDeathTimer |= IsPlayer(code, i) && IsNumber(code, i + 2, AlienShooter2SetDeathTimer);
                if (HasOpcode(code, i, ScriptOpcode.Assign) && !HasOpcode(code, i + 1, ScriptOpcode.Pop) && !HasOpcode(code, i - 1, ScriptOpcode.SetIndex) &&
                    _variableNames.TryGetValue(code[i].Operand, out string? name))
                {
                    bool isLiteralStored = IsNumber(code, i - 1) && (i == 1 || EndsStatement(code[i - 2].Opcode));
                    assignments.Add(new ScriptAssignment(name, isLiteralStored ? code[i - 1].Operand : 1));
                }
            }

            return new ScriptBlock(conditions, destroys.ToArray(), shouldLoseLevel, IsAnyCalledIn(_wins, code), move, shouldSetDeathTimer, IsAnyCalledIn(_lights, code),
                assignments.ToArray());
        }

        private static bool IsAnyCalledIn(ScriptCallee[] callees, ReadOnlySpan<ScriptInstruction> code)
        {
            foreach (var callee in callees)
            {
                if (callee.IsCalledIn(code))
                    return true;
            }

            return false;
        }

        private readonly record struct IfStatement(int Start, int Jump, int BodyEnd, int End);

        private ScriptCrowdRule[] ReadCrowdRules(ReadOnlySpan<ScriptInstruction> code)
        {
            var rules = new List<ScriptCrowdRule>();
            for (int i = 0; i < code.Length; i++)
            {
                if (!IsNumber(code, i) || !IsNumber(code, i + 1, (int)VidDataType.Count))
                    continue;

                int call = _getVidData.SkipDefaults(code, i + 2);
                if (!_getVidData.IsExternAt(code, call) || !IsNumber(code, call + 1) || !HasOpcode(code, call + 2, ScriptOpcode.Greater) || !HasOpcode(code, call + 3, ScriptOpcode.JumpIfFalse))
                    continue;

                var body = GetBody(code, call + 3);
                if (body.IsEmpty || body[^1].Opcode != ScriptOpcode.Jump)
                    continue;

                var points = new List<Vector3>();
                KnownObject placed = 0;
                for (int at = 0; at + 4 < body.Length; at++)
                {
                    if (!IsNumber(body, at) || !IsNumber(body, at + 1) || !IsNumber(body, at + 2) || !IsNumber(body, at + 3) || HasOpcode(body, at - 1, ScriptOpcode.PushNumber) ||
                        _createSprite.SkipDefaults(body, at + 4) != at + 6 || !_createSprite.IsExternAt(body, at + 6))
                        continue;

                    if (points.Count == 0)
                    {
                        placed = (KnownObject)body[at].Operand;
                    }

                    points.Add(new Vector3(body[at + 1].Operand, body[at + 2].Operand, body[at + 3].Operand));
                }

                if (points.Count > 0)
                {
                    rules.Add(new ScriptCrowdRule((KnownObject)code[i].Operand, code[call + 1].Operand, placed, points.ToArray()));
                }
            }

            return rules.ToArray();
        }

    }
}
