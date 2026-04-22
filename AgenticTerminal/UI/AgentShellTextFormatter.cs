using System.Text;
using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using GitHub.Copilot.SDK;

namespace AgenticTerminal.UI;

public static class AgentShellTextFormatter
{
    public static string FormatConversation(IReadOnlyList<ConversationMessage> messages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            builder.Append('[');
            builder.Append(message.Timestamp.ToLocalTime().ToString("HH:mm"));
            builder.Append("] ");
            builder.Append(message.Role);
            builder.AppendLine();
            builder.AppendLine(message.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string FormatSession(ConversationSessionSummary summary)
    {
        return $"{summary.UpdatedAt.ToLocalTime():MM-dd HH:mm}  {summary.Title}";
    }

    public static string BuildSessionSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Sessions");
        builder.AppendLine("Enter = open session");
        builder.AppendLine("F2 = new session");
        return builder.ToString().TrimEnd();
    }

    public static string BuildStatusText(CopilotAgentSessionManager manager)
    {
        return BuildStatusText(manager, Hex1bFocusTarget.Terminal);
    }

    public static string BuildStatusText(CopilotAgentSessionManager manager, Hex1bFocusTarget focusTarget)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var builder = new StringBuilder();
        builder.Append("Status: ");
        builder.AppendLine(manager.StatusText);
        builder.Append("Focus: ");
        builder.Append(focusTarget switch
        {
            Hex1bFocusTarget.Approval => "approval",
            Hex1bFocusTarget.UserInput => "user input",
            Hex1bFocusTarget.Prompt => "prompt",
            Hex1bFocusTarget.Sessions => "sessions",
            _ => "terminal"
        });
        builder.AppendLine(" · F7 terminal · F8 interaction · F9 sessions · F4 models");

        builder.Append("Model: ");
        builder.AppendLine(BuildModelSummary(manager));

        var approval = manager.PendingApproval;
        builder.AppendLine(approval is null
            ? manager.HasPendingUserInputRequest
                ? "Prompt: locked while agent input is waiting"
                : "Prompt: Enter send · Shift-Enter new line"
            : "Prompt: locked while approval is waiting");

        if (approval is null)
        {
            builder.AppendLine("Approval: none");
        }
        else
        {
            builder.AppendLine("Approval: waiting for single-key confirmation");
            builder.AppendLine("Approval: focus the approval box and press Y or N");
        }

        var userInputRequest = manager.PendingUserInputRequest;
        if (userInputRequest is null)
        {
            builder.AppendLine("Question: none");
        }
        else
        {
            builder.AppendLine("Question: waiting for user input");
            builder.AppendLine(BuildUserInputHelpText(userInputRequest));
        }

        var toolActivityText = BuildToolActivityText(manager);
        if (!string.IsNullOrWhiteSpace(toolActivityText))
        {
            builder.AppendLine(toolActivityText);
        }

        var promptTimingText = BuildPromptTimingText(manager);
        if (!string.IsNullOrWhiteSpace(promptTimingText))
        {
            builder.AppendLine(promptTimingText);
        }

        builder.Append("App: F2 new session · F4 change model · F6 debug · F10 quit");
        return builder.ToString();
    }

    public static string BuildModelMenuTitle(CopilotAgentSessionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return $"Model: {BuildModelSummary(manager)}";
    }

    public static string BuildHostWindowTitle(CopilotAgentSessionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return $"AgenticTerminal - {BuildModelSummary(manager)}";
    }

    public static string FormatModelOption(ModelInfo model, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(model);

        var builder = new StringBuilder();
        if (isActive)
        {
            builder.Append("● ");
        }
        else
        {
            builder.Append("  ");
        }

        builder.Append(string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name);

        if (!string.IsNullOrWhiteSpace(model.Name) && !string.Equals(model.Name, model.Id, StringComparison.Ordinal))
        {
            builder.Append(" (");
            builder.Append(model.Id);
            builder.Append(')');
        }

        builder.Append(" · ");
        builder.Append(FormatTokenMultiplier(model.Billing?.Multiplier));
        return builder.ToString();
    }

    public static string BuildModelSummary(CopilotAgentSessionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var activeModel = manager.AvailableModels.FirstOrDefault(model => string.Equals(model.Id, manager.ActiveModelId, StringComparison.Ordinal));
        var modelName = string.IsNullOrWhiteSpace(activeModel?.Name)
            ? manager.ActiveModelId ?? "Copilot default"
            : activeModel.Name;

        return $"{modelName} · {FormatRemainingQuota(manager.RemainingQuotaPercentage)}";
    }

    private static string FormatRemainingQuota(double? remainingQuotaPercentage)
    {
        return remainingQuotaPercentage is >= 0
            ? $"{remainingQuotaPercentage.Value:0.#}% left"
            : "quota unknown";
    }

    private static string FormatTokenMultiplier(double? multiplier)
    {
        return multiplier is > 0
            ? $"{multiplier.Value:0.##}×"
            : "multiplier unknown";
    }

