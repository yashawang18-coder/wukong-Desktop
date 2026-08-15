using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wukong.Desktop;
using Wukong.Domain;

if (args.Contains("--show-panel-smoke", StringComparer.Ordinal))
    return ShowPanelSmoke();
if (args.Contains("--capture-panel-screens", StringComparer.Ordinal))
    return CapturePanelScreens(args.SkipWhile(x => x != "--capture-panel-screens").Skip(1).FirstOrDefault() ?? Path.Combine(".publish-check", "ux-panel-album-coin-fixes-v1", "screenshots"));

var tests = new (string Name, Action Run)[]
{
    ("input adapter emits input events", InputAdapterEmitsEvents),
    ("startup factory creates one main window", StartupFactoryCreatesOneMainWindow),
    ("control panel xaml constructs", ControlPanelXamlConstructs),
    ("agent windows construct and desktop chat starts hidden", AgentWindowsConstructAndChatStartsHidden),
    ("desktop chat keyboard semantics distinguish send and newline", DesktopChatKeyboardSemantics),
    ("desktop chat sensor is limited to lower blank region", DesktopChatSensorIsLimited),
    ("desktop chat placement stays visible at all corners", DesktopChatPlacementStaysVisible),
    ("main window pet scale changes image size", MainWindowPetScaleChangesImageSize),
    ("initial placement stays in work area", InitialPlacementStaysInWorkArea),
    ("phase15 motion assets are copied and decodable", Phase15MotionAssetsAreCopiedAndDecodable),
    ("lifecycle microloop candidates are indexed and gated", LifecycleMicroloopCandidatesAreIndexedAndGated),
    ("developer lifecycle candidate can request playback", DeveloperLifecycleCandidateCanRequestPlayback),
    ("command action candidates are indexed and validated", CommandActionCandidatesAreIndexed),
    ("command candidates stay out of autonomous and production commands", CommandCandidatesStayGated),
    ("developer forced command candidate can request playback", DeveloperForcedCommandCandidateCanRequestPlayback),
    ("magic candidate assets are indexed and validated", MagicCandidateAssetsAreIndexed),
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
    ("control panel exposes magic specials tab", ControlPanelExposesMagicSpecialsTab),
    ("control panel tab buttons share visual metrics", ControlPanelTabButtonsShareVisualMetrics),
    ("gesture interpreter distinguishes touch stroke drag and rapid tap", GestureInterpreterDistinguishesGestures),
    ("rapid tap has priority over owner touch", RapidTapHasPriorityOverOwnerTouch),
    ("runtime requests touch motion and returns decisions", RuntimeRequestsTouchMotion),
    ("runtime rapid tap does not request touch motion", RuntimeRapidTapDoesNotRequestTouchMotion),
    ("album folder item reads local markdown album", AlbumFolderItemReadsLocalMarkdownAlbum),
    ("album folder item reads xhs markdown album", AlbumFolderItemReadsXhsMarkdownAlbum),
    ("album markdown update preserves unknown fields", AlbumMarkdownUpdatePreservesUnknownFields),
    ("album media unlink handles persistence and keeps files", AlbumMediaUnlinkHandlesPersistenceAndKeepsFiles),
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
                Width = 1180,
                Height = 760
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
    var normal = runtime.Motions.First(x => x.BehaviorId == Phase15BehaviorIds.ProneIdle);
    var magic = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.AccioBroom);
    var coin = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.PetrificusTotalus);
    var panel = new Grid { Width = 760, Height = 280, Background = Brushes.White };
    panel.ColumnDefinitions.Add(new ColumnDefinition());
    panel.ColumnDefinitions.Add(new ColumnDefinition());
    panel.ColumnDefinitions.Add(new ColumnDefinition());
    AddVisualSample(panel, normal, reference, "????", 0);
    AddVisualSample(panel, magic, reference, "????", 1);
    AddVisualSample(panel, coin, reference, "??/??", 2);
    panel.Measure(new Size(panel.Width, panel.Height));
    panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
    var bitmap = new RenderTargetBitmap((int)panel.Width, (int)panel.Height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(panel);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(Path.Combine(outputRoot, "visual-size-comparison.png"));
    encoder.Save(stream);
}

