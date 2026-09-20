using Wukong.Domain;

namespace Wukong.Application;

public enum DialogueActType
{
    SocialReply,
    BehaviorAccepted,
    BehaviorRejected,
    BehaviorDeferred,
    BehaviorFailed,
    BehaviorInterrupted,
    BehaviorCompleted,
    Initiative
}

public enum DialogueStateClaim
{
    Sleeping,
    Walking,
    Eating,
    Drinking,
    Prone,
    SideLying,
    Sitting,
    Standing,
    Driving,
    Flying,
    UsingMagic,
    Happy,
    Tired,
    Busy
}

public enum DialogueTemporalClaim
{
    Current,
    Preparing,
    Completed,
    Desired,
    Hypothetical
}

public sealed record DialogueClaim(DialogueStateClaim State, DialogueTemporalClaim Temporal);

public sealed record DialogueAct(
    DialogueActType Type,
    string Text,
    Guid? RelatedRequestId,
    IReadOnlyList<DialogueClaim> Claims);

public sealed record DialogueTruthSnapshot(
    PetEpisodeKind Episode,
    StablePosture Posture,
    string? CurrentActionId,
    Guid? ActiveRequestId,
    bool IsBusy,
    IReadOnlyList<PetRecentExperience> RecentOutcomes);

public sealed record DialogueValidationResult(
    bool IsValid,
    string Text,
    IReadOnlyList<string> ReasonCodes,
    bool UsedSafeFallback);

public sealed class DialogueStateClaimValidator
{
    public DialogueValidationResult Validate(DialogueAct act, DialogueTruthSnapshot truth)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(truth);
        var reasons = new List<string>();
        foreach (var claim in act.Claims)
        {
            if (!IsValid(claim, act.RelatedRequestId, truth))
                reasons.Add($"claim_mismatch:{claim.State}:{claim.Temporal}");
        }

        if (reasons.Count == 0)
            return new DialogueValidationResult(true, act.Text, Array.Empty<string>(), false);

        return new DialogueValidationResult(false, SafeCurrentStateText(truth), reasons, true);
    }

    public DialogueAct InferClaims(string text, Guid? requestId = null, DialogueActType type = DialogueActType.SocialReply)
    {
        var value = text ?? string.Empty;
        var temporal = ContainsAny(value, "想", "希望", "想要")
            ? DialogueTemporalClaim.Desired
            : ContainsAny(value, "准备", "马上", "这就")
                ? DialogueTemporalClaim.Preparing
                : ContainsAny(value, "完了", "完啦", "已经吃完", "已经喝完")
                    ? DialogueTemporalClaim.Completed
                    : DialogueTemporalClaim.Current;
        var claims = new List<DialogueClaim>();
        AddIf(claims, value, DialogueStateClaim.Sleeping, temporal, "睡", "睡觉");
        AddIf(claims, value, DialogueStateClaim.Walking, temporal, "散步", "走动", "巡视");
        AddIf(claims, value, DialogueStateClaim.Eating, temporal, "吃饭", "吃狗粮", "吃完");
        AddIf(claims, value, DialogueStateClaim.Drinking, temporal, "喝水", "喝完");
        AddIf(claims, value, DialogueStateClaim.Driving, temporal, "兜风", "开车");
        AddIf(claims, value, DialogueStateClaim.Flying, temporal, "飞", "扫把");
        AddIf(claims, value, DialogueStateClaim.Sitting, temporal, "坐着", "坐下");
        AddIf(claims, value, DialogueStateClaim.Prone, temporal, "趴着", "趴下");
        AddIf(claims, value, DialogueStateClaim.Standing, temporal, "站着", "站起来", "起身");
        return new DialogueAct(type, value, requestId, claims);
    }

    private static bool IsValid(DialogueClaim claim, Guid? requestId, DialogueTruthSnapshot truth) => claim.Temporal switch
    {
        DialogueTemporalClaim.Hypothetical or DialogueTemporalClaim.Desired => true,
        DialogueTemporalClaim.Preparing => requestId is not null && truth.ActiveRequestId == requestId,
        DialogueTemporalClaim.Completed => truth.RecentOutcomes.Any(item =>
            item.Status == ExecutionStatus.Completed && Matches(claim.State, item.BehaviorId, truth)),
        _ => MatchesCurrent(claim.State, truth)
    };

    private static bool MatchesCurrent(DialogueStateClaim state, DialogueTruthSnapshot truth) => state switch
    {
        DialogueStateClaim.Sleeping => truth.Episode == PetEpisodeKind.Sleeping,
        DialogueStateClaim.Walking => ContainsAny(truth.CurrentActionId, "walk", "patrol"),
        DialogueStateClaim.Eating => truth.Episode == PetEpisodeKind.Eating || ContainsAny(truth.CurrentActionId, "eat", "kibble"),
        DialogueStateClaim.Drinking => truth.Episode == PetEpisodeKind.Drinking || ContainsAny(truth.CurrentActionId, "drink", "water"),
        DialogueStateClaim.Prone => truth.Posture == StablePosture.Prone,
        DialogueStateClaim.SideLying => ContainsAny(truth.CurrentActionId, "side", "sleep"),
        DialogueStateClaim.Sitting => truth.Posture == StablePosture.Sit,
        DialogueStateClaim.Standing => truth.Posture == StablePosture.Stand,
        DialogueStateClaim.Driving => truth.Episode == PetEpisodeKind.VehicleActivity || ContainsAny(truth.CurrentActionId, "car_ride"),
        DialogueStateClaim.Flying => ContainsAny(truth.CurrentActionId, "broom"),
        DialogueStateClaim.UsingMagic => truth.Episode == PetEpisodeKind.MagicActivity,
        DialogueStateClaim.Busy => truth.IsBusy,
        DialogueStateClaim.Tired => truth.Episode is PetEpisodeKind.Sleeping or PetEpisodeKind.Recovering,
        DialogueStateClaim.Happy => true,
        _ => false
    };

    private static bool Matches(DialogueStateClaim state, string behaviorId, DialogueTruthSnapshot truth) => state switch
    {
        DialogueStateClaim.Sleeping => ContainsAny(behaviorId, "sleep"),
        DialogueStateClaim.Walking => ContainsAny(behaviorId, "walk", "patrol"),
        DialogueStateClaim.Eating => ContainsAny(behaviorId, "eat", "kibble"),
        DialogueStateClaim.Drinking => ContainsAny(behaviorId, "drink", "water"),
        DialogueStateClaim.Driving => ContainsAny(behaviorId, "car_ride"),
        DialogueStateClaim.Flying => ContainsAny(behaviorId, "broom"),
        _ => MatchesCurrent(state, truth)
    };

    private static string SafeCurrentStateText(DialogueTruthSnapshot truth) => truth.Episode switch
    {
        PetEpisodeKind.Sleeping => "我还在睡觉呀。",
        PetEpisodeKind.Exploring => "我正在走走看看。",
        PetEpisodeKind.Eating => "我正在吃饭饭。",
        PetEpisodeKind.Drinking => "我正在喝水呀。",
        PetEpisodeKind.VehicleActivity => "我正在兜风呢。",
        PetEpisodeKind.MagicActivity => "我还在变魔法呢。",
        _ => truth.Posture switch
        {
            StablePosture.Stand => "我还站在这里呀。",
            StablePosture.Sit => "我还坐在这里呀。",
            _ => "我还趴在这里呢。"
        }
    };

    private static void AddIf(ICollection<DialogueClaim> claims, string value, DialogueStateClaim state, DialogueTemporalClaim temporal, params string[] terms)
    {
        if (ContainsAny(value, terms))
            claims.Add(new DialogueClaim(state, temporal));
    }

    private static bool ContainsAny(string? value, params string[] terms) =>
        !string.IsNullOrWhiteSpace(value) && terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

