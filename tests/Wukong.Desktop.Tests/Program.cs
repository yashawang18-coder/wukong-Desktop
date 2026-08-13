using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wukong.Desktop;
using Wukong.Domain;

if (args.Contains("--show-panel-smoke", StringComparer.Ordinal))
    return ShowPanelSmoke();

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
    ("command action candidates are indexed and validated", CommandActionCandidatesAreIndexed),
    ("command candidates stay out of autonomous and production commands", CommandCandidatesStayGated),
    ("developer forced command candidate can request playback", DeveloperForcedCommandCandidateCanRequestPlayback),
    ("magic mock assets are indexed and validated", MagicMockAssetsAreIndexed),
    ("owner and panel magic use prototype preview gate", OwnerAndPanelMagicUsePrototypePreviewGate),
    ("non owner sources cannot prototype preview magic", NonOwnerSourcesCannotPrototypePreviewMagic),
    ("stop clears petrification and requests idle", StopClearsPetrificationAndRequestsIdle),
    ("main window context menu matches owner action contract", MainWindowContextMenuMatchesContract),
    ("control panel exposes magic specials tab", ControlPanelExposesMagicSpecialsTab),
    ("gesture interpreter distinguishes touch stroke drag and rapid tap", GestureInterpreterDistinguishesGestures),
    ("rapid tap has priority over owner touch", RapidTapHasPriorityOverOwnerTouch),
    ("runtime requests touch motion and returns decisions", RuntimeRequestsTouchMotion),
    ("runtime rapid tap does not request touch motion", RuntimeRapidTapDoesNotRequestTouchMotion),
    ("album folder item reads local markdown album", AlbumFolderItemReadsLocalMarkdownAlbum),
    ("album folder item reads xhs markdown album", AlbumFolderItemReadsXhsMarkdownAlbum),
    ("album markdown update preserves unknown fields", AlbumMarkdownUpdatePreservesUnknownFields),
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
    thread.Join();
    if (failure is not null)
    {
        Console.Error.WriteLine(failure);
        return 1;
    }
    return 0;
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
            Assert(panel.FindName("UseLongTermMemoryCheck") is CheckBox, "long term memory switch missing");
            Assert(panel.FindName("UseAlbumMemoryCheck") is CheckBox, "album memory switch missing");
            Assert(panel.FindName("UseShortTermMemoryCheck") is CheckBox, "short term memory switch missing");
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
            Assert(Math.Abs(window.Width - 400) < 0.001, "window width did not scale");
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

static void MagicMockAssetsAreIndexed()
{
    var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
    var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", MagicBehaviorIds.AssetBatch, "manifest.json");
    Assert(File.Exists(manifestPath), "magic mock manifest was not copied to output");

    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    Assert(manifest.RootElement.GetProperty("runtime_approved").GetBoolean() == false, "magic mock must not be runtime approved");
    Assert(manifest.RootElement.GetProperty("runtime_use").GetBoolean() == false, "magic mock must not enable production runtime use");
    Assert(manifest.RootElement.GetProperty("prototype_use").GetBoolean(), "magic mock must explicitly enable prototype use");
    var actions = manifest.RootElement.GetProperty("actions").EnumerateArray().ToArray();
    Assert(actions.Length == 5, "magic mock manifest must include four magic actions plus petrification release");

    foreach (var action in actions)
    {
        Assert(!action.GetProperty("runtime_approved").GetBoolean(), "mock action was runtime approved");
        Assert(!action.GetProperty("runtime_use").GetBoolean(), "mock action enabled production runtime use");
        Assert(action.GetProperty("prototype_use").GetBoolean(), "mock action did not enable prototype preview");
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
                Assert(bitmap.PixelWidth == 256 && bitmap.PixelHeight == 256, "magic mock frame dimensions changed");
                Assert(HasAlpha(bitmap.Format), "magic mock frame is not alpha-capable");
            }
        }
    }

    var catalog = DesktopMotionCatalog.Load(output);
    var magic = catalog.Motions.Where(x => x.Category == "宠物魔法").ToArray();
    Assert(magic.Length == 5, "magic mock assets were not indexed");
    Assert(magic.All(x => x.PrototypeUse), "all magic mocks must be prototype-use only");
    Assert(magic.All(x => !x.RuntimeEnabled), "magic mocks must stay out of production runtime");
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
            Assert(headers.SequenceEqual(new[] { "停下", "聊天", "吃一下", "玩一下", "口令", "宠物魔法", "打开面板", "退出" }), "top-level context menu order changed");

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
            var list = panel.FindName("MagicSpecialList") as ItemsControl;
            Assert(list is not null, "magic specials list missing");
            Assert(commandList!.Items.Count == 4, "command assets tab must display four command candidates");
            Assert(list!.Items.Count == 4, "magic specials must display four owner-facing cards");
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

static void AutonomousTickCanRequestMotion()
{
    var runtime = new DesktopRuntimeHost();
    PetMotionRequest? request = null;
    runtime.MotionRequested += (_, item) => request = item;
    typeof(DesktopRuntimeHost)
        .GetField("_currentStartedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(runtime, DateTimeOffset.Now - TimeSpan.FromSeconds(30));
    runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
    Assert(request is not null, "autonomous tick did not leave a playable motion request trail");
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
