using System.Text.Json;
using System.Reflection;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class ProneRuntimePromotionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Ids = FrontProneExpressionBehaviorIds.All.Append(ProneHappyHotPantingBehaviorIds.HappyHotPanting).ToArray();
    public static void NormalGateAndRecovery()
    {
        foreach (var id in Ids)
        {
            var now=DateTimeOffset.Now;
            var runtime=new DesktopRuntimeHost(now:()=>now);
            SetFront(runtime);
            runtime.StartIdle("test_front");
            now=now.AddMinutes(2);
            PetMotionRequest? request=null;
            runtime.MotionRequested+=(_,value)=>request=value;
            Check(Submit(runtime,id)==PetActionResult.Accepted,"approved Normal request rejected: "+id);
            Check(request is {ExecutionMode:BehaviorExecutionMode.Normal,Source:BehaviorRequestSource.AutonomousTick},"prototype path used");
            var active=request!;
            runtime.CompleteMotion(active.RequestId,active.Motion.BehaviorId,"microexpression");
            Check(runtime.CurrentBehaviorId==LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,"wrong recovery pose");
            Check(!runtime.AgentStateSnapshot.Runtime.IsBusy && runtime.AgentStateSnapshot.Runtime.CurrentPoseId=="prone.awake.front","busy/pose leak");
            var snapshot=JsonSerializer.Serialize(runtime.AgentStateSnapshot);
            runtime.CompleteMotion(active.RequestId,active.Motion.BehaviorId,"late");
            Check(JsonSerializer.Serialize(runtime.AgentStateSnapshot)==snapshot,"duplicate outcome settled twice");
            Check(Submit(runtime,id)==PetActionResult.Deferred,"shared cooldown bypassed");
            runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,PetRuntimeState.Default with
                {CurrentPosture=StablePosture.Prone,CurrentPoseId="prone.awake.left_front"},RelationshipState.Default,1);
            now=now.AddMinutes(3);
            Check(Submit(runtime,id)==PetActionResult.Deferred,"side pose hard-cut to front");
        }
    }
    public static void SchedulingAndSidePreference()
    {
        var now=DateTimeOffset.Now;
        var runtime=new DesktopRuntimeHost(now:()=>now);
        SetFront(runtime);
        runtime.StartIdle("front_test");
        var method=typeof(DesktopRuntimeHost).GetMethod("BuildAutonomousEpisodeBindings",Private)!;
        foreach(var episode in new[]{PetEpisodeKind.Resting,PetEpisodeKind.Observing})
        {
            var allowed=(IReadOnlySet<string>)method.Invoke(runtime,new object[]{episode})!;
            Check(Ids.All(allowed.Contains),"enabled front actions not bound to episode");
        }
        typeof(DesktopRuntimeHost).GetField("_nextFrontProneExpressionAt",Private)!.SetValue(runtime,now.AddSeconds(45));
        var cooled=(IReadOnlySet<string>)method.Invoke(runtime,new object[]{PetEpisodeKind.Observing})!;
        Check(!Ids.Any(cooled.Contains),"authoritative selector ignored shared cooldown");
        var preferences=AutonomousBehaviorPreferences.Default;
        Check(DesktopRuntimeHost.ShouldKeepCurrentStableIdle(LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1,
            LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1), "side idle became a finite busy execution");
        var capabilityCatalog=DesktopBehaviorCapabilityCatalog.Create(DesktopMotionCatalog.Load(AppContext.BaseDirectory).Motions);
        var engine=new BehaviorDecisionEngine();
        var selected=new HashSet<string>();
        foreach(var episode in new[]{PetEpisodeKind.Resting,PetEpisodeKind.Observing})
        for(var seed=0;seed<100;seed++)
        {
            var state=runtime.AgentStateSnapshot with
            {
                Episode=new PetEpisodeState(episode,now.AddMinutes(-2),TimeSpan.Zero,"test"),
                Runtime=runtime.AgentStateSnapshot.Runtime with {Curiosity=0.95,Focus=0.8,Arousal=0.6}
            };
            var weights=capabilityCatalog.Capabilities.ToDictionary(x=>x.BehaviorId,x=>DesktopRuntimeHost.AutonomousBehaviorWeightFor(x.BehaviorId,preferences));
            var result=engine.Decide(state,capabilityCatalog,new BehaviorDecisionInput(BehaviorRequestSource.AutonomousTick,
                now,LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,now.AddMinutes(-2),true,new Dictionary<string,DateTimeOffset>(),
                Array.Empty<string>(),seed,false,true,Ids.ToHashSet(),weights));
            Check(result.SelectedBehaviorId is not null && Ids.Contains(result.SelectedBehaviorId),"approved front microevents not selectable");
            selected.Add(result.SelectedBehaviorId!);
        }
        Check(selected.Count>=2,"microevent choice collapsed to one action");
        Check(DesktopRuntimeHost.AutonomousBehaviorWeightFor(LifecycleCandidateBehaviorIds.LivelyDailyP2,preferences)<
            DesktopRuntimeHost.AutonomousBehaviorWeightFor(AutonomousDailyCandidateBehaviorIds.StandToSit,preferences)*0.25,
            "side lifecycle preference not reduced");
        for(var seed=0;seed<100;seed++)
            Check(DesktopRuntimeHost.ChooseAutonomousProneLoopCycles(new Random(seed)) is >=1 and <=2,"long backward-looking hold restored");
        var catalog=DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        foreach(var id in new[]{LifecycleCandidateBehaviorIds.LivelyDailyP2,LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1})
        {
            var exit=catalog.Find(id)!.Phases.First(x=>x.Name=="exit");
            Check(!exit.Frames.First().Contains("14-stable-prone",StringComparison.OrdinalIgnoreCase),"backward anchor reinserted in exit");
        }
    }
    private static void SetFront(DesktopRuntimeHost runtime)=>runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,
        PetRuntimeState.Default with{CurrentPosture=StablePosture.Prone,CurrentPoseId="prone.awake.front",Energy=0.75,MoodValence=0.8,Stress=0.1},RelationshipState.Default,1);
    private static PetActionResult Submit(DesktopRuntimeHost runtime,string id)=>(PetActionResult)typeof(DesktopRuntimeHost)
        .GetMethod("SubmitBehavior",Private)!.Invoke(runtime,new object[]{BehaviorRequestSource.AutonomousTick,id,"test",-5,BehaviorExecutionMode.Normal,false})!;
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
