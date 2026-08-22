using System.Windows;

namespace Wukong.Desktop;

public static class DesktopChatPlacement
{
    private const double Gap = 5;
    private const double Margin = 8;

    public static Point Place(Rect workArea, Rect petBounds, Size overlaySize)
    {
        var width = Math.Min(Math.Max(overlaySize.Width, 1), Math.Max(1, workArea.Width - Margin * 2));
        var height = Math.Min(Math.Max(overlaySize.Height, 1), Math.Max(1, workArea.Height - Margin * 2));
        var preferredLeft = petBounds.Left + (petBounds.Width - width) / 2;
        var below = petBounds.Bottom + Gap;
        var above = petBounds.Top - height - Gap;
        var preferredTop = above >= workArea.Top + Margin ? above : below;
        var left = Math.Clamp(preferredLeft, workArea.Left + Margin, workArea.Right - width - Margin);
        var top = Math.Clamp(preferredTop, workArea.Top + Margin, workArea.Bottom - height - Margin);
        return new Point(left, top);
    }

    public static double PetGap => Gap;
}

public static class InitiativeSpeechSchedule
{
    public static TimeSpan NextInterval(Random random) =>
        TimeSpan.FromSeconds(random.Next(180, 421));

    public static string SelectMessage(Random random, Wukong.Application.StablePosture posture)
    {
        var messages = posture switch
        {
            Wukong.Application.StablePosture.Prone => new[] { "我在这里趴一会儿。", "主人，我安静陪着你。", "今天也想待在你旁边。" },
            Wukong.Application.StablePosture.Sit => new[] { "主人，我在听。", "要不要和我说句话？", "我正看着你呢。" },
            _ => new[] { "主人，我在这里。", "刚刚想到你了。", "忙完记得看看我。" }
        };
        return messages[random.Next(messages.Length)];
    }

    public static bool CanSpeakDuring(string behaviorId, bool isPetrified) =>
        !isPetrified &&
        !behaviorId.StartsWith("wk.magic.", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(behaviorId, "wk.interaction.car_ride", StringComparison.OrdinalIgnoreCase);
}
