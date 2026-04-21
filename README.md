# AgenticTerminal

AgenticTerminal is a .NET 10 terminal application that combines a Hex1b-based terminal UI with a GitHub Copilot-driven agent workflow. It lets you work in a terminal pane while sending prompts to Copilot, reviewing proposed shell commands, and approving or denying command execution inside the same application.

## Features

- Split terminal and agent interface built with Hex1b
- Interactive PTY-backed terminal session for local shell interaction
- Copilot-powered agent session management using `GitHub.Copilot.SDK`
- Terminal-safe application shortcuts using function keys for global actions
- Approval flow for shell commands proposed by the agent
- In-app model picker with per-model token multiplier display and remaining Copilot quota in the host window title
- Conversation persistence for saved sessions
- Smoke test mode for scripted startup validation
- Automated tests for terminal capture, PTY integration, startup behavior, UI formatting, shortcut routing, and isolated dead-key repro harnesses

## Project structure

- `AgenticTerminal/` - main application
  - `Agent/` - Copilot session management, prompt handling, and transcript logic
  - `Approvals/` - pending command approval queue and request models
  - `Persistence/` - saved conversation session storage
  - `Startup/` - command-line parsing and smoke test support
  - `Terminal/` - terminal session abstractions, PTY integration, snapshots, and command capture
  - `UI/` - Hex1b application shell and status formatting
- `AgenticTerminal.Tests/` - xUnit test project
- `AgenticTerminal.TestHost/` - fake shell host used by PTY integration tests

## Requirements

- .NET SDK 10
- Windows with `pwsh.exe` available on `PATH` for the normal interactive application path
- A signed-in GitHub Copilot environment for agent functionality
- Access to `https://nuget.pkg.github.com/enmasse/index.json` for the `Hex1b` package (configure a GitHub Packages token if your environment does not already have credentials)

## Running the application

From the repository root:

- `dotnet run --project AgenticTerminal/AgenticTerminal.csproj`

The application starts a terminal session, initializes a Copilot client using the current working directory, and opens the Hex1b shell UI.

## Keyboard shortcuts

Application-global shortcuts are mapped to terminal-safe function keys to reduce conflicts with shells and applications running inside the embedded terminal:

- `F2` - create a new session
- `F4` - open the model picker
- `F7` - focus the terminal pane
- `F8` - focus the prompt pane
- `F9` - focus the sessions list
- `F10` - quit the application
- `Ctrl+Left` / `Ctrl+Right` - resize the terminal split

Prompt editing:

- `Enter` - send the prompt
- `Shift+Enter` - insert a newline

## Command-line options

The app supports a small set of startup options:

- `--smoke-test <prompt>` - runs a non-interactive smoke test prompt and exits
- `--smoke-test-timeout <seconds>` - sets the smoke test timeout
- `--model <name>` - requests a specific Copilot model

Example:

- `dotnet run --project AgenticTerminal/AgenticTerminal.csproj -- --smoke-test "Reply with READY" --smoke-test-timeout 30`

## How it works

1. The app creates a terminal session and a `CopilotAgentSessionManager`.
2. The terminal is shown in the left pane, while the agent UI is shown in the right pane.
3. Prompts entered in the agent pane are composed with a terminal snapshot and sent to Copilot.
4. If the agent proposes a shell command, the app queues an approval request.
5. The user approves or denies execution from the approval box or keyboard shortcuts.
6. Approved commands are executed through the terminal session and their output is captured back into the agent flow.

## Testing

Run the test suite from the repository root:

- `dotnet test AgenticTerminal.Tests/AgenticTerminal.Tests.csproj`

The test project includes:

- command capture parsing tests
- PTY session integration tests using the fake shell host
- startup option and smoke test coverage
- persistence tests
- UI formatting and shell behavior tests
- runtime interaction tests for function-key shortcuts and prompt key behavior
- an isolated dead-key repro harness for diagnosing composed-input behavior

## Notes

- Build output and generated restore artifacts are ignored through `.gitignore` and should not be committed.
- The test host exists only to support PTY integration testing and is not part of the end-user application.
- Dead keys currently appear to be lost upstream of this application in the Hex1b input translation path. This affects both the prompt and the embedded terminal when using layouts such as US-International.
