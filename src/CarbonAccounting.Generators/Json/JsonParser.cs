using System;
using System.Globalization;
using System.Text;

namespace CarbonAccounting.Generators.Json;

/// <summary>Raised when a catalog file is not well-formed JSON.</summary>
internal sealed class JsonParseException : Exception
{
    public JsonParseException(string message, int position)
        : base(message)
    {
        Position = position;
    }

    /// <summary>Zero-based character offset the error was detected at.</summary>
    public int Position { get; }
}

/// <summary>
/// A recursive-descent JSON reader, hand-written so the generator itself pulls in no
/// package beyond Roslyn. Analyzers that ship extra assemblies are a well-known source
/// of assembly-load conflicts in Visual Studio, and the input here is a handful of
/// small files under our own control.
/// </summary>
/// <remarks>
/// Supports the full JSON grammar except for the parts the catalog schema does not
/// use: no big-integer preservation (all numbers become <see cref="double"/>).
/// </remarks>
internal static class JsonParser
{
    public static JsonNode Parse(string text)
    {
        int position = 0;
        SkipWhitespace(text, ref position);
        JsonNode value = ParseValue(text, ref position);
        SkipWhitespace(text, ref position);

        if (position != text.Length)
        {
            throw new JsonParseException("Unexpected trailing content after the top-level JSON value.", position);
        }

        return value;
    }

    private static JsonNode ParseValue(string text, ref int position)
    {
        if (position >= text.Length)
        {
            throw new JsonParseException("Unexpected end of input; a JSON value was expected.", position);
        }

        int start = position;
        JsonNode node;

        switch (text[position])
        {
            case '{':
                node = ParseObject(text, ref position);
                break;
            case '[':
                node = ParseArray(text, ref position);
                break;
            case '"':
                node = new JsonString(ParseString(text, ref position));
                break;
            case 't':
                Expect(text, ref position, "true");
                node = new JsonBoolean(true);
                break;
            case 'f':
                Expect(text, ref position, "false");
                node = new JsonBoolean(false);
                break;
            case 'n':
                Expect(text, ref position, "null");
                node = new JsonNull();
                break;
            default:
                node = new JsonNumber(ParseNumber(text, ref position));
                break;
        }

        node.Start = start;
        node.Length = position - start;
        return node;
    }

    private static JsonObject ParseObject(string text, ref int position)
    {
        var result = new JsonObject();
        position++; // '{'
        SkipWhitespace(text, ref position);

        if (Peek(text, position) == '}')
        {
            position++;
            return result;
        }

        while (true)
        {
            SkipWhitespace(text, ref position);

            if (Peek(text, position) != '"')
            {
                throw new JsonParseException("A property name string was expected.", position);
            }

            string name = ParseString(text, ref position);
            SkipWhitespace(text, ref position);

            if (Peek(text, position) != ':')
            {
                throw new JsonParseException($"':' was expected after property '{name}'.", position);
            }

            position++;
            SkipWhitespace(text, ref position);
            result.Add(name, ParseValue(text, ref position));
            SkipWhitespace(text, ref position);

            char next = Peek(text, position);
            if (next == ',')
            {
                position++;
                continue;
            }

            if (next == '}')
            {
                position++;
                return result;
            }

            throw new JsonParseException("',' or '}' was expected.", position);
        }
    }

    private static JsonArray ParseArray(string text, ref int position)
    {
        var result = new JsonArray();
        position++; // '['
        SkipWhitespace(text, ref position);

        if (Peek(text, position) == ']')
        {
            position++;
            return result;
        }

        while (true)
        {
            SkipWhitespace(text, ref position);
            result.Items.Add(ParseValue(text, ref position));
            SkipWhitespace(text, ref position);

            char next = Peek(text, position);
            if (next == ',')
            {
                position++;
                continue;
            }

            if (next == ']')
            {
                position++;
                return result;
            }

            throw new JsonParseException("',' or ']' was expected.", position);
        }
    }

    private static string ParseString(string text, ref int position)
    {
        position++; // opening quote
        var builder = new StringBuilder();

        while (true)
        {
            if (position >= text.Length)
            {
                throw new JsonParseException("Unterminated string literal.", position);
            }

            char c = text[position];

            if (c == '"')
            {
                position++;
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                position++;
                continue;
            }

            position++;
            if (position >= text.Length)
            {
                throw new JsonParseException("Unterminated escape sequence.", position);
            }

            char escape = text[position++];
            switch (escape)
            {
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case '/': builder.Append('/'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'u':
                    if (position + 4 > text.Length)
                    {
                        throw new JsonParseException("Truncated \\u escape sequence.", position);
                    }

                    string hex = text.Substring(position, 4);
                    if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                    {
                        throw new JsonParseException($"'{hex}' is not a valid \\u escape.", position);
                    }

                    builder.Append((char)code);
                    position += 4;
                    break;
                default:
                    throw new JsonParseException($"'\\{escape}' is not a valid escape sequence.", position - 1);
            }
        }
    }

    private static double ParseNumber(string text, ref int position)
    {
        int start = position;

        if (Peek(text, position) == '-')
        {
            position++;
        }

        while (position < text.Length && IsNumberChar(text[position]))
        {
            position++;
        }

        string literal = text.Substring(start, position - start);

        if (literal.Length == 0 ||
            !double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new JsonParseException($"'{literal}' is not a valid JSON number.", start);
        }

        return value;
    }

    private static bool IsNumberChar(char c) =>
        (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-';

    private static void Expect(string text, ref int position, string literal)
    {
        if (position + literal.Length > text.Length ||
            string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
        {
            throw new JsonParseException($"'{literal}' was expected.", position);
        }

        position += literal.Length;
    }

    private static char Peek(string text, int position) =>
        position < text.Length ? text[position] : '\0';

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length)
        {
            char c = text[position];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                position++;
                continue;
            }

            return;
        }
    }
}
