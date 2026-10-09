using System.IO;
using System.Text.Json;

namespace Wukong.Desktop;

public sealed record RuntimeAssetAuditRow(string Name, string BehaviorId, string Batch,
    string Visual, string Approval, string Route, string Outcome, string Issue);

public static class RuntimeAssetAudit
{
    public static IReadOnlyList<RuntimeAssetAuditRow> Inspect(IEnumerable<PlayableMotion> motions, PetrifiedCoinAssets? coins = null)
    {
        var all = motions.ToArray();
        var duplicateIds = all.GroupBy(item => item.BehaviorId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count(item => item.RuntimeEnabled && !item.IsExpired) > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = all.Select(motion =>
        {
            var definition = DesktopBehaviorDefinitionCatalog.Find(motion.BehaviorId);
            var issue = duplicateIds.Contains(motion.BehaviorId) ? "ERROR: duplicate_runtime_binding" :
                motion.IsExpired && motion.RuntimeEnabled ? "ERROR: deprecated_runtime_enabled" :
                motion.RuntimeEnabled && definition is null ? "ERROR: missing_behavior_definition" :
                motion.RuntimeEnabled && definition?.AssetBatch is { } batch && batch != motion.AssetBatch ? "ERROR: canonical_batch_mismatch" :
                motion.RuntimeEnabled && definition is { IsStablePresentation: false, Outcome: null } ? "ERROR: missing_outcome" :
                motion.RuntimeEnabled && !motion.RuntimeApproved ? "候选 EXE 临时加载，不代表正式批准" :
                motion.PrototypeUse && !motion.RuntimeApproved ? "仅主人原型展示，Windows 正式验收记录待补齐" : "一致";
            return new RuntimeAssetAuditRow(motion.DisplayName, motion.BehaviorId, motion.AssetBatch,
                motion.VisualApproved ? "已批准" : "未批准", motion.RuntimeApproved ? "已批准" : "未批准",
                motion.IsExpired ? "已过期" : motion.RuntimeEnabled ? "Normal" : motion.PrototypeUse ? "PrototypePreview" : "DeveloperPreview",
                definition?.IsStablePresentation == true ? "稳定显示，不结算" : definition?.Outcome is not null ? "显式 Reducer 配置" : "仅预览，无正式结算",
                issue);
        }).ToList();
        if (coins is not null)
        {
            var metadataPath = Path.Combine(coins.Root, "petrificus_coin", "v19", "asset.json");
            try
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                var root = metadata.RootElement;
                var approved = root.GetProperty("runtime_approved").GetBoolean();
                var use = root.GetProperty("runtime_use").GetBoolean();
                rows.Add(new("石化金币", MagicBehaviorIds.PetrifiedCoin, "petrificus_coin/v19",
                    root.GetProperty("visual_approved").GetBoolean() ? "已批准" : "未批准", approved ? "已批准" : "未批准",
                    "随主人石化原型展示", "继承石化显示生命周期",
                    approved || use ? "ERROR: coin_approval_route_mismatch" : root.GetProperty("runtime_validation").GetString() ?? "pending"));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            { rows.Add(new("石化金币", MagicBehaviorIds.PetrifiedCoin, "v19", "未知", "未知", "未确认", "未确认", "ERROR: coin_metadata_unreadable")); }
        }
        return rows;
    }
}
