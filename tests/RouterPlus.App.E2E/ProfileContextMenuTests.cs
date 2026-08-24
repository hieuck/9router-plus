namespace RouterPlus.App.E2E;

public sealed class ProfileContextMenuTests
{
    [Fact]
    public async Task Right_click_profile_opens_expected_context_menu()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.RightClickProfile("Harness Alpha");
        var elapsed = driver.WaitForContextMenu(TimeSpan.FromSeconds(3));

        Assert.InRange(elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        Assert.All(
            new[]
            {
                "Đăng nhập Google bằng Chrome",
                "Tự động đăng nhập Google",
                "Mở thư mục profile",
                "Sao chép tên profile",
                "Xóa profile…"
            },
            header => Assert.True(driver.ContextMenuContains(header), header));
    }

    [Fact]
    public async Task Right_click_can_be_repeated_for_different_profiles()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        foreach (var profileName in new[] { "Harness Alpha", "Harness Beta", "Harness Alpha" })
        {
            Console.WriteLine($"Right-clicking {profileName}");
            driver.RightClickProfile(profileName);
            driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
            driver.DismissContextMenu();
        }
    }
}
