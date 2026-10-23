public class Options {
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> flags = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string name, string defaultValue = null) => values.TryGetValue(name, out var value) ? value : defaultValue;
    public string Require(string name) => Get(name) ?? throw new ArgumentException($"missing --{name}");

    public static Options Parse(IEnumerable<string> args)
    {
        var options = new Options();
        string key = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--"))
            {
                if (key != null) options.flags.Add(key);
                key = arg.Substring(2);
            }
            else if (key != null)
            {
                options.values[key] = arg;
                key = null;
            }
        }
        if (key != null) options.flags.Add(key);
        return options;
    }
}