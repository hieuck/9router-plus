using System;
using System.IO;
using System.Runtime.CompilerServices;

internal static class TestAppDataIsolation
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", root);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        };
    }
}