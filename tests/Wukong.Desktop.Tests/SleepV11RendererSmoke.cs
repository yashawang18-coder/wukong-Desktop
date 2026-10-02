using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class SleepV11RendererSmoke
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
                    var motions = runtime.Motions.Where(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch).ToArray();
                    Check(motions.Length == 7, "sleep v11 catalog is incomplete");

                    foreach (var motion in motions)
                    {
                        var snapshot = JsonSerializer.Serialize(runtime.AgentStateSnapshot);
                        var result = await runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId);
                        Check(result == PetActionResult.Accepted, $"preview rejected {motion.BehaviorId}");
                        var seen = await ObserveMotion(window, runtime, image, motion, output, "preview");
                        Check(JsonSerializer.Serialize(runtime.AgentStateSnapshot) == snapshot, $"preview changed state: {motion.BehaviorId}");
                        evidence.Add(new { motion.BehaviorId, mode = "DeveloperPreview", frames = seen });
                    }

                    foreach (var motion in motions.Where(x => SleepCandidateBehaviorIds.AutonomousAllowed.Contains(x.BehaviorId)))
                    {
                        var frontProne = string.Equals(
                            motion.BehaviorId,
                            SleepCandidateBehaviorIds.SprawledFrontBreath,
                            StringComparison.OrdinalIgnoreCase);
                        runtime.UpdateBehaviorAgentMock(
                            TemperamentProfile.Default,
                            PetRuntimeState.Default with
                            {
                                CurrentPosture = StablePosture.Prone,
                                CurrentPoseId = motion.StartPose,
                                Energy = 0.25,
                                Stress = 0.08
                            },
                            RelationshipState.Default,
                            71);
                        typeof(DesktopRuntimeHost).GetField("_frontProneProfileActive", Private)!.SetValue(runtime, frontProne);
                        runtime.StartIdle("sleep_v11_renderer_fixture");
                        typeof(DesktopRuntimeHost).GetField("_currentStartedAt", Private)!.SetValue(
                            runtime,
                            DateTimeOffset.UtcNow - TimeSpan.FromSeconds(4));
                        var result = (PetActionResult)typeof(DesktopRuntimeHost).GetMethod("SubmitBehavior", Private)!.Invoke(runtime,
                            new object[] { BehaviorRequestSource.AutonomousTick, motion.BehaviorId, "sleep_v11_renderer_normal", 7, BehaviorExecutionMode.Normal, false })!;
                        Check(result == PetActionResult.Accepted, $"normal route rejected {motion.BehaviorId}");
                        var seen = await ObserveMotion(window, runtime, image, motion, output, "normal");
                        Check(!runtime.AgentStateSnapshot.Runtime.IsBusy, $"normal sleep leaked busy: {motion.BehaviorId}");
                        evidence.Add(new { motion.BehaviorId, mode = "Normal", frames = seen, pose = runtime.AgentStateSnapshot.Runtime.CurrentPoseId });
                    }

                    File.WriteAllText(
                        Path.Combine(output, "renderer.json"),
                        JsonSerializer.Serialize(new { passed = true, batch = SleepCandidateBehaviorIds.AssetBatch, evidence }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    window.Close();
                    app.Shutdown();
                }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        Console.WriteLine("Real WPF sleep v11 playback passed: " + output);
        return 0;
    }

    private static async Task<int> ObserveMotion(
        MainWindow window,
        DesktopRuntimeHost runtime,
        Image image,
        PlayableMotion motion,
        string output,
        string mode)
    {
        var expected = motion.Phases.SelectMany(x => x.Frames).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var started = DateTimeOffset.UtcNow;
        while (runtime.CurrentBehaviorId == motion.BehaviorId && (DateTimeOffset.UtcNow - started).TotalSeconds < 12)
        {
            Check(image.Source is BitmapSource { IsFrozen: true }, "sleep image is not a frozen bitmap");
            Check(Math.Abs(window.ActionLocalScale - 0.78) < 0.001, "sleep action changed local scale");
            if (seen.Add(runtime.CurrentAsset))
                Capture(window, Path.Combine(output, $"{mode}-{Sanitize(motion.BehaviorId)}-{seen.Count:00}.png"));
            if (motion.Phases.All(x => x.Loop) && seen.Count == expected)
            {
                var stop = typeof(MainWindow).GetMethod("StopCurrentBehaviorAsync", Private)!;
                await (Task)stop.Invoke(window, new object[] { "sleep_v11_renderer_stop" })!;
                ((DispatcherTimer)typeof(MainWindow).GetField("_autonomousTimer", Private)!.GetValue(window)!).Stop();
            }
            await Task.Delay(20);
        }
        Check(seen.Count == expected, $"{motion.BehaviorId} displayed {seen.Count}/{expected} unique frames");
        Check(runtime.CurrentBehaviorId != motion.BehaviorId, $"{motion.BehaviorId} did not settle");
        return seen.Count;
    }

    private static string Sanitize(string value) => value.Replace('.', '-').Replace('_', '-');

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var target = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        target.Render(window);
        var pixels = new byte[target.PixelWidth * target.PixelHeight * 4];
        target.CopyPixels(pixels, target.PixelWidth * 4, 0);
        Check(pixels.Where((_, index) => index % 4 == 3).Count(value => value > 16) > 1000, "rendered sleep frame is blank");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
