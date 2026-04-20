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
            Hex1bFocusTarget.Prompt => "prompt",
            Hex1bFocusTarget.Sessions => "sessions",
            _ => "terminal"
        });
        builder.AppendLine(" · F7 terminal · F8 prompt/approval · F9 sessions · F4 models");

        builder.Append("Model: ");
        builder.AppendLine(BuildModelSummary(manager));

        var approval = manager.PendingApproval;
        builder.AppendLine(approval is null
            ? "Prompt: Enter send · Shift-Enter new line"
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

        builder.Append("App: F2 new session · F4 change model · F10 quit");
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
}
