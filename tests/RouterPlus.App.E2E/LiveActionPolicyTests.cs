using System.Reflection;

namespace RouterPlus.App.E2E;

public sealed class LiveActionPolicyTests
{
    [Fact]
    public void Profile_deletion_is_not_allowed_in_live_harness()
    {
        var policyType = typeof(LiveTestEnvironment).Assembly
            .GetType("RouterPlus.App.E2E.LiveActionPolicy");
        Assert.NotNull(policyType);

        var isAllowed = policyType!.GetMethod(
            "IsAllowed",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(isAllowed);

        var result = isAllowed!.Invoke(null, new object[] { "Xóa profile…" });
        Assert.Equal(false, result);
    }
}
