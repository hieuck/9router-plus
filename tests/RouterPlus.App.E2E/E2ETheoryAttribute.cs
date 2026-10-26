using System;
using Xunit;

namespace RouterPlus.App.E2E;

/// <summary>
/// Theory attribute for interactive E2E tests that launch the real application and control mouse/keyboard.
/// These tests are skipped by default. Set ROUTERPLUS_RUN_E2E=1 to run them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class E2ETheoryAttribute : TheoryAttribute
{
    public E2ETheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROUTERPLUS_RUN_E2E") != "1")
        {
            Skip = "Interactive E2E disabled. Set ROUTERPLUS_RUN_E2E=1 to run.";
        }
    }
}