public enum NormalizedOwnerIntentKind
{
    None,
    Stop,
    Sit,
    Down,
    Paw,
    Jump,
    Spin,
    Eat,
    Drink,
    Sleep,
    Walk,
    CarRide,
    BroomFlight,
    Apparate,
    Petrify
}

public sealed record NormalizedOwnerIntent(NormalizedOwnerIntentKind Kind, string IntentId, string OriginalText)
{
    public bool IsBehaviorIntent => Kind != NormalizedOwnerIntentKind.None;
}

public sealed class OwnerIntentNormalizer
{
    public NormalizedOwnerIntent Normalize(string text)
    {
        var value = (text ?? string.Empty).Trim();
        var kind = value switch
        {
            _ when Contains(value, "停下", "停") => NormalizedOwnerIntentKind.Stop,
            _ when Contains(value, "喝水") => NormalizedOwnerIntentKind.Drink,
            _ when Contains(value, "吃饭", "吃狗粮") => NormalizedOwnerIntentKind.Eat,
            _ when Contains(value, "睡觉", "去睡", "睡一会") => NormalizedOwnerIntentKind.Sleep,
            _ when Contains(value, "兜风") => NormalizedOwnerIntentKind.CarRide,
            _ when Contains(value, "扫把", "飞行") => NormalizedOwnerIntentKind.BroomFlight,
            _ when Contains(value, "幻影移形", "消失再出现") => NormalizedOwnerIntentKind.Apparate,
            _ when Contains(value, "石化") => NormalizedOwnerIntentKind.Petrify,
            _ when Contains(value, "散步", "走走", "巡视") => NormalizedOwnerIntentKind.Walk,
            _ when Contains(value, "转圈") => NormalizedOwnerIntentKind.Spin,
            _ when Contains(value, "跳一下", "跳跃", "跳") => NormalizedOwnerIntentKind.Jump,
            _ when Contains(value, "握手", "伸爪", "抬爪") => NormalizedOwnerIntentKind.Paw,
            _ when Contains(value, "趴下", "卧") => NormalizedOwnerIntentKind.Down,
            _ when Contains(value, "坐下", "坐") => NormalizedOwnerIntentKind.Sit,
            _ => NormalizedOwnerIntentKind.None
        };
        return new NormalizedOwnerIntent(kind, IntentId(kind), value);
    }

    private static string IntentId(NormalizedOwnerIntentKind kind) => kind switch
    {
        NormalizedOwnerIntentKind.Stop => "owner.stop",
        NormalizedOwnerIntentKind.Sit => "command.sit",
        NormalizedOwnerIntentKind.Down => "command.down",
        NormalizedOwnerIntentKind.Paw => "command.paw",
        NormalizedOwnerIntentKind.Jump => "command.jump",
        NormalizedOwnerIntentKind.Spin => "command.spin",
        NormalizedOwnerIntentKind.Eat => "interaction.eat",
        NormalizedOwnerIntentKind.Drink => "interaction.drink",
        NormalizedOwnerIntentKind.Sleep => "episode.sleep",
        NormalizedOwnerIntentKind.Walk => "episode.explore",
        NormalizedOwnerIntentKind.CarRide => "interaction.car_ride",
        NormalizedOwnerIntentKind.BroomFlight => "magic.accio_broom",
        NormalizedOwnerIntentKind.Apparate => "magic.apparate",
        NormalizedOwnerIntentKind.Petrify => "magic.petrify",
        _ => "conversation.social"
    };

    private static bool Contains(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
