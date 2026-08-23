using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

if (args.Contains("--show-panel-smoke", StringComparer.Ordinal))
    return ShowPanelSmoke();
if (args.Contains("--capture-panel-screens", StringComparer.Ordinal))
    return CapturePanelScreens(args.SkipWhile(x => x != "--capture-panel-screens").Skip(1).FirstOrDefault() ?? Path.Combine(".publish-check", "ux-panel-album-coin-fixes-v1", "screenshots"));
if (args.Contains("--car-ride-memory-smoke", StringComparer.Ordinal))
{
    var secondsArg = args.SkipWhile(x => x != "--car-ride-memory-smoke").Skip(1).FirstOrDefault();
    var seconds = int.TryParse(secondsArg, out var parsedSeconds) ? parsedSeconds : 300;
    var output = args.SkipWhile(x => x != "--car-ride-memory-smoke").Skip(2).FirstOrDefault() ?? Path.Combine(".publish-check", "interaction-car-ride-v8-candidate", "car-ride-memory-smoke.json");
    return CarRideMemorySmoke(seconds, output);
}

var tests = new (string Name, Action Run)[]
{
    ("input adapter emits input events", InputAdapterEmitsEvents),
    ("startup factory creates one main window", StartupFactoryCreatesOneMainWindow),
    ("desktop single instance rejects a duplicate process", DesktopSingleInstanceRejectsDuplicate),
    ("control panel xaml constructs", ControlPanelXamlConstructs),
    ("agent windows construct and desktop chat starts hidden", AgentWindowsConstructAndChatStartsHidden),
    ("desktop chat uses single-line enter send semantics", DesktopChatKeyboardSemantics),
    ("desktop chat sensor is limited to lower blank region", DesktopChatSensorIsLimited),
    ("desktop chat placement stays visible at all corners", DesktopChatPlacementStaysVisible),
    ("desktop input opens directly below the live pet window", DesktopInputOpensBelowPet),
    ("double click targets compact chat and initiative speech stays low frequency", DesktopChatAndInitiativeContract),
    ("main window pet scale changes image size", MainWindowPetScaleChangesImageSize),
    ("initial placement stays in work area", InitialPlacementStaysInWorkArea),
    ("phase15 motion assets are copied and decodable", Phase15MotionAssetsAreCopiedAndDecodable),
    ("lifecycle microloop candidates are indexed and gated", LifecycleMicroloopCandidatesAreIndexedAndGated),
    ("developer lifecycle candidate can request playback", DeveloperLifecycleCandidateCanRequestPlayback),
    ("autonomous daily candidates are indexed and remain gated", AutonomousDailyCandidatesAreIndexedAndRemainGated),
    ("developer autonomous daily candidate can request playback", DeveloperAutonomousDailyCandidateCanRequestPlayback),
    ("command action candidates are indexed and validated", CommandActionCandidatesAreIndexed),
    ("behavior agent command mock assets are indexed and gated", BehaviorAgentCommandMockAssetsAreIndexedAndGated),
    ("command candidates stay out of autonomous and production commands", CommandCandidatesStayGated),
    ("behavior agent mock owner command uses posture branch", BehaviorAgentMockOwnerCommandUsesPostureBranch),
    ("approved owner commands tolerate missing posture bridge assets", ApprovedOwnerCommandsTolerateMissingPostureBridgeAssets),
    ("command completion holds the exact terminal frame", CommandCompletionHoldsExactTerminalFrame),
    ("reported commands keep rendered size at terminal hold", ReportedCommandTerminalHoldsKeepRenderScale),
    ("command groups share one batch visual scale", CommandGroupsShareOneBatchVisualScale),
    ("behavior agent mock closed keeps formal runtime unchanged", BehaviorAgentMockClosedKeepsFormalRuntime),
    ("developer forced command candidate can request playback", DeveloperForcedCommandCandidateCanRequestPlayback),
    ("magic candidate assets are indexed and validated", MagicCandidateAssetsAreIndexed),
    ("car ride candidate assets are indexed and gated", CarRideCandidateAssetsAreIndexedAndGated),
    ("owner and panel car ride use approved normal gate", OwnerAndPanelCarRideUseApprovedNormalGate),
    ("non owner sources cannot trigger car ride", NonOwnerSourcesCannotTriggerCarRide),
    ("petrified coin assets and checksums are complete", PetrifiedCoinAssetsAndChecksumsAreComplete),
    ("owner and panel magic use prototype preview gate", OwnerAndPanelMagicUsePrototypePreviewGate),
    ("non owner sources cannot prototype preview magic", NonOwnerSourcesCannotPrototypePreviewMagic),
    ("petrified coin inactivity reaches all four states", PetrifiedCoinInactivityReachesAllFourStates),
    ("petrified coin timing is configurable", PetrifiedCoinTimingIsConfigurable),
    ("petrified coin default timing and visual scale are stable", PetrifiedCoinDefaultTimingAndVisualScaleAreStable),
    ("motion visual sizing normalizes alpha bounds", MotionVisualSizingNormalizesAlphaBounds),
    ("petrified coin clicks reset and double clicks flip", PetrifiedCoinClicksResetAndDoubleClicksFlip),
    ("stop clears petrification and requests idle", StopClearsPetrificationAndRequestsIdle),
    ("main window context menu matches owner action contract", MainWindowContextMenuMatchesContract),
    ("broom direction quantizer covers eight directions", BroomDirectionQuantizerCoversEightDirections),
    ("car ride direction quantizer matches v8 directions", CarRideDirectionQuantizerMatchesV8Directions),
    ("showcase durations and physical routes stay bounded", ShowcaseDurationsAndPhysicalRoutesStayBounded),
    ("apparate target stays visible and relocates", ApparateTargetStaysVisibleAndRelocates),
    ("control panel exposes magic specials tab", ControlPanelExposesMagicSpecialsTab),
    ("expired asset cards are gray but remain previewable", ExpiredAssetCardsAreGrayButRemainPreviewable),
    ("control panel car ride copy matches approved runtime state", ControlPanelCarRideCopyMatchesApprovedRuntimeState),
    ("control panel tab buttons share visual metrics", ControlPanelTabButtonsShareVisualMetrics),
    ("gesture interpreter distinguishes touch stroke drag and rapid tap", GestureInterpreterDistinguishesGestures),
    ("rapid tap has priority over owner touch", RapidTapHasPriorityOverOwnerTouch),
    ("runtime keeps prone touch candidate behind manifest gate", RuntimeRequestsTouchMotion),
    ("runtime rapid tap does not request touch motion", RuntimeRapidTapDoesNotRequestTouchMotion),
    ("interaction decision uses state relationship and asset gates", InteractionDecisionUsesStateRelationshipAndAssetGates),
    ("album folder item reads local markdown album", AlbumFolderItemReadsLocalMarkdownAlbum),
    ("album folder item reads xhs markdown album", AlbumFolderItemReadsXhsMarkdownAlbum),
    ("album markdown update preserves unknown fields", AlbumMarkdownUpdatePreservesUnknownFields),
    ("album media unlink handles persistence and keeps files", AlbumMediaUnlinkHandlesPersistenceAndKeepsFiles),
    ("album folder removal persists and keeps local files", AlbumFolderRemovalPersistsAndKeepsFiles),
    ("autonomous tick can request a motion after dwell", AutonomousTickCanRequestMotion),
    ("bootstrap log redacts and does not throw", BootstrapLogRedactsAndDoesNotThrow)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static int CarRideMemorySmoke(int seconds, string outputPath)
{
    Exception? failure = null;
    var samples = new List<object>();
    var thread = new Thread(() =>
    {
        try
        {
            var app = EnsureTestApplication();
            var window = new MainWindow();
            app.MainWindow = window;
            window.Show();
            window.UpdateLayout();
            var root = (FrameworkElement)window.FindName("Root");
            var carRideMenu = FindMenuItemByTag(root.ContextMenu!, CarRideBehaviorIds.CarRide)
                ?? throw new InvalidOperationException("car ride menu item missing");
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var started = DateTimeOffset.UtcNow;
            long peakPrivate = 0;
            long peakWorking = 0;
            int peakCacheFrames = 0;
            long peakCacheBytes = 0;
            var triggerTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            triggerTimer.Tick += (_, _) => carRideMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var sampleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            sampleTimer.Tick += (_, _) =>
            {
                process.Refresh();
                peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                peakWorking = Math.Max(peakWorking, process.WorkingSet64);
                peakCacheFrames = Math.Max(peakCacheFrames, window.DecodedFrameCacheCount);
                peakCacheBytes = Math.Max(peakCacheBytes, window.EstimatedDecodedFrameBytes);
                samples.Add(new
                {
                    elapsed_seconds = (int)(DateTimeOffset.UtcNow - started).TotalSeconds,
                    private_bytes = process.PrivateMemorySize64,
                    working_set_bytes = process.WorkingSet64,
                    decoded_frame_cache_count = window.DecodedFrameCacheCount,
                    decoded_frame_cache_bytes = window.EstimatedDecodedFrameBytes,
                    decoded_frame_cache_evictions = window.DecodedFrameCacheEvictions
                });
            };
            var stopTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, seconds)) };
            stopTimer.Tick += (_, _) =>
            {
                stopTimer.Stop();
                sampleTimer.Stop();
                triggerTimer.Stop();
                window.Close();
                window.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
            };
            carRideMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            sampleTimer.Start();
            triggerTimer.Start();
            stopTimer.Start();
            System.Windows.Threading.Dispatcher.Run();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(seconds + 30)))
    {
        Console.Error.WriteLine("car ride memory smoke timed out");
        return 2;
    }
    if (failure is not null)
    {
        Console.Error.WriteLine(failure);
        return 1;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    File.WriteAllText(outputPath, JsonSerializer.Serialize(new
    {
        duration_seconds = seconds,
        max_decoded_frame_cache_count = samples.Select(x => (int)x.GetType().GetProperty("decoded_frame_cache_count")!.GetValue(x)!).DefaultIfEmpty(0).Max(),
        max_decoded_frame_cache_bytes = samples.Select(x => (long)x.GetType().GetProperty("decoded_frame_cache_bytes")!.GetValue(x)!).DefaultIfEmpty(0).Max(),
        sample_count = samples.Count,
        samples
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"car ride memory smoke: {Path.GetFullPath(outputPath)}");
    return 0;
}

static MenuItem? FindMenuItemByTag(ItemsControl root, string tag)
{
    foreach (var item in root.Items.OfType<object>())
    {
        if (item is MenuItem menuItem)
        {
            if (string.Equals(menuItem.Tag?.ToString(), tag, StringComparison.Ordinal))
                return menuItem;
            var nested = FindMenuItemByTag(menuItem, tag);
            if (nested is not null)
                return nested;
        }
    }
    return null;
}
static int ShowPanelSmoke()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = EnsureTestApplication();
            var panel = new ControlPanelWindow(new DesktopRuntimeHost());
            app.MainWindow = panel;
            panel.Closed += (_, _) => panel.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
            panel.Show();
            System.Windows.Threading.Dispatcher.Run();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(45)))
        return 2;
    if (failure is not null)
    {
        Console.Error.WriteLine(failure);
        return 1;
    }
    return 0;
}

static int CapturePanelScreens(string outputRoot)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        string? previousAlbumRoot = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        string? screenshotAlbumRoot = null;
        try
        {
            var app = EnsureTestApplication();
            Directory.CreateDirectory(outputRoot);
            screenshotAlbumRoot = CreateScreenshotAlbumRoot();
            Environment.SetEnvironmentVariable("WUKONG_ALBUM_ROOT", screenshotAlbumRoot);
            var panel = new ControlPanelWindow(new DesktopRuntimeHost())
            {
                Width = 1360,
                Height = 880
            };
            app.MainWindow = panel;
            panel.Show();
            panel.UpdateLayout();

            CapturePanel(panel, outputRoot, "owner-current.png");
            ClickNavByTag(panel, "Profile");
            CapturePanel(panel, outputRoot, "profile-tabs.png");
            ClickNavByTag(panel, "Album");
            CapturePanel(panel, outputRoot, "album-all-media-list.png");
            if (panel.FindName("AlbumMediaList") is ListBox mediaList && mediaList.Items.Count > 1)
            {
                mediaList.SelectedIndex = 1;
                panel.UpdateLayout();
                CapturePanel(panel, outputRoot, "subalbum-selected-preview.png");
                ClickNamedButton(panel, "UnbindAlbumMediaButton");
                CapturePanel(panel, outputRoot, "album-unbind-after.png");
            }
            ClickNavByTag(panel, "Model");
            CapturePanel(panel, outputRoot, "model-tabs.png");
            ClickNamedButton(panel, "MemoryConfigTabButton");
            CapturePanel(panel, outputRoot, "memory-config-toggle.png");
            ClickNavByTag(panel, "Assets");
            CapturePanel(panel, outputRoot, "assets-normal-base.png");
            ClickNamedButton(panel, "CommandAssetsTabButton");
            CapturePanel(panel, outputRoot, "assets-normal-command.png");
            if (panel.FindName("CommandAssetsPanel") is ScrollViewer commandAssetsPanel)
            {
                commandAssetsPanel.ScrollToEnd();
                panel.UpdateLayout();
                CapturePanel(panel, outputRoot, "assets-normal-command-expired.png");
            }
            ClickNamedButton(panel, "MagicAssetsTabButton");
            CapturePanel(panel, outputRoot, "assets-magic-specials.png");
            CaptureVisualSizeComparison(outputRoot);
            panel.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            Environment.SetEnvironmentVariable("WUKONG_ALBUM_ROOT", previousAlbumRoot);
            if (!string.IsNullOrWhiteSpace(screenshotAlbumRoot))
                TryDeleteDirectory(screenshotAlbumRoot);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(45)))
    {
        Console.Error.WriteLine("panel screenshot capture timed out");
        return 2;
    }
    if (failure is not null)
    {
        Console.Error.WriteLine(failure);
        return 1;
    }

    Console.WriteLine($"screenshots: {Path.GetFullPath(outputRoot)}");
    return 0;
}

static string CreateScreenshotAlbumRoot()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-panel-screens-" + Guid.NewGuid().ToString("N"));
    var album = Path.Combine(root, "daily-home");
    Directory.CreateDirectory(album);
    var runtime = new DesktopRuntimeHost();
    var frames = runtime.Motions.Take(3).Select(x => x.FirstFrame).Where(File.Exists).ToArray();
    for (var i = 0; i < frames.Length; i++)
        File.Copy(frames[i], Path.Combine(album, $"sample-{i + 1:00}.png"), overwrite: true);
    File.WriteAllText(Path.Combine(album, "album.md"), string.Join(Environment.NewLine, new[] { "---", "title: daily-home", "time: 2026-08-15", "media:", "  - \"sample-01.png\"", "  - \"sample-02.png\"", "  - \"sample-03.png\"", "---", "", "# daily-home", "", "## ??", "", "screenshot fixture", "", "## ??", "", "- `sample-01.png`", "- `sample-02.png`", "- `sample-03.png`" }), System.Text.Encoding.UTF8);
    return root;
}

