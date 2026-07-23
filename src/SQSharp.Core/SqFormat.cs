using System;
using System.Text;
using System.Globalization;

namespace SQSharp.Core;

/// <summary>
/// SQF-style string formatting — similar to sprintf.
/// Supports %1, %2, ... positional placeholders.
/// </summary>
public static class SqFormat
{
    /// <summary>
    /// Format a string with positional arguments.
    /// format ["Hello %1, you have %2 items", name, count]
    /// </summary>
    public static string Format(string template, SqValue[] args)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            if (template[i] == '%' && i + 1 < template.Length)
            {
                // Parse the number after %
                int j = i + 1;
                while (j < template.Length && char.IsDigit(template[j]))
                    j++;

                if (j > i + 1)
                {
                    string numStr = template.Substring(i + 1, j - i - 1);
                    if (int.TryParse(numStr, out int argIndex) && argIndex >= 1 && argIndex <= args.Length)
                    {
                        sb.Append(ValueToString(args[argIndex - 1]));
                        i = j;
                        continue;
                    }
                }
                // %% → literal %
                if (i + 1 < template.Length && template[i + 1] == '%')
                {
                    sb.Append('%');
                    i += 2;
                    continue;
                }
            }
            sb.Append(template[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string ValueToString(SqValue value)
    {
        return value.Type switch
        {
            SqType.Nothing => "nil",
            SqType.Boolean => value.AsBoolOrDefault() ? "true" : "false",
            SqType.Number => value.AsNumberOrDefault().ToString(CultureInfo.InvariantCulture),
            SqType.String => value.AsString(),
            SqType.Array => ArrayToString(value.AsArray()),
            SqType.Code => "{code}",
            _ => value.ToString() ?? "nil"
        };
    }

    private static string ArrayToString(SqArray arr)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < arr.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(ValueToString(arr[i]));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
