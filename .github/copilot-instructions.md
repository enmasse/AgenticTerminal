# Copilot Instructions

## Project Guidelines
- User prefers test-driven work when implementing features in this repository.
- User expects verification of workspace structure and does not want claims that no test project exists without deeper checks.
- When gathering requirements for AgenticTerminal features, make reasonable assumptions and avoid pushing too many low-level implementation decisions back to the user; details can be refined later.
- For AgenticTerminal UI, user prefers a simple left-right split with text-based dialogs and no fake button-heavy controls. User dislikes fake modal dialogs in AgenticTerminal UI and prefers they be removed instead of interrupting terminal interaction.
- For AgenticTerminal, user wants the prompt UI to behave more like a simple REPL, with minimal clutter and compact Yes/No confirmation prompts. Command confirmation should use immediate [Yes/No/All] choices triggered by single-key shortcuts without requiring Enter. Pressing Enter should send the prompt, while Ctrl-Enter should be reserved for creating multi-line prompts.
- For AgenticTerminal, user wants agent history and prompting combined into the same REPL-style history view instead of a separate prompt input box. When adjusting the agent history UI, prefer no horizontal scrollbar; keep it vertically scrolled to the bottom on startup instead.
- For AgenticTerminal, prefer Hex1b's built-in PTY and headless terminal support over custom terminal backend wrappers when possible.
- User prefers architecture that is independent of the running shell rather than relying on shell-specific completion behavior.
- For AgenticTerminal, do not hide agent-issued command wrappers if the goal is for the user to see and learn from the agent; visible behavior is preferred over concealment.
- For AgenticTerminal, the wrapper should be transparent, passing through stdin/stdout/stderr and statuses like the regular command, and allowing the agent to wrap commands in pipelines while tracking command completion.
- For AgenticTerminal backchannel design, both pipeline completion modes should be supported: reporting only the final pipeline status or reporting each stage separately, with the agent choosing the appropriate mode. Choose a protocol that does not hamper future expansion, even if the initial implementation is minimal. Pipeline stage events should carry only their own stage-local context; the agent should infer higher-level connections instead of the protocol encoding them explicitly.
- For AgenticTerminal, prefer a short visible wrapper executable name such as 'agt'.
- For AgenticTerminal argv[0]-based wrapper mode, invoking 'agt' without arguments should start the full terminal experience by default.
- For AgenticTerminal backchannel behavior, if no started event is received within an agent timeout, prompt whether to terminate the process with Ctrl-C or equivalent.
- For AgenticTerminal, 'agt' should return a distinct non-zero exit code when the session backchannel is unavailable, separate from wrapped command exit codes.

## Copilot Model Information
- When showing Copilot model info in the UI, 'token budget' means remaining GitHub Copilot quota percentage, and the model picker should show each model's token multiplier.
- When displaying model and quota in the window title bar, include a way to change the model using a direct action like Ctrl+M, without adding a top-level menu bar to keep the UI minimal.

## Interaction Guidelines
- When requirements are unclear, ask questions one by one instead of batching them together.