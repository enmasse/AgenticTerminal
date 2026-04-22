using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class AppConfigurationLoaderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "AgenticTerminal.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WithMissingFile_ReturnsEmptyConfiguration()
    {
        var configuration = AppConfigurationLoader.Load(Path.Combine(_tempDirectory, "settings.json"));

        Assert.Equal(AppConfiguration.Empty, configuration);
    }

    [Fact]
    public void Load_WithValidJson_ReturnsConfiguredModel()
    {
        Directory.CreateDirectory(_tempDirectory);
        var configurationPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(configurationPath, "{\"copilotModel\":\"claude-sonnet-4.5\",\"firstTokenTimeoutSeconds\":20,\"showDebugPanelByDefault\":true}");

        var configuration = AppConfigurationLoader.Load(configurationPath);

        Assert.Equal("claude-sonnet-4.5", configuration.CopilotModel);
        Assert.Equal(20, configuration.FirstTokenTimeoutSeconds);
        Assert.True(configuration.ShowDebugPanelByDefault);
    }

    [Fact]
    public void Load_WithInvalidJson_ThrowsInvalidOperationException()
    {
        Directory.CreateDirectory(_tempDirectory);
        var configurationPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(configurationPath, "{not json}");

        var exception = Assert.Throws<InvalidOperationException>(() => AppConfigurationLoader.Load(configurationPath));

        Assert.Contains(configurationPath, exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
