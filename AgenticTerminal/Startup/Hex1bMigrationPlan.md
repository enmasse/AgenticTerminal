# hex1b Migration Cutover Points

## Current host seams
- `Program.cs` owns `Terminal.Gui` initialization, shutdown, and status bar setup.
- `UI/AgentTerminalWindow.cs` owns the left-right split, focus shortcuts, session list rendering, prompt rendering, and approval/status text.
- `UI/TerminalSurfaceView.cs` owns terminal-host-specific rendering and key routing into the terminal session.

## Current terminal seams
- `ITerminalSession` already isolates process/session behavior from the host UI.
- `ITerminalDisplayState` already isolates display reads for cursor position and viewport lines.
- `XTermTerminalEmulator` currently implements display and input generation behind the app's own abstractions.

## Recommended next hex1b cutover order
1. Expand the existing `Hex1bApplicationShell` preview from lifecycle-only status text to a real left-right split shell.
2. Introduce a host-neutral agent panel model for sessions, conversation text, status text, and focus commands.
3. Replace `TerminalSurfaceView` with a `hex1b` terminal surface that reads from `ITerminalDisplayState` and routes keys through the terminal input encoder.
4. Replace `AgentTerminalWindow` with a `hex1b` app shell implementation using the same session manager and terminal session contracts.
5. Flip `ApplicationShellFactory` from Terminal.Gui fallback to Hex1b as the default host.
6. Remove `Terminal.Gui` once the hex1b shell reaches parity.

## Dependency removal targets after parity
- Remove `Terminal.Gui` package reference after `Program.cs`, `AgentTerminalWindow`, and `TerminalSurfaceView` are no longer used.
- Re-evaluate `XTerm.NET` only after confirming whether hex1b fully replaces terminal emulation as well as hosting.

## Non-goals for the first cutover
- Do not rewrite `CopilotAgentSessionManager`, approvals, persistence, or smoke-test orchestration.
- Do not change `ITerminalSession` contracts unless hex1b integration proves they are insufficient.