static void CaptureVisualSizeComparison(string outputRoot)
{
    var runtime = new DesktopRuntimeHost();
    var reference = runtime.ReferenceVisualFramePath;
    var normal = runtime.Motions.First(x => x.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop);
    var magic = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.AccioBroom);
    var petrified = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.PetrificusTotalus);
    var carRide = runtime.CarRideCandidateMotions.Single();
    var carRoot = Path.Combine(Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!, "WukongAssets", "action-batches", CarRideBehaviorIds.AssetBatch);
    var samples = new[]
    {
        (Label: "normal", Frame: normal.FirstFrame, Scale: normal.VisualScale),
        (Label: "magic", Frame: magic.FirstFrame, Scale: magic.VisualScale),
        (Label: "petrified", Frame: petrified.FirstFrame, Scale: petrified.VisualScale),
        (Label: "car right", Frame: Path.Combine(carRoot, "sequences", "directions", "right", "frame-001.png"), Scale: carRide.VisualScale),
        (Label: "car front", Frame: Path.Combine(carRoot, "sequences", "directions", "front", "frame-001.png"), Scale: carRide.VisualScale),
        (Label: "car rear", Frame: Path.Combine(carRoot, "sequences", "directions", "rear", "frame-001.png"), Scale: carRide.VisualScale),
        (Label: "expression", Frame: Path.Combine(carRoot, "sequences", "expressions", "head-tilt", "frame-00.png"), Scale: carRide.VisualScale),
        (Label: "turn mid", Frame: Path.Combine(carRoot, "sequences", "transitions", "turn", "right-to-front-right", "frame-01.png"), Scale: carRide.VisualScale)
    };

    var panel = new Grid { Width = 1680, Height = 560, Background = Brushes.White };
    for (var i = 0; i < 4; i++)
        panel.ColumnDefinitions.Add(new ColumnDefinition());
    for (var i = 0; i < 2; i++)
        panel.RowDefinitions.Add(new RowDefinition());

    for (var i = 0; i < samples.Length; i++)
    {
        var stack = BuildVisualSample(samples[i].Frame, reference, samples[i].Label, samples[i].Scale);
        Grid.SetColumn(stack, i % 4);
        Grid.SetRow(stack, i / 4);
        panel.Children.Add(stack);
    }

    panel.Measure(new Size(panel.Width, panel.Height));
    panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
    var bitmap = new RenderTargetBitmap((int)panel.Width, (int)panel.Height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(panel);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(Path.Combine(outputRoot, "visual-size-comparison-car-ride-v8.png"));
    encoder.Save(stream);
}

static StackPanel BuildVisualSample(string framePath, string reference, string label, double scale)
{
    var size = MotionVisualSizer.PreviewRenderSize(framePath, reference, scale, 190);
    var metrics = MotionVisualSizer.Measure(framePath);
    var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    stack.Children.Add(new Border
    {
        Width = 210,
        Height = 210,
        Background = new SolidColorBrush(Color.FromRgb(238, 236, 229)),
        CornerRadius = new CornerRadius(12),
        Child = new Image { Source = BitmapFrame.Create(new Uri(framePath, UriKind.Absolute)), Width = size, Height = size, Stretch = Stretch.Uniform }
    });
    stack.Children.Add(new TextBlock { Text = $"{label} / visible {metrics.VisibleWidth}x{metrics.VisibleHeight} / render {size:0}px", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
    return stack;
}
static void CapturePanel(ControlPanelWindow panel, string outputRoot, string fileName)
{
    panel.UpdateLayout();
    var width = Math.Max(1, (int)Math.Ceiling(panel.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(panel.ActualHeight));
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(panel);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(Path.Combine(outputRoot, fileName));
    encoder.Save(stream);
}

static void ClickNamedButton(ControlPanelWindow panel, string name)
{
    if (panel.FindName(name) is Button button)
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    panel.UpdateLayout();
}

static void ClickNavByTag(ControlPanelWindow panel, string tag)
{
    var button = FindButtonByTag(panel, tag) ?? throw new InvalidOperationException($"nav button not found: {tag}");
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    panel.UpdateLayout();
}

static Button? FindButtonByTag(DependencyObject root, string tag)
{
    if (root is Button button && string.Equals(button.Tag?.ToString(), tag, StringComparison.Ordinal))
        return button;
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
    {
        var found = FindButtonByTag(VisualTreeHelper.GetChild(root, i), tag);
        if (found is not null)
            return found;
    }
    return null;
}

static void ClickButtonByContent(DependencyObject root, string content)
{
    var button = FindButtonByContent(root, content) ?? throw new InvalidOperationException($"button not found: {content}");
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if (root is UIElement element)
        element.UpdateLayout();
}

static Button? FindButtonByContent(DependencyObject root, string content)
{
    foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<object>())
    {
        if (child is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            return button;
        if (child is DependencyObject dependencyObject)
        {
            var match = FindButtonByContent(dependencyObject, content);
            if (match is not null)
                return match;
        }
    }

    return null;
}

static void InputAdapterEmitsEvents()
{
    var item = DesktopInputEventAdapter.PointerUp(new Point(12.5, 7));
    Assert(item.Kind == InputEventKind.PointerUp, "wrong input kind");
    Assert(item.Source == BehaviorRequestSource.OwnerUi, "wrong source");
    Assert(item.Data["x"] == "12.5", "x coordinate not captured");
}

static void StartupFactoryCreatesOneMainWindow()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = EnsureTestApplication();
            var first = DesktopStartup.EnsureMainWindow(app);
            var second = DesktopStartup.EnsureMainWindow(app);
            Assert(ReferenceEquals(first, second), "startup factory created multiple main windows");
            Assert(ReferenceEquals(app.MainWindow, first), "main window was not assigned to application");
            Assert(first.Width > 0 && first.Height > 0, "main window has invalid initial size");
            first.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
        throw failure;
}

static void DesktopSingleInstanceRejectsDuplicate()
{
    var name = "Wukong.Desktop.Tests." + Guid.NewGuid().ToString("N");
    using var primary = DesktopSingleInstance.Acquire(name);
    using var duplicate = DesktopSingleInstance.Acquire(name);
    Assert(primary.IsPrimary, "first process did not acquire the desktop instance");
    Assert(!duplicate.IsPrimary, "duplicate process acquired a second desktop instance");
}

static void ControlPanelXamlConstructs()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            var panel = new ControlPanelWindow(new DesktopRuntimeHost());
            Assert(panel.FindName("ModelConfigPanel") is not null, "model configuration tab panel missing");
            Assert(panel.FindName("MemoryConfigPanel") is not null, "memory configuration tab panel missing");
            Assert(panel.FindName("PetSettingPanel") is not null, "pet setting tab panel missing");
            Assert(panel.FindName("UseLongTermMemoryCheck") is ToggleButton, "long term memory switch missing");
            Assert(panel.FindName("UseAlbumMemoryCheck") is ToggleButton, "album memory switch missing");
            Assert(panel.FindName("UseShortTermMemoryCheck") is ToggleButton, "short term memory switch missing");
            Assert(panel.FindName("LongTermMemoryText") is StackPanel { HorizontalAlignment: HorizontalAlignment.Center, VerticalAlignment: VerticalAlignment.Center }, "long term memory text should be centered");
            Assert(panel.FindName("AlbumMemoryText") is StackPanel { HorizontalAlignment: HorizontalAlignment.Center, VerticalAlignment: VerticalAlignment.Center }, "album memory text should be centered");
            Assert(panel.FindName("ShortTermMemoryText") is StackPanel { HorizontalAlignment: HorizontalAlignment.Center, VerticalAlignment: VerticalAlignment.Center }, "short term memory text should be centered");
            Assert(panel.FindName("DeleteSelectedAlbumButton") is Button, "independent album deletion button missing");
            Assert(panel.FindName("OwnerBirthdayPicker") is DatePicker, "owner birthday field missing");
            Assert(panel.FindName("OwnerPetCallNameText") is TextBox, "owner pet call name field missing");
            var prompt = panel.FindName("PetPromptText") as TextBox;
            Assert(prompt?.ContextMenu is not null, "pet prompt context menu missing");
            var promptCommands = prompt!.ContextMenu!.Items.OfType<MenuItem>().Select(x => x.Command).ToArray();
            Assert(promptCommands.Contains(ApplicationCommands.Cut) &&
                   promptCommands.Contains(ApplicationCommands.Copy) &&
                   promptCommands.Contains(ApplicationCommands.Paste) &&
                   promptCommands.Contains(ApplicationCommands.Delete), "pet prompt basic editing commands missing");
            Assert(panel.FindName("PetHarnessCombo") is null, "removed pet harness field is still registered");
            Assert(panel.FindName("OwnerToneCombo") is null, "removed owner tone field is still registered");
            panel.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
        throw failure;
}

static void AgentWindowsConstructAndChatStartsHidden()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            using var agent = DesktopAgentRuntime.CreateDefault();
            var chat = new DesktopChatWindow(agent);
            var bubble = new DesktopSpeechBubbleWindow();
            var login = new DeveloperLoginWindow(agent.DeveloperSession);
            Assert(!chat.IsExpanded, "desktop chat should be hidden until the sensor is clicked");
            Assert(chat.Height is >= 50 and <= 60, "desktop input should be a single compact row");
            Assert(chat.FindName("ChatInput") is TextBox, "desktop chat input missing");
            Assert(chat.FindName("ChatList") is null, "desktop input should not embed conversation history");
            Assert(chat.FindName("ChatStatus") is null, "desktop input should not show a status row");
            Assert(chat.FindName("CancelButton") is null, "desktop input should not show a separate cancel button");
            var xaml = File.ReadAllText(Path.GetFullPath(Path.Combine("src", "Wukong.Desktop", "DesktopChatWindow.xaml")));
            Assert(!xaml.Contains("和悟空说话", StringComparison.Ordinal), "desktop input should not show a redundant title");
            chat.Close();
            bubble.Close();
            login.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw failure;
}

static void DesktopChatKeyboardSemantics()
{
    Assert(DesktopChatWindow.ShouldSend(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None), "Enter should send");
    Assert(!DesktopChatWindow.ShouldSend(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Shift), "Shift+Enter should not send from the single-line input");
    Assert(!DesktopChatWindow.ShouldSend(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None), "Escape should not send");
}

static void DesktopChatSensorIsLimited()
{
    var size = new Size(320, 320);
    Assert(MainWindow.IsChatSensorPoint(size, new Point(160, 305)), "lower center should open chat");
    Assert(!MainWindow.IsChatSensorPoint(size, new Point(160, 160)), "pet body region must not open chat");
    Assert(!MainWindow.IsChatSensorPoint(size, new Point(20, 305)), "lower corner must not open chat");
}

static void DesktopChatPlacementStaysVisible()
{
    var workArea = new Rect(0, 0, 1280, 720);
    var overlay = new Size(420, 54);
    var pets = new[]
    {
        new Rect(0, 0, 320, 320),
        new Rect(960, 0, 320, 320),
        new Rect(0, 400, 320, 320),
        new Rect(960, 400, 320, 320)
    };
    foreach (var pet in pets)
    {
        var adjustedPet = DesktopChatPlacement.MakeRoomBelow(workArea, pet, overlay);
        var point = DesktopChatPlacement.Place(workArea, adjustedPet, overlay);
        Assert(point.X >= workArea.Left && point.Y >= workArea.Top, "chat escaped top or left work area");
        Assert(point.X + overlay.Width <= workArea.Right && point.Y + overlay.Height <= workArea.Bottom, "chat escaped right or bottom work area");
        Assert(Math.Abs(point.Y - adjustedPet.Bottom - DesktopChatPlacement.PetGap) < 0.001, "chat should stay directly below the adjusted pet");
    }
    var bubble = DesktopChatPlacement.PlaceSpeechAbove(workArea, new Rect(480, 300, 320, 320), new Size(320, 100));
    Assert(bubble.Y < 300, "assistant reply bubble should be above the pet");

    var visible = DesktopChatPlacement.VisibleSubjectBounds(
        new Rect(10, 20, 310, 310),
        new MotionVisibleMetrics(1000, 1000, new Int32Rect(200, 100, 500, 700)));
    Assert(Math.Abs(visible.Left - 72) < 0.001, "visible alpha left coordinate is wrong");
    Assert(Math.Abs(visible.Top - 51) < 0.001, "visible alpha top coordinate is wrong");
    Assert(Math.Abs(visible.Width - 155) < 0.001, "visible alpha width is wrong");
    Assert(Math.Abs(visible.Height - 217) < 0.001, "visible alpha height is wrong");
}

