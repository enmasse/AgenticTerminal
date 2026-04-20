using System.Text;
using System.Text.RegularExpressions;

namespace AgenticTerminal.Startup;

public static partial class SmokeTestOutputSanitizer
{
    [GeneratedRegex("\\u001B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex AnsiSequenceRegex();

    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var withoutAnsi = AnsiSequenceRegex().Replace(text, string.Empty);
        var lines = new List<string>();
        var builder = new StringBuilder();

        for (var index = 0; index < withoutAnsi.Length; index++)
        {
            var character = withoutAnsi[index];
            switch (character)
            {
                case '\r' when index + 1 < withoutAnsi.Length && withoutAnsi[index + 1] == '\n':
                    lines.Add(builder.ToString());
                    builder.Clear();
                    index++;
                    break;

                case '\r':
                    builder.Clear();
                    break;

                case '\n':
                    lines.Add(builder.ToString());
                    builder.Clear();
                    break;

                default:
                    if (character == '\t' || !char.IsControl(character))
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        lines.Add(builder.ToString());
        builder.Clear();

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (lineIndex > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[lineIndex]);
        }

        return builder.ToString();
    }
}
