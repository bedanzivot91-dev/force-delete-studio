using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(NPVideoStudio.UnitTests.TestAppBuilder))]

namespace NPVideoStudio.UnitTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<NPVideoStudio.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
