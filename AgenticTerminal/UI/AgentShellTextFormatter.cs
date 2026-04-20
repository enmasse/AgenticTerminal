using System.Text;
using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;

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
        builder.AppendLine("Ctrl-N = new session");
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
        builder.AppendLine(" · Ctrl-T terminal · Ctrl-P prompt/approval · Ctrl-S sessions");

        var approval = manager.PendingApproval;
        builder.AppendLine(approval is null
            ? "Prompt: Enter send · Ctrl-Enter new line"
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

        builder.Append("App: Ctrl-N new session · Ctrl-Q quit");
        return builder.ToString();
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
