using System.Text;

namespace Tinysnap.Linux;

/// <summary>GNOME's settings through the <c>gsettings</c> tool, with string arrays read and written
/// in its text form, for example <c>['&lt;Super&gt;a', 'Print']</c>.</summary>
internal sealed class GSettings(Func<string, string[], (int Exit, string Output)>? run = null)
{
    private readonly Func<string, string[], (int Exit, string Output)> run = run ?? Commands.Run;

    public string? Get(string schema, string key) => run("gsettings", ["get", schema, key]) is (0, var output) ? output : null;

    public bool Set(string schema, string key, string value) => run("gsettings", ["set", schema, key, value]).Exit == 0;

    public void Reset(string schema, string key) => run("gsettings", ["reset", schema, key]);

    public void ResetAll(string schema) => run("gsettings", ["reset-recursively", schema]);

    public IReadOnlyList<string> GetStrings(string schema, string key) => Get(schema, key) is { } text ? ParseStrings(text) : [];

    public bool SetStrings(string schema, string key, IEnumerable<string> values) =>
        Set(schema, key, "[" + string.Join(", ", values.Select(Quote)) + "]");

    /// <summary>Every value a schema holds, one per key, as <c>list-recursively</c> prints them.</summary>
    public IEnumerable<string> ListValues(string schema) =>
        run("gsettings", ["list-recursively", schema]) is (0, var output)
            ? output.Split('\n').Select(line => line.Split(' ', 3)).Where(parts => parts.Length == 3).Select(parts => parts[2])
            : [];

    public static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>The strings in a printed array; anything that is not a string array reads as none.</summary>
    public static IReadOnlyList<string> ParseStrings(string text)
    {
        var strings = new List<string>();
        var current = new StringBuilder();
        var inside = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!inside)
            {
                if (c == '\'') inside = true;
                continue;
            }
            if (c == '\\' && i + 1 < text.Length) current.Append(text[++i]);
            else if (c == '\'')
            {
                strings.Add(current.ToString());
                current.Clear();
                inside = false;
            }
            else current.Append(c);
        }
        return strings;
    }
}
