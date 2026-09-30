using System.Globalization;

namespace LargeFileTools.Shared;

/// <summary>
/// Parses the applications' small, explicit option sets.
/// </summary>
public sealed class CliArguments
{
    /// <summary>
    /// Supplied options and their values.
    /// </summary>
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    /// <summary>
    /// Validates option names, values, and duplicates.
    /// </summary>
    public CliArguments(string[] args, params string[] valueOptions)
    {
        var allowed = new HashSet<string>(valueOptions, StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (values.ContainsKey(option))
                throw new CliException($"Duplicate option: {option}.");
            if (!allowed.Contains(option))
                throw new CliException($"Unknown option: {option}. Use --help.");
            if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new CliException($"Missing value for {option}.");
            values.Add(option, args[i]);
        }
    }

    /// <summary>
    /// Returns a required, nonempty value.
    /// </summary>
    public string Required(string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new CliException($"Missing required option: {name}.");

    /// <summary>
    /// Returns a supplied value or its default.
    /// </summary>
    public string Get(string name, string defaultValue) => values.GetValueOrDefault(name, defaultValue);

    /// <summary>
    /// Reads an integer with an optional lower bound.
    /// </summary>
    public int Integer(string name, int defaultValue, int minimum = int.MinValue)
    {
        if (!values.TryGetValue(name, out var text))
            return defaultValue;
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < minimum)
            throw new CliException($"{name} must be an integer from {minimum} to {int.MaxValue}.");
        return value;
    }

    /// <summary>
    /// Reads whole megabytes and converts them to bytes.
    /// </summary>
    public long Megabytes(string name, int? defaultValue = null)
    {
        var text = values.TryGetValue(name, out var supplied)
            ? supplied
            : defaultValue?.ToString(CultureInfo.InvariantCulture) ?? Required(name);
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var megabytes) || megabytes <= 0)
            throw new CliException($"{name} must be a whole number from 1 to {int.MaxValue} MB.");
        return megabytes * 1024L * 1024;
    }
}
