using System.IO;
using System.Windows.Controls;
using Wukong.Application;

namespace Wukong.Desktop;

public sealed class PosturePolicyEditor
{
    public StablePosture Posture { get; init; }
    public string Name => Posture switch { StablePosture.Stand => "站立", StablePosture.Sit => "坐着", _ => "趴着" };
    public double MinimumSeconds { get; set; }
    public double MaximumSeconds { get; set; }
    public double IdleWeight { get; set; }
}

public sealed record CompanionTimeZoneChoice(string Id, string Name);

public partial class ControlPanelWindow
{
    private PosturePolicyEditor[] _posturePolicyRows = Array.Empty<PosturePolicyEditor>();
    private void ApplyPolicyToUi(AutonomyPolicyProfile policy)
    {
        _posturePolicyRows = Enum.GetValues<StablePosture>().Select(posture => new PosturePolicyEditor
        {
            Posture = posture,
            MinimumSeconds = policy.PostureFor(posture).MinimumDwell.TotalSeconds,
            MaximumSeconds = policy.PostureFor(posture).MaximumDwell.TotalSeconds,
            IdleWeight = policy.PostureFor(posture).IdlePreferenceWeight
        }).ToArray();
        PosturePolicyGrid.ItemsSource = _posturePolicyRows;
        InitiativeEnabledCheck.IsChecked = policy.Speech.Enabled;
        RebelliousnessSlider.Value = policy.Commands.Rebelliousness * 100;
        InitiativeFrequencySlider.Value = policy.Speech.FrequencyMultiplier;
        QuietHoursCheck.IsChecked = policy.Speech.QuietHoursEnabled;
        QuietStartCombo.ItemsSource = Enumerable.Range(0, 24);
        QuietEndCombo.ItemsSource = Enumerable.Range(0, 24);
        QuietStartCombo.SelectedItem = policy.Speech.QuietStartHour;
        QuietEndCombo.SelectedItem = policy.Speech.QuietEndHour;
        SpeechBudgetText.Text = policy.Speech.EightHourBudget.ToString();
        StartWithWindowsCheck.IsChecked = policy.Presence.StartWithWindows;
        StartupGreetingCheck.IsChecked = policy.Presence.StartupGreetingEnabled;
        LongAbsenceGreetingCheck.IsChecked = policy.Presence.LongAbsenceGreetingEnabled;
        LateNightCareCheck.IsChecked = policy.Presence.LateNightCareEnabled;
        LongAbsenceHoursText.Text = policy.Presence.LongAbsenceHours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        UseDeviceTimeZoneCheck.IsChecked = policy.Time.UseDeviceTimeZone;
        ManualTimeZoneCombo.ItemsSource = CompanionTimeZones(policy.Time.ManualTimeZoneId);
        ManualTimeZoneCombo.SelectedValue = policy.Time.ManualTimeZoneId;
        if (ManualTimeZoneCombo.SelectedIndex < 0) ManualTimeZoneCombo.Text = policy.Time.ManualTimeZoneId;
        UpdateCompanionTimeControls(policy.Time);
        AutonomyPolicyStatusText.Text = _runtime.AutonomyPolicyStatus.Contains("preserved", StringComparison.Ordinal)
            ? "策略文件有误，原文件已保留，当前使用默认值。" : "生活节奏已载入";
        AutonomyPolicyStatusText.ToolTip = Path.Combine(_agent.DataPaths.AgentDirectory, "autonomy-policy.json");
    }

