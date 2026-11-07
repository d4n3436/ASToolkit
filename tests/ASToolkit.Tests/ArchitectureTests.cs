using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using ASToolkit.Core.Generation;
using Xunit;

namespace ASToolkit.Tests;

public sealed class ArchitectureTests
{
    private const string Root = "ASToolkit.Core";
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    private static readonly Lazy<Dictionary<string, HashSet<string>>> Edges = new(() => GetNamespaceEdges(typeof(LevelGenerator).Assembly));

    [Fact]
    public void NamespacesFormNoCycle()
    {
        var edges = Edges.Value;
        var reach = edges.Keys.ToDictionary(name => name, name => GetReach(edges, name));
        string[] cycles = reach
            .SelectMany(pair => pair.Value.Where(other => string.CompareOrdinal(pair.Key, other) < 0 && reach.GetValueOrDefault(other)?.Contains(pair.Key) == true)
                .Select(other => $"{pair.Key} <-> {other}"))
            .Order()
            .ToArray();

        Assert.True(cycles.Length == 0, string.Join(Environment.NewLine, cycles));
    }

    [Fact]
    public void GenerationRootHoldsOnlyTheGenerator()
    {
        string[] names = typeof(LevelGenerator).Assembly.GetTypes()
            .Where(type => type is { Namespace: $"{Root}.Generation", IsNested: false } && !type.IsDefined(typeof(CompilerGeneratedAttribute)))
            .Select(type => type.Name)
            .Order()
            .ToArray();

        Assert.Equal(["GeneratedLevel", "LevelGenerator"], names);
    }

    private static HashSet<string> GetReach(Dictionary<string, HashSet<string>> edges, string from)
    {
        var reached = new HashSet<string>();
        var stack = new Stack<string>(edges[from]);
        while (stack.Count > 0)
        {
            string name = stack.Pop();
            if (!reached.Add(name))
                continue;
            
            foreach (string next in edges.GetValueOrDefault(name) ?? [])
            {
                stack.Push(next);
            }
        }

        return reached;
    }

    private static Dictionary<string, HashSet<string>> GetNamespaceEdges(Assembly assembly)
    {
        var edges = new Dictionary<string, HashSet<string>>();
        foreach (var type in assembly.GetTypes())
        {
            string from = type.Namespace ?? "";
            if (!from.StartsWith(Root, StringComparison.Ordinal))
            {
                continue;
            }

            if (!edges.TryGetValue(from, out var targets))
            {
                targets = edges[from] = [];
            }

            var named = new HashSet<Type>();
            AddType(named, type.BaseType);
            foreach (var face in type.GetInterfaces())
            {
                AddType(named, face);
            }

            foreach (var field in type.GetFields(Declared))
            {
                AddType(named, field.FieldType);
            }

            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                AddMethod(named, method);
            }

            targets.UnionWith(named
                .Select(other => other.Namespace ?? "")
                .Where(name => name != from && name.StartsWith(Root, StringComparison.Ordinal)));
        }

        return edges;
    }

    private static void AddMethod(HashSet<Type> named, MethodBase method)
    {
        if (method is MethodInfo info)
        {
            AddType(named, info.ReturnType);
        }

        foreach (var parameter in method.GetParameters())
        {
            AddType(named, parameter.ParameterType);
        }

        var body = method.GetMethodBody();
        if (body is null)
        {
            return;
        }

        foreach (var local in body.LocalVariables)
        {
            AddType(named, local.LocalType);
        }

        byte[] il = body.GetILAsByteArray() ?? [];
        var owner = method.DeclaringType;
        var typeArguments = owner is not null && owner.IsGenericType ? owner.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        int at = 0;
        while (at < il.Length)
        {
            short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
            at += il[at] == 0xFE ? 2 : 1;
            var code = OpCodesByValue[value];
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                var member = method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                AddType(named, member as Type ?? member?.DeclaringType);
                if (member is MethodInfo { IsGenericMethod: true } called)
                {
                    foreach (var argument in called.GetGenericArguments())
                    {
                        AddType(named, argument);
                    }
                }
            }

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                _ => 4
            };
        }
    }

    private static void AddType(HashSet<Type> named, Type? type)
    {
        if (type is null || type.IsGenericParameter || !named.Add(type))
        {
            return;
        }

        AddType(named, type.GetElementType());
        if (!type.IsGenericType)
            return;
        
        foreach (var argument in type.GetGenericArguments())
        {
            AddType(named, argument);
        }
    }
}
