using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;
using Wukong.Infrastructure;
using System.IO;
using System.Text.Json;

internal static class PolicyRefinementTests
{
    public static void SpeechCommandsAndAssetAudit()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var context = new InitiativeSpeechContext(PetRuntimeState.Default with { Hunger = .95, Stress = .1 },
            TemperamentProfile.Default, RelationshipState.Default, now, null, true, false, false, false, 45);
        var service = new InitiativeSpeechDecisionService();
        Assert(!service.Decide(context with { Policy = new() { Enabled = false } }).ShouldSpeak, "Disabled speech still speaks");
        var normal = service.Decide(context);
        var slower = service.Decide(context with { Policy = new() { FrequencyMultiplier = .5 } });
        Assert(slower.NextCheck == normal.NextCheck * 2, "Frequency setting did not affect scheduler");
        Assert(new InitiativeSpeechOptions { QuietStartHour = 21, QuietEndHour = 8 }.IsQuietAt(now.AddHours(10)), "Overnight quiet interval broken");
        Assert(!new InitiativeSpeechOptions { QuietStartHour = 21, QuietEndHour = 8 }.IsQuietAt(now), "Daytime incorrectly quiet");
        Assert(!service.Decide(context with { Episode = PetEpisodeKind.Sleeping }).ShouldSpeak, "Policy bypassed sleep gate");
        var motions = DesktopMotionCatalog.Load(AppContext.BaseDirectory).Motions;
        var capability = DesktopBehaviorCapabilityCatalog.Create(motions).Find(MockCommandActionIds.Jump)!;
        var state = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand, CurrentPoseId = "stand.neutral.left_front", Energy = .3, Stress = .1 }
        };
        var policy = new BehaviorParticipationPolicy();
        var refusal = policy.Evaluate(capability, state, BehaviorRequestSource.OwnerContextMenu, now,
            new CommandParticipationOptions { HighEffortMinimumEnergy = .4 });
        Assert(refusal.ReasonCode == "energy_too_low", "Configured command energy threshold ignored");
        var missing = policy.Evaluate(capability with { ProductionApproved = false }, state,
            BehaviorRequestSource.OwnerContextMenu, now, new CommandParticipationOptions { HighAcceptThreshold = .4 });
        Assert(missing.Disposition == RequestDisposition.Deferred && missing.ReasonCode == "capability_unavailable", "Policy bypassed asset approval");
        var coins = PetrifiedCoinAssets.Load(AppContext.BaseDirectory);
        var audit = RuntimeAssetAudit.Inspect(motions, coins);
        Assert(!audit.Any(item => item.Issue.StartsWith("ERROR:")), string.Join("; ", audit.Where(item => item.Issue.StartsWith("ERROR:"))));
        Assert(audit.Single(item => item.BehaviorId == MagicBehaviorIds.PetrifiedCoin).Approval == "未批准", "Coin candidate auto-promoted");
        Assert(!motions.Any(item => item.AssetBatch == "WK-AUTONOMOUS-PATROL-WALK-v1-candidate"), "Old patrol registered alongside v8");
        foreach (var relative in new[] { "petrificus_coin/v18/a.png", "petrificus_coin/v19/../../old.png" })
        {
            try { PetrifiedCoinAssets.ResolveCanonicalCoinPath(coins.Root, relative); throw new InvalidOperationException("Old coin path accepted"); }
            catch (InvalidDataException) { }
        }
    }

    public static void PortableAlbumsSurviveRelocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "wukong-albums-" + Guid.NewGuid().ToString("N"));
        var oldDataRoot = Environment.GetEnvironmentVariable(PortableDataLayout.DataRootEnvironmentVariable);
        var oldAlbums = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        Environment.SetEnvironmentVariable(PortableDataLayout.DataRootEnvironmentVariable, null);
        Environment.SetEnvironmentVariable("WUKONG_ALBUM_ROOT", null);
        try
        {
            var first = Path.Combine(root, "first");
            var album = Path.Combine(first, "WukongDefaults", "albums", "Trip");
            Directory.CreateDirectory(album);
            File.WriteAllText(Path.Combine(album, "album.md"), "---\ntitle: Trip\n---\nA local bundled album.");
            File.WriteAllBytes(Path.Combine(album, "photo.png"), new byte[] { 1, 2, 3 });
            var profileDefaults = Path.Combine(first, "WukongDefaults", "profile");
            Directory.CreateDirectory(profileDefaults);
            File.WriteAllText(Path.Combine(profileDefaults, PortableAlbumBinding.FileName), "albums");
            var layout = PortableDataLayout.Initialize(first);
            var resolved = PortableAlbumBinding.Resolve(layout.AlbumsDirectory, layout.ProfileDirectory);
            Assert(resolved == layout.AlbumsDirectory && File.Exists(Path.Combine(resolved, "Trip", "album.md")), "Bundled first-run album missing");
            PortableAlbumBinding.Save(layout.AlbumsDirectory, layout.ProfileDirectory);
            Assert(File.ReadAllText(Path.Combine(layout.ProfileDirectory, PortableAlbumBinding.FileName)) == "albums", "Portable binding saved absolute path");
            File.Delete(Path.Combine(resolved, "Trip", "photo.png"));
            PortableDataLayout.Initialize(first);
            Assert(!File.Exists(Path.Combine(resolved, "Trip", "photo.png")), "Deleted photo restored on next launch");
            var second = Path.Combine(root, "second");
            Directory.Move(first, second);
            var moved = PortableDataLayout.Initialize(second);
            var movedRoot = PortableAlbumBinding.Resolve(moved.AlbumsDirectory, moved.ProfileDirectory);
            Assert(movedRoot == moved.AlbumsDirectory && File.Exists(Path.Combine(movedRoot, "Trip", "album.md")), "Album broke after moving EXE folder");
            Assert(AlbumFolderItem.GetDefaultAlbumRoot(moved.AlbumsDirectory, moved.ProfileDirectory) == movedRoot, "Panel and memory resolve different albums");
            var external = Path.Combine(root, "personal");
            Directory.CreateDirectory(external);
            PortableAlbumBinding.Save(external, moved.ProfileDirectory);
            PortableDataLayout.Initialize(second);
            Assert(PortableAlbumBinding.Resolve(moved.AlbumsDirectory, moved.ProfileDirectory) == external, "Existing recipient binding overwritten");
            Directory.Delete(external);
            Assert(PortableAlbumBinding.Resolve(moved.AlbumsDirectory, moved.ProfileDirectory) == moved.AlbumsDirectory, "Missing sender absolute path failed to fall back to packaged album");
            File.WriteAllText(Path.Combine(moved.ProfileDirectory, PortableAlbumBinding.FileName), "../outside");
            Assert(PortableAlbumBinding.Resolve(moved.AlbumsDirectory, moved.ProfileDirectory) == moved.AlbumsDirectory, "Relative binding escaped portable root");
        }
        finally
        {
            Environment.SetEnvironmentVariable(PortableDataLayout.DataRootEnvironmentVariable, oldDataRoot);
            Environment.SetEnvironmentVariable("WUKONG_ALBUM_ROOT", oldAlbums);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    public static void SingleSelectorAndReducer()
    {
        PostureDwellSurvivesIdleRefresh();
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var runtime = new DesktopRuntimeHost(now: () => now, rolloutOptions: AutonomousAgentRolloutOptions.ShadowOnly);
        runtime.StartIdle();
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);
        for (var index = 0; index < 240; index++)
        {
            now += TimeSpan.FromSeconds(15);
            runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
        }
        Assert(requests.Count == 0, "Shadow-only mode submitted a legacy or second execution");
        Assert(runtime.AgentStateSnapshot.RecentExperience.Count == 0, "Shadow decision wrote behavior memory");
        BehaviorAgentRolloutTests.TenThousandAutonomousDecisionsNeverSelectForbiddenCapabilities();
        ReducerCompletionRuntimeTests.DuplicateCompletionCannotRewardTwice();
        ReducerCompletionRuntimeTests.LateCompletionAfterStopIsIgnored();
        ReducerCompletionRuntimeTests.FailedExecutionSettlesOnlyOnce();
        ReducerCompletionRuntimeTests.PreviewFailureRestoresWithoutLearning();
        ReducerCompletionRuntimeTests.StableIdleCompletionDoesNotRecordActivity();
        ReducerCompletionRuntimeTests.ThirtyMinuteVirtualContinuityRejectsStaleCallbacks();
    }

    private static void PostureDwellSurvivesIdleRefresh()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var runtime = new DesktopRuntimeHost(now: () => now);
        runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default, PetRuntimeState.Default with
        {
            CurrentPosture = StablePosture.Sit, CurrentPoseId = "sit.neutral.left_front",
            Energy = .62, Arousal = .42, Stress = .12
        }, RelationshipState.Default, 17);
        runtime.StartIdle("test");
        now += TimeSpan.FromSeconds(60);
        runtime.StartIdle("same_posture_refresh");
        now += TimeSpan.FromSeconds(31);
        typeof(DesktopRuntimeHost).GetField("_nextAutonomousDecisionAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(runtime, now);
        PetMotionRequest? selected = null;
        runtime.MotionRequested += (_, request) => selected = request;
        runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
        Assert(selected?.Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.SitToProne,
            "Refreshing the same sitting pose reset its maximum dwell clock");
        runtime.CompleteMotion(selected!.RequestId, selected.Motion.BehaviorId, selected.Motion.Phases.Last().Name);
        Assert(runtime.CurrentStablePosture == StablePosture.Prone, "Posture exit did not settle correctly");
    }

    public static void PolicyRoundTripAndValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "wukong-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var legacy = new AutonomousBehaviorPreferences(1.7, 1.6, 1.3, .3);
            File.WriteAllText(Path.Combine(root, "autonomous-behavior-preferences.json"), JsonSerializer.Serialize(legacy));
            var store = new FileAutonomyPolicyStore(root);
            var initial = store.LoadAsync().GetAwaiter().GetResult();
            Assert(initial.Profile.EffectivePreferences == legacy, "Owner preferences were reset during migration");
            var profile = initial.Profile with
            {
                ProfileId = "roundtrip",
                Speech = initial.Profile.Speech with { QuietStartHour = 21, QuietEndHour = 8 },
                EpisodePostures = new()
                {
                    [PetEpisodeKind.Resting] = new()
                    {
                        [StablePosture.Sit] = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(20), .4)
                    }
                }
            };
            store.SaveAsync(profile).GetAwaiter().GetResult();
            var loaded = store.LoadAsync().GetAwaiter().GetResult();
            Assert(loaded.Status == "loaded", loaded.Status);
            Assert(loaded.Profile.PostureFor(StablePosture.Sit, PetEpisodeKind.Resting).MaximumDwell.TotalSeconds == 35, "Episode policy lost");
            Assert(loaded.Profile.PostureFor(StablePosture.Sit, PetEpisodeKind.Observing).MaximumDwell.TotalSeconds == 90, "Override escaped its Episode");
            var runtime = new DesktopRuntimeHost();
            var before = JsonSerializer.Serialize(runtime.AgentStateSnapshot);
            runtime.UpdateAutonomyPolicy(loaded.Profile);
            Assert(runtime.AutonomyPolicy.ProfileId == "roundtrip", "Runtime did not apply persisted policy");
            Assert(JsonSerializer.Serialize(runtime.AgentStateSnapshot) == before, "Policy editing mutated pet state");
            var bridge = new FileAutonomousBehaviorPreferencesStore(root);
            bridge.SaveAsync(legacy with { WalkingWeight = 1.2 }).GetAwaiter().GetResult();
            var fromBridge = store.LoadAsync().GetAwaiter().GetResult().Profile;
            Assert(fromBridge.ProfileId == "roundtrip" && fromBridge.Speech.QuietStartHour == 21, "Preference edit lost policy fields");
            Assert(fromBridge.EffectivePreferences.WalkingWeight == 1.2, "Preference store is split from policy");
            var invalid = "{\"SchemaVersion\":999}";
            File.WriteAllText(Path.Combine(root, FileAutonomyPolicyStore.FileName), invalid);
            Assert(store.LoadAsync().GetAwaiter().GetResult().Status.StartsWith("invalid_preserved:"), "Invalid policy silently loaded");
            Assert(File.ReadAllText(Path.Combine(root, FileAutonomyPolicyStore.FileName)) == invalid, "Invalid user policy overwritten");
            Assert((profile with { RetryMaximumSeconds = 0 }).Validate().Count > 0, "Invalid retry bounds accepted");
            Assert((profile with { BehaviorMultipliers = new() { ["x"] = double.NaN } }).Validate().Count > 0, "NaN accepted");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void DefinitionsAreSingleAndFailClosed()
    {
        var motions = DesktopMotionCatalog.Load(AppContext.BaseDirectory).Motions;
        var capabilities = DesktopBehaviorCapabilityCatalog.Create(motions);
        foreach (var motion in motions.Where(item => item.RuntimeEnabled && !item.IsExpired))
        {
            var definition = DesktopBehaviorDefinitionCatalog.Find(motion.BehaviorId);
            Assert(definition is not null, $"Enabled motion has no definition: {motion.BehaviorId}");
            Assert(definition!.IsStablePresentation || definition.Outcome is not null, $"Missing outcome: {motion.BehaviorId}");
            Assert(definition.AssetBatch is null || definition.AssetBatch == motion.AssetBatch, $"Wrong batch: {motion.BehaviorId}");
        }
        foreach (var episode in Enum.GetValues<PetEpisodeKind>())
        foreach (var id in DesktopAutonomousEpisodeBindings.For(episode))
        {
            var definition = DesktopBehaviorDefinitionCatalog.Find(id)!;
            Assert(definition.AutonomousAllowed && definition.Episodes.Contains(episode), "Episode projection drifted");
            Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(id), "Runtime allowlist drifted");
        }
        var fake = motions.First() with { BehaviorId = "wk.fake.idle.eat.jump", RuntimeEnabled = true, AutonomousBindingEnabled = true };
        var rejected = DesktopBehaviorCapabilityCatalog.Create(new[] { fake }).Capabilities.Single();
        Assert(!rejected.RuntimeUse && !rejected.AutonomousBindingEnabled, "Name inference enabled unknown behavior");
        Assert(rejected.StateEffects == new PetStateEffects(), "Unknown behavior received guessed rewards");
        var walk = motions.First(item => item.BehaviorId == PatrolWalkCandidateBehaviorIds.WalkLeft);
        var legacy = DesktopBehaviorCapabilityCatalog.Create(new[] { walk with { AssetBatch = "WK-AUTONOMOUS-PATROL-WALK-v1-candidate" } }).Capabilities.Single();
        Assert(!legacy.RuntimeUse, "Historical patrol batch acquired active binding");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(MockCommandActionIds.Jump), "Command jump entered autonomy");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(MockCommandActionIds.Spin), "Command spin entered autonomy");
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
