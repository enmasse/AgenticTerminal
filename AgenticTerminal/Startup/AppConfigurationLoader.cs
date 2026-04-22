using System.Text.Json;

namespace AgenticTerminal.Startup;

public static class AppConfigurationLoader
{
    private const string ConfigurationDirectoryName = ".agenticterminal";
    private const string ConfigurationFileName = "settings.json";

    public static string GetDefaultConfigurationPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            throw new InvalidOperationException("The user profile path could not be resolved.");
        }

        return Path.Combine(userProfilePath, ConfigurationDirectoryName, ConfigurationFileName);
    }

    public static AppConfiguration Load(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        if (!File.Exists(configurationPath))
        {
            return AppConfiguration.Empty;
        }

        try
        {
            var json = File.ReadAllText(configurationPath);
            var configuration = JsonSerializer.Deserialize<AppConfigurationDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return new AppConfiguration
            {
                CopilotModel = string.IsNullOrWhiteSpace(configuration?.CopilotModel)
                    ? null
                    : configuration.CopilotModel,
                FirstTokenTimeoutSeconds = configuration?.FirstTokenTimeoutSeconds is > 0
                    ? configuration.FirstTokenTimeoutSeconds
                    : null,
                ShowDebugPanelByDefault = configuration?.ShowDebugPanelByDefault
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The configuration file '{configurationPath}' is invalid.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"The configuration file '{configurationPath}' could not be read.", exception);
        }
    }

    private sealed record AppConfigurationDocument(string? CopilotModel, int? FirstTokenTimeoutSeconds, bool? ShowDebugPanelByDefault);
}
