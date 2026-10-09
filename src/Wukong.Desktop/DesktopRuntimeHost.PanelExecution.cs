using Wukong.Application;
using Wukong.Domain;

namespace Wukong.Desktop;

public sealed partial class DesktopRuntimeHost
{
    private readonly Queue<string> _pendingPanelSequence = new();

    private PetActionResult StartPanelMotionSequence(PlayableMotion target, BehaviorCapability capability)
    {
        if (target.WindowMotionEnabled && !HasPanelWalkSpace(target))
        {
            UpdateDecision(PetActionResult.Deferred, "ControlPanel", "panel_walk_no_space",
                "这个方向没有足够空间，请先拖动悟空或选择另一方向");
            return PetActionResult.Deferred;
        }
        var approved = target.RuntimeEnabled && target.EffectiveRuntimeApproved;
        if (!approved)
        {
            if (!PetPoseCompatibility.IsCompatible(capability.StartPoseFamily, _agentState))
                return PanelPoseUnavailable();
            return SubmitBehavior(BehaviorRequestSource.ControlPanel, target.BehaviorId,
                "panel_candidate_preview", 7, BehaviorExecutionMode.DeveloperPreview);
        }
        var gate = EvaluateGate(BehaviorRequestSource.ControlPanel, BehaviorExecutionMode.Normal, target);
        if (!gate.Allowed)
        {
            UpdateDecision(PetActionResult.Deferred, "ControlPanel", gate.ReasonCode, gate.UserFacingReason);
            return PetActionResult.Deferred;
        }
        var plan = PlanPanelTransitions(capability);
        if (plan is null) return PanelPoseUnavailable();
        _pendingPanelSequence.Clear();
        foreach (var id in plan.Append(target.BehaviorId)) _pendingPanelSequence.Enqueue(id);
        var result = SubmitNextPanelMotion();
        if (result == PetActionResult.Accepted && plan.Count > 0)
            UpdateDecision(result, "ControlPanel", "panel_posture_preparing", "正在自然调整姿态，随后执行所选动作");
        return result;
    }

    // Search only approved posture edges, never arbitrary actions or camera cuts.
    private IReadOnlyList<string>? PlanPanelTransitions(BehaviorCapability target, PetRuntimeState? from = null)
    {
        var edges = new[]
        {
            AutonomousDailyCandidateBehaviorIds.ProneToSit, AutonomousDailyCandidateBehaviorIds.SitToStand,
            AutonomousDailyCandidateBehaviorIds.StandToSit, AutonomousDailyCandidateBehaviorIds.SitToProne,
            WakeRiseCandidateBehaviorIds.FrontRiseFull, WakeRiseCandidateBehaviorIds.FrontRiseSit,
            WakeRiseCandidateBehaviorIds.FrontSitStand
        };
        var queue = new Queue<(PetRuntimeState State, string[] Steps)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((from ?? _agentState, Array.Empty<string>()));
        while (queue.TryDequeue(out var node))
        {
            if (PetPoseCompatibility.IsCompatible(target.StartPoseFamily, node.State)) return node.Steps;
            if (!visited.Add(PetPoseCompatibility.FamilyFor(node.State.CurrentPoseId, node.State.CurrentPosture))) continue;
            foreach (var id in edges)
            {
                var edge = _behaviorCapabilities.Find(id);
                var motion = _catalog.Find(id);
                if (edge is null || motion is not { RuntimeEnabled: true, EffectiveRuntimeApproved: true, IsExpired: false } ||
                    !PetPoseCompatibility.IsCompatible(edge.StartPoseFamily, node.State)) continue;
                queue.Enqueue((node.State with { CurrentPosture = edge.EndPosture, CurrentPoseId = edge.EndPoseId },
                    node.Steps.Append(id).ToArray()));
            }
        }
        return null;
    }

    private PetActionResult PanelPoseUnavailable()
    {
        UpdateDecision(PetActionResult.Deferred, "ControlPanel", "panel_pose_bridge_missing",
            "当前姿态缺少已批准的自然过渡，可先查看动画；不会强切视角");
        return PetActionResult.Deferred;
    }

    public PanelAvailabilityContext PanelAvailabilitySnapshot => new(this);

