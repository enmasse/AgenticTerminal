using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using AgenticTerminal.Terminal;
using ProtoBuf;

namespace AgenticTerminal.Startup;

internal static class AgtWrapperRunner
{
    public const int BackchannelUnavailableExitCode = 201;
    public const int InvalidArgumentsExitCode = 202;
    public const int WrapperFailureExitCode = 203;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        AgtCommandLineOptions options;
        try
        {
            options = AgtCommandLineParser.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return InvalidArgumentsExitCode;
        }

        using var process = new Process
        {
            StartInfo = BuildProcessStartInfo(options)
        };

        if (!process.Start())
        {
            Console.Error.WriteLine("Failed to start the wrapped command.");
            return WrapperFailureExitCode;
        }

        await using var backchannelClient = await AgentCommandBackchannelClient.ConnectAsync(options.SessionId, cancellationToken);
        if (backchannelClient is null)
        {
            Console.Error.WriteLine("The AgenticTerminal session backchannel is unavailable.");
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return BackchannelUnavailableExitCode;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var commandId = Guid.NewGuid().ToString("N");
        await backchannelClient.SendAsync(new AgentCommandBackchannelEvent
        {
            SessionId = options.SessionId,
            CommandId = commandId,
            EventType = AgentCommandBackchannelEventType.Started,
            CommandText = options.DisplayCommand,
            ProcessId = process.Id,
            StartedAtUnixTimeMilliseconds = startedAt.ToUnixTimeMilliseconds()
        }, cancellationToken);

        var stdoutBuffer = new TextTailBuffer();
        var stderrBuffer = new TextTailBuffer();
        var stdoutTask = PumpOutputAsync(process.StandardOutput.BaseStream, Console.OpenStandardOutput(), stdoutBuffer, cancellationToken);
        var stderrTask = PumpOutputAsync(process.StandardError.BaseStream, Console.OpenStandardError(), stderrBuffer, cancellationToken);
        _ = PumpInputAsync(Console.OpenStandardInput(), process.StandardInput.BaseStream);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);

        var completedAt = DateTimeOffset.UtcNow;
        await backchannelClient.SendAsync(new AgentCommandBackchannelEvent
        {
            SessionId = options.SessionId,
            CommandId = commandId,
            EventType = AgentCommandBackchannelEventType.Completed,
            CommandText = options.DisplayCommand,
            ProcessId = process.Id,
            StartedAtUnixTimeMilliseconds = startedAt.ToUnixTimeMilliseconds(),
            CompletedAtUnixTimeMilliseconds = completedAt.ToUnixTimeMilliseconds(),
            ExitCode = process.ExitCode,
            StandardOutputTail = stdoutBuffer.GetTail(),
            StandardErrorTail = stderrBuffer.GetTail()
        }, cancellationToken);

        return process.ExitCode;
    }

    private static ProcessStartInfo BuildProcessStartInfo(AgtCommandLineOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory
        };

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task PumpOutputAsync(Stream source, Stream destination, TextTailBuffer tailBuffer, CancellationToken cancellationToken)
    {
        var bytes = new byte[4096];
        var decoder = new UTF8Encoding(false, false).GetDecoder();
        var chars = new char[4096];
        while (true)
        {
            var bytesRead = await source.ReadAsync(bytes, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(bytes.AsMemory(0, bytesRead), cancellationToken);
            await destination.FlushAsync(cancellationToken);
            var charCount = decoder.GetChars(bytes, 0, bytesRead, chars, 0, flush: false);
            if (charCount > 0)
            {
                tailBuffer.Append(new string(chars, 0, charCount));
            }
        }
    }

    private static async Task PumpInputAsync(Stream source, Stream destination)
    {
        try
        {
            await source.CopyToAsync(destination);
        }
        catch
        {
        }
        finally
        {
            try
            {
                await destination.FlushAsync();
            }
            catch
            {
            }

            destination.Dispose();
        }
    }

    private sealed class AgentCommandBackchannelClient(Stream stream) : IAsyncDisposable
    {
        private readonly Stream _stream = stream;

        public static async Task<AgentCommandBackchannelClient?> ConnectAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", AgentCommandBackchannelSession.BuildPipeName(sessionId), PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, cancellationToken);
                return new AgentCommandBackchannelClient(pipe);
            }
            catch (IOException)
            {
                return null;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public async Task SendAsync(AgentCommandBackchannelEvent backchannelEvent, CancellationToken cancellationToken)
        {
            using var payloadStream = new MemoryStream();
            Serializer.Serialize(payloadStream, backchannelEvent);
            var payload = payloadStream.ToArray();
            var lengthBuffer = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
            await _stream.WriteAsync(lengthBuffer, cancellationToken);
            await _stream.WriteAsync(payload, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _stream.DisposeAsync();
        }
    }

    private sealed class TextTailBuffer
    {
        private readonly Queue<string> _lines = new();
        private readonly StringBuilder _currentLine = new();
        private const int MaxLines = 40;

        public void Append(string text)
        {
            foreach (var character in text)
            {
                if (character == '\r')
                {
                    continue;
                }

                if (character == '\n')
                {
                    _lines.Enqueue(_currentLine.ToString());
                    _currentLine.Clear();
                    while (_lines.Count > MaxLines)
                    {
                        _lines.Dequeue();
                    }
                    continue;
                }

                _currentLine.Append(character);
            }
        }

        public string GetTail()
        {
            var lines = _lines.ToList();
            if (_currentLine.Length > 0)
            {
                lines.Add(_currentLine.ToString());
            }

            return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }
    }
}
