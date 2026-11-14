namespace ASToolkit.Core.Simulation;

internal static class TimelineExtensions
{
    // Searches entries kept in time order with a bound no entry equals, leaving the count of earlier entries in the complement
    public static int CountAtOrBefore<T>(this ReadOnlySpan<T> entries, int time) where T : ITimed =>
        ~entries.BinarySearch(new AfterTime<T>(time));

    public static int FindLastAtOrBefore<T>(this ReadOnlySpan<T> entries, int time) where T : ITimed =>
        entries.CountAtOrBefore(time) - 1;

    private readonly struct AfterTime<T> : IComparable<T> where T : ITimed
    {
        private readonly int _time;

        public AfterTime(int time)
        {
            _time = time;
        }

        public int CompareTo(T? other) => other!.Time <= _time ? 1 : -1;
    }
}