    public string DescribePanelAvailability(PlayableMotion motion)
    {
        if (motion.IsExpired || motion.Deprecated) return "暂不可执行 · 已过期";
        if (!motion.VisualApproved && !motion.RuntimeApproved) return "暂不可执行 · 尚待视觉验收";
        if (_agentState.IsBusy) return "暂不可执行 · 等当前动作安全结束";
        if (motion.WindowMotionEnabled && !HasPanelWalkSpace(motion)) return "暂不可执行 · 这个方向空间不足";
        var mode = motion.RuntimeEnabled && motion.EffectiveRuntimeApproved ? BehaviorExecutionMode.Normal :
            motion.Category == "宠物魔法" ? BehaviorExecutionMode.PrototypePreview : BehaviorExecutionMode.DeveloperPreview;
        var gate = EvaluateGate(BehaviorRequestSource.ControlPanel, mode, motion);
        if (!gate.Allowed) return "暂不可执行 · " + gate.UserFacingReason;
        if (motion.Category == "宠物魔法" || motion.BehaviorId == CarRideBehaviorIds.CarRide)
            return "可立即执行 · 仍经过安全门禁";
        var capability = _behaviorCapabilities.Find(motion.BehaviorId);
        if (capability is null) return "暂不可执行 · 尚未注册运行能力";
        if (PetPoseCompatibility.IsCompatible(capability.StartPoseFamily, _agentState)) return "可立即执行";
        if (mode != BehaviorExecutionMode.Normal) return "暂不可执行 · 候选需要匹配起始姿态";
        var plan = PlanPanelTransitions(capability);
        if (plan is null && capability.StartPoseFamily == "sleep") return "暂不可执行 · 需先处于对应睡姿，不强行切换视角";
        if (plan is null) return "暂不可执行 · 缺少到" + PoseFamilyLabel(capability.StartPoseFamily) + "的已批准过渡";
        if (motion.Category == "口令动作") return "需要调整姿态 · 手和吃按当前坐/趴状态选分支";
        return "需要调整姿态 · " + string.Join(" → ", plan.Select(id => _catalog.Find(id)?.DisplayName ?? id));
    }

    public IReadOnlyList<string> DescribeMissingPoseBridges()
    {
        var rows = new List<string>();
        foreach (var (pose, posture) in new[] { ("stand.neutral.left_front", StablePosture.Stand),
            ("sit.neutral.left_front", StablePosture.Sit), ("prone.awake.left_front", StablePosture.Prone),
            ("prone.awake.front", StablePosture.Prone) })
        {
            var state = _agentState with { CurrentPosture = posture, CurrentPoseId = pose, IsBusy = false };
            foreach (var motion in Motions.Where(x => IsBaseAssetMotion(x) && x.RuntimeEnabled && !x.IsExpired && !x.Deprecated))
            {
                var capability = _behaviorCapabilities.Find(motion.BehaviorId);
                if (capability is not null && capability.StartPoseFamily != "sleep" && PlanPanelTransitions(capability, state) is null)
                    rows.Add($"{PoseFamilyLabel(PetPoseCompatibility.FamilyFor(pose, posture))} -> {motion.DisplayName} ({motion.BehaviorId}): requires {capability.StartPoseFamily}; no approved path");
            }
        }
        return rows.Distinct().ToArray();
    }

    private static string PoseFamilyLabel(string family) => family switch
    {
        "stand" => "站姿", "sit" => "坐姿", "prone.front" => "正面趴姿",
        "prone.non_front" => "侧向趴姿", "sleep" => "对应睡姿", _ => family
    };

    private bool HasPanelWalkSpace(PlayableMotion motion) =>
        motion.Direction.Contains("right", StringComparison.OrdinalIgnoreCase) ? _patrolCanMoveRight : _patrolCanMoveLeft;

    private PetActionResult SubmitNextPanelMotion()
    {
        var id = _pendingPanelSequence.Dequeue();
        var motion = _catalog.Find(id);
        var capability = _behaviorCapabilities.Find(id);
        if (motion is null || capability is null || !PetPoseCompatibility.IsCompatible(capability.StartPoseFamily, _agentState) ||
            (motion.WindowMotionEnabled && !HasPanelWalkSpace(motion)))
        {
            _pendingPanelSequence.Clear();
            return PanelPoseUnavailable();
        }
        var result = SubmitBehavior(BehaviorRequestSource.ControlPanel, id, $"panel_sequence:{id}", 7);
        if (result != PetActionResult.Accepted) _pendingPanelSequence.Clear();
        return result;
    }

    private bool ContinuePanelMotionSequence() =>
        _pendingPanelSequence.Count > 0 && SubmitNextPanelMotion() == PetActionResult.Accepted;
}
