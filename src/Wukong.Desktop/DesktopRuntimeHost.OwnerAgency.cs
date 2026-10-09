using Wukong.Application;
using Wukong.Domain;

namespace Wukong.Desktop;

public sealed partial class DesktopRuntimeHost
{
    // Session input pressure, not a second pet state or long-term memory.
    private readonly Queue<DateTimeOffset> _ownerRequestTimes = new();
    private double _promptAgencyBias;

    public void UpdateOwnerPromptAgency(string prompt) => _promptAgencyBias = OwnerPromptAgency.Bias(prompt);

    public string OwnerDecisionFeedback => LastTrigger switch
    {
        "owner_request_pressure" => "老爸，别一直催我嘛。",
        "owner_request_pause" => "让我歇一会儿嘛。",
        "not_sleepy_now" => "我还不困呀。",
        "need_already_satisfied" => CurrentReason,
        "needs_quiet_now" or "stress_too_high" => "我想先安静一会儿。",
        "energy_too_low" => "有点累啦，先歇歇。",
        _ => CurrentReason.Split(" · ")[0] is { Length: > 0 and <= 30 } shortReason
            ? shortReason : "现在还做不了这个，看看面板说明吧。"
    };

    private ParticipationDecision EvaluateOwnerParticipation(BehaviorCapability capability, BehaviorRequestSource source)
    {
        if (source == BehaviorRequestSource.ControlPanel)
            return new(RequestDisposition.Accepted, "panel_inspection", "面板验收不参与意愿或记忆学习");
        var owner = source is BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.OwnerDialogue or BehaviorRequestSource.OwnerUi;
        var now = _now();
        if (owner && capability.ParticipationMode != BehaviorParticipationMode.ForcedByOwner)
        {
            while (_ownerRequestTimes.TryPeek(out var time) && (now < time || now - time > TimeSpan.FromSeconds(_autonomyPolicy.Commands.RequestBurstSeconds)))
                _ownerRequestTimes.Dequeue();
            while (_ownerRequestTimes.Count >= 32) _ownerRequestTimes.Dequeue();
            _ownerRequestTimes.Enqueue(now);
        }
        return _participationPolicy.Evaluate(capability, _petAgentState, source, now, _autonomyPolicy.Commands,
            owner ? _ownerRequestTimes.Count : 0, _promptAgencyBias);
    }

    private PetActionResult? AdmitOwnerActivity(string id, BehaviorRequestSource source)
    {
        if (source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.OwnerDialogue or BehaviorRequestSource.OwnerUi)) return null;
        if (!SleepCandidateBehaviorIds.All.Contains(id) && HandleOwnerSleepStimulus(source) is { } sleepResult)
            return sleepResult;
        var motion = _catalog.Find(id);
        var capability = _behaviorCapabilities.Find(id);
        if (motion is null || capability is null) return null; // Existing submission reports MissingAsset.
        var gate = EvaluateGate(source, BehaviorExecutionMode.Normal, motion);
        if (!gate.Allowed) return null; // Never present unavailable art as a personality refusal.
        // Assess willingness for the requested goal; its existing planner still
        // validates and executes every physical posture transition afterwards.
        var decision = EvaluateOwnerParticipation(capability with
        {
            StartPostures = new HashSet<StablePosture> { _agentState.CurrentPosture },
            StartPoseFamily = PetPoseCompatibility.FamilyFor(_agentState.CurrentPoseId, _agentState.CurrentPosture)
        }, source);
        Trace("owner_activity_participation", $"action={id} result={decision.Disposition} reason={decision.ReasonCode} pressure={_ownerRequestTimes.Count} prompt_bias={_promptAgencyBias:0.00}");
        if (decision.Disposition == RequestDisposition.Accepted) return null;
        var result = decision.Disposition == RequestDisposition.Rejected ? PetActionResult.Rejected : PetActionResult.Deferred;
        UpdateCommandDecision(result, source, decision, id);
        return result;
    }
}
