# Copilot Instructions

## Project Guidelines
- User prefers test-driven work when implementing features in this repository.
- For AgenticTerminal UI, user prefers a simple left-right split with text-based dialogs and no fake button-heavy controls. User dislikes fake modal dialogs in AgenticTerminal UI and prefers they be removed instead of interrupting terminal interaction.
- For AgenticTerminal, user wants the prompt UI to behave more like a simple REPL, with minimal clutter and compact Yes/No confirmation prompts. Command confirmation should use immediate [Yes/No/All] choices triggered by single-key shortcuts without requiring Enter. Pressing Enter should send the prompt, while Ctrl-Enter should be reserved for creating multi-line prompts.
- For AgenticTerminal, user wants agent history and prompting combined into the same REPL-style history view instead of a separate prompt input box.
- For AgenticTerminal, prefer Hex1b's built-in PTY and headless terminal support over custom terminal backend wrappers when possible.