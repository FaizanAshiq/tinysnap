namespace Tinysnap.Linux.Tests;

/// <summary>A gsettings that keeps everything in memory, schemas and relocatable paths alike.</summary>
internal sealed class FakeGSettings
{
    public Dictionary<string, string> Values { get; } = [];

    public (int Exit, string Output) Run(string program, string[] args)
    {
        switch (args)
        {
            case ["get", var schema, var key]:
                return Values.TryGetValue($"{schema}|{key}", out var stored) ? (0, stored) : (0, "@as []");
            case ["set", var schema, var key, var written]:
                Values[$"{schema}|{key}"] = written;
                return (0, "");
            case ["reset", var schema, var key]:
                Values.Remove($"{schema}|{key}");
                return (0, "");
            case ["reset-recursively", var schema]:
                foreach (var key in Values.Keys.Where(k => k.StartsWith(schema + "|")).ToList()) Values.Remove(key);
                return (0, "");
            case ["list-recursively", var schema]:
                return (0, string.Join('\n', Values.Where(p => p.Key.StartsWith(schema + "|"))
                                                   .Select(p => $"{schema} {p.Key[(schema.Length + 1)..]} {p.Value}")));
            default:
                return (1, "");
        }
    }
}
