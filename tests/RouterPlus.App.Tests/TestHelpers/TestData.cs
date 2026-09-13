using System;
using System.IO;
using RouterPlus.Core.Chrome;

namespace RouterPlus.App.Tests.TestHelpers
{
    public static class TestData
    {
        public static ChromeProfile CreateProfile(
            string name = "Synthetic",
            string directoryName = "Default",
            string? userDataDirectory = null)
        {
            var root = userDataDirectory ?? CreateTempDirectory();
            return new ChromeProfile(
                ChromeProfile.CreateId(root, directoryName),
                name,
                directoryName,
                root,
                false);
        }

        public static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "rp-app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}