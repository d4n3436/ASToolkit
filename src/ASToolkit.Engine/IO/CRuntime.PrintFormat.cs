namespace ASToolkit.Engine.IO;

public static partial class CRuntime
{
    private const int MaxDigits = 32;

    private static ReadOnlySpan<byte> LowerDigits => "0123456789abcdef"u8;

    private static ReadOnlySpan<byte> UpperDigits => "0123456789ABCDEF"u8;

    private static ReadOnlySpan<byte> NullText => "(null)"u8;

    internal static int PrintFormat(Span<byte> destination, ReadOnlySpan<byte> format, ReadOnlySpan<FormatArgument> arguments)
    {
        var output = new PrintOutput(destination);
        int next = 0;
        int i = 0;
        while (i < format.Length && format[i] != 0)
        {
            byte character = format[i++];

            if (character != '%')
            {
                output.Put(character);
                continue;
            }

            bool left = false, plus = false, space = false, alternate = false, zero = false;
            for (; i < format.Length; i++)
            {
                switch (format[i])
                {
                    case (byte)'-': left = true; continue;
                    case (byte)'+': plus = true; continue;
                    case (byte)' ': space = true; continue;
                    case (byte)'#': alternate = true; continue;
                    case (byte)'0': zero = true; continue;
                }

                break;
            }

            int width;
            if (i < format.Length && format[i] == '*')
            {
                width = Take(arguments, ref next).Number;
                i++;

                if (width < 0)
                {
                    left = true;
                    width = -width;
                }
            }
            else
            {
                width = ReadDigits(format, ref i);
            }

            int precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                if (i < format.Length && format[i] == '*')
                {
                    precision = Math.Max(0, Take(arguments, ref next).Number);
                    i++;
                }
                else
                {
                    precision = ReadDigits(format, ref i);
                }
            }

            while (i < format.Length && format[i] is (byte)'h' or (byte)'l' or (byte)'L' or (byte)'w')
            {
                i++;
            }

            if (i >= format.Length)
                break;

            byte conversion = format[i++];
            switch (conversion)
            {
                case (byte)'d':
                case (byte)'i':
                {
                    int value = Take(arguments, ref next).Number;
                    long magnitude = Math.Abs((long)value);
                    byte sign = value < 0 ? (byte)'-' : plus ? (byte)'+' : space ? (byte)' ' : (byte)0;
                    WriteNumber(ref output, (ulong)magnitude, 10, LowerDigits, sign, default, width, precision, left, zero);
                    break;
                }
                case (byte)'u':
                    WriteNumber(ref output, unchecked((uint)Take(arguments, ref next).Number), 10, LowerDigits, 0, default, width, precision, left, zero);
                    break;
                case (byte)'x':
                case (byte)'X':
                {
                    uint value = unchecked((uint)Take(arguments, ref next).Number);
                    var digits = conversion == 'X' ? UpperDigits : LowerDigits;
                    var prefix = alternate && value != 0 ? conversion == 'X' ? "0X"u8 : "0x"u8 : default;
                    WriteNumber(ref output, value, 16, digits, 0, prefix, width, precision, left, zero);
                    break;
                }
                case (byte)'o':
                    WriteNumber(ref output, unchecked((uint)Take(arguments, ref next).Number), 8, LowerDigits, 0, default, width, precision, left, zero);
                    break;
                case (byte)'c':
                    Pad(ref output, width - 1, left, false);
                    output.Put(unchecked((byte)Take(arguments, ref next).Number));
                    Pad(ref output, width - 1, !left, false);
                    break;
                case (byte)'s':
                {
                    var argument = Take(arguments, ref next);
                    var text = GetString(argument.IsText ? argument.Text.Span : NullText);

                    if (precision >= 0 && precision < text.Length)
                    {
                        text = text[..precision];
                    }

                    Pad(ref output, width - text.Length, left, false);
                    output.Put(text);
                    Pad(ref output, width - text.Length, !left, false);
                    break;
                }
                case (byte)'%':
                    output.Put((byte)'%');
                    break;
                default:
                    output.Put(conversion);
                    break;
            }
        }

        return output.Length;
    }

    private static int ReadDigits(ReadOnlySpan<byte> format, ref int index)
    {
        int value = 0;
        while (index < format.Length && char.IsAsciiDigit((char)format[index]))
        {
            value = value * 10 + format[index++] - '0';
        }

        return value;
    }

    private static FormatArgument Take(ReadOnlySpan<FormatArgument> arguments, ref int next) => next < arguments.Length ? arguments[next++] : default;

    private static void WriteNumber(ref PrintOutput output, ulong value, int radix, ReadOnlySpan<byte> digits, byte sign, ReadOnlySpan<byte> prefix, int width, int precision, bool left, bool zero)
    {
        Span<byte> buffer = stackalloc byte[MaxDigits];
        int count = 0;
        while (value != 0)
        {
            buffer[MaxDigits - ++count] = digits[(int)(value % (ulong)radix)];
            value /= (ulong)radix;
        }

        if (count == 0 && precision != 0)
        {
            buffer[MaxDigits - ++count] = (byte)'0';
        }

        int zeros = Math.Max(0, precision - count);
        int body = zeros + count + prefix.Length + (sign != 0 ? 1 : 0);
        bool padsZeros = zero && !left && precision < 0;

        if (!padsZeros)
        {
            Pad(ref output, width - body, left, false);
        }

        if (sign != 0)
        {
            output.Put(sign);
        }

        output.Put(prefix);

        if (padsZeros)
        {
            Pad(ref output, width - body, false, true);
        }

        for (int z = 0; z < zeros; z++)
        {
            output.Put((byte)'0');
        }

        output.Put(buffer[(MaxDigits - count)..]);

        if (!padsZeros)
        {
            Pad(ref output, width - body, !left, false);
        }
    }

    private static void Pad(ref PrintOutput output, int count, bool skip, bool zeros)
    {
        if (skip)
            return;

        for (int k = 0; k < count; k++)
        {
            output.Put(zeros ? (byte)'0' : (byte)' ');
        }
    }
}
