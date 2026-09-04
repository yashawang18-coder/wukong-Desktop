using Wukong.Application;

namespace Wukong.Desktop;

public sealed record InteractionDecisionContext(
    PetGestureKind Gesture,
    int TapBurst,
    PetRuntimeState State,
    TemperamentProfile Temperament,
    RelationshipState Relationship,
    DateTimeOffset Now,
    bool IsStableIdle,
    bool IsCurrentInterruptible,
    bool IsPetrified,
    IReadOnlySet<string> RuntimeEnabledBehaviorIds);

public sealed record InteractionDecision(
    PetActionResult Disposition,
    PetGestureKind EffectiveGesture,
    string? BehaviorId,
    string ReasonCode,
    string UserFacingReason,
    PetRuntimeState UpdatedState);

public sealed class InteractionDecisionService
{
    private readonly BehaviorAgentMockEngine _stateEngine = new();

    public InteractionDecision Decide(InteractionDecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State.Clamp();
        var effective = context.Gesture == PetGestureKind.OwnerTouch && context.TapBurst >= 3
            ? PetGestureKind.RapidTap
            : context.Gesture;

        if (effective is PetGestureKind.None or PetGestureKind.DoubleClick)
            return Decision(PetActionResult.Deferred, effective, null, "handled_by_desktop_ui", "该输入由桌面界面处理", state);
        if (effective == PetGestureKind.Drag)
            return Decision(PetActionResult.Deferred, effective, null, "drag_repositions_pet", "拖拽只移动悟空，不伪造互动动画", state);
        if (context.IsPetrified)
            return Decision(PetActionResult.Deferred, effective, null, "petrified_interaction_route", "石化状态使用金币互动入口", state);
        if (!context.IsStableIdle && !context.IsCurrentInterruptible)
            return Decision(PetActionResult.Deferred, effective, null, "busy_non_interruptible", "当前动作不能安全中断", state);

        if (effective == PetGestureKind.RapidTap)
        {
            state = _stateEngine.ApplyRepeatedClick(state, context.Temperament, Math.Max(3, context.TapBurst));
            if (state.Stress >= 0.78)
                return Decision(PetActionResult.Rejected, effective, null, "rapid_tap_stress_limit", "连续点击让悟空有些紧张，先让它缓一缓", state);
            return WithRuntimeAsset(
                context,
                effective,
                Phase15BehaviorIds.LookAround,
                "rapid_tap_response_asset_locked",
                "已记录连续点击带来的紧张变化；回应动作素材尚未获准", state);
        }

        if (context.Relationship.TouchAcceptance01 < 0.25 || state.Stress >= 0.76)
        {
            state = state with
            {
                Stress = Math.Clamp(state.Stress + 0.02 + context.Temperament.Sensitivity01 * 0.02, 0, 1),
                Arousal = Math.Clamp(state.Arousal + 0.02, 0, 1),
                LastInteractionAt = context.Now
            };
            return Decision(PetActionResult.Rejected, effective, null, "touch_acceptance_low", "悟空现在不太想被碰", state);
        }

        state = state with
        {
            SocialNeed = Math.Clamp(state.SocialNeed - (effective == PetGestureKind.Stroke ? 0.025 : 0.015), 0, 1),
            Comfort = Math.Clamp(state.Comfort + 0.012, 0, 1),
            MoodValence = Math.Clamp(state.MoodValence + 0.008, 0, 1),
            LastInteractionAt = context.Now
        };
        var behaviorId = effective == PetGestureKind.Stroke
            ? Phase15BehaviorIds.StrokeEnjoy
            : Phase15BehaviorIds.ProneTouch;
        if (behaviorId == Phase15BehaviorIds.ProneTouch && state.CurrentPosture != StablePosture.Prone)
            return Decision(PetActionResult.Deferred, effective, null, "touch_pose_incompatible", "已记录互动，但趴姿触摸动作与当前姿态不兼容", state);
        var lockedReason = effective == PetGestureKind.Stroke
            ? "stroke_response_asset_locked"
            : "prone_touch_runtime_qa_pending";
        return WithRuntimeAsset(context, effective, behaviorId, lockedReason, "互动状态已记录；对应动作素材尚未通过运行时验收", state);
    }

    private static InteractionDecision WithRuntimeAsset(
        InteractionDecisionContext context,
        PetGestureKind gesture,
        string behaviorId,
        string lockedReason,
        string lockedMessage,
        PetRuntimeState state) =>
        context.RuntimeEnabledBehaviorIds.Contains(behaviorId)
            ? Decision(PetActionResult.Accepted, gesture, behaviorId, "interaction_behavior_selected", "已选择合格的互动动作", state)
            : Decision(PetActionResult.Deferred, gesture, null, lockedReason, lockedMessage, state);

    private static InteractionDecision Decision(
        PetActionResult disposition,
        PetGestureKind gesture,
        string? behaviorId,
        string reason,
        string userFacing,
        PetRuntimeState state) =>
        new(disposition, gesture, behaviorId, reason, userFacing, state.Clamp());
}