static void AddVisualSample(Grid root, PlayableMotion motion, string reference, string label, int column)
{
    var size = MotionVisualSizer.PreviewRenderSize(motion.FirstFrame, reference, motion.VisualScale, 190);
    var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    stack.Children.Add(new Border
    {
        Width = 210,
        Height = 210,
        Background = new SolidColorBrush(Color.FromRgb(238, 236, 229)),
        CornerRadius = new CornerRadius(12),
        Child = new Image { Source = BitmapFrame.Create(new Uri(motion.FirstFrame, UriKind.Absolute)), Width = size, Height = size, Stretch = Stretch.Uniform }
    });
    stack.Children.Add(new TextBlock { Text = $"{label} / {motion.VisibleSubjectHeight}px", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
    Grid.SetColumn(stack, column);
    root.Children.Add(stack);
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
            Assert(panel.FindName("OwnerBirthdayPicker") is DatePicker, "owner birthday field missing");
            Assert(panel.FindName("OwnerPetCallNameText") is TextBox, "owner pet call name field missing");
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
            var login = new DeveloperLoginWindow(agent.DeveloperSession);
            Assert(!chat.IsExpanded, "desktop chat should be hidden until the sensor is clicked");
            chat.Close();
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
    Assert(!DesktopChatWindow.ShouldSend(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Shift), "Shift+Enter should insert a newline");
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
    var overlay = new Size(420, 286);
    var pets = new[]
    {
        new Rect(0, 0, 320, 320),
        new Rect(960, 0, 320, 320),
        new Rect(0, 400, 320, 320),
        new Rect(960, 400, 320, 320)
    };
    foreach (var pet in pets)
    {
        var point = DesktopChatPlacement.Place(workArea, pet, overlay);
        Assert(point.X >= workArea.Left && point.Y >= workArea.Top, "chat escaped top or left work area");
        Assert(point.X + overlay.Width <= workArea.Right && point.Y + overlay.Height <= workArea.Bottom, "chat escaped right or bottom work area");
    }
    var bottomPosition = DesktopChatPlacement.Place(workArea, pets[2], overlay);
    Assert(bottomPosition.Y < pets[2].Top, "chat should open upward near the bottom edge");
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
    var lifecycle = catalog.Motions.Where(x => x.Category == "候选动作").ToArray();
    Assert(lifecycle.Length == 4, "lifecycle candidates were not indexed");
    Assert(lifecycle.All(x => x.RuntimeEnabled), "P3 lifecycle candidates must be enabled for autonomous runtime");
    Assert(lifecycle.All(x => x.CandidateProfile == "developer_lifecycle_microloops_v2"), "candidate profile was not preserved");
    Assert(lifecycle.Single(x => x.BehaviorId == LifecycleCandidateBehaviorIds.LivelyDailyP2).Phases.Select(x => x.Name).SequenceEqual(new[] { "intro", "loop", "exit", "interrupt_exit", "fallback" }), "full lifecycle phases are wrong");
    Assert(lifecycle.Single(x => x.BehaviorId == LifecycleCandidateBehaviorIds.StandIdleMicroloop).Phases.Single().DurationTotalMs(180) == 7240, "stand microloop timing changed");
    Assert(lifecycle.Single(x => x.BehaviorId == LifecycleCandidateBehaviorIds.LivelyDailyP2).MissingContent == "None", "approved lifecycle motion still reports missing runtime content");
}

static void DeveloperLifecycleCandidateCanRequestPlayback()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    int? requestedSize = null;
    runtime.MotionRequested += (_, item) => request = item;
    runtime.PetPixelSizeRequested += (_, pixels) => requestedSize = pixels;

    var ownerResult = runtime.SubmitContextMenuIntentAsync(new SemanticIntent(SemanticIntentKind.Quiet, NaturalLanguage: "test")).GetAwaiter().GetResult();
    Assert(ownerResult == PetActionResult.Accepted, "test setup idle request failed");
    request = null;

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
    var commandMotions = catalog.Motions.Where(x => x.Category == "口令动作").ToArray();
    Assert(commandMotions.Length == 4, "command candidates were not indexed");
    Assert(commandMotions.All(x => !x.RuntimeEnabled), "command candidates must remain runtime locked");
    Assert(commandMotions.All(x => x.FrameCount is 8 or 9 or 10), "command candidate frame counts wrong");
}

static void CommandCandidatesStayGated()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;

    var command = runtime.SubmitOwnerCommandAsync("抬爪").GetAwaiter().GetResult();
    Assert(command == PetActionResult.Deferred, "unapproved command candidate should defer for production command");
    Assert(request is null, "production command bypassed runtime candidate gate");

    typeof(DesktopRuntimeHost)
        .GetField("_currentStartedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(30));
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

    now += TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2);
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
    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.ControlPanel).GetAwaiter().GetResult();

    now = started + TimeSpan.FromSeconds(1.8);
    Assert(runtime.RefreshPetrifiedCoinState(), "custom settle threshold was not used");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Flat, "custom settle threshold selected wrong state");
    now = started + TimeSpan.FromSeconds(3.7);
    Assert(runtime.RefreshPetrifiedCoinState(), "custom fade threshold was not used");
    Assert(runtime.CurrentCoinState == PetrifiedCoinState.Faded, "custom fade threshold selected wrong state");
    now = started + TimeSpan.FromSeconds(5.7);
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
    Assert(requests.Last().Motion.VisualScale > 1.34 && requests.Last().Motion.VisualScale < 1.36, "petrification intro must render at enlarged magic pet scale");
    Assert(runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult() == PetActionResult.Accepted, "coin activity reset was not accepted");

    now = started + TimeSpan.FromSeconds(4.99);
    Assert(!runtime.RefreshPetrifiedCoinState(), "default coin hold changed before five seconds");
    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusRelease, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
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
    var normal = runtime.Motions.First(x => x.BehaviorId == Phase15BehaviorIds.ProneIdle);
    var broom = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.AccioBroom);
    var petrify = runtime.MagicMotions.First(x => x.BehaviorId == MagicBehaviorIds.PetrificusTotalus);
    var requests = new List<PetMotionRequest>();
    runtime.MotionRequested += (_, item) => requests.Add(item);

    var normalHeight = VisibleHeightRatio(normal, reference);
    var broomRatio = VisibleHeightRatio(broom, reference) / normalHeight;
    var petrifyRatio = VisibleHeightRatio(petrify, reference) / normalHeight;

    runtime.SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
    runtime.SubmitPetrifiedCoinClickAsync().GetAwaiter().GetResult();
    var coinRatio = VisibleHeightRatio(requests.Last().Motion, reference) / normalHeight;

    Assert(broomRatio is > 1.20 and < 1.45, $"magic pet visual height ratio was {broomRatio:0.000}");
    Assert(petrifyRatio is > 1.20 and < 1.45, $"petrification intro visual height ratio was {petrifyRatio:0.000}");
    Assert(coinRatio is > 0.62 and < 0.72, $"petrified coin visual height ratio was {coinRatio:0.000}");
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
    Assert(requests.Last().Motion.BehaviorId == Phase15BehaviorIds.ProneIdle, "stop did not request idle recovery");
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
            var headers = menu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToArray();
            Assert(headers.SequenceEqual(new[] { "聊天", "吃一下", "玩一下", "口令", "宠物魔法", "停下", "打开面板", "退出" }), $"top-level context menu order changed: {string.Join(",", headers)}");
            Assert(!headers.Contains("大小", StringComparer.Ordinal), "scale menu must not be shown in the context menu");
            var stopIndex = Array.IndexOf(headers, "停下");
            Assert(stopIndex >= 0 && stopIndex + 1 < headers.Length && headers[stopIndex + 1] == "打开面板", "stop must sit immediately above open panel");

            var commands = menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "口令"));
            Assert(commands.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(new[] { "坐", "卧", "停", "转圈", "手", "吃" }), "command submenu order changed");

            var magic = menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "宠物魔法"));
            Assert(magic.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(new[] { "Accio Broom", "Apparate", "Petrificus Totalus", "Scourgify" }), "magic submenu order changed");

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
            Assert(panel.FindName("CommandAssetsPanel") is ScrollViewer, "command assets panel missing");
            var commandList = panel.FindName("CommandAssetList") as ItemsControl;
            Assert(commandList is not null, "command asset list missing");
            Assert(panel.FindName("MagicAssetsPanel") is ScrollViewer, "magic specials panel missing");
            Assert(panel.FindName("LifecycleCandidateList") is ItemsControl, "lifecycle candidate developer list missing");
            var list = panel.FindName("MagicSpecialList") as ItemsControl;
            Assert(list is not null, "magic specials list missing");
            Assert(commandList!.Items.Count == 4, "command assets tab must display four command candidates");
            Assert(list!.Items.Count == 4, "magic specials must display four owner-facing cards");
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
    var result = runtime.SubmitGestureAsync(PetGestureKind.OwnerTouch, BehaviorRequestSource.OwnerUi).GetAwaiter().GetResult();
    Assert(result == PetActionResult.Accepted, "touch was not accepted");
    Assert(request is not null, "touch did not request a motion");
    Assert(request!.Motion.BehaviorId == Phase15BehaviorIds.ProneTouch, "touch chose wrong behavior");
    Assert(request.Motion.Phases.Any(x => x.Name == "loop" && x.Frames.Count > 0), "touch motion missing loop frames");
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

