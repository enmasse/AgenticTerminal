# Copilot Instructions

## Project Guidelines
- User prefers test-driven work when implementing features in this repository.
- User expects verification of workspace structure and does not want claims that no test project exists without deeper checks.
- For AgenticTerminal UI, user prefers a simple left-right split with text-based dialogs and no fake button-heavy controls. User dislikes fake modal dialogs in AgenticTerminal UI and prefers they be removed instead of interrupting terminal interaction.
- For AgenticTerminal, user wants the prompt UI to behave more like a simple REPL, with minimal clutter and compact Yes/No confirmation prompts. Command confirmation should use immediate [Yes/No/All] choices triggered by single-key shortcuts without requiring Enter. Pressing Enter should send the prompt, while Ctrl-Enter should be reserved for creating multi-line prompts.
- For AgenticTerminal, user wants agent history and prompting combined into the same REPL-style history view instead of a separate prompt input box. When adjusting the agent history UI, prefer no horizontal scrollbar; keep it vertically scrolled to the bottom on startup instead.
- For AgenticTerminal, prefer Hex1b's built-in PTY and headless terminal support over custom terminal backend wrappers when possible.
- User prefers architecture that is independent of the running shell rather than relying on shell-specific completion behavior.

## Copilot Model Information
- When showing Copilot model info in the UI, 'token budget' means remaining GitHub Copilot quota percentage, and the model picker should show each model's token multiplier.
- When displaying model and quota in the window title bar, include a way to change the model using a direct action like Ctrl+M, without adding a top-level menu bar to keep the UI minimal.