    public static string BuildPromptLine(string promptText, bool isFocused)
    {
        var prompt = string.IsNullOrEmpty(promptText) ? string.Empty : promptText;
        return isFocused
            ? $"> {prompt}_"
            : $"> {prompt}";
    }

    public static string BuildApprovalPrompt(ApprovalRequest? approval)
    {
        if (approval is null)
        {
            return string.Empty;
        }

        return $"Approve terminal command?{Environment.NewLine}{approval.CommandText}{Environment.NewLine}[Y]es / [N]o";
    }

    public static string BuildToolActivityText(CopilotAgentSessionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var activity = manager.CurrentToolActivity ?? manager.LastToolActivity;
        if (activity is null)
        {
            return "Tool: none";
        }

        var builder = new StringBuilder();
        builder.Append("Tool: ");
        builder.AppendLine(activity.DisplayName ?? activity.ToolName);
        builder.Append("Tool status: ");
        builder.AppendLine(activity.IsRunning
            ? "running"
            : activity.Succeeded
                ? "completed"
                : "failed");

        if (!string.IsNullOrWhiteSpace(activity.ArgumentsSummary))
        {
            builder.Append("Tool args: ");
            builder.AppendLine(activity.ArgumentsSummary);
        }

        if (!string.IsNullOrWhiteSpace(activity.ResultSummary))
        {
            builder.Append("Tool result: ");
            builder.AppendLine(activity.ResultSummary);
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildDebugPanelText(CopilotAgentSessionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var builder = new StringBuilder();
        builder.Append("Debug · Session: ");
        builder.AppendLine(manager.ActiveSessionId ?? "none");
        builder.Append("Debug · Model: ");
        builder.AppendLine(BuildModelSummary(manager));
        builder.Append("Debug · First-token timeout: ");
        builder.AppendLine(FormatDuration(manager.FirstTokenTimeout));
        builder.AppendLine(BuildPromptTimingText(manager));
        builder.AppendLine(BuildToolActivityText(manager));

        builder.Append("Debug · Interaction: ");
        if (manager.PendingApproval is not null)
        {
            builder.Append("approval pending");
        }
        else if (manager.PendingUserInputRequest is not null)
        {
            builder.Append("waiting for user input");
        }
        else
        {
            builder.Append("none");
        }

        return builder.ToString();
    }

    public static string BuildPromptTimingText(CopilotAgentSessionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var timing = manager.LatestPromptTiming;
        if (timing is null)
        {
            return "Latency: no prompt measurements yet";
        }

        var builder = new StringBuilder();
        builder.Append("Latency: persist ");
        builder.Append(FormatDuration(timing.PersistDuration));
        builder.Append(" · snapshot ");
        builder.Append(FormatDuration(timing.SnapshotDuration));
        builder.Append(" · send ");
        builder.AppendLine(FormatDuration(timing.SendDuration));

        builder.Append("Response: first token ");
        builder.Append(FormatDuration(timing.TimeToFirstToken));
        builder.Append(" · total ");
        builder.AppendLine(FormatDuration(timing.TotalResponseDuration));

        if (!string.IsNullOrWhiteSpace(timing.LastToolName))
        {
            builder.Append("Tool timing: ");
            builder.Append(timing.LastToolName);
            builder.Append(" · ");
            builder.AppendLine(FormatDuration(timing.ActiveToolDuration));
        }

        if (!string.IsNullOrWhiteSpace(timing.LastError))
        {
            builder.Append("Latency status: ");
            builder.AppendLine(timing.LastError);
        }
        else if (timing.IsCompleted)
        {
            builder.AppendLine("Latency status: completed");
        }
        else if (timing.IsWaitingForFirstToken)
        {
            builder.AppendLine("Latency status: waiting for first token");
        }
        else
        {
            builder.AppendLine("Latency status: streaming");
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildUserInputPrompt(AgentUserInputRequestState? request)
    {
        if (request is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(request.Question);
        if (request.HasChoices)
        {
            builder.AppendLine();
            builder.AppendLine("Choices:");
            for (var index = 0; index < request.Choices.Count; index++)
            {
                builder.Append("- ");
                builder.AppendLine(request.Choices[index]);
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildUserInputHelpText(AgentUserInputRequestState? request)
    {
        if (request is null)
        {
            return string.Empty;
        }

        if (request.HasChoices && !request.AllowFreeformInput)
        {
            return "Question: choose an option with Enter · Esc cancels";
        }

        if (request.HasChoices)
        {
            return "Question: pick a choice or type a custom answer · Enter sends · Shift-Enter new line · Esc cancels";
        }

        return "Question: type a response · Enter sends · Shift-Enter new line · Esc cancels";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "n/a";
        }

        if (duration.Value.TotalMilliseconds < 1000)
        {
            return $"{duration.Value.TotalMilliseconds:0} ms";
        }

        return $"{duration.Value.TotalSeconds:0.00} s";
    }
}
