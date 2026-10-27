using System.IO;

namespace RouterPlus.TestHelpers
{
    /// <summary>
    /// Shared temp directory helpers for test projects.
    /// </summary>
    public static class TestDirectory
    {
        /// <summary>
        /// Creates a unique temporary directory under %TEMP%.
        /// </summary>
        public static string Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "rp-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>
        /// Best-effort recursive delete of a temporary directory.
        /// </summary>
        public static void Delete(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}