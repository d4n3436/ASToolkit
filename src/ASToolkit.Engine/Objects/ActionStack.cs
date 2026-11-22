using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a sprite's order stack, whose popped entries stay for stack jumps.
/// </summary>
public sealed class ActionStack
{
    private const int GrowthStep = 4;

    private Act[] _orders = [];

    /// <summary>
    /// Gets the number of entries on the stack.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the top entry.
    /// </summary>
    public Act Top => _orders[Count - 1];

    /// <summary>
    /// Gets the entries from the bottom up.
    /// </summary>
    public ReadOnlySpan<Act> Orders => _orders.AsSpan(0, Count);

    /// <summary>
    /// Gets the entry at an index, counted from the bottom.
    /// </summary>
    /// <param name="index">The index.</param>
    public Act this[int index] => _orders[index];

    /// <summary>
    /// Pushes an entry.
    /// </summary>
    /// <param name="order">The entry.</param>
    [EngineRoutine(0x00446930)]
    public void Push(Act order)
    {
        if (Count >= _orders.Length)
        {
            Array.Resize(ref _orders, _orders.Length * 2 + GrowthStep);
        }

        _orders[Count++] = order;
    }

    /// <summary>
    /// Inserts an entry at an index and moves the entries above it up.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="order">The entry.</param>
    [EngineRoutine(0x00446a10)]
    public void Insert(int index, Act order)
    {
        int count = Count;
        Push(order);

        if (index < count)
        {
            Array.Copy(_orders, index, _orders, index + 1, count - index);
        }
        else
        {
            EnsureCapacity(index + 1);
        }

        _orders[index] = order;
    }

    /// <summary>
    /// Pops the top entry.
    /// </summary>
    /// <returns>The entry.</returns>
    public Act Pop() => _orders[--Count];

    /// <summary>
    /// Removes every entry.
    /// </summary>
    public void Clear()
    {
        _orders = [];
        Count = 0;
    }

    /// <summary>
    /// Sets the number of entries and brings back popped entries.
    /// </summary>
    /// <param name="count">The number of entries.</param>
    public void SetCount(int count)
    {
        Count = count;
        EnsureCapacity(count);
    }

    /// <summary>
    /// Replaces the entry at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="order">The entry.</param>
    public void Replace(int index, Act order) => _orders[index] = order;

    /// <summary>
    /// Loads the entries of a map sprite's stack.
    /// </summary>
    /// <param name="orders">The entries from the bottom up.</param>
    public void Load(ReadOnlySpan<Act> orders)
    {
        Clear();
        foreach (var order in orders)
        {
            Push(order);
        }

        for (int i = Count - 1; i >= 0; i--)
        {
            if (i != 0 && _orders[i].Value == Act.StopMarker && _orders[i - 1].Value == Act.StopMarker)
            {
                _orders[i] = _orders[--Count];
            }
        }
    }

    [EngineRoutine(0x00446be0)]
    private void EnsureCapacity(int capacity)
    {
        if (_orders.Length < capacity)
        {
            Array.Resize(ref _orders, capacity);
        }
    }
}