    private AutonomyPolicyProfile ReadPolicyFromUi(AutonomousBehaviorPreferences preferences)
    {
        if (!PosturePolicyGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !PosturePolicyGrid.CommitEdit(DataGridEditingUnit.Row, true))
            throw new ArgumentException("请检查停留时间和偏好的数值");
        if (!int.TryParse(SpeechBudgetText.Text, out var budget) || QuietStartCombo.SelectedItem is not int start || QuietEndCombo.SelectedItem is not int end)
            throw new ArgumentException("请填写发言次数和安静时段");
        if (!double.TryParse(LongAbsenceHoursText.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var longAbsenceHours))
            throw new ArgumentException("请填写久别判定小时数");
        var manualTimeZone = ManualTimeZoneCombo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(manualTimeZone)) manualTimeZone = ManualTimeZoneCombo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(manualTimeZone)) manualTimeZone = CompanionClock.AucklandIanaId;
        var current = _runtime.AutonomyPolicy;
        var postures = new Dictionary<StablePosture, StablePostureAutonomyPolicy>(current.Postures);
        foreach (var row in _posturePolicyRows)
        {
            if (!double.IsFinite(row.MinimumSeconds) || !double.IsFinite(row.MaximumSeconds) ||
                row.MinimumSeconds < 1 || row.MaximumSeconds > 86400 || row.MaximumSeconds <= row.MinimumSeconds)
                throw new ArgumentException("最长停留必须大于最短停留，范围为 1–86400 秒");
            var previous = current.PostureFor(row.Posture);
            var delayMinimum = Math.Max(row.MinimumSeconds, previous.DecisionDelayMinimum.TotalSeconds);
            postures[row.Posture] = new(TimeSpan.FromSeconds(row.MinimumSeconds), TimeSpan.FromSeconds(row.MaximumSeconds),
                TimeSpan.FromSeconds(delayMinimum), TimeSpan.FromSeconds(Math.Max(delayMinimum + 1, previous.DecisionDelayMaximum.TotalSeconds)), row.IdleWeight);
        }
        var result = current with
        {
            Postures = postures, OwnerPreferences = preferences,
            Commands = current.Commands with { Rebelliousness = RebelliousnessSlider.Value / 100 },
            Time = current.Time with
            {
                UseDeviceTimeZone = UseDeviceTimeZoneCheck.IsChecked == true,
                ManualTimeZoneId = manualTimeZone
            },
            Presence = current.Presence with
            {
                StartWithWindows = StartWithWindowsCheck.IsChecked == true,
                StartupGreetingEnabled = StartupGreetingCheck.IsChecked == true,
                LongAbsenceGreetingEnabled = LongAbsenceGreetingCheck.IsChecked == true,
                LongAbsenceHours = longAbsenceHours,
                LateNightCareEnabled = LateNightCareCheck.IsChecked == true
            },
            Speech = current.Speech with
            {
                Enabled = InitiativeEnabledCheck.IsChecked == true,
                FrequencyMultiplier = InitiativeFrequencySlider.Value,
                QuietHoursEnabled = QuietHoursCheck.IsChecked == true,
                QuietStartHour = start, QuietEndHour = end,
                EightHourBudget = budget, UnansweredBudget = Math.Min(budget, current.Speech.UnansweredBudget)
            }
        };
        if (result.Validate().Count != 0) throw new ArgumentException("请检查停留时间、待机偏好（0.05–2）和发言次数（1–24）");
        return result;
    }

    private static IReadOnlyList<CompanionTimeZoneChoice> CompanionTimeZones(string configured)
    {
        var local = TimeZoneInfo.Local;
        var choices = new[]
        {
            new CompanionTimeZoneChoice(local.Id, $"当前设备：{local.DisplayName}"),
            new CompanionTimeZoneChoice(CompanionClock.AucklandIanaId, "新西兰奥克兰"),
            new CompanionTimeZoneChoice("China Standard Time", "中国标准时间"),
            new CompanionTimeZoneChoice("Tokyo Standard Time", "日本标准时间"),
            new CompanionTimeZoneChoice("GMT Standard Time", "英国时间"),
            new CompanionTimeZoneChoice("Eastern Standard Time", "美国东部时间")
        }.GroupBy(choice => choice.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        if (!choices.Any(choice => string.Equals(choice.Id, configured, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new CompanionTimeZoneChoice(configured, configured));
        return choices;
    }

    private void UseDeviceTimeZoneCheck_Changed(object sender, System.Windows.RoutedEventArgs e) =>
        UpdateCompanionTimeControls(_runtime.AutonomyPolicy.Time with
        {
            UseDeviceTimeZone = UseDeviceTimeZoneCheck.IsChecked == true,
            ManualTimeZoneId = ManualTimeZoneCombo?.SelectedValue as string ?? ManualTimeZoneCombo?.Text ?? CompanionClock.AucklandIanaId
        });

    private void UpdateCompanionTimeControls(CompanionTimeOptions options)
    {
        if (ManualTimeZoneCombo is null || CompanionTimeStatusText is null) return;
        ManualTimeZoneCombo.IsEnabled = !options.UseDeviceTimeZone;
        var effective = options.UseDeviceTimeZone ? TimeZoneInfo.Local : CompanionClock.Resolve(options.ManualTimeZoneId);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, effective);
        CompanionTimeStatusText.Text = $"当前按 {effective.DisplayName} · {now:yyyy-MM-dd HH:mm}";
    }

    private string ApplyWindowsStartupPreference(AutonomyPolicyProfile policy)
    {
        try
        {
            var result = new WindowsStartupRegistration().Synchronize(policy.Presence.StartWithWindows);
            return result.Status switch
            {
                "enabled" => " · 已开启开机启动",
                "disabled" => " · 已关闭开机启动",
                "publish_executable_required" => " · 发布版首次运行时应用开机启动",
                _ => " · 开机启动设置暂未应用"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $" · 开机启动设置失败：{ex.GetType().Name}";
        }
    }
}