static void DesktopInputOpensBelowPet()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        MainWindow? window = null;
        try
        {
            _ = EnsureTestApplication();
            window = new MainWindow { Left = 420, Top = 330 };
            window.Show();
            window.UpdateLayout();
            typeof(MainWindow)
                .GetMethod("OpenChatForInput", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null);
            var chat = (DesktopChatWindow?)typeof(MainWindow)
                .GetField("_chatWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(window);
            Assert(chat is { IsVisible: true }, "double-click input window did not open");
            chat!.UpdateLayout();
            var visiblePet = (Rect)typeof(MainWindow)
                .GetMethod("CurrentVisiblePetBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null)!;
            var petBottom = visiblePet.Bottom;
            Assert(Math.Abs(chat.Top - petBottom - DesktopChatPlacement.PetGap) < 1.0, "desktop input is not adjacent below the pet");
            Assert(chat.Top + chat.ActualHeight <= SystemParameters.WorkArea.Bottom + 1, "desktop input escaped the work area");
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            failure = ex.InnerException;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            window?.Close();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw failure;
}

static void DesktopChatAndInitiativeContract()
{
    Assert(MainWindow.OpensChatOnGesture(PetGestureKind.DoubleClick), "double click should open chat");
    Assert(!MainWindow.OpensChatOnGesture(PetGestureKind.OwnerTouch), "single touch must not open chat");
    var first = InitiativeSpeechSchedule.NextInterval(new Random(42));
    var second = InitiativeSpeechSchedule.NextInterval(new Random(42));
    Assert(first == second, "initiative interval should be deterministic for a fixed random seed");
    Assert(first >= TimeSpan.FromMinutes(3) && first <= TimeSpan.FromMinutes(7), "initiative speech is not low frequency");
    Assert(!InitiativeSpeechSchedule.CanSpeakDuring("wk.magic.apparate", false), "magic should suppress initiative speech");
    Assert(!InitiativeSpeechSchedule.CanSpeakDuring("wk.interaction.car_ride", false), "car ride should suppress initiative speech");
    Assert(!InitiativeSpeechSchedule.CanSpeakDuring("wk.command.jump", false), "command motion should suppress initiative speech");
    Assert(InitiativeSpeechSchedule.CanSpeakDuring(LifecycleCandidateBehaviorIds.StandIdleMicroloop, false), "stable idle should allow initiative speech");
    var hungerText = InitiativeSpeechSchedule.SelectMessage(new Random(4), InitiativeSpeechTopic.Hunger, StablePosture.Sit);
    Assert(hungerText.Contains("饿", StringComparison.Ordinal) || hungerText.Contains("肚子", StringComparison.Ordinal) || hungerText.Contains("吃", StringComparison.Ordinal), "hunger initiative used unrelated copy");
}

static void MainWindowPetScaleChangesImageSize()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            var window = new MainWindow();
            window.SetPetScaleForTest(1.25);
            Assert(Math.Abs(window.PetScale - 1.25) < 0.001, "pet scale was not applied");
            Assert(Math.Abs(window.Width - 400) < 0.001, "window width did not scale up");

            window.SetPetScaleForTest(0.65);
            Assert(Math.Abs(window.PetScale - 0.65) < 0.001, "pet scale did not shrink");
            Assert(Math.Abs(window.Width - 208) < 0.001, "window width did not scale down");

            window.SetPetScaleForTest(0.2);
            Assert(Math.Abs(window.PetScale - 0.5) < 0.001, "pet scale did not clamp to minimum");
            Assert(Math.Abs(window.Width - 160) < 0.001, "minimum window width changed");
            window.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
        throw failure;
}

static void InitialPlacementStaysInWorkArea()
{
    var workArea = new Rect(0, 0, 1920, 1040);
    var position = WindowPlacement.BottomRight(workArea, 280, 280, 24);
    Assert(position.X >= workArea.Left, "left coordinate outside work area");
    Assert(position.Y >= workArea.Top, "top coordinate outside work area");
    Assert(position.X + 280 <= workArea.Right, "right edge outside work area");
    Assert(position.Y + 280 <= workArea.Bottom, "bottom edge outside work area");
}

static Application EnsureTestApplication()
{
    if (Application.Current is not null)
        return Application.Current;

    var app = new App();
    app.InitializeComponent();
    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    return app;
}

static void Phase15MotionAssetsAreCopiedAndDecodable()
{
    var desktopAssembly = typeof(MainWindow).Assembly.Location;
    var path = Path.Combine(
        Path.GetDirectoryName(desktopAssembly) ?? throw new InvalidOperationException("desktop output directory missing"),
        "WukongAssets",
        "actions",
        "WK-CORE-PRONE-IDLE-LF-v1",
        "runtime-frames",
        "v3",
        "frame-001.png");
    Assert(File.Exists(path), "phase15 idle frame was not copied to desktop output");

    using var stream = File.OpenRead(path);
    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    var frame = decoder.Frames.Single();
    Assert(frame.PixelWidth > 0 && frame.PixelHeight > 0, "phase15 frame has invalid dimensions");

    var catalog = DesktopMotionCatalog.Load(Path.GetDirectoryName(desktopAssembly)!);
    Assert(catalog.Motions.Count >= 6, "phase15 playable registry did not load enough motions");
    Assert(catalog.RequiredIdle.FrameCount >= 5, "idle motion is not animated");
}


static void LifecycleMicroloopCandidatesAreIndexedAndGated()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", LifecycleCandidateBehaviorIds.AssetBatch, "manifest.json");
    Assert(File.Exists(manifestPath), "lifecycle candidate manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    Assert(manifest.RootElement.GetProperty("runtime_approved").GetBoolean(), "lifecycle batch must be runtime approved after P3 QA");
    Assert(manifest.RootElement.GetProperty("runtime_use").GetBoolean(), "lifecycle batch must enable autonomous runtime use after P3 QA");
    Assert(manifest.RootElement.GetProperty("developer_candidate_profile_only").GetBoolean(), "lifecycle batch must still expose developer profile controls");
    var actions = manifest.RootElement.GetProperty("actions").EnumerateArray().ToArray();
    Assert(actions.Length == 4, "lifecycle manifest must describe full lifecycle plus three microloops");
    Assert(actions.All(x => !x.GetProperty("behavior_id").GetString()!.StartsWith("wk.command.", StringComparison.Ordinal)), "lifecycle candidates must not use command IDs");

    foreach (var action in actions)
    {
        Assert(action.GetProperty("runtime_approved").GetBoolean(), "lifecycle action was not runtime approved after P3 QA");
        Assert(action.GetProperty("runtime_use").GetBoolean(), "lifecycle action did not enable runtime use after P3 QA");
        foreach (var phase in action.GetProperty("phases").EnumerateArray())
        {
            var frames = phase.GetProperty("frames").EnumerateArray().ToArray();
            Assert(frames.Length == phase.GetProperty("frame_count").GetInt32(), "lifecycle phase frame count mismatch");
            foreach (var frame in frames)
            {
                var framePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, frame.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Assert(File.Exists(framePath), $"lifecycle frame missing: {framePath}");
                Assert(new FileInfo(framePath).Length == frame.GetProperty("bytes").GetInt64(), "lifecycle frame byte length mismatch");
                Assert(Sha256(framePath) == frame.GetProperty("sha256").GetString(), "lifecycle frame sha256 mismatch");
                Assert(frame.GetProperty("duration_ms").GetInt32() > 0, "lifecycle frame duration missing");
                using var stream = File.OpenRead(framePath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var bitmap = decoder.Frames.Single();
                Assert(bitmap.PixelWidth == 1024 && bitmap.PixelHeight == 1024, "lifecycle frame must stay 1024x1024");
                Assert(HasAlpha(bitmap.Format), "lifecycle frame is not alpha-capable");
            }
        }
    }

    var microCycles = actions.Where(x => x.TryGetProperty("cycle_ms", out _)).Select(x => x.GetProperty("cycle_ms").GetInt32()).OrderBy(x => x).ToArray();
    Assert(microCycles.SequenceEqual(new[] { 7240, 7680, 8900 }), "microloop cycle durations changed");

    var catalog = DesktopMotionCatalog.Load(output);
    var lifecycle = catalog.Motions.Where(x => x.AssetBatch == LifecycleCandidateBehaviorIds.AssetBatch).ToArray();
    Assert(lifecycle.Length == 4, "lifecycle candidates were not indexed");
    Assert(lifecycle.All(x => x.Category == "基础动作"), "approved lifecycle motions must appear under basic assets");
    Assert(lifecycle.All(x => x.RuntimeEnabled), "P3 lifecycle candidates must be enabled for autonomous runtime");
    Assert(lifecycle.All(x => x.VisualScale is > 0.91 and < 0.93), "approved basic motions must use the shared 0.92 pet scale");
    Assert(lifecycle.All(x => x.CandidateProfile == "developer_lifecycle_microloops_v2"), "candidate profile was not preserved");
    Assert(lifecycle.Single(x => x.BehaviorId == LifecycleCandidateBehaviorIds.LivelyDailyP2).Phases.Select(x => x.Name).SequenceEqual(new[] { "intro", "loop", "exit", "interrupt_exit", "fallback" }), "full lifecycle phases are wrong");
    Assert(lifecycle.Single(x => x.BehaviorId == LifecycleCandidateBehaviorIds.StandIdleMicroloop).Phases.Single().DurationTotalMs(180) == 7240, "stand microloop timing changed");
    Assert(lifecycle.Single(x => x.BehaviorId == LifecycleCandidateBehaviorIds.LivelyDailyP2).MissingContent == "None", "approved lifecycle motion still reports missing runtime content");

    var legacyProne = catalog.Motions.Where(x => x.BehaviorId is Phase15BehaviorIds.ProneIdle or Phase15BehaviorIds.ProneBreath or Phase15BehaviorIds.ProneIdleV3Candidate).ToArray();
    Assert(legacyProne.All(x => !x.RuntimeEnabled && x.Disposition == "已过期"), "legacy prone idle assets must remain archived and runtime disabled");
    Assert(catalog.RequiredIdle.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop, "runtime idle must use the approved P2 prone microloop");
}

static void DeveloperLifecycleCandidateCanRequestPlayback()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    int? requestedSize = null;
    runtime.MotionRequested += (_, item) => request = item;
    runtime.PetPixelSizeRequested += (_, pixels) => requestedSize = pixels;

    var normal = runtime.SubmitOwnerCommandAsync("\u505c").GetAwaiter().GetResult();
    Assert(normal == PetActionResult.Interrupted, "stop path changed");

    var result = runtime.SubmitDeveloperCandidateMotionAsync(LifecycleCandidateBehaviorIds.LivelyDailyP2).GetAwaiter().GetResult();
    Assert(result == PetActionResult.Accepted, "developer lifecycle candidate was not accepted");
    Assert(request is not null, "developer lifecycle candidate did not request playback");
    Assert(request!.Motion.BehaviorId == LifecycleCandidateBehaviorIds.LivelyDailyP2, "wrong lifecycle behavior requested");
    Assert(request.Motion.Phases.Any(x => x.Name == "interrupt_exit"), "lifecycle candidate missing interrupt_exit");
    Assert(request.Motion.HasVariableFrameDurations, "lifecycle candidate did not preserve per-frame durations");
    runtime.RequestPetPixelSize(192);
    Assert(requestedSize == 192, "developer size switch did not emit 192px request");
    runtime.CompleteMotion(LifecycleCandidateBehaviorIds.LivelyDailyP2, "exit");
    Assert(runtime.CurrentStablePosture == StablePosture.Stand, "full lifecycle exit must finish in stable stand");
    Assert(request.Motion.BehaviorId == LifecycleCandidateBehaviorIds.StandIdleMicroloop, "full lifecycle exit did not enter stand microloop");
}

static void AutonomousDailyCandidatesAreIndexedAndRemainGated()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", AutonomousDailyCandidateBehaviorIds.AssetBatch, "manifest.json");
    Assert(File.Exists(manifestPath), "autonomous daily candidate manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = manifest.RootElement;
    Assert(root.GetProperty("asset_stage").GetString() == "production_candidate_owner_qa_pending", "autonomous daily asset stage changed");
    Assert(!root.GetProperty("autonomous_semantics_owner_approved").GetBoolean(), "autonomous semantics must remain owner-unapproved");
    Assert(!root.GetProperty("runtime_approved").GetBoolean(), "autonomous daily batch must remain runtime locked");
    Assert(!root.GetProperty("runtime_use").GetBoolean(), "autonomous daily batch must keep runtime_use=false");
    Assert(!root.GetProperty("may_enter_autonomous_pool_by_default").GetBoolean(), "autonomous daily batch must not enter autonomous pool by default");

    var expected = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [AutonomousDailyCandidateBehaviorIds.StandToSit] = 10,
        [AutonomousDailyCandidateBehaviorIds.SitToProne] = 12,
        [AutonomousDailyCandidateBehaviorIds.ProneToSit] = 4,
        [AutonomousDailyCandidateBehaviorIds.SitToStand] = 5,
        [AutonomousDailyCandidateBehaviorIds.PlayfulHop] = 12,
        [AutonomousDailyCandidateBehaviorIds.PlayfulSpin] = 16
    };
    var actions = root.GetProperty("actions").EnumerateArray().ToArray();
    Assert(actions.Length == expected.Count, "autonomous daily manifest must contain six review actions");
    foreach (var action in actions)
    {
        var behaviorId = action.GetProperty("behavior_id").GetString()!;
        Assert(expected.TryGetValue(behaviorId, out var expectedFrames), $"unexpected autonomous daily behavior: {behaviorId}");
        Assert(action.GetProperty("source_motion_design_approved").GetBoolean(), "source motion design approval must be preserved");
        Assert(!action.GetProperty("autonomous_semantics_owner_approved").GetBoolean(), "action semantics must remain owner-unapproved");
        Assert(!action.GetProperty("runtime_approved").GetBoolean(), "daily action unexpectedly runtime approved");
        Assert(!action.GetProperty("runtime_use").GetBoolean(), "daily action unexpectedly enabled for runtime");
        var frames = action.GetProperty("frames").EnumerateArray().ToArray();
        Assert(frames.Length == expectedFrames, $"frame count changed for {behaviorId}");
        foreach (var frame in frames)
        {
            var framePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, frame.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(framePath), $"autonomous daily frame missing: {framePath}");
            Assert(new FileInfo(framePath).Length == frame.GetProperty("bytes").GetInt64(), "autonomous daily frame byte length mismatch");
            Assert(Sha256(framePath) == frame.GetProperty("sha256").GetString(), "autonomous daily frame sha256 mismatch");
            Assert(frame.GetProperty("duration_ms").GetInt32() > 0, "autonomous daily frame duration missing");
        }
    }

    var catalog = DesktopMotionCatalog.Load(output);
    var candidates = catalog.Motions.Where(x => x.AssetBatch == AutonomousDailyCandidateBehaviorIds.AssetBatch).ToArray();
    Assert(candidates.Length == expected.Count, "autonomous daily candidates were not indexed for review");
    Assert(candidates.Sum(x => x.FrameCount) == 59, "autonomous daily review set must contain 59 frames");
    Assert(candidates.All(x => x.Category == "自主日常候选"), "daily candidates must use the isolated review category");
    Assert(candidates.All(x => !x.RuntimeEnabled && !x.PrototypeUse), "daily candidates must remain outside normal and prototype runtime gates");
    Assert(candidates.All(x => x.Disposition == "候选审阅"), "daily candidate cards must display review-only disposition");
    Assert(candidates.All(x => x.CandidateProfile == "production_candidate_owner_qa_pending"), "daily candidate stage was not preserved");
    Assert(candidates.All(x => x.VisualScale is > 0.91 and < 0.93), "daily candidates must inherit the approved 0.92 visual scale");
    Assert(candidates.All(x => x.Phases.Single().FrameDurationsMs?.Count == x.FrameCount), "daily candidate per-frame durations were not loaded");
}

