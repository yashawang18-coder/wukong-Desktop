using Wukong.Application;
using Wukong.Desktop;
using Wukong.Infrastructure;
using System.IO;

internal static class CompanionExperienceTests
{
    public static void DeviceAndManualTimeZonesStayExplicit()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var auckland = CompanionClock.ToCompanionTime(instant, new CompanionTimeOptions
        {
            UseDeviceTimeZone = false,
            ManualTimeZoneId = CompanionClock.AucklandIanaId
        });
        Assert(auckland.Hour == 13, $"Auckland DST conversion drifted: {auckland:O}");
        var device = CompanionClock.ToCompanionTime(instant, new CompanionTimeOptions { UseDeviceTimeZone = true });
        Assert(device.Offset == TimeZoneInfo.Local.GetUtcOffset(instant), "device time zone was not used");
    }

    public static void StartupAbsenceAndLateNightCareAreBounded()
    {
        var service = new CompanionGreetingDecisionService();
        var now = new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero);
        var time = new CompanionTimeOptions { UseDeviceTimeZone = false, ManualTimeZoneId = "UTC" };
        var presence = new CompanionPresenceOptions();
        var session = new CompanionSessionState { LastActiveAtUtc = now.AddHours(-30) };
        var longAbsence = service.Decide(Context(now, true, session, time, presence));
        Assert(longAbsence.Kind == CompanionGreetingKind.LongAbsence && longAbsence.CountsAsLateNightCare,
            "long absence did not win startup greeting or combine late-night care");
        Assert(longAbsence.Text.Contains("想你", StringComparison.Ordinal), "long absence greeting lost the missing-you intent");

        var firstNight = service.Decide(Context(now, false, new CompanionSessionState(), time, presence));
        Assert(firstNight.Kind == CompanionGreetingKind.LateNightCare, "late-night care did not trigger");
        var sameNight = service.Decide(Context(now.AddMinutes(20), false,
            new CompanionSessionState { LastLateNightCareAtUtc = now }, time, presence));
        Assert(!sameNight.ShouldSpeak, "late-night care repeated in the same night");

        var quietStartup = service.Decide(Context(new DateTimeOffset(2026, 10, 9, 1, 0, 0, TimeSpan.Zero), true,
            new CompanionSessionState(), time, presence with { LateNightCareEnabled = false }));
        Assert(!quietStartup.ShouldSpeak && quietStartup.ReasonCode == "quiet_hours",
            "ordinary startup greeting ignored quiet hours");
    }

    public static void SessionStoreAndWindowsStartupPreferencePersist()
    {
        var root = Path.Combine(Path.GetTempPath(), "wukong-companion-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileCompanionSessionStateStore(root);
            var expected = new CompanionSessionState
            {
                LastActiveAtUtc = new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero),
                LastLateNightCareAtUtc = new DateTimeOffset(2026, 10, 8, 23, 30, 0, TimeSpan.Zero),
                LaunchCount = 4
            };
            store.SaveAsync(expected).GetAwaiter().GetResult();
            var actual = store.LoadAsync().GetAwaiter().GetResult();
            Assert(actual == expected.Clamp(), "companion session did not round-trip atomically");

            var executable = Path.Combine(root, "Wukong.Desktop.exe");
            File.WriteAllBytes(executable, new byte[] { 0x4D, 0x5A });
            var fake = new MemoryStartupStore();
            var startup = new WindowsStartupRegistration(fake);
            var enabled = startup.Synchronize(true, executable);
            Assert(enabled.Enabled && fake.Value == WindowsStartupRegistration.BuildCommand(executable),
                "first-run startup registration was not written");
            var disabled = startup.Synchronize(false, executable);
            Assert(!disabled.Enabled && fake.Value is null, "owner opt-out did not remove the startup registration");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static CompanionGreetingContext Context(
        DateTimeOffset now,
        bool startup,
        CompanionSessionState session,
        CompanionTimeOptions time,
        CompanionPresenceOptions presence) =>
        new(now, startup, session, time, presence, new InitiativeSpeechOptions(),
            PetDecisionMemoryProfile.Empty, StablePosture.Prone, 0);

    private sealed class MemoryStartupStore : IStartupRegistrationStore
    {
        public string? Value { get; private set; }
        public string? Read(string valueName) => Value;
        public void Write(string valueName, string command) => Value = command;
        public void Delete(string valueName) => Value = null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
