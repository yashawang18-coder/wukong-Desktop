namespace Wukong.Application;

// Conservative, bounded hints from the owner-authored prompt, never model output.
// Unrecognized prose remains dialogue-only; none of these terms grant a capability.
public static class OwnerPromptAgency
{
    public static double Bias(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return 0;
        var clauses = prompt.Split(new[] { '\n', '\r', '。', '，', '；', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var independent = clauses.Any(x =>
            (x.Contains("有点叛逆", StringComparison.Ordinal) || x.Contains("有自己的主意", StringComparison.Ordinal) ||
             x.Contains("不是言听计从", StringComparison.Ordinal)) &&
            !x.Contains("不要", StringComparison.Ordinal) && !x.Contains("不能", StringComparison.Ordinal));
        return independent ? .20 : 0;
    }
}
