using RouterPlus.Core.Updates;

namespace RouterPlus.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var result = await new UpdateTransaction().ExecuteAsync(options);
            return (int)result;
        }
        catch (ArgumentException)
        {
            return (int)UpdateTransactionResult.ValidationFailed;
        }
        catch (FormatException)
        {
            return (int)UpdateTransactionResult.ValidationFailed;
        }
    }

    private static UpdateTransactionOptions ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var restart = false;
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (string.Equals(key, "--restart", StringComparison.OrdinalIgnoreCase))
            {
                if (restart)
                {
                    throw new ArgumentException("Duplicate restart flag.");
                }

                restart = true;
                continue;
            }

            if (!key.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException("Invalid updater arguments.");
            }

            var value = args[++index];
            if (string.IsNullOrWhiteSpace(value) || !values.TryAdd(key, value))
            {
                throw new ArgumentException("Duplicate or empty updater argument.");
            }
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--pid", "--target", "--staging", "--backup", "--app", "--version"
        };
        if (values.Keys.Any(key => !allowed.Contains(key)) || !restart)
        {
            throw new ArgumentException("Updater arguments are incomplete.");
        }

        return new UpdateTransactionOptions(
            Get(values, "--target"),
            Get(values, "--staging"),
            Get(values, "--backup"),
            Get(values, "--app"),
            int.Parse(Get(values, "--pid")),
            ReleaseVersion.Parse(Get(values, "--version")),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
            ? value
            : throw new ArgumentException($"Missing argument: {key}");
}
