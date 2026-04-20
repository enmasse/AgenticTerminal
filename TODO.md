# AgenticTerminal TODO

## MVP limitations
- Broaden the VT parser and terminal surface so more full-screen terminal apps render correctly, including richer ANSI styling, alternate screen buffers, mouse reporting, and additional cursor modes.
- Persist the shell session when possible, or snapshot and restore enough shell state to approximate continuation between launches.
- Improve startup and auth UX so GitHub login failures or Copilot CLI issues are surfaced inside the TUI instead of only on stderr.
- Investigate and fix dead-key composition in Hex1b input translation so US-International and similar layouts work in both the prompt and the embedded terminal.
- Add canonical focus traversal and dialog cancellation keys such as `Tab`, `Shift+Tab`, and `Esc` around the current function-key command model.
- Add prompt history navigation and editable drafts per session.

## Full implementation roadmap
1. Introduce a dedicated application core project so the UI, agent orchestration, terminal hosting, and persistence are split into separate assemblies.
2. Add adapter tests around the Copilot SDK event flow and permission handling using deterministic fakes.
3. Replace transcript replay in the system prompt with first-class Copilot session resume once resumable custom tool registration is stable.
4. Add session metadata such as model, creation source, working directory, and per-session shell settings.
5. Add configurable model selection and reasoning-effort controls.
6. Add structured approval cards with diffed command previews, risk hints, and allow-once / deny-once actions.
7. Stream tool execution state into the conversation pane so the user can see pending, running, and completed commands.
8. Capture terminal output incrementally for tool responses instead of returning only buffered command results.
9. Add durable prompt history, searchable session lists, and explicit session rename/delete actions.
10. Add export and import for saved conversations.
11. Add configuration storage for theme, layout ratios, startup directory, preferred shell, and default model.
12. Add a richer TUI layout with resizable panes, focus indicators, and modal dialogs for session management.
13. Add telemetry hooks and structured logging around Copilot connection state and shell execution.
14. Add packaging for a self-contained Windows distribution with first-run checks for Copilot CLI prerequisites.
15. Add end-to-end tests for persistence, approval flow, ConPTY startup, and interactive terminal behavior in a headless harness.
16. Evaluate whether AgenticTerminal should wrap a custom Hex1b presentation adapter locally or contribute an upstream Hex1b fix for composed keyboard input.
