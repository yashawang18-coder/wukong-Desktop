using System.Windows;
using Wukong.Application;

namespace Wukong.Desktop;

public static class DesktopChatPlacement
{
    private const double Gap = 4;
    private const double Margin = 8;

    public static Rect VisibleSubjectBounds(Rect imageElementBounds, MotionVisibleMetrics metrics)
    {
        if (imageElementBounds.Width <= 0 || imageElementBounds.Height <= 0 ||
            metrics.CanvasWidth <= 0 || metrics.CanvasHeight <= 0 ||
            metrics.Bounds.Width <= 0 || metrics.Bounds.Height <= 0)
            return imageElementBounds;

        var scale = Math.Min(
            imageElementBounds.Width / metrics.CanvasWidth,
            imageElementBounds.Height / metrics.CanvasHeight);
        var renderedWidth = metrics.CanvasWidth * scale;
        var renderedHeight = metrics.CanvasHeight * scale;
        var renderedLeft = imageElementBounds.Left + (imageElementBounds.Width - renderedWidth) / 2;
        var renderedTop = imageElementBounds.Top + (imageElementBounds.Height - renderedHeight) / 2;
        return new Rect(
            renderedLeft + metrics.Bounds.X * scale,
            renderedTop + metrics.Bounds.Y * scale,
            metrics.Bounds.Width * scale,
            metrics.Bounds.Height * scale);
    }

    public static Point Place(Rect workArea, Rect petBounds, Size overlaySize)
    {
        var width = Math.Min(Math.Max(overlaySize.Width, 1), Math.Max(1, workArea.Width - Margin * 2));
        var height = Math.Min(Math.Max(overlaySize.Height, 1), Math.Max(1, workArea.Height - Margin * 2));
        var preferredLeft = petBounds.Left + (petBounds.Width - width) / 2;
        var below = petBounds.Bottom + Gap;
        var above = petBounds.Top - height - Gap;
        var preferredTop = below + height <= workArea.Bottom - Margin ? below : above;
        var left = Math.Clamp(preferredLeft, workArea.Left + Margin, workArea.Right - width - Margin);
        var top = Math.Clamp(preferredTop, workArea.Top + Margin, workArea.Bottom - height - Margin);
        return new Point(left, top);
    }

    public static double PetGap => Gap;

    public static Rect MakeRoomBelow(Rect workArea, Rect petBounds, Size overlaySize)
    {
        var requiredBottom = petBounds.Bottom + Gap + overlaySize.Height + Margin;
        if (requiredBottom <= workArea.Bottom)
            return petBounds;
        var adjustedTop = Math.Max(workArea.Top + Margin, petBounds.Top - (requiredBottom - workArea.Bottom));
        return new Rect(petBounds.Left, adjustedTop, petBounds.Width, petBounds.Height);
    }

    public static Point PlaceSpeechAbove(Rect workArea, Rect petBounds, Size bubbleSize)
    {
        var width = Math.Min(Math.Max(bubbleSize.Width, 1), Math.Max(1, workArea.Width - Margin * 2));
        var height = Math.Min(Math.Max(bubbleSize.Height, 1), Math.Max(1, workArea.Height - Margin * 2));
        var left = Math.Clamp(petBounds.Left + (petBounds.Width - width) / 2, workArea.Left + Margin, workArea.Right - width - Margin);
        var top = Math.Clamp(petBounds.Top - height - Gap, workArea.Top + Margin, workArea.Bottom - height - Margin);
        return new Point(left, top);
    }
}

public static class InitiativeSpeechSchedule
{
    public static TimeSpan NextInterval(Random random) =>
        TimeSpan.FromSeconds(random.Next(180, 421));

    public static string SelectMessage(Random random, InitiativeSpeechTopic topic, StablePosture posture)
    {
        var messages = topic switch
        {
            InitiativeSpeechTopic.Hunger => new[] { "主人，我好像有一点饿了。", "肚子在轻轻提醒我啦。", "等你方便时，可以给我一点吃的吗？" },
            InitiativeSpeechTopic.Play => new[] { "休息一下，要不要陪我玩一会儿？", "我现在有一点想活动啦。", "忙完以后，陪我动一动吧。" },
            InitiativeSpeechTopic.Curiosity => new[] { "刚才好像有点新动静。", "我在看看周围发生了什么。", "主人，你在忙什么呀？" },
            InitiativeSpeechTopic.Rest => new[] { "我先安静歇一会儿。", "今天想靠着你慢慢休息。", "我有一点困，但还在陪你。" },
            InitiativeSpeechTopic.Companionship when posture == StablePosture.Prone => new[] { "主人，我安静陪着你。", "今天也想待在你旁边。", "我在这里趴一会儿。" },
            InitiativeSpeechTopic.Companionship when posture == StablePosture.Sit => new[] { "主人，我在听。", "要不要和我说句话？", "我正看着你呢。" },
            _ => new[] { "主人，我在这里。", "刚刚想到你了。", "忙完记得看看我。" }
        };
        return messages[random.Next(messages.Length)];
    }

    public static bool CanSpeakDuring(string behaviorId, bool isPetrified) =>
        !isPetrified &&
        (string.Equals(behaviorId, Phase15BehaviorIds.ProneIdle, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(behaviorId, LifecycleCandidateBehaviorIds.StandIdleMicroloop, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(behaviorId, LifecycleCandidateBehaviorIds.SitIdleMicroloop, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(behaviorId, LifecycleCandidateBehaviorIds.ProneIdleMicroloop, StringComparison.OrdinalIgnoreCase) ||
         behaviorId.StartsWith("wk.runtime.posture_hold.", StringComparison.OrdinalIgnoreCase));
}
