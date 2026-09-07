using System.Reflection;
using RouterPlus.Updater;

namespace RouterPlus.Updater.Tests;

public sealed class ProgramArgumentTests
{
    [Fact]
    public void ParseArguments_builds_options_from_case_insensitive_complete_arguments()
    {
        var options = Parse(
            "--RESTART",
            "--pid", "123",
            "--target", "C:\\live",
            "--staging", "C:\\staging",
            "--backup", "C:\\backup",
            "--app", "C:\\live\\RouterPlus.exe",
            "--version", "1.2.3");

        Assert.Equal(123, options.ParentProcessId);
        Assert.Equal("C:\\live", options.TargetDirectory);
        Assert.Equal("1.2.3", options.Version.ToString());
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void ParseArguments_rejects_incomplete_or_malformed_arguments(string[] arguments)
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse(arguments));

        Assert.NotEmpty(exception.Message);
    }

    public static IEnumerable<object[]> InvalidArguments =>
    [
        new object[] { new[] { "--restart" } },
        new object[] { new[] { "--restart", "--pid" } },
        new object[] { new[] { "--restart", "--pid", "123", "--unknown", "value" } },
        new object[] { new[] { "--restart", "--pid", "123", "--pid", "456" } },
        new object[] { new[] { "--restart", "--pid", "123", "--target", "" } },
        new object[] { new[] { "--restart", "--pid", "not-a-number", "--target", "C:\\live", "--staging", "C:\\staging", "--backup", "C:\\backup", "--app", "C:\\live\\RouterPlus.exe", "--version", "1.2.3" } }
    ];

    private static UpdateTransactionOptions Parse(params string[] arguments)
    {
        var program = typeof(UpdateTransaction).Assembly.GetType("RouterPlus.Updater.Program")!;
        var method = program.GetMethod("ParseArguments", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (UpdateTransactionOptions)method.Invoke(null, new object[] { arguments })!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }
}