static void DeveloperAutonomousDailyCandidateCanRequestPlayback()
{
    var runtime = new DesktopRuntimeHost();
    Assert(runtime.AutonomousDailyCandidateMotions.Count == 6, "runtime review list must expose six autonomous daily candidates");

    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;
    var result = runtime.SubmitDeveloperCandidateMotionAsync(AutonomousDailyCandidateBehaviorIds.PlayfulHop).GetAwaiter().GetResult();
    Assert(result == PetActionResult.Accepted, "developer autonomous daily review was not accepted");
    Assert(request is not null, "developer autonomous daily review did not request playback");
    Assert(request!.Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.PlayfulHop, "wrong daily candidate requested");
    Assert(request.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "daily candidate must use the developer-only gate");
    Assert(!request.Motion.RuntimeEnabled, "developer review must not change the runtime gate");
    Assert(request.Motion.Phases.Single().Frames.Count == 12, "playful hop review frame count changed");
    Assert(request.Motion.HasVariableFrameDurations, "daily candidate per-frame timings were not preserved");

    runtime.CompleteMotion(AutonomousDailyCandidateBehaviorIds.PlayfulHop, "review");
    Assert(runtime.CurrentStablePosture == StablePosture.Stand, "playful hop review must settle to stand");
    Assert(request.Motion.BehaviorId == LifecycleCandidateBehaviorIds.StandIdleMicroloop, "daily review did not return to approved stable stand microloop");

    typeof(DesktopRuntimeHost)
        .GetField("_nextAutonomousDecisionAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(runtime, DateTimeOffset.MinValue);
    request = null;
    runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
    Assert(request is null || request.Motion.AssetBatch != AutonomousDailyCandidateBehaviorIds.AssetBatch, "runtime autonomous tick selected an owner-unapproved daily candidate");
}

static void CommandActionCandidatesAreIndexed()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", "WK-COMMAND-ACTION-CANDIDATES-v3", "manifest.json");
    Assert(File.Exists(manifestPath), "command candidate manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var actions = manifest.RootElement.GetProperty("actions").EnumerateArray().ToArray();
    Assert(actions.Length == 4, "command manifest must describe four actions");

    foreach (var action in actions)
    {
        var frames = action.GetProperty("frames").EnumerateArray().ToArray();
        Assert(frames.Length == action.GetProperty("frame_count").GetInt32(), "manifest frame count mismatch");
        foreach (var frame in frames)
        {
            var framePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, frame.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(framePath), $"manifest frame missing: {framePath}");
            Assert(new FileInfo(framePath).Length == frame.GetProperty("bytes").GetInt64(), "frame byte length mismatch");
            using var stream = File.OpenRead(framePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames.Single();
            Assert(bitmap.PixelWidth == frame.GetProperty("width").GetInt32(), "frame width mismatch");
            Assert(bitmap.PixelHeight == frame.GetProperty("height").GetInt32(), "frame height mismatch");
            Assert(HasAlpha(bitmap.Format), "command frame is not alpha-capable");
            Assert(Sha256(framePath) == frame.GetProperty("sha256").GetString(), "frame sha256 mismatch");
        }
    }

    var catalog = DesktopMotionCatalog.Load(output);
    var commandMotions = catalog.Motions.Where(x => x.AssetBatch == "WK-COMMAND-ACTION-CANDIDATES-v3").ToArray();
    Assert(commandMotions.Length == 4, "command candidates were not indexed");
    Assert(commandMotions.All(x => !x.RuntimeEnabled), "command candidates must remain runtime locked");
    Assert(commandMotions.All(x => x.Disposition == "已过期"), "legacy command candidates must be marked expired in the panel");
    Assert(commandMotions.All(x => x.FrameCount is 8 or 9 or 10), "command candidate frame counts wrong");
}

static void CommandCandidatesStayGated()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var command = runtime.SubmitOwnerCommandAsync("手").GetAwaiter().GetResult();
    Assert(command == PetActionResult.Accepted, "owner command candidate should be accepted for approved manual runtime");
    Assert(request is not null, "owner command candidate did not request playback");
    Assert(request!.Motion.AssetBatch == CommandMockBehaviorIds.AssetBatch, "owner command did not use isolated command candidate assets");
    Assert(!request.Motion.PrototypeUse, "approved owner command must not use prototype preview");
    Assert(request.Motion.RuntimeEnabled, "approved owner command must be runtime enabled");
    Assert(request.ExecutionMode == BehaviorExecutionMode.Normal, "approved owner command must use Normal execution mode");

    typeof(DesktopRuntimeHost)
        .GetField("_currentStartedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(30));
    request = null;
    runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
    Assert(request is null || !request.Motion.BehaviorId.StartsWith("wk.command.", StringComparison.Ordinal), "autonomous pool selected command candidate");
    Assert(request is null || !request.Motion.BehaviorId.StartsWith("wk.magic.", StringComparison.Ordinal), "autonomous pool selected magic candidate");
}

static void DeveloperForcedCommandCandidateCanRequestPlayback()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var result = runtime.SubmitDeveloperMotionAsync(CommandBehaviorIds.Jump).GetAwaiter().GetResult();
    Assert(result == PetActionResult.Accepted, "developer forced command candidate was not accepted");
    Assert(request is not null, "developer forced candidate did not request playback");
    Assert(request!.Motion.BehaviorId == CommandBehaviorIds.Jump, "developer forced playback selected wrong motion");
    Assert(request.Motion.FrameCount == 8, "jump candidate frame count wrong");
}

static void BehaviorAgentCommandMockAssetsAreIndexedAndGated()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-mocks", CommandMockBehaviorIds.AssetBatch, "manifest.json");
    Assert(File.Exists(manifestPath), "behavior agent command mock manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = manifest.RootElement;
    Assert(root.GetProperty("motion_design_approved").GetBoolean(), "mock motion design must be approved");
    Assert(root.GetProperty("production_asset").GetBoolean(), "approved command assets must be production assets");
    Assert(root.GetProperty("visual_approved").GetBoolean(), "approved command visuals must be marked approved");
    Assert(root.GetProperty("runtime_approved").GetBoolean(), "approved command assets must be runtime approved");
    Assert(root.GetProperty("runtime_use").GetBoolean(), "approved command assets must enable runtime use");
    Assert(!root.GetProperty("prototype_use").GetBoolean(), "approved command assets must not use prototype preview");
    Assert(root.GetProperty("asset_stage").GetString() == "runtime_approved_owner_command", "command candidate stage mismatch");

    var actions = root.GetProperty("actions").EnumerateArray().ToArray();
    Assert(actions.Length == 8, "command candidate manifest must include eight posture-aware command branches");
    foreach (var action in actions)
    {
        Assert(action.GetProperty("runtime_approved").GetBoolean(), "approved command action was not runtime approved");
        Assert(action.GetProperty("runtime_use").GetBoolean(), "approved command action did not enable runtime use");
        Assert(!action.GetProperty("prototype_use").GetBoolean(), "approved command action still uses prototype preview");
        var frames = action.GetProperty("frames").EnumerateArray().ToArray();
        Assert(frames.Length == action.GetProperty("frame_count").GetInt32(), "mock action frame count mismatch");
        foreach (var frame in frames)
        {
            var framePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, frame.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(framePath), $"mock frame missing: {framePath}");
            Assert(new FileInfo(framePath).Length == frame.GetProperty("bytes").GetInt64(), "mock frame byte length mismatch");
            Assert(Sha256(framePath) == frame.GetProperty("sha256").GetString(), "mock frame sha256 mismatch");
            using var stream = File.OpenRead(framePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames.Single();
            Assert(bitmap.PixelWidth == frame.GetProperty("width").GetInt32() && bitmap.PixelHeight == frame.GetProperty("height").GetInt32(), "command candidate frame dimensions changed");
            Assert(HasAlpha(bitmap.Format), "mock frame is not alpha-capable");
        }
    }

    var runtime = new DesktopRuntimeHost();
    Assert(runtime.CommandMotionMockMotions.Count == 8, "runtime did not index behavior agent command candidates");
    Assert(runtime.CommandMotionMockMotions.All(x => x.Category == "口令动作"), "approved command candidates must appear in command asset category");
    Assert(runtime.CommandMotionMockMotions.All(x => x.RuntimeEnabled && !x.PrototypeUse), "command candidates must be approved for manual owner runtime");
}

static void BehaviorAgentMockOwnerCommandUsesPostureBranch()
{
    var runtime = new DesktopRuntimeHost();
    runtime.SetBehaviorAgentMockEnabled(true);
    runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default, PetRuntimeState.Default with { CurrentPosture = StablePosture.Prone }, RelationshipState.Default, 31);
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var result = runtime.SubmitOwnerCommandAsync("手").GetAwaiter().GetResult();

    Assert(result == PetActionResult.Accepted, "enabled behavior agent mock should accept owner paw command");
    Assert(request is not null, "enabled behavior agent mock did not request playback");
    Assert(request!.Motion.BehaviorId == MockCommandActionIds.PawProne, "prone paw command did not choose PawProne branch");
    Assert(runtime.CurrentStablePosture == StablePosture.Prone, "paw prone should keep prone posture");
    Assert(runtime.BehaviorAgentSnapshot.Contains(MockCommandActionIds.PawProne, StringComparison.Ordinal), "developer snapshot missing selected mock action");
}

static void ApprovedOwnerCommandsTolerateMissingPostureBridgeAssets()
{
    var runtime = new DesktopRuntimeHost();
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    var sit = runtime.SubmitOwnerCommandAsync("坐").GetAwaiter().GetResult();
    Assert(sit == PetActionResult.Accepted, "sit command was not accepted");
    Assert(runtime.CurrentStablePosture == StablePosture.Prone, "sit command updated posture before playback completed");
    runtime.CompleteMotion(MockCommandActionIds.Sit, "test_complete");
    Assert(runtime.CurrentStablePosture == StablePosture.Sit, "sit command did not update stable posture after playback completed");

    var spin = runtime.SubmitOwnerCommandAsync("转圈").GetAwaiter().GetResult();
    Assert(spin == PetActionResult.Accepted, "spin from sit should play available spin while recording missing bridge");
    Assert(requests.Last().Motion.BehaviorId == MockCommandActionIds.Spin, "spin from sit did not select spin action");
    Assert(requests.Last().Motion.Phases.Any(x => x.Name.Contains(MockCommandActionIds.Spin, StringComparison.Ordinal)), "spin action frames were not included after missing bridge");
    Assert(requests.Last().Motion.RuntimeEnabled && requests.Last().ExecutionMode == BehaviorExecutionMode.Normal, "spin command did not stay on approved Normal path");
}

static void CommandCompletionHoldsExactTerminalFrame()
{
    var runtime = new DesktopRuntimeHost();
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    Assert(runtime.SubmitOwnerCommandAsync("坐").GetAwaiter().GetResult() == PetActionResult.Accepted, "sit command was not accepted");
    var command = requests.Last().Motion;
    var terminal = command.Phases.SelectMany(x => x.Frames).Last();
    var commandRenderScale = MotionVisualSizer.RenderScaleForMotion(command, runtime.ReferenceVisualFramePath);
    runtime.CompleteMotion(command.BehaviorId, "completed");

    var hold = requests.Last().Motion;
    var holdRenderScale = MotionVisualSizer.RenderScaleForMotion(hold, runtime.ReferenceVisualFramePath);
    Assert(hold.BehaviorId == "wk.runtime.posture_hold.sit", "command did not enter stable sit hold");
    Assert(hold.Phases.Count == 1 && hold.Phases[0].Loop, "terminal hold must be a single looping phase");
    Assert(hold.FirstFrame == terminal && hold.FrameCount == 1, "terminal hold flashed to an unrelated frame");
    Assert(Math.Abs(holdRenderScale - commandRenderScale) < 0.000001, $"terminal hold changed rendered size from {commandRenderScale:0.000000} to {holdRenderScale:0.000000}");
    Assert(hold.AssetBatch == command.AssetBatch, "terminal hold lost command asset provenance");
    Assert(requests.Last().ReturnToIdle && requests.Last().LoopCycles == 2, "terminal hold must settle briefly instead of freezing indefinitely");

    runtime.CompleteMotion(hold.BehaviorId, "settled");
    Assert(requests.Last().Motion.BehaviorId == LifecycleCandidateBehaviorIds.SitIdleMicroloop, "settled sit command did not enter the approved sit microloop");
    Assert(Math.Abs(MotionVisualSizer.RenderScaleForMotion(requests.Last().Motion, runtime.ReferenceVisualFramePath) - holdRenderScale) < 0.000001, "sit microloop did not inherit the command batch scale");
}

static void ReportedCommandTerminalHoldsKeepRenderScale()
{
    var cases = new[]
    {
        (Command: "Down", Posture: StablePosture.Sit),
        (Command: "Paw", Posture: StablePosture.Prone),
        (Command: "Jump", Posture: StablePosture.Stand),
        (Command: "Eat", Posture: StablePosture.Prone)
    };

    foreach (var item in cases)
    {
        var runtime = new DesktopRuntimeHost();
        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = item.Posture },
            RelationshipState.Default,
            seed: 83);
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);

        Assert(runtime.SubmitOwnerCommandAsync(item.Command).GetAwaiter().GetResult() == PetActionResult.Accepted, $"{item.Command} command was not accepted");
        var command = requests.Last().Motion;
        var terminal = command.Phases.SelectMany(x => x.Frames).Last();
        var commandRenderScale = MotionVisualSizer.RenderScaleForMotion(command, runtime.ReferenceVisualFramePath);
        runtime.CompleteMotion(command.BehaviorId, "completed");

        var hold = requests.Last().Motion;
        var holdRenderScale = MotionVisualSizer.RenderScaleForMotion(hold, runtime.ReferenceVisualFramePath);
        Assert(hold.BehaviorId.StartsWith("wk.runtime.posture_hold.", StringComparison.Ordinal), $"{item.Command} did not enter a stable posture hold");
        Assert(hold.Phases.Count == 1 && hold.Phases[0].Loop, $"{item.Command} terminal hold must be a single looping phase");
        Assert(hold.FirstFrame == terminal && hold.FrameCount == 1, $"{item.Command} terminal hold flashed to an unrelated frame");
        Assert(Math.Abs(holdRenderScale - commandRenderScale) < 0.000001, $"{item.Command} terminal hold changed rendered size from {commandRenderScale:0.000000} to {holdRenderScale:0.000000}");
        Assert(hold.AssetBatch == command.AssetBatch, $"{item.Command} terminal hold lost command asset provenance");
        Assert(requests.Last().ReturnToIdle && requests.Last().LoopCycles == 2, $"{item.Command} terminal hold must be finite");

        runtime.CompleteMotion(hold.BehaviorId, "settled");
        var expectedIdle = runtime.CurrentStablePosture switch
        {
            StablePosture.Stand => LifecycleCandidateBehaviorIds.StandIdleMicroloop,
            StablePosture.Sit => LifecycleCandidateBehaviorIds.SitIdleMicroloop,
            StablePosture.Prone => LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
            _ => throw new InvalidOperationException("unexpected posture")
        };
        Assert(requests.Last().Motion.BehaviorId == expectedIdle, $"{item.Command} did not enter its posture-compatible microloop after settling");
        Assert(Math.Abs(MotionVisualSizer.RenderScaleForMotion(requests.Last().Motion, runtime.ReferenceVisualFramePath) - holdRenderScale) < 0.000001, $"{item.Command} posture microloop changed scale after settling");
    }
}

