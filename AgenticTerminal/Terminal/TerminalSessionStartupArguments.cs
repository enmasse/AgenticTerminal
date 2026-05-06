namespace AgenticTerminal.Terminal;

public static class TerminalSessionStartupArguments
{
    private const string AgentCommandInvokeFunction = """
function __agenticterminal_invoke {
    param(
        [string]$EncodedCommand,
        [string]$CommandId,
        [string]$PipeName
    )

    $__agenticterminal_exit = 0
    $__agenticterminal_pipe = $null
    $__agenticterminal_writer = $null
    $__agenticterminal_command = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($EncodedCommand))

    try { & ([ScriptBlock]::Create($__agenticterminal_command)); if ($LASTEXITCODE -is [int]) { $__agenticterminal_exit = $LASTEXITCODE } }
    catch { $__agenticterminal_exit = 1; Write-Host $_ }
    finally {
        $__agenticterminal_pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [System.IO.Pipes.PipeDirection]::Out)
        $__agenticterminal_pipe.Connect(5000)
        $__agenticterminal_writer = [System.IO.StreamWriter]::new($__agenticterminal_pipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
        $__agenticterminal_writer.AutoFlush = $true
        $__agenticterminal_writer.WriteLine($CommandId + ':' + $__agenticterminal_exit)
        if ($null -ne $__agenticterminal_writer) { $__agenticterminal_writer.Dispose() }
        if ($null -ne $__agenticterminal_pipe) { $__agenticterminal_pipe.Dispose() }
    }
}
""";

    public static string Build(TerminalSessionStartupOptions options)
    {
        return string.Join(' ', BuildArgumentList(options).Select(Quote));
    }

    public static string[] BuildArgumentList(TerminalSessionStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string>
        {
            "-NoLogo"
        };

        if (!options.LoadUserProfile)
        {
            arguments.Add("-NoProfile");
        }

        var initializationCommands = new List<string>();
        if (options.SuppressPrompt)
        {
            initializationCommands.Add("function prompt { '' }");
        }

        initializationCommands.Add(options.Mode == TerminalSessionMode.HeadlessPipe
            ? "$PSStyle.OutputRendering='PlainText'"
            : "$PSStyle.OutputRendering='Ansi'");
        initializationCommands.Add("[Console]::InputEncoding=[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)");
        initializationCommands.Add(AgentCommandInvokeFunction);

        arguments.AddRange(["-NoExit", "-Command", string.Join("; ", initializationCommands)]);
        return [.. arguments];
    }

    private static string Quote(string text)
    {
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
