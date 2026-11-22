using ASToolkit.Formats;

namespace ASToolkit.Engine.IO;

public static partial class CRuntime
{
    private const int ShortSortLimit = 8;
    private const int StackSize = 30;

    // C Runtime's qsort
    [EngineRoutine(0x0046da72)]
    internal static void Sort<T>(Span<T> items, Comparison<T> compare)
    {
        if (items.Length < 2)
            return;

        Span<int> lowStack = stackalloc int[StackSize];
        Span<int> highStack = stackalloc int[StackSize];
        int stackCount = 0;
        int low = 0;
        int high = items.Length - 1;
        while (true)
        {
            int size = high - low + 1;
            if (size > ShortSortLimit)
            {
                Swap(items, low + size / 2, low);
                int lowGuy = low;
                int highGuy = high + 1;
                while (true)
                {
                    do
                    {
                        lowGuy++;
                    }
                    while (lowGuy <= high && compare(items[lowGuy], items[low]) <= 0);

                    do
                    {
                        highGuy--;
                    }
                    while (highGuy > low && compare(items[highGuy], items[low]) >= 0);

                    if (highGuy < lowGuy)
                        break;

                    Swap(items, lowGuy, highGuy);
                }

                Swap(items, low, highGuy);
                if (highGuy - low > high - lowGuy)
                {
                    if (low + 1 < highGuy)
                    {
                        lowStack[stackCount] = low;
                        highStack[stackCount] = highGuy - 1;
                        stackCount++;
                    }

                    if (lowGuy < high)
                    {
                        low = lowGuy;
                        continue;
                    }
                }
                else
                {
                    if (lowGuy < high)
                    {
                        lowStack[stackCount] = lowGuy;
                        highStack[stackCount] = high;
                        stackCount++;
                    }

                    if (low + 1 < highGuy)
                    {
                        high = highGuy - 1;
                        continue;
                    }
                }
            }
            else
            {
                ShortSort(items, low, high, compare);
            }

            stackCount--;
            if (stackCount < 0)
                return;

            low = lowStack[stackCount];
            high = highStack[stackCount];
        }
    }

    [EngineRoutine(0x0046dbc6)]
    internal static void ShortSort<T>(Span<T> items, int low, int high, Comparison<T> compare)
    {
        for (; low < high; high--)
        {
            int largest = low;
            for (int i = low + 1; i <= high; i++)
            {
                if (compare(items[i], items[largest]) > 0)
                {
                    largest = i;
                }
            }

            Swap(items, largest, high);
        }
    }

    [EngineRoutine(0x0046dc14)]
    internal static void Swap<T>(Span<T> items, int first, int second)
    {
        if (first != second)
        {
            (items[first], items[second]) = (items[second], items[first]);
        }
    }
}