static void CommandGroupsShareOneBatchVisualScale()
{
    var runtime = new DesktopRuntimeHost();
    var motions = runtime.CommandMotionMockMotions
        .Where(x => x.RuntimeEnabled && !x.PrototypeUse)
        .ToArray();
    Assert(motions.Length == 8, "expected all eight approved command branches");
    Assert(motions.All(x => x.ScaleReferenceFrames is { Count: 1 }), "command groups must declare one shared scale reference");
    Assert(motions.Select(x => x.ScaleReferenceFrames![0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1, "command groups do not share the same scale reference frame");

    var scales = motions
        .Select(x => MotionVisualSizer.RenderScaleForMotion(x, runtime.ReferenceVisualFramePath))
        .ToArray();
    Assert(scales.Max() - scales.Min() < 0.000001, $"command group scales diverged: {string.Join(", ", scales.Select(x => x.ToString("0.000000")))}");
    var previewSizes = motions.Select(x => x.PreviewRenderSize).ToArray();
    Assert(previewSizes.Max() - previewSizes.Min() < 0.000001, "command asset previews do not use the shared batch scale");
}

static void BehaviorAgentMockClosedKeepsFormalRuntime()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var command = runtime.SubmitOwnerCommandAsync("手").GetAwaiter().GetResult();
    Assert(command == PetActionResult.Accepted, "explicit owner command should not require developer mock toggle");
    Assert(request is not null, "explicit owner command did not request playback");
    Assert(request!.Motion.AssetBatch == CommandMockBehaviorIds.AssetBatch, "explicit owner command did not use approved command candidates");
    Assert(!request.Motion.PrototypeUse && request.Motion.RuntimeEnabled, "owner command must use approved Normal runtime");

    runtime.SetBehaviorAgentMockEnabled(true);
    typeof(DesktopRuntimeHost)
        .GetField("_currentStartedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(60));
    request = null;
    runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
    Assert(request is null || !request.Motion.BehaviorId.StartsWith("wk.command.", StringComparison.Ordinal), "autonomous tick must not trigger command mock assets");
}
static void MagicCandidateAssetsAreIndexed()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", MagicBehaviorIds.AssetBatch, "manifest.json");
    Assert(File.Exists(manifestPath), "magic candidate manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    Assert(manifest.RootElement.GetProperty("runtime_approved").GetBoolean() == false, "magic candidate must not be runtime approved");
    Assert(manifest.RootElement.GetProperty("runtime_use").GetBoolean() == false, "magic candidate must not enable production runtime use");
    Assert(manifest.RootElement.GetProperty("prototype_use").GetBoolean(), "magic candidate must explicitly enable prototype use");
    var actions = manifest.RootElement.GetProperty("actions").EnumerateArray().ToArray();
    Assert(actions.Length == 5, "magic candidate manifest must include four magic actions plus petrification release");

    foreach (var action in actions)
    {
        Assert(!action.GetProperty("runtime_approved").GetBoolean(), "candidate action was runtime approved");
        Assert(!action.GetProperty("runtime_use").GetBoolean(), "candidate action enabled production runtime use");
        Assert(action.GetProperty("prototype_use").GetBoolean(), "candidate action did not enable prototype preview");
        foreach (var phase in action.GetProperty("phases").EnumerateArray())
        {
            foreach (var frame in phase.GetProperty("frames").EnumerateArray())
            {
                var framePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, frame.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Assert(File.Exists(framePath), $"magic frame missing: {framePath}");
                Assert(new FileInfo(framePath).Length == frame.GetProperty("bytes").GetInt64(), "magic frame byte length mismatch");
                Assert(Sha256(framePath) == frame.GetProperty("sha256").GetString(), "magic frame sha256 mismatch");
                using var stream = File.OpenRead(framePath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var bitmap = decoder.Frames.Single();
                Assert(bitmap.PixelWidth == frame.GetProperty("width").GetInt32(), "magic candidate frame width mismatch");
                Assert(bitmap.PixelHeight == frame.GetProperty("height").GetInt32(), "magic candidate frame height mismatch");
                Assert(HasAlpha(bitmap.Format), "magic candidate frame is not alpha-capable");
            }
        }
    }

    var catalog = DesktopMotionCatalog.Load(output);
    var magic = catalog.Motions.Where(x => x.Category == "宠物魔法").ToArray();
    Assert(magic.Length == 5, "magic candidate assets were not indexed");
    Assert(magic.All(x => x.PrototypeUse), "all magic candidates must be prototype-use only");
    Assert(magic.All(x => !x.RuntimeEnabled), "magic candidates must stay out of production runtime");
    Assert(magic.Single(x => x.BehaviorId == MagicBehaviorIds.AccioBroom).FrameCount == 24, "broom playback mapping is incomplete");
    Assert(magic.Single(x => x.BehaviorId == MagicBehaviorIds.AccioBroom).DirectionalFrames?.Count == 8, "broom eight-way frame map is incomplete");
    Assert(magic.Single(x => x.BehaviorId == MagicBehaviorIds.Apparate).FrameCount == 29, "apparate playback mapping is incomplete");
    Assert(magic.Single(x => x.BehaviorId == MagicBehaviorIds.PetrificusTotalus).FrameCount == 18, "petrification-to-coin mapping is incomplete");
}

static void CarRideCandidateAssetsAreIndexedAndGated()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", CarRideBehaviorIds.AssetBatch, "manifest.json");
    Assert(File.Exists(manifestPath), "car ride candidate manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = manifest.RootElement;
    Assert(root.GetProperty("visual_approved").GetBoolean(), "car ride should retain visual approval");
    Assert(!root.GetProperty("art_candidate").GetBoolean(), "approved car ride must not remain an art candidate");
    Assert(root.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "car ride runtime validation state changed");
    Assert(root.GetProperty("runtime_approved").GetBoolean(), "car ride must be runtime approved after Windows renderer QA");
    Assert(root.GetProperty("runtime_use").GetBoolean(), "car ride manual runtime use must be enabled after approval");
    Assert(!root.GetProperty("prototype_use").GetBoolean(), "car ride must no longer depend on prototype preview after approval");
    Assert(root.GetProperty("source_zip_sha256").GetString() == "bf92f38e3cc976236584d8581cbb8f0f1965257c31837c0d1fd69c7670e9f7e1", "source ZIP SHA record changed");
    Assert(root.GetProperty("display_name").GetString() == "Car ride v8", "manifest display name must not say candidate");
    Assert(root.GetProperty("category").GetString() == "Owner manual interaction", "manifest category must describe approved manual interaction");

    var allSequences = root.GetProperty("all_sequences").EnumerateObject().ToArray();
    var frameRefs = allSequences.SelectMany(x => x.Value.EnumerateArray().Select(y => y.GetString())).Where(x => x is not null).ToArray();
    Assert(frameRefs.Length == 222, $"car ride runtime frame count mismatch: {frameRefs.Length}");
    Assert(allSequences.Count(x => x.Name.StartsWith("directions/", StringComparison.Ordinal)) == 8, "car ride direction loop count changed");
    Assert(allSequences.Count(x => x.Name.StartsWith("start/", StringComparison.Ordinal)) == 8, "car ride start sequence count changed");
    Assert(allSequences.Count(x => x.Name.StartsWith("brake/", StringComparison.Ordinal)) == 8, "car ride brake sequence count changed");
    Assert(allSequences.Count(x => x.Name.StartsWith("turn/", StringComparison.Ordinal)) == 16, "car ride turn sequence count changed");
    Assert(allSequences.Count(x => x.Name.StartsWith("expressions/", StringComparison.Ordinal)) == 5, "car ride expression sequence count changed");

    var batchRoot = Path.GetDirectoryName(manifestPath)!;
    foreach (var phase in root.GetProperty("phases").EnumerateArray())
    {
        foreach (var frame in phase.GetProperty("frames").EnumerateArray())
        {
            var framePath = Path.Combine(batchRoot, frame.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(framePath), $"car ride frame missing: {framePath}");
            Assert(new FileInfo(framePath).Length == frame.GetProperty("bytes").GetInt64(), "car ride frame byte length mismatch");
            Assert(Sha256(framePath) == frame.GetProperty("sha256").GetString(), "car ride frame sha256 mismatch");
            using var stream = File.OpenRead(framePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames.Single();
            Assert(bitmap.PixelWidth == 1024 && bitmap.PixelHeight == 1024, "car ride frame must stay 1024x1024");
            Assert(HasAlpha(bitmap.Format), "car ride frame is not alpha-capable");
        }
    }

    var catalog = DesktopMotionCatalog.Load(output);
    var motions = catalog.Motions.Where(x => string.Equals(x.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase)).ToArray();
    Assert(motions.Length == 1, "car ride candidate was not indexed exactly once");
    Assert(motions[0].DisplayName == "Car ride v8", "car ride display name must not say candidate");
    Assert(motions[0].RuntimeEnabled, "car ride must be available to the approved manual runtime path");
    Assert(!motions[0].PrototypeUse, "car ride must not depend on prototype preview after approval");
    Assert(motions[0].Effect == DesktopMotionEffect.CarRide, "car ride effect mapping missing");
    Assert(motions[0].DirectionalFrames?.Count == 8, "car ride eight-way frame map is incomplete");
    var phaseNames = motions[0].Phases.Select(x => x.Name).ToArray();
    Assert(motions[0].FrameCount == 102, $"car ride runtime should use the extended 102-frame route, got {motions[0].FrameCount}");
    foreach (var direction in new[] { "right", "front-right", "front", "front-left", "left", "rear-left", "rear", "rear-right" })
        Assert(phaseNames.Contains($"loop:{direction}"), $"car ride loop for {direction} missing");
    foreach (var turn in new[] { "right-to-front-right", "front-right-to-front", "front-to-front-left", "front-left-to-left", "left-to-rear-left", "rear-left-to-rear", "rear-to-rear-right", "rear-right-to-right" })
        Assert(phaseNames.Contains($"turn:{turn}"), $"car ride turn {turn} missing");
    Assert(phaseNames.Contains("expression:head-tilt"), "car ride expression phase missing");
    Assert(phaseNames.Last() == "interrupt_exit", "car ride interrupt exit must remain last");
}

static void OwnerAndPanelCarRideUseApprovedNormalGate()
{
    var runtime = new DesktopRuntimeHost();
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    var ownerResult = runtime.SubmitCarRideAsync(BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    Assert(ownerResult == PetActionResult.Accepted, "owner context menu car ride was not accepted");
    Assert(requests.Count == 1, "accepted car ride did not request playback");
    Assert(requests[0].ExecutionMode == BehaviorExecutionMode.Normal, "car ride did not use approved normal mode");
    Assert(requests[0].Source == BehaviorRequestSource.OwnerContextMenu, "owner car ride source was not preserved");

    var duplicate = runtime.SubmitCarRideAsync(BehaviorRequestSource.ControlPanel).GetAwaiter().GetResult();
    Assert(duplicate == PetActionResult.Deferred, "duplicate car ride should not create a second active request");
    Assert(requests.Count == 1, "duplicate car ride requested concurrent playback");

    runtime.StopAsync("test:stop-car-ride").GetAwaiter().GetResult();
    var panelResult = runtime.SubmitCarRideAsync(BehaviorRequestSource.ControlPanel).GetAwaiter().GetResult();
    Assert(panelResult == PetActionResult.Accepted, "control panel car ride was not accepted after stop");
    Assert(requests.Count == 3, "stop recovery plus panel car ride should request two additional motions");
    Assert(requests.Last().ExecutionMode == BehaviorExecutionMode.Normal, "panel car ride did not use approved normal mode");
    Assert(requests.Last().Source == BehaviorRequestSource.ControlPanel, "panel car ride source was not preserved");
}

static void NonOwnerSourcesCannotTriggerCarRide()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var dialoguePrototype = InvokePrivateSubmitBehavior(runtime, BehaviorRequestSource.Dialogue, BehaviorExecutionMode.PrototypePreview);
    var autonomousPrototype = InvokePrivateSubmitBehavior(runtime, BehaviorRequestSource.AutonomousTick, BehaviorExecutionMode.PrototypePreview);
    var dialogueNormal = InvokePrivateSubmitBehavior(runtime, BehaviorRequestSource.Dialogue, BehaviorExecutionMode.Normal);
    var autonomousNormal = InvokePrivateSubmitBehavior(runtime, BehaviorRequestSource.AutonomousTick, BehaviorExecutionMode.Normal);

    Assert(dialoguePrototype == PetActionResult.Deferred, "dialogue was allowed to prototype car ride");
    Assert(autonomousPrototype == PetActionResult.Deferred, "autonomous tick was allowed to prototype car ride");
    Assert(dialogueNormal == PetActionResult.Deferred, "dialogue was allowed to run approved manual car ride");
    Assert(autonomousNormal == PetActionResult.Deferred, "autonomous tick was allowed to run approved manual car ride");
    Assert(request is null, "forbidden car ride source requested playback");
}

static PetActionResult InvokePrivateSubmitBehavior(DesktopRuntimeHost runtime, BehaviorRequestSource source, BehaviorExecutionMode mode) =>
    (PetActionResult)typeof(DesktopRuntimeHost)
        .GetMethod("SubmitBehavior", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(runtime, new object[] { source, CarRideBehaviorIds.CarRide, "test", 18, mode, false })!;
static void PetrifiedCoinAssetsAndChecksumsAreComplete()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var root = Path.Combine(output, "WukongAssets", "action-batches", MagicBehaviorIds.AssetBatch);
    var manifestPath = Path.Combine(root, "coin-manifest.json");
    var checksumPath = Path.Combine(root, "coin-checksums.sha256");
    Assert(File.Exists(manifestPath), "coin manifest missing from output");
    Assert(File.Exists(checksumPath), "coin checksum file missing from output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var states = manifest.RootElement.GetProperty("states").EnumerateArray().ToArray();
    Assert(states.Length == 4, "coin must expose vivid, flat, faded, and exhausted states");
    var pngPaths = states.SelectMany(x => new[]
    {
        x.GetProperty("front").GetString()!,
        x.GetProperty("back").GetString()!
    }).ToList();
    foreach (var directory in manifest.RootElement.GetProperty("flip").GetProperty("front_to_back").GetProperty("directories_by_state").EnumerateObject())
    {
        var files = Directory.GetFiles(Path.Combine(root, directory.Value.GetString()!.Replace('/', Path.DirectorySeparatorChar)), "*.png").OrderBy(x => x).ToArray();
        Assert(files.Length == 9, $"coin flip {directory.Name} must have nine frames");
        pngPaths.AddRange(files.Select(x => Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/')));
    }
    Assert(pngPaths.Count == 44, "coin package must contain eight faces and 36 flip frames");

    var checksums = File.ReadAllLines(checksumPath)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Split("  ", 2, StringSplitOptions.None))
        .ToDictionary(x => x[1], x => x[0], StringComparer.Ordinal);
    foreach (var relative in pngPaths)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert(File.Exists(path), $"coin frame missing: {relative}");
        Assert(checksums.TryGetValue(relative, out var expected), $"coin checksum missing: {relative}");
        Assert(Sha256(path) == expected, $"coin checksum mismatch: {relative}");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames.Single();
        Assert(bitmap.PixelWidth == 1024 && bitmap.PixelHeight == 1024, "coin frame size must be normalized to 1024 square");
        Assert(HasAlpha(bitmap.Format), "coin frame is not alpha-capable");
    }
}

static void OwnerAndPanelMagicUsePrototypePreviewGate()
{
    var runtime = new DesktopRuntimeHost();
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    var ownerResult = runtime.SubmitMagicAsync(MagicBehaviorIds.AccioBroom, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    var panelResult = runtime.SubmitMagicAsync(MagicBehaviorIds.Scourgify, BehaviorRequestSource.ControlPanel).GetAwaiter().GetResult();

    Assert(ownerResult == PetActionResult.Accepted, "owner context menu magic was not accepted");
    Assert(panelResult == PetActionResult.Accepted, "control panel magic was not accepted");
    Assert(requests.Count == 2, "accepted magic did not request playback");
    Assert(requests.All(x => x.ExecutionMode == BehaviorExecutionMode.PrototypePreview), "magic did not use prototype preview execution mode");
    Assert(requests[0].Source == BehaviorRequestSource.OwnerContextMenu, "owner menu source was not preserved");
    Assert(requests[1].Source == BehaviorRequestSource.ControlPanel, "control panel source was not preserved");
}

static void NonOwnerSourcesCannotPrototypePreviewMagic()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var dialogueResult = runtime.SubmitMagicAsync(MagicBehaviorIds.AccioBroom, BehaviorRequestSource.Dialogue).GetAwaiter().GetResult();
    var autonomousResult = runtime.SubmitMagicAsync(MagicBehaviorIds.Apparate, BehaviorRequestSource.AutonomousTick).GetAwaiter().GetResult();

    Assert(dialogueResult == PetActionResult.Deferred, "dialogue was allowed to prototype magic");
    Assert(autonomousResult == PetActionResult.Deferred, "autonomous tick was allowed to prototype magic");
    Assert(request is null, "forbidden prototype source requested playback");
}

static void PetrifiedCoinInactivityReachesAllFourStates()
{
    var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    var runtime = new DesktopRuntimeHost(now: () => now);
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    Assert(runtime.IsCoinAssetsReady, "coin assets failed to load");
    Assert(runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult() == PetActionResult.Accepted, "petrification was not accepted");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Vivid && runtime.CurrentCoinSide == PetrifiedCoinSide.Front, "coin did not start vivid front");
    Assert(runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult() == PetActionResult.Accepted, "coin activity reset was not accepted");

    now += TimeSpan.FromSeconds(4.9);
    Assert(!runtime.RefreshPetrifiedCoinState(), "coin settled before five seconds");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Vivid, "coin did not remain vivid for the full initial hold");

    now += TimeSpan.FromMilliseconds(250);
    Assert(runtime.RefreshPetrifiedCoinState(), "coin did not settle to flat after five seconds");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Flat, "coin settled to wrong state");

    now = new DateTimeOffset(2026, 8, 15, 12, 10, 2, TimeSpan.Zero);
    Assert(runtime.RefreshPetrifiedCoinState(), "coin did not fade at ten minutes");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Faded, "coin ten-minute state is wrong");

    now = new DateTimeOffset(2026, 8, 15, 12, 20, 2, TimeSpan.Zero);
    Assert(runtime.RefreshPetrifiedCoinState(), "coin did not exhaust at twenty minutes");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Exhausted, "coin twenty-minute state is wrong");
    Assert(requests.Last().Motion.Phases.Single().Frames.Single().EndsWith("state-04-exhausted.png", StringComparison.OrdinalIgnoreCase), "exhausted state requested wrong frame");
}

