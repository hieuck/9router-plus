using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeProfileReader
{
    public IReadOnlyList<ChromeProfile> Read(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);

        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        if (!File.Exists(localStatePath))
        {
            return Array.Empty<ChromeProfile>();
        }

        return ChromeProfileParser.Parse(userDataDirectory, File.ReadAllText(localStatePath));
    }
}
