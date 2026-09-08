using RouterPlus.Updater;

namespace RouterPlus.Updater.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void ParseArguments_requires_restart_and_all_values()
    {
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(new[] { "--restart", "--pid", "123" }));
    }

    [Fact]
    public void ParseArguments_rejects_duplicate_restart_and_unknown_or_empty_values()
    {
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(new[] { "--restart", "--restart" }));
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(new[] { "--restart", "--unknown", "value" }));
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(new[] { "--restart", "--pid" }));
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(new[] { "--restart", "--pid", "" }));
        Assert.Throws<ArgumentException>(() => Program.ParseArguments(new[] { "value" }));
    }

    [Fact]
    public void ParseArguments_parses_case_insensitive_values_and_release_version()
    {
        var options = Program.ParseArguments(new[]
        {
            "--RESTART",
            "--PID", "123",
            "--TARGET", "C:\\live",
            "--staging", "C:\\staging",
            "--backup", "C:\\backup",
            "--app", "C:\\live\\RouterPlus.exe",
            "--version", "1.2.3"
        });

        Assert.Equal(123, options.ParentProcessId);
        Assert.Equal("C:\\live", options.TargetDirectory);
        Assert.Equal("1.2.3", options.Version.ToString());
        Assert.Equal(TimeSpan.FromSeconds(30), options.ParentWaitTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.HealthCheckTimeout);
    }

    [Fact]
    public void ParseArguments_rejects_invalid_pid_or_version_format()
    {
        var args = new[]
        {
            "--restart", "--pid", "not-a-pid",
            "--target", "C:\\live", "--staging", "C:\\staging",
            "--backup", "C:\\backup", "--app", "C:\\live\\RouterPlus.exe",
            "--version", "1.2.3"
        };
        Assert.Throws<FormatException>(() => Program.ParseArguments(args));

        args[2] = "123";
        args[^1] = "not-a-version";
        Assert.Throws<FormatException>(() => Program.ParseArguments(args));
    }
}

