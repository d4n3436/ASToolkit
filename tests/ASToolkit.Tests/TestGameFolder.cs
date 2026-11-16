using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Scripting;
using Xunit;

namespace ASToolkit.Tests;

internal static class TestGameFolder
{
    private const string EnvironmentVariable = "ALIEN_SHOOTER_TEST_GAME";

    private static readonly Lazy<ObjectTable> LoadedObjects = new(() => ObjectTable.Load(Path.Combine(RequireFolder(), "objects.res")));
    private static readonly Lazy<GameTable> LoadedTable = new(() => GameTable.Load(Path.Combine(RequireFolder(), "Maps", "common_table.lgc")));
    private static readonly Lazy<ReferenceLevelSet> LoadedReferences = new(() => ReferenceLevelSet.Load(RequireFolder(), LoadedObjects.Value));

    public static ObjectTable Objects
    {
        get
        {
            RequireFolder();
            return LoadedObjects.Value;
        }
    }

    public static GameTable Table => LoadedTable.Value;

    public static ReferenceLevelSet References
    {
        get
        {
            RequireFolder();
            return LoadedReferences.Value;
        }
    }

    public static string RequireFolder()
    {
        string? folder = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrEmpty(folder))
        {
            Assert.Skip($"{EnvironmentVariable} is not set to a game folder.");
        }

        return folder;
    }
}
