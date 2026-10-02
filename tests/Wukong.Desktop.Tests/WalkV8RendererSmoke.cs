using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wukong.Desktop;
using Wukong.Domain;
using Wukong.Application;

internal static class WalkV8RendererSmoke
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public static int Run(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        Exception? error = null;
        var evidence = new List<object>();
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new MainWindow();
            app.MainWindow = window;
            window.Show();
            window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await (Task)typeof(MainWindow).GetField("_agentStateLoadTask", Private)!.GetValue(window)!;
                    await Task.Delay(1000);
                    var runtime = (DesktopRuntimeHost)typeof(MainWindow).GetField("_runtime", Private)!.GetValue(window)!;
                    foreach (var name in new[] { "_autonomousTimer", "_initiativeSpeechTimer" })
                        ((DispatcherTimer)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!).Stop();
                    var image = (Image)window.FindName("PetImage");
                    var facing = (ScaleTransform)window.FindName("PetFacingTransform");
                    var cases = new[] { ("left", false, false), ("right", false, false), ("left", true, false), ("right", false, true) };
                    foreach (var (direction, stop, normal) in cases)
                    {
                        var area = SystemParameters.WorkArea;
                        window.Left = area.Left + (area.Width - window.Width) / 2;
                        window.Top = area.Top + Math.Max(0, area.Height - window.Height - 40);
                        var originX = window.Left;
                        var id = direction == "left" ? PatrolWalkCandidateBehaviorIds.WalkLeft : PatrolWalkCandidateBehaviorIds.WalkRight;
                        var result = normal ? StartAutonomousWalk(runtime, id) : await runtime.SubmitDeveloperCandidateMotionAsync(id);
                        PatrolWalkCandidateTests.Assert(result == PetActionResult.Accepted, "renderer preview gate failed");
                        var active = (PetMotionRequest)typeof(MainWindow).GetField("_activeRequest", Private)!.GetValue(window)!;
                        var seen = new HashSet<string>();
                        var phases = new HashSet<string>();
                        var started = DateTimeOffset.UtcNow;
                        var previousX = window.Left;
                        Task? stopping = null;
                        while (runtime.CurrentBehaviorId == id && (DateTimeOffset.UtcNow - started).TotalSeconds < 16)
                        {
                            PatrolWalkCandidateTests.Assert(facing.ScaleX == (direction == "right" ? -1 : 1), "render transform disagrees with direction");
                            PatrolWalkCandidateTests.Assert(image.Source is BitmapSource { IsFrozen: true }, "renderer image is not a frozen bitmap");
                            PatrolWalkCandidateTests.Assert(Math.Abs(window.ActionLocalScale - 0.68) < 0.001, "phase changed local scale");
                            PatrolWalkCandidateTests.Assert(direction == "left" ? window.Left <= previousX + 1 : window.Left >= previousX - 1, "window moved against gait");
                            previousX = window.Left;
                            phases.Add(runtime.CurrentPhase);
                            if (seen.Add(runtime.CurrentAsset))
                                Capture(window, Path.Combine(output, $"{direction}-{(normal ? "normal" : stop ? "stop" : "full")}-{seen.Count:00}.png"));
                            if (stop && stopping is null && runtime.CurrentPhase == "loop")
                            {
                                var method = typeof(MainWindow).GetMethod("StopCurrentBehaviorAsync", Private)!;
                                stopping = (Task)method.Invoke(window, new object[] { "renderer_stop" })!;
                                await (Task)method.Invoke(window, new object[] { "duplicate_renderer_stop" })!;
                            }
                            await Task.Delay(25);
                        }
                        if (stopping is not null) await stopping;
                        PatrolWalkCandidateTests.Assert(runtime.CurrentBehaviorId != id, "walking did not settle");
                        PatrolWalkCandidateTests.Assert(phases.SetEquals(new[] { "intro", "loop", "exit" }), "renderer did not play full lifecycle");
                        PatrolWalkCandidateTests.Assert(seen.Count == 13, "renderer did not show all canonical frames");
                        PatrolWalkCandidateTests.Assert(Math.Abs(previousX - originX) > 30, "walking remained in place");
                        PatrolWalkCandidateTests.Assert(!runtime.AgentStateSnapshot.Runtime.IsBusy, "walking leaked busy state");
                        if (normal)
                        {
                            PatrolWalkCandidateTests.Assert(runtime.CurrentStablePosture == StablePosture.Stand && facing.ScaleX == -1, "normal walk did not preserve standing/facing");
                            PatrolWalkCandidateTests.Assert(active.ExecutionMode == BehaviorExecutionMode.Normal && active.Source == BehaviorRequestSource.AutonomousTick, "normal window run bypassed autonomy");
                            PatrolWalkCandidateTests.Assert(!(bool)typeof(MainWindow).GetField("_suspendAnimationFrames", Private)!.GetValue(window)!, "normal walk left idle animation suspended");
                        }
                        evidence.Add(new { direction, stop, normal, frames = seen.Count, phases, displacement = previousX-originX,
                            elapsed_ms = (DateTimeOffset.UtcNow-started).TotalMilliseconds, active.RequestId });
                    }
                    var catalog = runtime.Motions.Select(x => new
                    {
                        x.BehaviorId, x.AssetBatch, x.Direction, x.SupportsHorizontalMirror, x.IsExpired, x.RuntimeEnabled,
                        reason = x.SupportsHorizontalMirror ? "shared whole-frame transform; locked per request"
                            : x.IsExpired ? "expired" : x.Effect != DesktopMotionEffect.None ? "effect or native directions"
                            : "front-only or not approved for mirror"
                    });
                    File.WriteAllText(Path.Combine(output, "mirror-audit.json"), JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
                    File.WriteAllText(Path.Combine(output, "renderer.json"), JsonSerializer.Serialize(new { passed=true, evidence }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex) { error = ex; }
                finally { window.Close(); app.Shutdown(); }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        Console.WriteLine($"WPF walk/mirror renderer smoke passed: {output}");
        return 0;
    }

    private static PetActionResult StartAutonomousWalk(DesktopRuntimeHost runtime, string id)
    {
        for (var seed=0; seed<100; seed++)
        {
            var now = DateTimeOffset.Now;
            runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,
                PetRuntimeState.Default with { CurrentPosture=StablePosture.Stand, Energy=0.85, Boredom=0.9, Stress=0.05 }, RelationshipState.Default, seed);
            runtime.StartIdle("renderer_normal_fixture");
            typeof(DesktopRuntimeHost).GetField("_petAgentState", Private)!.SetValue(runtime,
                runtime.AgentStateSnapshot with { Episode=new PetEpisodeState(PetEpisodeKind.Exploring, now, TimeSpan.FromMinutes(2), "renderer_fixture") });
            typeof(DesktopRuntimeHost).GetField("_currentStartedAt", Private)!.SetValue(runtime, now-TimeSpan.FromSeconds(60));
            typeof(DesktopRuntimeHost).GetField("_nextAutonomousDecisionAt", Private)!.SetValue(runtime, now-TimeSpan.FromSeconds(1));
            runtime.UpdatePatrolTravelSpace(0, 1000, 50);
            runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
            if (runtime.CurrentBehaviorId == id) return PetActionResult.Accepted;
        }
        throw new InvalidOperationException("normal autonomous renderer fixture did not select walking");
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var target = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(window);
        var pixels = new byte[target.PixelWidth*target.PixelHeight*4];
        target.CopyPixels(pixels, target.PixelWidth*4, 0);
        PatrolWalkCandidateTests.Assert(pixels.Where((_, i) => i%4 == 3).Count(x => x > 16) > 1000, "rendered pet is blank");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
