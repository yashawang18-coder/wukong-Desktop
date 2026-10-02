namespace Wukong.Application;

public enum PetMemoryTheme
{
    Companionship,
    Play,
    Explore,
    Food,
    Drink,
    Rest,
    Observe
}

public enum PetMemoryEvidenceSource
{
    ConfirmedConversation,
    AlbumDescription
}

public sealed record PetMemoryEvidence(
    string EvidenceId,
    PetMemoryEvidenceSource Source,
    string Text,
    PetMemoryTheme? SuggestedTheme = null)
{
    public DateTimeOffset? ObservedAt { get; init; }
    public double Confidence { get; init; } = 1;
}

public sealed record PetDecisionMemoryProfile(
    IReadOnlyDictionary<string, double> CategoryWeights,
    IReadOnlyDictionary<string, double> InitiativeTopicWeights,
    IReadOnlyDictionary<string, int> EvidenceCounts,
    DateTimeOffset GeneratedAt,
    string Fingerprint)
{
    public static PetDecisionMemoryProfile Empty { get; } = new(
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        DateTimeOffset.UnixEpoch,
        "empty");

    public double CategoryWeight(BehaviorSemanticCategory category) =>
        Read(CategoryWeights, category.ToString(), 0.10);

    public double InitiativeTopicWeight(InitiativeSpeechTopic topic) =>
        Read(InitiativeTopicWeights, topic.ToString(), 0.12);

    public PetDecisionMemoryProfile Clamp() => this with
    {
        CategoryWeights = ClampMap(CategoryWeights, 0.10),
        InitiativeTopicWeights = ClampMap(InitiativeTopicWeights, 0.12),
        EvidenceCounts = EvidenceCounts
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => Math.Clamp(item.Value, 0, 1000), StringComparer.OrdinalIgnoreCase),
        Fingerprint = string.IsNullOrWhiteSpace(Fingerprint) ? "empty" : Fingerprint.Trim()
    };

    private static IReadOnlyDictionary<string, double> ClampMap(
        IReadOnlyDictionary<string, double>? source,
        double maximum) =>
        (source ?? new Dictionary<string, double>())
        .Where(item => !string.IsNullOrWhiteSpace(item.Key) && double.IsFinite(item.Value))
        .ToDictionary(item => item.Key, item => Math.Clamp(item.Value, -maximum, maximum), StringComparer.OrdinalIgnoreCase);

    private static double Read(IReadOnlyDictionary<string, double>? source, string key, double maximum) =>
        source is not null && source.TryGetValue(key, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, -maximum, maximum)
            : 0;
}

