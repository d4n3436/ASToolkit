using System.Collections;

namespace ASToolkit.Engine.IO;

// Mirrors LIST<T>

/// <summary>
/// Represents a growable array whose entries past its count stay in place until they're overwritten.
/// </summary>
/// <typeparam name="T">The type of the entries.</typeparam>
public class MyList<T> : IReadOnlyList<T>
{
    private const int GrowthStep = 4;

    private T[] _items = [];

    internal MyList(MyError? error = null)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public int Count { get; internal set; }

    /// <summary>
    /// Gets the entries.
    /// </summary>
    public ReadOnlySpan<T> Items => _items.AsSpan(0, Count);

    internal ReadOnlySpan<T> Buffer => _items;

    internal MyError? Error { get; set; }

    /// <summary>
    /// Gets the entry at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    public ref T this[int index] => ref _items[index];

    T IReadOnlyList<T>.this[int index] => _items[index];

    /// <summary>
    /// Inserts an entry before the first one and moves the others up.
    /// </summary>
    /// <param name="item">The entry.</param>
    [EngineRoutine(0x00405ef0)]
    public void InsertFirst(T item)
    {
        ExpandForInsert();

        Array.Copy(_items, 0, _items, 1, Count++);
        _items[0] = item;
    }

    /// <summary>
    /// Returns the last entry and stores its index, to walk the list from its end.
    /// </summary>
    /// <param name="index">The index of the returned entry; unchanged if the list is empty.</param>
    /// <returns>The last entry, or the default value if the list is empty.</returns>
    [EngineRoutine(0x00412e60)]
    public T? BeginIterate(ref int index)
    {
        if (Count == 0)
            return default;

        index = Count - 1;
        return _items[index];
    }

    /// <summary>
    /// Steps a walk from the list's end to the previous entry.
    /// </summary>
    /// <param name="index">The index of the current entry, set to the index of the returned one.</param>
    /// <returns>The previous entry, or the default value once the walk passes the first entry.</returns>
    [EngineRoutine(0x00412e80)]
    public T? NextIterate(ref int index)
    {
        if (index > Count)
        {
            index = Count;
        }

        index--;
        return index >= 0 ? _items[index] : default;
    }

    /// <summary>
    /// Adds an entry after the last one.
    /// </summary>
    /// <param name="item">The entry.</param>
    [EngineRoutine(0x00424e60)]
    [EngineRoutine(0x00446930)]
    public void Insert(T item)
    {
        ExpandForInsert();

        _items[Count++] = item;
    }

    /// <summary>
    /// Grows the array to hold a number of entries, if it holds fewer.
    /// </summary>
    /// <param name="capacity">The number of entries.</param>
    [EngineRoutine(0x00425080)]
    [EngineRoutine(0x00446be0)]
    public void Expand(int capacity)
    {
        if (_items.Length < capacity)
        {
            Grow(capacity);
        }
    }

    /// <summary>
    /// Removes and returns the last entry.
    /// </summary>
    /// <returns>The entry.</returns>
    [EngineRoutine(0x0043ab70)]
    public T Pop() => _items[--Count];

    /// <summary>
    /// Returns the index of the last entry equal to a value.
    /// </summary>
    /// <param name="item">The value.</param>
    /// <returns>The index, or -1 if no entry equals the value.</returns>
    [EngineRoutine(0x00446900)]
    public int Location(T item)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            if (EqualityComparer<T>.Default.Equals(_items[i], item))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Inserts an entry at an index and moves up the entries after it.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="item">The entry.</param>
    [EngineRoutine(0x00446a10)]
    public void InsertBefore(int index, T item)
    {
        ExpandForInsert();

        int count = Count++;

        if (index < count)
        {
            Array.Copy(_items, index, _items, index + 1, count - index);
        }
        else
        {
            Expand(index + 1);
        }

        _items[index] = item;
    }

    /// <summary>
    /// Removes the entry at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>1 if the index is outside the list; otherwise, 0.</returns>
    [EngineRoutine(0x00446bb0)]
    public int DeleteNumber(int index)
    {
        if (index < 0 || index >= Count)
            return 1;

        Count--;
        _items[index] = _items[Count];

        return 0;
    }

    /// <summary>
    /// Removes every entry and frees the array.
    /// </summary>
    public void Release()
    {
        _items = [];
        Count = 0;
    }

    /// <summary>
    /// Returns an enumerator over the entries.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void ExpandForInsert()
    {
        if (Count >= _items.Length)
        {
            Grow(_items.Length * 2 + GrowthStep);
        }
    }

    private void Grow(int capacity)
    {
        try
        {
            Array.Resize(ref _items, capacity);
        }
        catch (OutOfMemoryException)
        {
            Error?.Fatal($"!!!ERROR!!!::LIST: Not enough memory {capacity}");
            throw;
        }
    }
}
