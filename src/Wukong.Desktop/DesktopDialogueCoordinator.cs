using Wukong.Application;

namespace Wukong.Desktop;

public sealed class DesktopDialogueCoordinator
{
    private readonly DesktopAgentRuntime _agent;
    private readonly DesktopRuntimeHost _runtime;

    public DesktopDialogueCoordinator(DesktopAgentRuntime agent, DesktopRuntimeHost runtime)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<ConversationTurnResult> SendAsync(string text, CancellationToken cancellationToken = default)
    {
        _runtime.RecordOwnerDialogueResponse(positive: !InitiativeSpeechDecisionService.IsExplicitQuietReply(text));
        var behavior = await _runtime.SubmitDialogueIntentAsync(text);
        if (behavior.Recognized && behavior.Act is not null)
        {
            await _agent.AppendLocalConversationTurnAsync(text, behavior.Act.Text, cancellationToken);
            return new ConversationTurnResult(
                true,
                behavior.Act.Text,
                null,
                null,
                "runtime-fact",
                "deterministic",
                TimeSpan.Zero,
                0);
        }

        _runtime.TryWakeFromConversation();

        var result = await _agent.Conversation.SendAsync(
            new ConversationRequest(DesktopAgentRuntime.DailySessionId, text),
            cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.AssistantText))
            return result;

        var validation = _runtime.ValidateDialogueReply(result.AssistantText);
        if (validation.IsValid)
            return result;

        await _agent.ReplaceLatestAssistantMessageAsync(validation.Text, cancellationToken);
        _runtime.ReportDialogueValidationFailure(validation.ReasonCodes);
        return result with { AssistantText = validation.Text };
    }
}
