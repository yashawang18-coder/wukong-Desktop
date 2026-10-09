using System.Reflection;
using System.Windows;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class PanelInteractionTests
{
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void PreparedWalkAndSleepStayUnrecorded()
    {
        var runtime = new DesktopRuntimeHost();
        runtime.UpdatePatrolTravelSpace(1000, 1000, 50);
        var before = runtime.AgentStateSnapshot;
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);
        var result = runtime.SubmitBaseMotionAsync(PatrolWalkCandidateBehaviorIds.WalkRight, BehaviorRequestSource.ControlPanel).Result;
        Assert(result == PetActionResult.Accepted, "panel walk preparation failed: " + runtime.CurrentReason);
        Drain(runtime, requests);
        var finite = requests.Where(request => request.TracksAgentLifecycle).ToArray();
        Assert(finite.Last().Motion.BehaviorId == PatrolWalkCandidateBehaviorIds.WalkRight, "prepared route did not reach walk");
        Assert(finite.Length >= 2 && finite.All(request => request.Source == BehaviorRequestSource.ControlPanel), "preparation lost panel source");
        Assert(finite.Last().Motion.WindowMotionEnabled, "walking has no window translation");
        AssertUnrecorded(before, runtime);
        requests.Clear();
        result = runtime.SubmitBaseMotionAsync(SleepCandidateBehaviorIds.MainLifecycle, BehaviorRequestSource.ControlPanel).Result;
        Assert(result == PetActionResult.Accepted, "panel sleep preparation failed: " + runtime.CurrentReason);
        Drain(runtime, requests);
        Assert(requests.Any(request => request.Motion.BehaviorId == SleepCandidateBehaviorIds.MainLifecycle), "sleep missing");
        Assert(requests.Any(request => request.Motion.BehaviorId == WakeRiseCandidateBehaviorIds.SideWake && request.Source == BehaviorRequestSource.ControlPanel), "wake leaked autonomous source");
        AssertUnrecorded(before, runtime);
    }

    public static void PanelCommandsAndFailuresAreIsolated()
    {
        var runtime = new DesktopRuntimeHost();
        var before = runtime.AgentStateSnapshot;
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);
        var result = runtime.SubmitOwnerCommandAsync("手", BehaviorRequestSource.ControlPanel).Result;
        Assert(result == PetActionResult.Accepted, "panel command failed: " + runtime.CurrentReason);
        Drain(runtime, requests);
        AssertUnrecorded(before, runtime);
        runtime.UpdatePatrolTravelSpace(0, 0, 50);
        Assert(runtime.SubmitBaseMotionAsync(PatrolWalkCandidateBehaviorIds.WalkLeft, BehaviorRequestSource.ControlPanel).Result == PetActionResult.Deferred,
            "blocked walk should not tread in place");
        runtime.UpdatePatrolTravelSpace(1000, 1000, 50);
        requests.Clear();
        Assert(runtime.SubmitBaseMotionAsync(PatrolWalkCandidateBehaviorIds.WalkLeft, BehaviorRequestSource.ControlPanel).Result == PetActionResult.Accepted, "walk plan failed");
        var pending = requests.Last();
        runtime.FailMotion(pending.RequestId, pending.Motion.BehaviorId, "test_decode_error");
        runtime.CompleteMotion(pending.RequestId, pending.Motion.BehaviorId, "exit");
        Assert(!runtime.AgentStateSnapshot.Runtime.IsBusy, "failure leaked busy lock");
        Assert(!requests.Any(request => request.Motion.BehaviorId == PatrolWalkCandidateBehaviorIds.WalkLeft), "failed prep still walked");
        AssertUnrecorded(before, runtime);
    }

    public static void ChatCentersAndSpeechDoesNotRepeat()
    {
        var workArea = new Rect(-1920, 0, 1920, 1080);
        var pet = new Rect(-70, 880, 64, 120);
        var chatSize = new Size(320, 48);
        var adjusted = DesktopChatPlacement.MakeRoomBelow(workArea, pet, chatSize);
        var chat = DesktopChatPlacement.Place(workArea, adjusted, chatSize);
        Assert(Math.Abs(chat.X + chatSize.Width/2 - adjusted.Left - adjusted.Width/2) < .001, "chat is diagonal at screen edge");
        Assert(chat.Y >= adjusted.Bottom && chat.Y + chatSize.Height < workArea.Bottom, "chat not below visible pet");
        string? previous = null;
        for (var seed = 0; seed < 100; seed++)
        {
            var next = InitiativeSpeechSchedule.SelectMessage(new Random(seed), InitiativeSpeechTopic.Companionship,
                StablePosture.Prone, previousMessage: previous);
            Assert(next != previous && next.Length <= 30, "initiative repeated or exceeded short reply budget");
            previous = next;
        }
    }

    public static void PetDebugClearPreservesOwnerMemory()
    {
        var oldRoot = Environment.GetEnvironmentVariable("WUKONG_DATA_ROOT");
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wukong-panel-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("WUKONG_DATA_ROOT", temp);
            using var agent = DesktopAgentRuntime.CreateDefault();
            var turn = new[] { new AgentChatMessage(AgentChatRole.User, "今天想去草地", DateTimeOffset.Now),
                new AgentChatMessage(AgentChatRole.Assistant, "想去玩啦", DateTimeOffset.Now) };
            agent.History.ReplaceAsync("model-debug-pet", turn).GetAwaiter().GetResult();
            agent.History.ReplaceAsync(DesktopAgentRuntime.DailySessionId, turn).GetAwaiter().GetResult();
            var candidate = agent.Conversation.SaveLatestTurnAsCandidateAsync(DesktopAgentRuntime.DailySessionId).GetAwaiter().GetResult();
            Assert(candidate is not null, "memory candidate creation is not functional");
            agent.Memory.SetStatusAsync(candidate!.Id, ConversationMemoryStatus.Confirmed).GetAwaiter().GetResult();
            agent.ClearPetSettingDebugHistoryAsync().GetAwaiter().GetResult();
            Assert(agent.History.ReadAsync("model-debug-pet").Result.Count == 0, "pet debug was not cleared");
            Assert(agent.History.ReadAsync(DesktopAgentRuntime.DailySessionId).Result.Count == 2, "owner history was cleared");
            Assert(agent.Memory.ReadAsync().Result.Single().Status == ConversationMemoryStatus.Confirmed, "confirmed memory was cleared");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WUKONG_DATA_ROOT", oldRoot);
            if (System.IO.Directory.Exists(temp)) System.IO.Directory.Delete(temp, true);
        }
    }

    private static void Drain(DesktopRuntimeHost runtime, List<PetMotionRequest> requests)
    {
        for (var i = 0; i < 12; i++)
        {
            var request = requests.Last();
            if (!request.ReturnToIdle) return;
            runtime.CompleteMotion(request.RequestId, request.Motion.BehaviorId, request.Motion.Phases.Last().Name);
            if (requests.Last().RequestId == request.RequestId) return;
        }
        throw new InvalidOperationException("panel sequence did not settle");
    }

    private static void AssertUnrecorded(PetAgentState before, DesktopRuntimeHost runtime)
    {
        var after = runtime.AgentStateSnapshot;
        Assert(before.Temperament == after.Temperament && before.Relationship == after.Relationship, "panel changed personality or relationship");
        Assert(before.RecentExperience.SequenceEqual(after.RecentExperience), "panel wrote experience");
        Assert(before.Preferences.SequenceEqual(after.Preferences), "panel wrote learned preference");
        Assert(before.Runtime.Energy == after.Runtime.Energy && before.Runtime.Hunger == after.Runtime.Hunger &&
            before.Runtime.MoodValence == after.Runtime.MoodValence && before.Runtime.LastActionId == after.Runtime.LastActionId, "panel applied behavioral effects");
        var statistics = (System.Collections.IDictionary)typeof(DesktopRuntimeHost).GetField("_behaviorTriggerStatistics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        Assert(statistics.Count == 0, "panel affected trigger statistics");
    }
}
