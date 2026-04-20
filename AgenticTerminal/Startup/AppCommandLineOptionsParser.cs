namespace AgenticTerminal.Startup;

public static class AppCommandLineOptionsParser
{
    public static AppCommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = AppCommandLineOptions.Interactive;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--smoke-test":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        throw new ArgumentException("The --smoke-test option requires a prompt.");
                    }

                    options = options with
                    {
                        RunSmokeTest = true,
                        SmokeTestPrompt = args[++index]
                    };
                    break;

                case "--smoke-test-timeout":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        throw new ArgumentException("The --smoke-test-timeout option requires a positive integer value.");
                    }

                    options = options with
                    {
                        SmokeTestTimeoutSeconds = timeoutSeconds
                    };
                    index++;
                    break;

                case "--model":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        throw new ArgumentException("The --model option requires a model name.");
                    }

                    options = options with
                    {
                        CopilotModel = args[++index]
                    };
                    break;
            }
        }

        return options;
    }
}
