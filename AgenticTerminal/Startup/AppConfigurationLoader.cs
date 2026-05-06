using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticTerminal.Startup;

public static class AppConfigurationLoader
{
    private const string ConfigurationDirectoryName = ".agenticterminal";
    private const string ConfigurationFileName = "settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

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
            var configuration = JsonSerializer.Deserialize<AppConfigurationDocument>(json, SerializerOptions);

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

    public static void Save(string configurationPath, AppConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentNullException.ThrowIfNull(configuration);

        try
        {
            var configurationDirectory = Path.GetDirectoryName(configurationPath);
            if (!string.IsNullOrWhiteSpace(configurationDirectory))
            {
                Directory.CreateDirectory(configurationDirectory);
            }

            var document = new AppConfigurationDocument(
                configuration.CopilotModel,
                configuration.FirstTokenTimeoutSeconds,
                configuration.ShowDebugPanelByDefault);
            var json = JsonSerializer.Serialize(document, SerializerOptions);
            File.WriteAllText(configurationPath, json);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"The configuration file '{configurationPath}' could not be written.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"The configuration file '{configurationPath}' could not be written.", exception);
        }
    }

    private sealed record AppConfigurationDocument(string? CopilotModel, int? FirstTokenTimeoutSeconds, bool? ShowDebugPanelByDefault);
}