static void PetrifiedCoinClicksResetAndDoubleClicksFlip()
{
    var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    var runtime = new DesktopRuntimeHost(now: () => now);
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);
    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();

    var transition = requests.Last().Motion;
    var visibleAt = now + TimeSpan.FromMilliseconds(transition.Phases.TakeWhile(x => !x.Loop).Sum(x => x.Frames.Count) * transition.FrameDurationMs);
    now = visibleAt + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2);
    runtime.RefreshPetrifiedCoinState();
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Faded, "test setup did not reach faded state");

    var toBack = runtime.SubmitPetrifiedCoinDoubleClickAsync().GetAwaiter().GetResult();
    Assert(toBack == PetActionResult.Accepted, "front double click was not accepted");
    Assert(runtime.CurrentCoinSide == PetrifiedCoinSide.Back && runtime.CurrentCoinState == PetrifiedCoinState.Faded, "front double click did not preserve color state on back");
    Assert(requests.Last().Motion.FrameCount == 9, "front-to-back flip must have nine frames");

    var toFront = runtime.SubmitPetrifiedCoinDoubleClickAsync().GetAwaiter().GetResult();
    Assert(toFront == PetActionResult.Accepted, "back double click was not accepted");
    Assert(runtime.CurrentCoinSide == PetrifiedCoinSide.Front && runtime.CurrentCoinState == PetrifiedCoinState.Vivid, "back double click did not reset to vivid front");
    Assert(requests.Last().Motion.FrameCount == 9, "back-to-front flip must have nine frames");
    Assert(requests.Last().Motion.Phases.Single().Frames.Last().Contains("vivid", StringComparison.OrdinalIgnoreCase), "back-to-front reset did not end on vivid artwork");

    now += TimeSpan.FromMinutes(20);
    runtime.RefreshPetrifiedCoinState();
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Exhausted, "reset inactivity clock did not continue aging");
    var click = runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult();
    Assert(click == PetActionResult.Accepted, "single coin click was not accepted");
    Assert(runtime.CurrentCoinSide == PetrifiedCoinSide.Front && runtime.CurrentCoinState == PetrifiedCoinState.Vivid, "single click did not restore vivid front");
    Assert(runtime.SubmitPetrifiedCoinClickAsync(BehaviorRequestSource.Dialogue).GetAwaiter().GetResult() == PetActionResult.Deferred, "dialogue was allowed to mutate coin state");
}

static void PetrifiedCoinTimingIsConfigurable()
{
    var started = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    var now = started;
    var options = new PetrifiedCoinOptions(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4));
    var runtime = new DesktopRuntimeHost(options, () => now);
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);
    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.ControlPanel).GetAwaiter().GetResult();

    var transition = requests.Last().Motion;
    var visibleAt = started + TimeSpan.FromMilliseconds(transition.Phases.TakeWhile(x => !x.Loop).Sum(x => x.Frames.Count) * transition.FrameDurationMs);
    now = visibleAt + TimeSpan.FromMilliseconds(150);
    Assert(runtime.RefreshPetrifiedCoinState(), "custom settle threshold was not used");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Flat, "custom settle threshold selected wrong state");
    now = visibleAt + TimeSpan.FromSeconds(2.1);
    Assert(runtime.RefreshPetrifiedCoinState(), "custom fade threshold was not used");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Faded, "custom fade threshold selected wrong state");
    now = visibleAt + TimeSpan.FromSeconds(4.1);
    Assert(runtime.RefreshPetrifiedCoinState(), "custom exhausted threshold was not used");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Exhausted, "custom exhausted threshold selected wrong state");
}

static void PetrifiedCoinDefaultTimingAndVisualScaleAreStable()
{
    var started = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    var now = started;
    var runtime = new DesktopRuntimeHost(now: () => now);
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    var petrify = requests.Last().Motion;
    var petrifyIntro = petrify.Phases.Single(x => x.Name == "intro");
    var initialCoin = petrify.Phases.Single(x => x.Loop);
    Assert(petrifyIntro.VisualScale is > 0.91 and < 0.93, "petrification intro must match the approved pet visual scale");
    Assert(petrify.VisualScale is > 0.91 and < 0.93, "petrification motion scale is larger than the approved pet visual scale");
    Assert(initialCoin.VisualScale is > 0.66 and < 0.67, "initial coin frame must use the coin phase scale immediately");
    Assert(petrify.FrameDurationMs >= 170, "petrification transition is still too fast");
    Assert(runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult() == PetActionResult.Accepted, "coin activity reset was not accepted");
    var explicitCoin = requests.Last().Motion;
    var initialCoinRenderScale = MotionVisualSizer.RenderScaleForPhase(petrify, initialCoin, runtime.ReferenceVisualFramePath);
    var explicitCoinRenderScale = MotionVisualSizer.RenderScaleForMotion(explicitCoin, runtime.ReferenceVisualFramePath);
    Assert(Math.Abs(initialCoinRenderScale - explicitCoinRenderScale) < 0.000001, $"initial coin changed rendered size from {initialCoinRenderScale:0.000000} to {explicitCoinRenderScale:0.000000}");

    now = started + TimeSpan.FromSeconds(4.99);
    Assert(!runtime.RefreshPetrifiedCoinState(), "default coin hold changed before five seconds");
    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusRelease, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    Assert(requests.Last().Motion.VisualScale is > 0.91 and < 0.93, "petrification release must match the approved pet visual scale");
    now = started + TimeSpan.FromSeconds(20);
    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    Assert(runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult() == PetActionResult.Accepted, "retriggered coin activity reset was not accepted");
    now += TimeSpan.FromSeconds(4.99);
    Assert(!runtime.RefreshPetrifiedCoinState(), "retriggered coin did not restart the five-second timer");
    now += TimeSpan.FromMilliseconds(20);
    Assert(runtime.RefreshPetrifiedCoinState(), "coin did not advance after restarted five-second hold");
    Assert(requests.Last().Motion.VisualScale > 0.66 && requests.Last().Motion.VisualScale < 0.67, "coin hold motion must render at two-thirds visual scale");
}

static void MotionVisualSizingNormalizesAlphaBounds()
{
    static double VisibleHeightRatio(PlayableMotion motion, string referenceFrame)
    {
        var metrics = MotionVisualSizer.Measure(motion.FirstFrame);
        var scale = MotionVisualSizer.RenderScaleFor(motion.FirstFrame, referenceFrame, motion.VisualScale);
        return metrics.VisibleHeight / (double)metrics.CanvasHeight * scale;
    }

    var runtime = new DesktopRuntimeHost();
    var reference = runtime.ReferenceVisualFramePath;
    var normal = runtime.Motions.First(x => x.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop);
    var broom = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.AccioBroom);
    var petrify = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.PetrificusTotalus);
    var command = runtime.CommandMotionMockMotions.First(x => x.BehaviorId == MockCommandActionIds.Sit);
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    var normalHeight = VisibleHeightRatio(normal, reference);
    var broomRatio = VisibleHeightRatio(broom, reference) / normalHeight;
    var petrifyRatio = VisibleHeightRatio(petrify, reference) / normalHeight;
    var commandRatio = VisibleHeightRatio(command, reference) / normalHeight;

    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult();
    var coinRatio = VisibleHeightRatio(requests.Last().Motion, reference) / normalHeight;

    Assert(broomRatio is > 1.42 and < 1.52, $"magic pet visual height ratio was {broomRatio:0.000}");
    Assert(petrifyRatio is > 0.98 and < 1.02, $"petrification intro visual height ratio was {petrifyRatio:0.000}");
    Assert(coinRatio is > 0.70 and < 0.75, $"petrified coin visual height ratio was {coinRatio:0.000}");
    Assert(commandRatio is > 0.98 and < 1.02, $"approved command visual height ratio was {commandRatio:0.000}");
    Assert(broom.VisibleSubjectHeight > 0 && petrify.VisibleSubjectHeight > 0, "visible alpha bounds were not measured");
}


static void StopClearsPetrificationAndRequestsIdle()
{
    var runtime = new DesktopRuntimeHost();
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    var petrify = runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    Assert(petrify == PetActionResult.Accepted, "petrify was not accepted");
    Assert(runtime.IsPetrified, "runtime did not enter petrified state");
    Assert(requests.Last().ReturnToIdle == false, "petrify should hold instead of immediately returning to idle");

    var stop = runtime.StopAsync("test:stop").GetAwaiter().GetResult();
    Assert(stop == PetActionResult.Interrupted, "stop did not return interrupted");
    Assert(!runtime.IsPetrified, "stop did not clear petrified state");
    Assert(requests.Last().Motion.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop, "stop did not request approved idle recovery");
}

static void MainWindowContextMenuMatchesContract()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            var window = new MainWindow();
            var root = (FrameworkElement)window.FindName("Root");
            var menu = root.ContextMenu ?? throw new InvalidOperationException("main context menu missing");
            var topLevelItems = menu.Items.OfType<MenuItem>().ToArray();
            var topLevelTags = topLevelItems.Select(x => x.Tag?.ToString()).Where(x => x is not null).ToArray();
            Assert(!topLevelTags.Contains(CarRideBehaviorIds.CarRide, StringComparer.Ordinal), "car ride must not be a top-level context menu item");
            Assert(!topLevelItems.Any(x => Equals(x.Header, "澶у皬")), "scale menu must not be shown in the context menu");

            var playMenu = topLevelItems.Single(x => x.Items.OfType<MenuItem>().Any(child => Equals(child.Tag?.ToString(), CarRideBehaviorIds.CarRide)));
            var playChildren = playMenu.Items.OfType<MenuItem>().ToArray();
            Assert(playChildren.Length == 2, "play submenu should contain exactly car ride and locked walk");
            Assert(playChildren[0].Tag?.ToString() == CarRideBehaviorIds.CarRide, "car ride must be the first play submenu item");
            Assert(playChildren[1].IsEnabled == false, "walk should be shown as locked");

            var commands = topLevelItems.Single(x => Equals(x.Header, "口令"));
            var commandChildren = commands.Items.OfType<MenuItem>().ToArray();
            Assert(commandChildren.Select(x => x.Header?.ToString()).SequenceEqual(new[] { "坐", "卧", "手", "跳", "转圈", "吃" }), "command submenu order changed");
            Assert(commandChildren.All(x => x.IsEnabled), "command mock menu items should be available through BehaviorRequest when mock is enabled");
            Assert(!commandChildren.Any(x => Equals(x.Header, "停")), "command submenu must not include stop");

            var magic = topLevelItems.Single(x => x.Items.OfType<MenuItem>().Any(child => Equals(child.Header, "Accio Broom")));
            Assert(magic.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(new[] { "Accio Broom", "Apparate", "Petrificus Totalus", "Scourgify" }), "magic submenu order changed");
            var magicIndex = Array.IndexOf(topLevelItems, magic);
            Assert(magicIndex >= 0 && topLevelItems.Length > magicIndex + 2, "magic, stop, and open panel menu group is incomplete");
            Assert(topLevelItems[magicIndex + 1].Items.Count == 0 && topLevelItems[magicIndex + 2].Items.Count == 0, "stop must sit immediately above open panel");

            window.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw failure;
}

static void BroomDirectionQuantizerCoversEightDirections()
{
    var origin = new Point(10, 10);
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(20, 10)) == "right", "right direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(20, 20)) == "down-right", "down-right direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(10, 20)) == "down", "down direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(0, 20)) == "down-left", "down-left direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(0, 10)) == "left", "left direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(0, 0)) == "up-left", "up-left direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(10, 0)) == "up", "up direction mismatch");
    Assert(MainWindow.ResolveEightWayDirection(origin, new Point(20, 0)) == "up-right", "up-right direction mismatch");
}

