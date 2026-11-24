using ASToolkit.Engine;

namespace ASToolkit.Bot;

internal readonly record struct ProgressMark(float Farthest, int Sites, int FewestEnemies, int ProgressedAt)
{
    public static ProgressMark Measure(Map map, PlayerBot bot, int enemies) =>
        new(bot.FarthestProgress, map.DynamiteSites.Count, enemies, map.Time);

    public ProgressMark Update(Map map, PlayerBot bot, int enemies)
    {
        if (bot.FarthestProgress > Farthest || map.DynamiteSites.Count < Sites || enemies < FewestEnemies)
            return new ProgressMark(bot.FarthestProgress, map.DynamiteSites.Count, Math.Min(FewestEnemies, enemies), map.Time);

        return this;
    }
}
