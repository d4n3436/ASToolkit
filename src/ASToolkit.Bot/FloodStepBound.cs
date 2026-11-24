namespace ASToolkit.Bot;

internal readonly struct FloodStepBound : IComparable<int>
{
    private readonly int[] _distance;
    private readonly int _maxSteps;

    public FloodStepBound(int[] distance, int maxSteps)
    {
        _distance = distance;
        _maxSteps = maxSteps;
    }

    public int CompareTo(int cell) => _distance[cell] <= _maxSteps ? 1 : -1;
}
