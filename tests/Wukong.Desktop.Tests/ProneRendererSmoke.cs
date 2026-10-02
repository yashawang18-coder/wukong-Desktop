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

internal static class ProneRendererSmoke
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
                    var runtime = (DesktopRuntimeHost)typeof(MainWindow).GetField("_runtime", Private)!.GetValue(window)!;
                    foreach (var name in new[] { "_autonomousTimer", "_initiativeSpeechTimer" })
                        ((DispatcherTimer)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!).Stop();
                    var image = (Image)window.FindName("PetImage");
                    foreach (var mode in new[] { BehaviorExecutionMode.DeveloperPreview, BehaviorExecutionMode.Normal })
                    foreach (var id in FrontProneExpressionBehaviorIds.All.Append(ProneHappyHotPantingBehaviorIds.HappyHotPanting))
                    {
                        runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,
                            PetRuntimeState.Default with { CurrentPosture=StablePosture.Prone, CurrentPoseId="prone.awake.front", MoodValence=0.8, Stress=0.1 },
                            RelationshipState.Default, 1);
                        runtime.StartIdle("renderer_front_fixture");
                        var result = mode == BehaviorExecutionMode.DeveloperPreview
                            ? await runtime.SubmitDeveloperCandidateMotionAsync(id)
                            : (PetActionResult)typeof(DesktopRuntimeHost).GetMethod("SubmitBehavior", Private)!.Invoke(runtime,
                                new object[] { BehaviorRequestSource.ControlPanel, id, "renderer_normal", 10, BehaviorExecutionMode.Normal, false })!;
                        Check(result == PetActionResult.Accepted, mode + " request rejected");
                        var seen = new HashSet<string>();
                        var started = DateTimeOffset.UtcNow;
                        while (runtime.CurrentBehaviorId == id && (DateTimeOffset.UtcNow-started).TotalSeconds < 20)
                        {
                            Check(image.Source is BitmapSource { IsFrozen:true }, "image is not frozen");
                            if (seen.Add(runtime.CurrentAsset)) Capture(window, Path.Combine(output, $"{mode}-{id}-{seen.Count:00}.png"));
                            await Task.Delay(20);
                        }
                        var expected = id == ProneHappyHotPantingBehaviorIds.HappyHotPanting ? 17 : 12;
                        Check(seen.Count == expected, $"expected {expected} displayed frame paths, got {seen.Count}");
                        Check(runtime.CurrentBehaviorId == LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, "did not recover front idle");
                        Check(!runtime.AgentStateSnapshot.Runtime.IsBusy, "busy leaked");
                        evidence.Add(new { id, mode=mode.ToString(), frames=seen.Count, elapsed_ms=(DateTimeOffset.UtcNow-started).TotalMilliseconds,
                            recovered_pose=runtime.AgentStateSnapshot.Runtime.CurrentPoseId });
                    }
                    File.WriteAllText(Path.Combine(output,"renderer.json"), JsonSerializer.Serialize(new { passed=true, evidence }, new JsonSerializerOptions { WriteIndented=true }));
                }
                catch (Exception ex) { error=ex; }
                finally { window.Close(); app.Shutdown(); }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        Console.WriteLine("Real WPF prone playback passed: " + output);
        return 0;
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var target=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
        target.Render(window);
        var pixels=new byte[target.PixelWidth*target.PixelHeight*4];
        target.CopyPixels(pixels,target.PixelWidth*4,0);
        Check(pixels.Where((_,i)=>i%4==3).Count(x=>x>16)>1000,"blank pet");
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream=File.Create(path);
        encoder.Save(stream);
    }
}