static void CarRideDirectionQuantizerMatchesV8Directions()
{
    var origin = new Point(10, 10);
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(20, 10)) == "right", "car ride right direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(20, 20)) == "front-right", "car ride front-right direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(10, 20)) == "front", "car ride front direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(0, 20)) == "front-left", "car ride front-left direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(0, 10)) == "left", "car ride left direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(0, 0)) == "rear-left", "car ride rear-left direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(10, 0)) == "rear", "car ride rear direction mismatch");
    Assert(MainWindow.ResolveCarRideDirection(origin, new Point(20, 0)) == "rear-right", "car ride rear-right direction mismatch");

    var path = MainWindow.BuildCarRidePreviewPath(new Point(400, 300), new Rect(0, 0, 1920, 1080), 320, 320);
    Assert(path.Count >= 10, "car ride route should cover the full direction ring");
    var routeDirections = new List<string>();
    var current = new Point(400, 300);
    foreach (var target in path)
    {
        routeDirections.Add(MainWindow.ResolveCarRideDirection(current, target));
        current = target;
    }

    foreach (var direction in new[] { "right", "front-right", "front", "front-left", "left", "rear-left", "rear", "rear-right" })
        Assert(routeDirections.Contains(direction), $"car ride route does not exercise {direction}");
}
static void ApparateTargetStaysVisibleAndRelocates()
{
    var workArea = new Rect(0, 0, 1920, 1080);
    var current = new Point(120, 140);
    for (var i = 0; i < 24; i++)
    {
        var target = MainWindow.ChooseApparateTarget(current, workArea, 320, 320);
        Assert(target.X >= workArea.Left && target.X <= workArea.Right - 320, "apparate target X leaves work area");
        Assert(target.Y >= workArea.Top && target.Y <= workArea.Bottom - 320, "apparate target Y leaves work area");
        Assert(Math.Sqrt(Math.Pow(target.X - current.X, 2) + Math.Pow(target.Y - current.Y, 2)) >= 120, "apparate should relocate instead of reappearing in place");
    }
}
static void ControlPanelExposesMagicSpecialsTab()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            var panel = new ControlPanelWindow(new DesktopRuntimeHost());
            Assert(panel.FindName("NormalAssetsPanel") is ScrollViewer, "normal assets panel missing");
            Assert(panel.FindName("PlayAssetsPanel") is ScrollViewer, "play assets panel missing");
            var playList = panel.FindName("PlayAssetList") as ItemsControl;
            Assert(playList is not null, "play asset list missing");
            Assert(panel.FindName("CommandAssetsPanel") is ScrollViewer, "command assets panel missing");
            var commandList = panel.FindName("CommandAssetList") as ItemsControl;
            Assert(commandList is not null, "command asset list missing");
            Assert(panel.FindName("MagicAssetsPanel") is ScrollViewer, "magic specials panel missing");
            Assert(panel.FindName("LifecycleCandidateList") is ItemsControl, "lifecycle candidate developer list missing");
            Assert(panel.FindName("AutonomousDailyAssetsPanel") is ScrollViewer, "autonomous daily review panel missing");
            var dailyList = panel.FindName("AutonomousDailyAssetList") as ItemsControl;
            Assert(dailyList is not null, "autonomous daily review list missing");
            Assert(dailyList!.Items.Count == 6, "autonomous daily review list must display six candidates");
            var interactionReview = panel.FindName("InteractionReviewAssetList") as ItemsControl;
            Assert(interactionReview is not null && interactionReview.Items.Count == 1, "interaction review list must display the gated prone-touch candidate");
            Assert(interactionReview!.Items.OfType<PlayableMotion>().Single().BehaviorId == Phase15BehaviorIds.ProneTouch, "interaction review list exposed the wrong behavior");
            Assert(panel.FindName("AutonomousDailyAssetsTabButton") is Button { Visibility: Visibility.Visible }, "autonomous daily developer tab must remain discoverable before authentication");
            Assert(panel.FindName("AutonomousDailyAssetsPanel") is ScrollViewer { Visibility: Visibility.Collapsed }, "autonomous daily candidates must remain inaccessible before developer authentication");
            Assert(panel.FindName("PreviewBackgroundButton") is Button, "light/dark preview background control missing");
            var list = panel.FindName("MagicSpecialList") as ItemsControl;
            Assert(list is not null, "magic specials list missing");
            Assert(commandList!.Items.Count == 12, "command assets tab must display eight approved v4 commands plus four expired v3 references");
            Assert(commandList.Items.OfType<PlayableMotion>().Count(x => x.AssetBatch == CommandMockBehaviorIds.AssetBatch && x.RuntimeEnabled) == 8, "command assets tab must include eight approved v4 commands");
            Assert(commandList.Items.OfType<PlayableMotion>().Count(x => x.Disposition == "已过期") == 4, "command assets tab must mark four legacy v3 commands expired");
            Assert(list!.Items.Count == 4, "magic specials must display four owner-facing cards");
            Assert(playList!.Items.Count == 1, "play assets tab must display one car ride card");
            var carRideDeveloper = panel.FindName("CarRideCandidateList") as ItemsControl;
            Assert(carRideDeveloper is not null, "developer car ride candidate list missing");
            Assert(carRideDeveloper!.Items.Count == 1, "developer car ride list must display one candidate motion");
            var lifecycle = (ItemsControl)panel.FindName("LifecycleCandidateList")!;
            Assert(lifecycle.Items.Count == 4, "developer lifecycle profile must display four candidate motions");
            panel.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw failure;
}

static void ExpiredAssetCardsAreGrayButRemainPreviewable()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            var runtime = new DesktopRuntimeHost();
            var panel = new ControlPanelWindow(runtime);
            var expired = runtime.Motions.First(x => x.Disposition == "已过期");
            Assert(expired.IsExpired, "expired motion did not expose its visual state");
            Assert(expired.IsUsable, "expired motion lost its preview frames");

            var cardStyle = (Style)panel.FindResource("AssetCard");
            var cardTrigger = cardStyle.Triggers.OfType<DataTrigger>().Single();
            Assert(Convert.ToBoolean(cardTrigger.Value), "expired card trigger value changed");
            Assert(cardTrigger.Setters.OfType<Setter>().Any(x => x.Property == Border.BackgroundProperty), "expired card does not switch to a gray background");
            Assert(!cardTrigger.Setters.OfType<Setter>().Any(x => x.Property == UIElement.IsEnabledProperty), "expired card style disables preview interaction");

            var imageStyle = (Style)panel.FindResource("AssetPreviewImage");
            var imageTrigger = imageStyle.Triggers.OfType<DataTrigger>().Single();
            Assert(Convert.ToBoolean(imageTrigger.Value), "expired preview trigger value changed");
            var opacity = imageTrigger.Setters.OfType<Setter>().Single(x => x.Property == UIElement.OpacityProperty);
            Assert(Convert.ToDouble(opacity.Value) > 0, "expired preview was made invisible");
            Assert(panel.FindResource("AssetDispositionBadge") is Style, "asset disposition badge style cannot be resolved");
            panel.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw failure;
}

static void ControlPanelTabButtonsShareVisualMetrics()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            _ = EnsureTestApplication();
            var panel = new ControlPanelWindow(new DesktopRuntimeHost());
            var selected = (Style)panel.FindResource("PanelTabButtonSelected");
            var normal = (Style)panel.FindResource("PanelTabButton");
            var tabNames = new[]
            {
                "ProfilePetTabButton",
                "ProfileOwnerTabButton",
                "ProfileRelationTabButton",
                "ProfileMemoryTabButton",
                "ModelConfigTabButton",
                "MemoryConfigTabButton",
                "PetSettingTabButton",
                "NormalAssetsTabButton",
                "MagicAssetsTabButton",
                "BaseAssetsTabButton",
                "PlayAssetsTabButton",
                "CommandAssetsTabButton"
            };
            foreach (var name in tabNames)
            {
                var button = panel.FindName(name) as Button;
                Assert(button is not null, $"tab button missing: {name}");
                Assert(ReferenceEquals(button!.Style, selected) || ReferenceEquals(button.Style, normal), $"tab button does not use shared style: {name}");
                Assert(button.MinWidth >= 104 && button.MinHeight >= 40, $"tab button visual metrics are too small: {name}");
                Assert(button.HorizontalContentAlignment == HorizontalAlignment.Center && button.VerticalContentAlignment == VerticalAlignment.Center, $"tab button alignment changed: {name}");
            }
            panel.Close();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw failure;
}

static void ControlPanelCarRideCopyMatchesApprovedRuntimeState()
{
    var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Wukong.Desktop", "ControlPanelWindow.xaml"));
    var xaml = File.ReadAllText(xamlPath);
    Assert(xaml.Contains("x:Name=\"PlayAssetsTabButton\"", StringComparison.Ordinal), "play asset tab missing");
    Assert(xaml.Contains("x:Name=\"PlayAssetList\"", StringComparison.Ordinal), "play asset list missing");
    Assert(xaml.Contains("Click=\"ShowPlayAsset_Click\"", StringComparison.Ordinal), "play asset execution button missing");
    Assert(!xaml.Contains("x:Name=\"CarRideSpecialList\"", StringComparison.Ordinal), "car ride still appears inside magic specials");
    Assert(xaml.Contains("x:Name=\"CarRideCandidateList\"", StringComparison.Ordinal), "developer car ride list missing");
    Assert(!xaml.Contains("Car ride candidate", StringComparison.Ordinal), "control panel still says car ride candidate");
    Assert(!xaml.Contains("not runtime approved", StringComparison.OrdinalIgnoreCase), "control panel still claims car ride is not runtime approved");
    Assert(!xaml.Contains("Owner-only PrototypePreview candidate", StringComparison.Ordinal), "control panel still claims PrototypePreview candidate state");
}

static void ShowcaseDurationsAndPhysicalRoutesStayBounded()
{
    var sampled = Enumerable.Range(1, 64).Select(seed => MainWindow.ChooseShowcaseDuration(new Random(seed))).ToArray();
    Assert(sampled.All(x => x >= TimeSpan.FromSeconds(10) && x <= TimeSpan.FromSeconds(20)), "broom/car showcase duration left the 10-20 second contract");
    Assert(sampled.Distinct().Count() > 8, "showcase duration is not meaningfully randomized");

    var workArea = new Rect(0, 0, 1920, 1080);
    var start = new Point(320, 280);
    var expectedDuration = TimeSpan.FromSeconds(16);
    var route = MainWindow.BuildCarRidePhysicalRoute(start, workArea, 320, 320, expectedDuration, new Random(240821));
    Assert(route.Count >= 5, "car ride route did not retain enough long physical segments");
    Assert(Math.Abs(route.Sum(x => x.Duration.TotalMilliseconds) - expectedDuration.TotalMilliseconds) < 1, "car ride route duration drifted");
    Assert(route[0].Easing == MotionEasing.Accelerate && route[^1].Easing == MotionEasing.Decelerate, "car ride did not accelerate and brake at route boundaries");

    var current = start;
    foreach (var segment in route)
    {
        Assert(segment.Duration >= TimeSpan.FromMilliseconds(850), "car ride introduced a short jitter segment");
        Assert(segment.Target.X >= workArea.Left && segment.Target.X <= workArea.Right - 320, "car ride route left horizontal work area");
        Assert(segment.Target.Y >= workArea.Top && segment.Target.Y <= workArea.Bottom - 320, "car ride route left vertical work area");
        Assert(MainWindow.ResolveCarRideDirection(current, segment.Target) == segment.Direction, "car body direction no longer matches window movement");
        current = segment.Target;
    }
}
static void GestureInterpreterDistinguishesGestures()
{
    Assert(GestureInterpreter.Interpret(new GestureSample(new Point(1, 1), new Point(2, 2), TimeSpan.FromMilliseconds(120), 1, true)) == PetGestureKind.None, "single click should not trigger touch");
    Assert(GestureInterpreter.Interpret(new GestureSample(new Point(1, 1), new Point(36, 20), TimeSpan.FromMilliseconds(400), 1, true)) == PetGestureKind.Stroke, "stroke not detected");
    Assert(GestureInterpreter.Interpret(new GestureSample(new Point(1, 1), new Point(120, 1), TimeSpan.FromMilliseconds(300), 1, true)) == PetGestureKind.Drag, "drag not detected");
    Assert(GestureInterpreter.Interpret(new GestureSample(new Point(1, 1), new Point(1, 1), TimeSpan.FromMilliseconds(120), 2, true)) == PetGestureKind.DoubleClick, "double click not detected");
    Assert(GestureInterpreter.Interpret(new GestureSample(new Point(1, 1), new Point(1, 1), TimeSpan.FromMilliseconds(120), 3, true)) == PetGestureKind.RapidTap, "rapid tap not detected");
    Assert(GestureInterpreter.Interpret(new GestureSample(new Point(1, 1), new Point(1, 1), TimeSpan.FromMilliseconds(120), 1, false)) == PetGestureKind.None, "transparent area should not trigger");
}

static void RapidTapHasPriorityOverOwnerTouch()
{
    var sample = new GestureSample(
        new Point(20, 20),
        new Point(23, 22),
        TimeSpan.FromMilliseconds(180),
        3,
        true);

    Assert(GestureInterpreter.Interpret(sample) == PetGestureKind.RapidTap, "rapid tap should win before single touch confirmation");
    Assert(GestureInterpreter.IsRapidTap(DateTimeOffset.Now, DateTimeOffset.Now - TimeSpan.FromMilliseconds(420), 3), "rapid tap threshold rejected valid burst");
    Assert(!GestureInterpreter.IsRapidTap(DateTimeOffset.Now, DateTimeOffset.Now - TimeSpan.FromMilliseconds(901), 3), "rapid tap threshold accepted stale burst");
}

static void RuntimeRequestsTouchMotion()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;
    var touch = runtime.Motions.Single(motion => motion.BehaviorId == Phase15BehaviorIds.ProneTouch);
    Assert(!touch.RuntimeEnabled, "touch catalog ignored asset.json runtime_use=false");
    Assert(touch.AssetBatch == "WK-INTERACTION-PRONE-TOUCH-v4-1", "touch catalog lost source batch identity");
    var result = runtime.SubmitGestureAsync(PetGestureKind.OwnerTouch, BehaviorRequestSource.OwnerUi).GetAwaiter().GetResult();
    Assert(result == PetActionResult.Deferred, "runtime-locked touch candidate was accepted");
    Assert(request is null, "runtime-locked touch candidate requested production playback");
    Assert(runtime.CurrentReason.Contains("prone_touch_runtime_qa_pending", StringComparison.Ordinal), "touch gate reason did not explain pending runtime QA");
}

static void RuntimeRapidTapDoesNotRequestTouchMotion()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;
    var result = runtime.SubmitGestureAsync(PetGestureKind.RapidTap, BehaviorRequestSource.OwnerUi).GetAwaiter().GetResult();
    Assert(result != PetActionResult.Accepted, "rapid tap should not accept a locked transition");
    Assert(request is null || request.Motion.BehaviorId != Phase15BehaviorIds.ProneTouch, "rapid tap must not request owner touch motion");
}

static void InteractionDecisionUsesStateRelationshipAndAssetGates()
{
    var service = new InteractionDecisionService();
    var now = new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero);
    var baseContext = new InteractionDecisionContext(
        PetGestureKind.OwnerTouch,
        1,
        PetRuntimeState.Default with { CurrentPosture = StablePosture.Prone },
        TemperamentProfile.Default,
        RelationshipState.Default,
        now,
        IsStableIdle: true,
        IsCurrentInterruptible: true,
        IsPetrified: false,
        RuntimeEnabledBehaviorIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    var locked = service.Decide(baseContext);
    Assert(locked.Disposition == PetActionResult.Deferred && locked.BehaviorId is null, "locked touch asset entered playback");
    Assert(locked.UpdatedState.SocialNeed < baseContext.State.SocialNeed, "accepted touch input did not update social state");

    var enabled = service.Decide(baseContext with
    {
        RuntimeEnabledBehaviorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Phase15BehaviorIds.ProneTouch }
    });
    Assert(enabled.Disposition == PetActionResult.Accepted && enabled.BehaviorId == Phase15BehaviorIds.ProneTouch, "approved touch asset was not selectable");

    var rejected = service.Decide(baseContext with
    {
        State = baseContext.State with { Stress = 0.82 },
        Relationship = RelationshipState.Default with { TouchAcceptance = 0.12 }
    });
    Assert(rejected.Disposition == PetActionResult.Rejected && rejected.ReasonCode == "touch_acceptance_low", "state/relationship did not reject unwanted touch");

    var rapid = service.Decide(baseContext with { Gesture = PetGestureKind.RapidTap, TapBurst = 4 });
    Assert(rapid.UpdatedState.Stress > baseContext.State.Stress, "rapid tap did not increase stress");
    Assert(rapid.Disposition != PetActionResult.Accepted, "rapid tap used a locked response asset");

    var drag = service.Decide(baseContext with { Gesture = PetGestureKind.Drag });
    Assert(drag.ReasonCode == "drag_repositions_pet" && drag.UpdatedState == baseContext.State.Clamp(), "drag was mixed with emotional animation state");
}