static void AlbumFolderItemReadsLocalMarkdownAlbum()
{
    var root = Path.Combine(Path.GetTempPath(), "wukong-album-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var album = Path.Combine(root, "park-day");
        Directory.CreateDirectory(album);
        File.WriteAllText(Path.Combine(album, "album.md"), "# park-day\r\n\r\ndate: 2026-08-11\r\n\r\n悟空在公园玩了一下午。");
        File.WriteAllBytes(Path.Combine(album, "cover.jpg"), new byte[] { 1, 2, 3 });

        var item = AlbumFolderItem.FromDirectory(album);

        Assert(item.Name == "park-day", "album folder name was not read");
        Assert(item.PhotoCount == 1, "album image count was not read");
        Assert(item.MarkdownPath.EndsWith("album.md", StringComparison.OrdinalIgnoreCase), "album markdown was not found");
        Assert(item.Description.Contains("悟空在公园", StringComparison.Ordinal), "album markdown description was not read");
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
static void AutonomousTickCanRequestMotion()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;
    typeof(DesktopRuntimeHost)
        .GetField("_currentStartedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(60));
    runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
    Assert(request is not null, "autonomous tick did not leave a playable motion request trail");
    Assert(request.Motion.BehaviorId is LifecycleCandidateBehaviorIds.ProneIdleMicroloop or LifecycleCandidateBehaviorIds.LivelyDailyP2 or Phase15BehaviorIds.ProneBreath or Phase15BehaviorIds.ProneIdle, "autonomous tick selected an out-of-scope behavior");
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