public interface IPetDecisionMemorySource
{
    Task<PetDecisionMemoryProfile> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class PetDecisionMemoryProjector
{
    private static readonly IReadOnlyDictionary<PetMemoryTheme, string[]> Keywords =
        new Dictionary<PetMemoryTheme, string[]>
        {
            [PetMemoryTheme.Companionship] = new[] { "老爸", "主人", "陪", "一起", "家人", "抱", "摸摸" },
            [PetMemoryTheme.Play] = new[] { "玩", "玩具", "草地", "跑", "散步", "出门", "兜风" },
            [PetMemoryTheme.Explore] = new[] { "旅行", "拍照", "日落", "公园", "南京", "江宁", "闻闻", "新朋友" },
            [PetMemoryTheme.Food] = new[] { "吃", "饭", "零食", "鸡胸", "肉干", "馒头", "猫粮" },
            [PetMemoryTheme.Drink] = new[] { "喝", "水", "水碗", "奶", "爪布奇诺" },
            [PetMemoryTheme.Rest] = new[] { "睡", "休息", "趴", "累", "安静", "困" },
            [PetMemoryTheme.Observe] = new[] { "看", "观察", "好奇", "听", "回头", "注意" }
        };

    public static IReadOnlyDictionary<PetMemoryTheme, string> AlbumQueries { get; } =
        new Dictionary<PetMemoryTheme, string>
        {
            [PetMemoryTheme.Companionship] = "老爸 主人 陪 一起 家人",
            [PetMemoryTheme.Play] = "玩 草地 玩具 散步 出门",
            [PetMemoryTheme.Explore] = "旅行 拍照 日落 公园 南京 江宁",
            [PetMemoryTheme.Food] = "吃 饭 零食 鸡胸 肉干",
            [PetMemoryTheme.Drink] = "喝 水 水碗 爪布奇诺",
            [PetMemoryTheme.Rest] = "睡 休息 趴 累 困",
            [PetMemoryTheme.Observe] = "看 观察 好奇 回头"
        };

    public PetDecisionMemoryProfile Project(
        IEnumerable<PetMemoryEvidence> evidence,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var items = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.EvidenceId) && !string.IsNullOrWhiteSpace(item.Text))
            .Where(item => item.ObservedAt is null || item.ObservedAt <= generatedAt.AddMinutes(5))
            .GroupBy(item => $"{item.Source}:{item.EvidenceId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Text.Length)
                .ThenBy(item => item.Text, StringComparer.Ordinal).First())
            .GroupBy(item => $"{item.Source}:{item.Text.Trim()}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ObservedAt).First())
            .OrderByDescending(item => item.ObservedAt)
            .Take(128)
            .OrderBy(item => item.Source)
            .ThenBy(item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var themeScores = Enum.GetValues<PetMemoryTheme>().ToDictionary(theme => theme, _ => 0.0);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["confirmed_conversation"] = items.Count(item => item.Source == PetMemoryEvidenceSource.ConfirmedConversation),
            ["album_description"] = items.Count(item => item.Source == PetMemoryEvidenceSource.AlbumDescription)
        };

        foreach (var item in items)
        {
            var matched = MatchThemes(item).ToArray();
            foreach (var theme in matched)
            {
                var unit = item.Source == PetMemoryEvidenceSource.ConfirmedConversation ? 0.018 : 0.012;
                var ageDays = Math.Max(0, (generatedAt - (item.ObservedAt ?? generatedAt)).TotalDays);
                var confidence = double.IsFinite(item.Confidence) ? Math.Clamp(item.Confidence, 0, 1) : 0;
                themeScores[theme] += unit * confidence * Math.Exp(-ageDays / 90) * EvidencePolarity(item.Text, theme);
                var key = $"theme:{theme}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var theme in themeScores.Keys.ToArray())
            themeScores[theme] = Math.Clamp(themeScores[theme], -0.10, 0.10);

        var categories = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [BehaviorSemanticCategory.Social.ToString()] = themeScores[PetMemoryTheme.Companionship],
            [BehaviorSemanticCategory.Explore.ToString()] = Math.Min(0.10, themeScores[PetMemoryTheme.Play] * 0.55 + themeScores[PetMemoryTheme.Explore] * 0.75),
            [BehaviorSemanticCategory.Observe.ToString()] = Math.Min(0.10, themeScores[PetMemoryTheme.Observe] + themeScores[PetMemoryTheme.Explore] * 0.25),
            [BehaviorSemanticCategory.Rest.ToString()] = themeScores[PetMemoryTheme.Rest],
            [BehaviorSemanticCategory.Food.ToString()] = themeScores[PetMemoryTheme.Food],
            [BehaviorSemanticCategory.Drink.ToString()] = themeScores[PetMemoryTheme.Drink]
        };
        var topics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [InitiativeSpeechTopic.Companionship.ToString()] = Math.Min(0.12, themeScores[PetMemoryTheme.Companionship] * 1.2),
            [InitiativeSpeechTopic.Play.ToString()] = Math.Min(0.12, themeScores[PetMemoryTheme.Play] + themeScores[PetMemoryTheme.Explore] * 0.35),
            [InitiativeSpeechTopic.Curiosity.ToString()] = Math.Min(0.12, themeScores[PetMemoryTheme.Observe] + themeScores[PetMemoryTheme.Explore] * 0.45),
            [InitiativeSpeechTopic.Hunger.ToString()] = Math.Min(0.12, themeScores[PetMemoryTheme.Food] * 1.2),
            [InitiativeSpeechTopic.Thirst.ToString()] = Math.Min(0.12, themeScores[PetMemoryTheme.Drink] * 1.2),
            [InitiativeSpeechTopic.Rest.ToString()] = Math.Min(0.12, themeScores[PetMemoryTheme.Rest] * 1.2)
        };
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", items.Select(item =>
                $"{item.Source}|{item.EvidenceId}|{item.ObservedAt:O}|{item.Confidence:R}|{item.Text}"))))).ToLowerInvariant();
        return new PetDecisionMemoryProfile(categories, topics, counts, generatedAt, fingerprint).Clamp();
    }

    private static IEnumerable<PetMemoryTheme> MatchThemes(PetMemoryEvidence evidence)
    {
        foreach (var pair in Keywords)
        {
            if (pair.Value.Any(keyword => evidence.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                yield return pair.Key;
        }
    }

    private static double EvidencePolarity(string text, PetMemoryTheme theme)
    {
        // Retrieval labels are not evidence. Explicit nearby aversion wins over
        // incidental mentions; ambiguous stories remain a weak familiarity signal.
        foreach (var keyword in Keywords[theme])
        {
            foreach (var prefix in new[] { "不喜欢", "讨厌", "不想", "不爱", "不愿意" })
                if (text.Contains(prefix + keyword, StringComparison.OrdinalIgnoreCase))
                    return -1;
        }
        return text.Contains("喜欢", StringComparison.Ordinal) || text.Contains("开心", StringComparison.Ordinal)
            ? 1
            : 0.35;
    }
}