static void AlbumFolderItemReadsLocalMarkdownAlbum()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-album-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var album = Path.Combine(root, "park-day");
        Directory.CreateDirectory(album);
        File.WriteAllText(Path.Combine(album, "album.md"), "# park-day\r\n\r\ndate: 2026-08-11\r\n\r\nWukong played in the park all afternoon.");
        File.WriteAllBytes(Path.Combine(album, "cover.jpg"), new byte[] { 1, 2, 3 });

        var item = AlbumFolderItem.FromDirectory(album);

        Assert(item.Name == "park-day", "album folder name was not read");
        Assert(item.PhotoCount == 1, "album image count was not read");
        Assert(item.MarkdownPath.EndsWith("album.md", StringComparison.OrdinalIgnoreCase), "album markdown was not found");
        Assert(item.Description.Contains("Wukong played", StringComparison.Ordinal), "album markdown description was not read");
        Assert(item.Status == "已读取描述", "album status did not reflect markdown");
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

static void AlbumFolderItemReadsXhsMarkdownAlbum()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-album-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var album = Path.Combine(root, "2025-12-13_01");
        Directory.CreateDirectory(album);
        File.WriteAllText(
            Path.Combine(album, "2025-12-13_title.md"),
            "---\r\n" +
            "title: \"\u4e24\u4e2a\u534a\u6708\u7684\u65f6\u5019\uff0c\u88ab\u8001\u7238\u5e26\u56de\u5bb6\u5566\uff01\"\r\n" +
            "time: \"2025-12-13 08:27:16 +08:00\"\r\n" +
            "media:\r\n" +
            "  - \"image_02.webp\"\r\n" +
            "  - \"image_01.webp\"\r\n" +
            "---\r\n\r\n" +
            "# \u4e24\u4e2a\u534a\u6708\u7684\u65f6\u5019\uff0c\u88ab\u8001\u7238\u5e26\u56de\u5bb6\u5566\uff01\r\n\r\n" +
            "\u65f6\u95f4: 2025-12-13 08:27:16 +08:00\r\n\r\n" +
            "## \u6b63\u6587\r\n\r\n" +
            "\u7b2c\u4e00\u6b21\u5750\u8f66\uff0c\u5934\u6655\u6655\u3002\r\n\r\n" +
            "## \u7d20\u6750\r\n\r\n" +
            "- `image_02.webp`\r\n",
            System.Text.Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(album, "image_01.webp"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(album, "image_02.webp"), new byte[] { 4, 5, 6 });

        var item = AlbumFolderItem.FromDirectory(album);

        Assert(item.Name.Contains("\u8001\u7238\u5e26\u56de\u5bb6", StringComparison.Ordinal), "xhs title was not read");
        Assert(item.DateText == "2025-12-13", "xhs date was not normalized");
        Assert(item.PhotoCount == 2, "xhs image count was not read");
        Assert(item.ThumbnailPath.EndsWith("image_02.webp", StringComparison.OrdinalIgnoreCase), "xhs media order did not drive thumbnail");
        Assert(item.Description.Contains("\u7b2c\u4e00\u6b21\u5750\u8f66", StringComparison.Ordinal), "xhs body description was not read");
        Assert(item.MediaFiles.SequenceEqual(new[] { "image_02.webp", "image_01.webp" }), "xhs media list was not preserved");
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

static void AlbumMarkdownUpdatePreservesUnknownFields()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-album-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var album = Path.Combine(root, "2025-12-13_01");
        Directory.CreateDirectory(album);
        File.WriteAllText(
            Path.Combine(album, "album.md"),
            "---\r\n" +
            "title: \"old title\"\r\n" +
            "source: \"xhs\"\r\n" +
            "time: \"2025-12-13 08:27:16 +08:00\"\r\n" +
            "media:\r\n" +
            "  - \"old.webp\"\r\n" +
            "---\r\n\r\n" +
            "# old title\r\n\r\n" +
            "\u65f6\u95f4: 2025-12-13 08:27:16 +08:00\r\n\r\n" +
            "## \u6b63\u6587\r\n\r\n" +
            "old body\r\n\r\n" +
            "## \u5730\u70b9\r\n\r\n" +
            "\u5357\u4eac\r\n\r\n" +
            "## \u7d20\u6750\r\n\r\n" +
            "- `old.webp`\r\n",
            System.Text.Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(album, "image_01.webp"), new byte[] { 1, 2, 3 });

        var item = AlbumFolderItem.FromDirectory(album);
        var updated = item.CreateMarkdown("2026-08-11", "\u65b0\u7684\u6b63\u6587", new[] { "image_01.webp" });

        Assert(updated.Contains("source: \"xhs\"", StringComparison.Ordinal), "unknown front matter was not preserved");
        Assert(updated.Contains("## \u5730\u70b9", StringComparison.Ordinal), "unknown body section was not preserved");
        Assert(updated.Contains("\u5357\u4eac", StringComparison.Ordinal), "unknown section content was not preserved");
        Assert(updated.Contains("time: \"2026-08-11\"", StringComparison.Ordinal), "date was not updated");
        Assert(updated.Contains("- \"image_01.webp\"", StringComparison.Ordinal), "media binding was not updated");
        Assert(!updated.Contains("old.webp", StringComparison.Ordinal), "old media binding was preserved incorrectly");
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

static void AlbumMediaUnlinkHandlesPersistenceAndKeepsFiles()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-album-unlink-" + Guid.NewGuid().ToString("N"));
    try
    {
        var album = Path.Combine(root, "home-day");
        Directory.CreateDirectory(album);
        var keepFile = Path.Combine(album, "keep.webp");
        var removeFile = Path.Combine(album, "remove.webp");
        File.WriteAllBytes(keepFile, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(removeFile, new byte[] { 4, 5, 6 });
        var markdownText = string.Join(Environment.NewLine, new[]
        {
            "---",
            "title: home-day",
            "time: 2026-08-15",
            "media:",
            "  - \"keep.webp\"",
            "  - \"remove.webp\"",
            "---",
            string.Empty,
            "# home-day",
            string.Empty,
            "## Body",
            string.Empty,
            "album body",
            string.Empty,
            "## ??",
            string.Empty,
            "- `keep.webp`",
            "- `remove.webp`",
            string.Empty
        });
        File.WriteAllText(Path.Combine(album, "album.md"), markdownText, System.Text.Encoding.UTF8);

        var bindings = new List<AlbumMediaItem>
        {
            new("keep.webp", keepFile, "found"),
            new("remove.webp", removeFile, "found")
        };
        var noSelection = AlbumMediaBindingEditor.Unbind(null, bindings, _ => true);
        Assert(noSelection.Status == AlbumMediaUnbindStatus.NoSelection, "no selection should be reported");
        Assert(bindings.Count == 2, "no selection must not change bindings");

        var notFound = AlbumMediaBindingEditor.Unbind("missing.webp", bindings, _ => true);
        Assert(notFound.Status == AlbumMediaUnbindStatus.NotFound, "missing binding should be reported");
        Assert(bindings.Count == 2, "missing binding must not change bindings");

        var failure = AlbumMediaBindingEditor.Unbind("remove.webp", bindings, _ => throw new IOException("locked"));
        Assert(failure.Status == AlbumMediaUnbindStatus.PersistenceFailed, "persistence failure should be reported");
        Assert(bindings.Select(x => x.FileName).SequenceEqual(new[] { "keep.webp", "remove.webp" }), "failed persistence must restore bindings");

        var item = AlbumFolderItem.FromDirectory(album);
        bindings = item.MediaFiles.Select(x => new AlbumMediaItem(x, Path.Combine(album, x), "found")).ToList();
        var success = AlbumMediaBindingEditor.Unbind("remove.webp", bindings, mediaFiles =>
        {
            File.WriteAllText(item.MarkdownPath, item.CreateMarkdown("2026-08-15", item.Description, mediaFiles.Select(x => x.FileName).ToArray()), System.Text.Encoding.UTF8);
            return true;
        });
        Assert(success.Status == AlbumMediaUnbindStatus.Success, "normal unlink should succeed");
        Assert(bindings.Count == 1 && bindings.Single().FileName == "keep.webp", "unbound media should disappear from the current album UI model");
        Assert(File.Exists(removeFile), "unlink must not delete local original file");
        var reloaded = AlbumFolderItem.FromDirectory(album);
        Assert(reloaded.MediaFiles.SequenceEqual(new[] { "keep.webp" }), "restart should preserve the unbound media state");
        var markdown = File.ReadAllText(item.MarkdownPath, System.Text.Encoding.UTF8);
        Assert(markdown.Contains("keep.webp", StringComparison.Ordinal), "remaining binding was not persisted");
        Assert(!markdown.Contains("- `remove.webp`", StringComparison.Ordinal) && !markdown.Contains("- \"remove.webp\"", StringComparison.Ordinal), "removed binding was still persisted");

        var deleteItem = AlbumFolderItem.FromDirectory(album);
        bindings = deleteItem.MediaFiles.Select(x => new AlbumMediaItem(x, Path.Combine(album, x), "found")).ToList();
        var delete = AlbumMediaBindingEditor.Delete("keep.webp", bindings, mediaFiles =>
        {
            File.WriteAllText(deleteItem.MarkdownPath, deleteItem.CreateMarkdown("2026-08-15", deleteItem.Description, mediaFiles.Select(x => x.FileName).ToArray()), System.Text.Encoding.UTF8);
            return true;
        });
        Assert(delete.Status == AlbumMediaUnbindStatus.Success, "delete record should succeed");
        Assert(File.Exists(keepFile), "delete record must not delete local original file by default");
        var afterDeleteReload = AlbumFolderItem.FromDirectory(album);
        Assert(afterDeleteReload.MediaFiles.Count == 0, "restart should preserve deleted media record state");
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

static void AlbumFolderRemovalPersistsAndKeepsFiles()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-album-folder-remove-" + Guid.NewGuid().ToString("N"));
    try
    {
        var album = Path.Combine(root, "child-album");
        Directory.CreateDirectory(album);
        var original = Path.Combine(album, "original.png");
        File.WriteAllBytes(original, new byte[] { 1, 2, 3, 4 });
        var item = AlbumFolderItem.FromDirectory(album);

        var noSelection = AlbumFolderVisibility.RemoveFromCatalog(null, _ => true);
        Assert(noSelection.Status == AlbumFolderRemovalStatus.NoSelection, "missing child album selection should be reported");

        var failed = AlbumFolderVisibility.RemoveFromCatalog(item, _ => throw new IOException("read-only"));
        Assert(failed.Status == AlbumFolderRemovalStatus.PersistenceFailed, "marker persistence failure should be reported");
        Assert(AlbumFolderVisibility.IsVisible(album), "failed removal hid the album in memory");

        var removed = AlbumFolderVisibility.RemoveFromCatalog(item, marker =>
        {
            File.WriteAllText(marker, "hidden_from_wukong_album=true\n", System.Text.Encoding.UTF8);
            return true;
        });
        Assert(removed.Status == AlbumFolderRemovalStatus.Success, "child album removal failed");
        Assert(Directory.Exists(album), "child album removal deleted the local directory");
        Assert(File.Exists(original), "child album removal deleted the original image");
        Assert(!AlbumFolderVisibility.IsVisible(album), "removed child album reappeared after persistence");
        Assert(!Directory.GetDirectories(root).Where(AlbumFolderVisibility.IsVisible).Any(), "restart scan rediscovered the removed child album");
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}
static void AutonomousTickCanRequestMotion()
{
    var runtime = new DesktopRuntimeHost();
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);
    runtime.StartIdle("Startup");
    var energyBeforeTick = runtime.Energy;
    var hungerBeforeTick = runtime.Hunger;
    Assert(runtime.CurrentStablePosture == StablePosture.Stand, "default healthy runtime state should start in stable stand");
    Assert(requests.Last().Motion.BehaviorId == LifecycleCandidateBehaviorIds.StandIdleMicroloop, "startup is still hard-coded to prone idle");

    for (var attempt = 0; attempt < 8 && requests.All(x => x.Motion.BehaviorId != LifecycleCandidateBehaviorIds.LivelyDailyP2); attempt++)
    {
        typeof(DesktopRuntimeHost)
            .GetField("_currentStartedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(60));
        typeof(DesktopRuntimeHost)
            .GetField("_nextAutonomousDecisionAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(1));
        runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
    }

    Assert(runtime.Energy < energyBeforeTick, "unified runtime state did not reduce energy during autonomous ticks");
    Assert(runtime.Hunger > hungerBeforeTick, "unified runtime state did not increase hunger during autonomous ticks");
    Assert(requests.Any(x => x.Motion.BehaviorId == LifecycleCandidateBehaviorIds.LivelyDailyP2), "state-driven autonomous scheduling never selected the complete lively lifecycle");
    Assert(requests.All(x => x.Motion.BehaviorId is LifecycleCandidateBehaviorIds.ProneIdleMicroloop or LifecycleCandidateBehaviorIds.SitIdleMicroloop or LifecycleCandidateBehaviorIds.StandIdleMicroloop or LifecycleCandidateBehaviorIds.LivelyDailyP2), "autonomous tick selected an expired or out-of-scope behavior");
}

static void BootstrapLogRedactsAndDoesNotThrow()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-bootstrap-tests", Guid.NewGuid().ToString("N"));
    try
    {
        BootstrapLog.WriteToDirectory(
            root,
            "startup Authorization: Bearer abc token=secret C:\\Users\\alice\\file.txt",
            new { apiKey = "sk-secret1234567890" },
            new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero));

        var file = Directory.GetFiles(root, "*.log", SearchOption.AllDirectories).Single();
        var text = File.ReadAllText(file);
        Assert(text.Contains("[redacted]", StringComparison.Ordinal), "bootstrap log did not redact credentials");
        Assert(!text.Contains("secret", StringComparison.OrdinalIgnoreCase), "bootstrap log leaked secret");
        Assert(!text.Contains("C:\\", StringComparison.Ordinal), "bootstrap log leaked path");

        var invalidRoot = Path.Combine(root, "not-a-directory");
        File.WriteAllText(invalidRoot, "file");
        BootstrapLog.WriteToDirectory(invalidRoot, "this must not throw", new { token = "secret" });
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static bool HasAlpha(System.Windows.Media.PixelFormat format) =>
    format == System.Windows.Media.PixelFormats.Bgra32 ||
    format == System.Windows.Media.PixelFormats.Pbgra32 ||
    format == System.Windows.Media.PixelFormats.Prgba64 ||
    format == System.Windows.Media.PixelFormats.Rgba64 ||
    format == System.Windows.Media.PixelFormats.Rgba128Float;

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
    }
}
