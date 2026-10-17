using Xunit;

namespace ASToolkit.Tests;

internal static class TestGameFolder
{
    private const string EnvironmentVariable = "ALIEN_SHOOTER_TEST_GAME";

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
