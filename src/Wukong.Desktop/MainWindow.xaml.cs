using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wukong.Application;
using Wukong.Domain;

namespace Wukong.Desktop;

public enum MotionEasing
{
    Linear,
    Accelerate,
    Decelerate,
    EaseInOut
}

public enum MotionRouteVisibility
{
    Visible,
    ExitOffscreen,
    ReenterFromOffscreen
}

public sealed record MotionRouteSegment(
    Point Target,
    string Direction,
    TimeSpan Duration,
    MotionEasing Easing,
    MotionRouteVisibility Visibility = MotionRouteVisibility.Visible,
    Point? StartOverride = null,
    TimeSpan? HiddenHold = null);

public sealed record EffectDisplaySnapshot(
    double UserScale,
    double ActionLocalScale,
    double Opacity,
    double Left,
    double Top,
    double Width,
    double Height,
    Point GroundAnchor,
    string Posture,
    Rect WorkingArea);

public sealed record CarRideStartupMetrics(
    double RequestToPipelineMs,
    double ScaleCalculationMs,
    double FirstFrameDecodeMs,
    double FirstVisibleMs);

public partial class MainWindow : Window
{
    private readonly DesktopRuntimeHost _runtime;
    private readonly DesktopAgentRuntime _agentRuntime;
    private readonly DispatcherTimer _autonomousTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _coinStateTimer;
    private readonly DispatcherTimer _coinSingleClickTimer;
    private readonly DispatcherTimer _ownerSingleClickTimer;
    private readonly DispatcherTimer _initiativeSpeechTimer = new();
    private readonly Dictionary<string, BitmapImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _imageCacheOrder = new();
    private readonly object _imageCacheSync = new();
    private readonly Random _effectRandom = new(240821);
    private readonly Random _initiativeSpeechRandom = new();
    private int _imageCacheEvictions;
    private const int MaxDecodedFrameCache = 36;
    private const double BaseWindowSize = 320;
    private const double BasePetImageSize = 310;
    private const double BaseFallbackSize = 240;
    private const double MinPetScale = 0.5;
    private const double MaxPetScale = 2.5;
    private const double PetScaleStep = 0.08;
    private ControlPanelWindow? _controlPanel;
    private DesktopChatWindow? _chatWindow;
    private DesktopSpeechBubbleWindow? _speechBubbleWindow;
    private PetMotionRequest? _activeRequest;
    private int _phaseIndex;
    private int _frameIndex;
    private int _loopCount;
    private int _visualScalePhaseIndex = -1;
    private Point _pointerDown;
    private DateTimeOffset _pointerDownAt;
    private bool _dragStarted;
    private DateTimeOffset _lastTapCandidateAt;
    private Point _lastTapCandidate;
    private int _tapCandidateCount;
    private double _petScale = 1.0;
    private CancellationTokenSource? _effectCancellation;
    private double _savedOpacity = 1.0;
    private string _broomDirection = "right";
    private string _carRideDirection = "right";
    private bool _suspendAnimationFrames;
    private string _currentFramePath = string.Empty;
    private double _lockedFrameScale = 1.0;
    private EffectDisplaySnapshot? _effectDisplaySnapshot;
    public CarRideStartupMetrics? LastCarRideStartupMetrics { get; private set; }

    public MainWindow()
    {
        BootstrapLog.WriteRaw("mainwindow_ctor_entered");
        InitializeComponent();

        _runtime = new DesktopRuntimeHost();
        _agentRuntime = DesktopAgentRuntime.CreateDefault(BuildConversationRuntimeState);
        _runtime.MotionRequested += Runtime_MotionRequested;
        _runtime.PetPixelSizeRequested += Runtime_PetPixelSizeRequested;
        _runtime.PetScaleRequested += Runtime_PetScaleRequested;
        LocationChanged += (_, _) => RepositionChat();
        SizeChanged += (_, _) => RepositionChat();
        Closed += (_, _) =>
        {
            _effectCancellation?.Cancel();
            _coinStateTimer.Stop();
            _coinSingleClickTimer.Stop();
            _ownerSingleClickTimer.Stop();
            _initiativeSpeechTimer.Stop();
            var chatWindow = _chatWindow;
            _chatWindow = null;
            chatWindow?.Close();
            var speechBubbleWindow = _speechBubbleWindow;
            _speechBubbleWindow = null;
            speechBubbleWindow?.Close();
            lock (_imageCacheSync)
            {
                _imageCache.Clear();
                _imageCacheOrder.Clear();
            }
            _agentRuntime.Dispose();
        };

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _animationTimer.Tick += (_, _) => AdvanceFrame();

        _autonomousTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autonomousTimer.Tick += async (_, _) => await _runtime.SubmitAutonomousTickAsync();

        _coinStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _coinStateTimer.Tick += (_, _) => _runtime.RefreshPetrifiedCoinState();

        _coinSingleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
        _coinSingleClickTimer.Tick += async (_, _) =>
        {
            _coinSingleClickTimer.Stop();
            if (_runtime.IsPetrified)
                await _runtime.SubmitPetrifiedCoinClickAsync();
        };

        _ownerSingleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
        _ownerSingleClickTimer.Tick += async (_, _) =>
        {
            _ownerSingleClickTimer.Stop();
            if (!_runtime.IsPetrified)
                await _runtime.SubmitGestureAsync(PetGestureKind.OwnerTouch, BehaviorRequestSource.OwnerUi);
        };

        _initiativeSpeechTimer.Tick += InitiativeSpeechTimer_Tick;

        ApplyPetScale(LoadPetScale(), persist: false);
        _runtime.StartIdle();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyVisiblePlacement(WindowPlacement.BottomRight(
            SystemParameters.WorkArea,
            Width,
            Height,
            24));
        _autonomousTimer.Start();
        _coinStateTimer.Start();
        ScheduleNextInitiativeSpeech();
        BootstrapLog.WriteRaw("mainwindow_loaded_handler");
        BootstrapLog.Write("MainWindow Loaded", this.Snapshot());
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        BootstrapLog.WriteRaw("mainwindow_source_initialized_handler");
        BootstrapLog.Write("MainWindow SourceInitialized", this.Snapshot());
    }

    private void Window_ContentRendered(object sender, EventArgs e)
    {
        BootstrapLog.WriteRaw("mainwindow_content_rendered_handler");
        BootstrapLog.Write("MainWindow ContentRendered", this.Snapshot());
    }

    private async void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_runtime.IsPetrified && e.ClickCount >= 2)
        {
            _coinSingleClickTimer.Stop();
            ResetTapCandidates();
            await _runtime.SubmitPetrifiedCoinDoubleClickAsync();
            e.Handled = true;
            return;
        }

        _pointerDown = e.GetPosition(this);
        _pointerDownAt = DateTimeOffset.Now;
        _dragStarted = false;
        await _runtime.RecordInputAsync(DesktopInputEventAdapter.PointerDown(_pointerDown));
        PetImage.CaptureMouse();
        e.Handled = true;
    }

    private async void PetImage_MouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);
        await _runtime.RecordInputAsync(DesktopInputEventAdapter.PointerMove(current));

        if (e.LeftButton != MouseButtonState.Pressed || _dragStarted)
            return;

        var distance = Distance(_pointerDown, current);
        var held = DateTimeOffset.Now - _pointerDownAt;
        if (distance > 78 && held > TimeSpan.FromMilliseconds(180))
        {
            _dragStarted = true;
            PetImage.ReleaseMouseCapture();
            try
            {
                DragMove();
            }
            catch (InvalidOperationException ex)
            {
                BootstrapLog.Write("DragMove skipped", ex);
            }
        }
    }

    private async void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var up = e.GetPosition(this);
        PetImage.ReleaseMouseCapture();
        await _runtime.RecordInputAsync(DesktopInputEventAdapter.PointerUp(up));

        if (_dragStarted)
        {
            _dragStarted = false;
            e.Handled = true;
            return;
        }

        var now = DateTimeOffset.Now;
        var hitVisibleBody = _runtime.IsPetrified ? HitVisibleBody(up, alphaThreshold: 4) : HitVisibleBody(up);
        var duration = now - _pointerDownAt;
        var distance = Distance(_pointerDown, up);
        var sample = new GestureSample(
            _pointerDown,
            up,
            duration,
            e.ClickCount,
            hitVisibleBody);
        var gesture = GestureInterpreter.Interpret(sample);
        if (!hitVisibleBody && IsChatSensorPoint(up))
        {
            ToggleChat();
            e.Handled = true;
            return;
        }
        var tapCandidateCount = IsTapCandidate(hitVisibleBody, duration, distance)
            ? RegisterTapCandidate(up, now)
            : 0;
        if (tapCandidateCount >= 3)
            gesture = PetGestureKind.RapidTap;
        else if (tapCandidateCount == 2)
            gesture = PetGestureKind.DoubleClick;

        if (_runtime.IsPetrified && tapCandidateCount > 0)
        {
            if (gesture == PetGestureKind.DoubleClick || tapCandidateCount >= 2)
            {
                _coinSingleClickTimer.Stop();
                ResetTapCandidates();
                await _runtime.SubmitPetrifiedCoinDoubleClickAsync();
            }
            else
            {
                _coinSingleClickTimer.Stop();
                _coinSingleClickTimer.Start();
            }
            e.Handled = true;
            return;
        }

        if (gesture == PetGestureKind.RapidTap)
        {
            _ownerSingleClickTimer.Stop();
            ResetTapCandidates();
            await _runtime.SubmitGestureAsync(gesture, BehaviorRequestSource.OwnerUi);
        }
        else if (OpensChatOnGesture(gesture))
        {
            _ownerSingleClickTimer.Stop();
            ResetTapCandidates();
            await _runtime.SubmitGestureAsync(PetGestureKind.DoubleClick, BehaviorRequestSource.OwnerUi);
            OpenChatForInput();
        }
        else
        {
            if (tapCandidateCount == 1)
            {
                _ownerSingleClickTimer.Stop();
                _ownerSingleClickTimer.Start();
            }
            else if (gesture is PetGestureKind.Stroke or PetGestureKind.Drag)
            {
                _ownerSingleClickTimer.Stop();
            }
            if (gesture is PetGestureKind.Stroke or PetGestureKind.Drag)
                await _runtime.SubmitGestureAsync(gesture, BehaviorRequestSource.OwnerUi);
        }
        e.Handled = true;
    }

    private static bool IsTapCandidate(bool hitVisibleBody, TimeSpan duration, double distance) =>
        hitVisibleBody && duration <= TimeSpan.FromMilliseconds(520) && distance <= 8;

    private int RegisterTapCandidate(Point up, DateTimeOffset now)
    {
        var sameBurst =
            now - _lastTapCandidateAt <= TimeSpan.FromMilliseconds(900) &&
            Distance(_lastTapCandidate, up) <= 8;
        _tapCandidateCount = sameBurst ? _tapCandidateCount + 1 : 1;
        _lastTapCandidateAt = now;
        _lastTapCandidate = up;
        return _tapCandidateCount;
    }

    private void ResetTapCandidates()
    {
        _tapCandidateCount = 0;
        _lastTapCandidateAt = default;
        _lastTapCandidate = default;
    }

    private async void StopMenuItem_Click(object sender, RoutedEventArgs e) => await StopCurrentBehaviorAsync("menu:stop");

    private async void OwnerCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string command })
            await _runtime.SubmitOwnerCommandAsync(command);
    }

    private async void MagicMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string behaviorId })
            await _runtime.SubmitMagicAsync(behaviorId, BehaviorRequestSource.OwnerContextMenu);
    }

    private async void CarRideMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitCarRideAsync(BehaviorRequestSource.OwnerContextMenu);

    private void OpenPanelMenuItem_Click(object sender, RoutedEventArgs e) => OpenControlPanel();

    private void ChatMenuItem_Click(object sender, RoutedEventArgs e) => ToggleChat();

    private void PetScaleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string action })
            return;

        switch (action)
        {
            case "increase":
                AdjustPetScale(PetScaleStep);
                break;
            case "decrease":
                AdjustPetScale(-PetScaleStep);
                break;
            case "reset":
                ApplyPetScale(1.0, persist: true);
                break;
        }
    }

    private void AdjustPetScale(double delta) => ApplyPetScale(_petScale + delta, persist: true);

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _effectCancellation?.Cancel();
        RestoreWindowAfterEffect();
        _animationTimer.Stop();
        _coinStateTimer.Stop();
        _coinSingleClickTimer.Stop();
        _ownerSingleClickTimer.Stop();
        _autonomousTimer.Stop();
        _controlPanel?.Close();
        Close();
        System.Windows.Application.Current?.Shutdown();
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var step = e.Delta > 0 ? PetScaleStep : -PetScaleStep;
        AdjustPetScale(step);
        e.Handled = true;
    }


    private void Runtime_PetPixelSizeRequested(object? sender, int pixels)
    {
        Dispatcher.Invoke(() => SetPetScaleForTest(pixels / BasePetImageSize));
    }

    private void Runtime_PetScaleRequested(object? sender, double scale)
    {
        Dispatcher.Invoke(() => ApplyPetScale(scale, persist: true));
    }

    private void SetAnimationIntervalForCurrentFrame(PlayableMotion motion, int phaseIndex, int frameIndex, bool useDirectionalFrames)
    {
        var phases = motion.Phases.Where(x => x.Frames.Count > 0).ToList();
        if (phaseIndex < 0 || phaseIndex >= phases.Count)
            return;

        var phase = phases[phaseIndex];
        var duration = useDirectionalFrames ? motion.FrameDurationMs : phase.DurationForFrame(frameIndex, motion.FrameDurationMs);
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(16, duration));
    }

    private void Runtime_MotionRequested(object? sender, PetMotionRequest request)
    {
        Dispatcher.Invoke(() =>
        {
            var pipelineElapsed = ElapsedMilliseconds(request.RequestedAtTimestamp);
            _activeRequest = request;
            _phaseIndex = 0;
            _frameIndex = 0;
            _loopCount = 0;
            _visualScalePhaseIndex = -1;
            PrepareMotionEffect(request);
            var firstPhase = request.Motion.Phases.FirstOrDefault(x => x.Frames.Count > 0);
            var scaleStarted = Stopwatch.GetTimestamp();
            if (firstPhase is not null)
                ApplyMotionVisualScale(request.Motion, firstPhase, 0);
            else
                ApplyMotionVisualScale(request.Motion);
            var scaleElapsed = ElapsedMilliseconds(scaleStarted);
            SetAnimationIntervalForCurrentFrame(request.Motion, phaseIndex: 0, frameIndex: 0, useDirectionalFrames: false);
            var firstFrameStarted = Stopwatch.GetTimestamp();
            ShowFirstAvailableFrame(request.Motion);
            var firstFrameElapsed = ElapsedMilliseconds(firstFrameStarted);
            StartMotionEffect(request);
            _animationTimer.Start();
            if (request.Motion.Effect == DesktopMotionEffect.CarRide)
            {
                PrefetchCarRideStartupFrames(request.Motion);
                Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    var firstVisible = ElapsedMilliseconds(request.RequestedAtTimestamp);
                    LastCarRideStartupMetrics = new CarRideStartupMetrics(pipelineElapsed, scaleElapsed, firstFrameElapsed, firstVisible);
                    _runtime.ReportPerformance($"car_ride_first_visible pipeline_ms={pipelineElapsed:0.0} scale_ms={scaleElapsed:0.0} first_frame_decode_ms={firstFrameElapsed:0.0} total_ms={firstVisible:0.0}");
                });
            }
        });
    }

    private void AdvanceFrame()
    {
        if (_activeRequest is null || _suspendAnimationFrames)
            return;

        var phases = _activeRequest.Motion.Phases.Where(x => x.Frames.Count > 0).ToList();
        if (phases.Count == 0)
        {
            ShowFallback("missing frames");
            return;
        }

        if (_phaseIndex >= phases.Count)
        {
            FinishCurrentMotion();
            return;
        }

        var phase = phases[_phaseIndex];
        ApplyMotionVisualScale(_activeRequest.Motion, phase, _phaseIndex);
        if (phase.Name is "interrupt_exit" or "fallback")
        {
            FinishCurrentMotion();
            return;
        }
        var phaseFrames = phase.Frames;
        var directionKey = _activeRequest.Motion.Effect switch
        {
            DesktopMotionEffect.BroomFlight => _broomDirection,
            DesktopMotionEffect.CarRide => _carRideDirection,
            _ => string.Empty
        };
        if (phase.Loop &&
            !string.IsNullOrWhiteSpace(directionKey) &&
            _activeRequest.Motion.DirectionalFrames?.TryGetValue(directionKey, out var directional) == true &&
            directional.Count > 0)
        {
            phaseFrames = directional;
        }
        var frame = phaseFrames[Math.Clamp(_frameIndex, 0, phaseFrames.Count - 1)];
        SetFrame(frame, phase.Name);
        _frameIndex++;

        if (_frameIndex < phaseFrames.Count)
        {
            SetAnimationIntervalForCurrentFrame(_activeRequest.Motion, _phaseIndex, _frameIndex, !ReferenceEquals(phaseFrames, phase.Frames));
            return;
        }

        if (phase.Loop)
        {
            _loopCount++;
            if (_activeRequest.LoopCycles == int.MaxValue || _loopCount < _activeRequest.LoopCycles)
            {
                _frameIndex = 0;
                SetAnimationIntervalForCurrentFrame(_activeRequest.Motion, _phaseIndex, _frameIndex, !ReferenceEquals(phaseFrames, phase.Frames));
                return;
            }
        }

        _phaseIndex++;
        _frameIndex = 0;
        _loopCount = 0;
        SetAnimationIntervalForCurrentFrame(_activeRequest.Motion, _phaseIndex, _frameIndex, useDirectionalFrames: false);
    }

    private void FinishCurrentMotion()
    {
        if (_activeRequest is null)
            return;

        var behaviorId = _activeRequest.Motion.BehaviorId;
        var phase = _runtime.CurrentPhase;
        _animationTimer.Stop();
        _coinSingleClickTimer.Stop();
        _ownerSingleClickTimer.Stop();
        var returnToIdle = _activeRequest.ReturnToIdle;
        _activeRequest = null;
        if (returnToIdle)
            _runtime.CompleteMotion(behaviorId, phase);
    }

    private async Task StopCurrentBehaviorAsync(string reason)
    {
        _effectCancellation?.Cancel();
        RestoreWindowAfterEffect();
        _animationTimer.Stop();
        _coinSingleClickTimer.Stop();
        _ownerSingleClickTimer.Stop();
        _suspendAnimationFrames = false;
        _activeRequest = null;
        _autonomousTimer.Start();
        await _runtime.StopAsync(reason);
    }

    private void PrepareMotionEffect(PetMotionRequest request)
    {
        _effectCancellation?.Cancel();
        _effectCancellation?.Dispose();
        _effectCancellation = new CancellationTokenSource();
        RestoreWindowAfterEffect();
        _effectDisplaySnapshot = request.Motion.Effect == DesktopMotionEffect.None
            ? null
            : CaptureEffectDisplaySnapshot();

        if (request.Motion.Effect == DesktopMotionEffect.Petrify)
            _autonomousTimer.Stop();
        else if (request.Motion.Effect == DesktopMotionEffect.PetrifyRelease)
            _autonomousTimer.Start();

    }

    private void StartMotionEffect(PetMotionRequest request)
    {
        var token = _effectCancellation?.Token ?? CancellationToken.None;
        _ = request.Motion.Effect switch
        {
            DesktopMotionEffect.BroomFlight => RunBroomFlightAsync(request, token),
            DesktopMotionEffect.Apparate => RunApparateAsync(request, token),
            DesktopMotionEffect.Scourgify => RunScourgifyAsync(token),
            DesktopMotionEffect.CarRide => RunCarRideAsync(request, token),
            _ => Task.CompletedTask
        };
    }

    private async Task RunBroomFlightAsync(PetMotionRequest request, CancellationToken token)
    {
        try
        {
            var workArea = WindowPlacement.CurrentWorkingArea(this);
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var start = new Point(Left, Top);
            var duration = ChooseShowcaseDuration(_effectRandom);
            var route = BuildRandomFlightRoute(start, workArea, width, height, duration, _effectRandom);
            var routePoints = new[] { start }
                .Concat(route.Where(x => x.Visibility != MotionRouteVisibility.ExitOffscreen).Select(x => x.Target))
                .ToArray();
            _runtime.ReportBroomFlightMetrics(
                routePoints.Max(x => x.X) - routePoints.Min(x => x.X),
                routePoints.Max(x => x.Y) - routePoints.Min(x => x.Y),
                workArea);
            var current = start;
            foreach (var segment in route)
            {
                current = await PrepareRouteSegmentStartAsync(current, segment, token);
                await MoveWindowAsync(current, segment.Target, segment.Duration, token, segment.Easing);
                current = segment.Target;
            }
            await PlayNamedMotionPhaseAsync(request.Motion, "exit", token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _runtime.ReportError($"broom_flight_failed:{ex.GetType().Name}");
        }
        finally
        {
            _suspendAnimationFrames = false;
            RestoreWindowAfterEffect();
            ApplyVisiblePlacement(new Point(Left, Top));
            if (ReferenceEquals(_activeRequest, request))
                FinishCurrentMotion();
        }
    }

    private async Task RunApparateAsync(PetMotionRequest request, CancellationToken token)
    {
        _savedOpacity = Opacity;
        _suspendAnimationFrames = true;
        try
        {
            ShowFirstAvailableFrame(request.Motion);
            for (var i = 1; i <= 18; i++)
            {
                token.ThrowIfCancellationRequested();
                Opacity = Math.Max(0, _savedOpacity * (1 - i / 18.0));
                await Task.Delay(80, token);
            }

            Opacity = 0;
            await Task.Delay(Random.Shared.Next(5000, 10001), token);

            var workArea = WindowPlacement.CurrentWorkingArea(this);
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var target = ChooseApparateTarget(new Point(Left, Top), workArea, width, height);
            Left = target.X;
            Top = target.Y;

            for (var i = 1; i <= 18; i++)
            {
                token.ThrowIfCancellationRequested();
                ShowApparateReappearFrame(request.Motion, i, 18);
                Opacity = Math.Min(_savedOpacity, _savedOpacity * i / 18.0);
                await Task.Delay(80, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _runtime.ReportError($"apparate_failed:{ex.GetType().Name}");
        }
        finally
        {
            RestoreWindowAfterEffect();
            _suspendAnimationFrames = false;
            if (ReferenceEquals(_activeRequest, request))
                FinishCurrentMotion();
        }
    }

    private void ShowApparateReappearFrame(PlayableMotion motion, int step, int totalSteps)
    {
        var exit = motion.Phases.FirstOrDefault(x => string.Equals(x.Name, "exit", StringComparison.OrdinalIgnoreCase));
        if (exit?.Frames.Count > 0 != true)
            return;

        var frameIndex = Math.Clamp((int)Math.Floor((step - 1) / (double)Math.Max(1, totalSteps) * exit.Frames.Count), 0, exit.Frames.Count - 1);
        SetFrame(exit.Frames[frameIndex], "apparate_reappear", motion.VisualScale);
    }
    private async Task RunScourgifyAsync(CancellationToken token)
    {
        try
        {
            PhaseBadge.Visibility = Visibility.Visible;
            PhaseText.Text = "Scourgify";
            var workArea = WindowPlacement.CurrentWorkingArea(this);
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var y = Math.Max(workArea.Top, workArea.Bottom - height - 22);
            var left = ClampToWorkArea(new Point(workArea.Left + 24, y), workArea, width, height);
            var right = ClampToWorkArea(new Point(workArea.Right - width - 24, y), workArea, width, height);
            await MoveWindowAsync(new Point(Left, Top), left, TimeSpan.FromMilliseconds(450), token);
            await MoveWindowAsync(left, right, TimeSpan.FromMilliseconds(1500), token);
            await MoveWindowAsync(right, left, TimeSpan.FromMilliseconds(1200), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _runtime.ReportError($"scourgify_failed:{ex.GetType().Name}");
        }
        finally
        {
            PhaseBadge.Visibility = Visibility.Collapsed;
            RestoreWindowAfterEffect();
            ApplyVisiblePlacement(new Point(Left, Top));
        }
    }

    private async Task RunCarRideAsync(PetMotionRequest request, CancellationToken token)
    {
        try
        {
            var workArea = WindowPlacement.CurrentWorkingArea(this);
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var start = ClampToWorkArea(new Point(Left, Top), workArea, width, height);
            var duration = ChooseShowcaseDuration(_effectRandom);
            var route = BuildCarRidePhysicalRoute(start, workArea, width, height, duration, _effectRandom);
            var current = start;
            var direction = _carRideDirection;
            var roadGazeCount = 0;
            var nextRoadGazeAt = DateTimeOffset.MinValue;
            foreach (var segment in route)
            {
                current = await PrepareRouteSegmentStartAsync(current, segment, token);
                if (!string.Equals(direction, segment.Direction, StringComparison.OrdinalIgnoreCase))
                    await PlayCarRideTurnPathAsync(request.Motion, direction, segment.Direction, token);
                _carRideDirection = segment.Direction;
                var movement = MoveWindowAsync(current, segment.Target, segment.Duration, token, segment.Easing);
                if (CanPlayCarRoadGaze(request.Motion, segment, roadGazeCount, nextRoadGazeAt) &&
                    _effectRandom.NextDouble() < 0.42)
                {
                    var key = $"road-gaze/{segment.Direction}";
                    if (request.Motion.NamedSequences?.TryGetValue(key, out var gazeFrames) == true && gazeFrames.Count > 0)
                    {
                        roadGazeCount++;
                        nextRoadGazeAt = DateTimeOffset.UtcNow.AddSeconds(_effectRandom.Next(6, 13));
                        await PlayFramesAsync(request.Motion, $"road-gaze:{segment.Direction}", gazeFrames, token);
                    }
                }
                await movement;
                current = segment.Target;
                direction = segment.Direction;
            }
            await PlayCarRideBrakeAsync(request.Motion, direction, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _runtime.ReportError($"car_ride_failed:{ex.GetType().Name}");
        }
        finally
        {
            _suspendAnimationFrames = false;
            RestoreWindowAfterEffect();
            ApplyVisiblePlacement(new Point(Left, Top));
            if (ReferenceEquals(_activeRequest, request))
                FinishCurrentMotion();
        }
    }

    private async Task<Point> PrepareRouteSegmentStartAsync(
        Point current,
        MotionRouteSegment segment,
        CancellationToken token)
    {
        if (segment.Visibility != MotionRouteVisibility.ReenterFromOffscreen || segment.StartOverride is not Point hiddenStart)
            return current;

        Left = hiddenStart.X;
        Top = hiddenStart.Y;
        if (segment.HiddenHold is { } hiddenHold && hiddenHold > TimeSpan.Zero)
            await Task.Delay(hiddenHold, token);
        return hiddenStart;
    }

    private bool CanPlayCarRoadGaze(
        PlayableMotion motion,
        MotionRouteSegment segment,
        int roadGazeCount,
        DateTimeOffset nextRoadGazeAt)
    {
        var key = $"road-gaze/{segment.Direction}";
        var frameCount = 0;
        if (motion.NamedSequences?.TryGetValue(key, out var frames) == true)
            frameCount = frames.Count;
        return IsCarRoadGazeWindowEligible(
            segment,
            roadGazeCount,
            nextRoadGazeAt,
            DateTimeOffset.UtcNow,
            _frameIndex,
            frameCount);
    }

    public static bool IsCarRoadGazeWindowEligible(
        MotionRouteSegment segment,
        int roadGazeCount,
        DateTimeOffset nextRoadGazeAt,
        DateTimeOffset now,
        int currentFrameIndex,
        int sequenceFrameCount) =>
        roadGazeCount < 2 &&
        now >= nextRoadGazeAt &&
        segment.Visibility == MotionRouteVisibility.Visible &&
        segment.Duration >= TimeSpan.FromMilliseconds(1800) &&
        segment.Direction is "left" or "right" &&
        currentFrameIndex == 0 &&
        sequenceFrameCount >= 6 &&
        sequenceFrameCount % 6 == 0;
    private async Task MoveWindowAsync(Point from, Point to, TimeSpan duration, CancellationToken token, MotionEasing easing = MotionEasing.EaseInOut)
    {
        if (_activeRequest?.Motion.Effect == DesktopMotionEffect.BroomFlight)
            _broomDirection = ResolveEightWayDirection(from, to);
        else if (_activeRequest?.Motion.Effect == DesktopMotionEffect.CarRide)
            _carRideDirection = ResolveCarRideDirection(from, to);
        var steps = Math.Max(1, (int)(duration.TotalMilliseconds / 24));
        for (var i = 1; i <= steps; i++)
        {
            token.ThrowIfCancellationRequested();
            var progress = i / (double)steps;
            var t = easing switch
            {
                MotionEasing.Accelerate => progress * progress,
                MotionEasing.Decelerate => 1 - Math.Pow(1 - progress, 2),
                MotionEasing.Linear => progress,
                _ => EaseInOut(progress)
            };
            Left = from.X + (to.X - from.X) * t;
            Top = from.Y + (to.Y - from.Y) * t;
            await Task.Delay(24, token);
        }
    }

    public static TimeSpan ChooseShowcaseDuration(Random random) =>
        TimeSpan.FromMilliseconds(random.Next(10_000, 20_001));

    public static IReadOnlyList<MotionRouteSegment> BuildRandomFlightRoute(
        Point start,
        Rect workArea,
        double width,
        double height,
        TimeSpan totalDuration,
        Random random)
    {
        var origin = ClampToWorkArea(start, workArea, width, height);
        var minX = workArea.Left;
        var maxX = Math.Max(minX, workArea.Right - width);
        var minY = workArea.Top;
        var maxY = Math.Max(minY, workArea.Bottom - height);
        var availableWidth = Math.Max(1, maxX - minX);
        var availableHeight = Math.Max(1, maxY - minY);
        var spanX = availableWidth * (0.62 + random.NextDouble() * 0.18);
        var spanY = availableHeight * (0.50 + random.NextDouble() * 0.22);
        var left = Math.Clamp(origin.X - spanX * (0.42 + random.NextDouble() * 0.16), minX, maxX - spanX);
        var top = Math.Clamp(origin.Y - spanY * (0.42 + random.NextDouble() * 0.16), minY, maxY - spanY);
        var right = left + spanX;
        var bottom = top + spanY;
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var points = new List<Point>
        {
            new(right, centerY + spanY * 0.24),
            new(right - spanX * 0.18, top),
            new(left + spanX * 0.22, top + spanY * 0.12),
            new(left, bottom - spanY * 0.18),
            new(centerX + spanX * 0.16, bottom),
            new(right, centerY - spanY * 0.08),
            origin
        };

        var route = BuildVariablePaceRoute(
            origin,
            points,
            totalDuration,
            ResolveEightWayDirection,
            random,
            fastSpeedRange: (430, 620),
            slowSpeedRange: (190, 300),
            slowOnDirectionChange: true);
        if (random.NextDouble() < 0.20)
            InsertOffscreenExcursion(route, workArea, width, height, random, ResolveEightWayDirection);
        NormalizeRouteDuration(route, totalDuration);
        return route;
    }

    public static IReadOnlyList<MotionRouteSegment> BuildCarRidePhysicalRoute(
        Point start,
        Rect workArea,
        double width,
        double height,
        TimeSpan totalDuration,
        Random random)
    {
        var minX = workArea.Left;
        var maxX = Math.Max(minX, workArea.Right - width);
        var minY = workArea.Top;
        var maxY = Math.Max(minY, workArea.Bottom - height);
        var origin = ClampToWorkArea(start, workArea, width, height);
        var availableWidth = Math.Max(1, maxX - minX);
        var availableHeight = Math.Max(1, maxY - minY);
        var spanX = availableWidth * (0.62 + random.NextDouble() * 0.18);
        var spanY = availableHeight * (0.38 + random.NextDouble() * 0.20);
        var left = Math.Clamp(origin.X - spanX * (0.42 + random.NextDouble() * 0.16), minX, maxX - spanX);
        var top = Math.Clamp(origin.Y - spanY * (0.42 + random.NextDouble() * 0.16), minY, maxY - spanY);
        var right = left + spanX;
        var bottom = top + spanY;
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var ring = new List<Point>
        {
            new(right, centerY),
            new(right, bottom),
            new(centerX, bottom),
            new(left, bottom),
            new(left, centerY),
            new(left, top),
            new(centerX, top),
            new(right, top)
        };
        if (random.NextDouble() < 0.5)
            ring.Reverse();

        var nearestIndex = Enumerable.Range(0, ring.Count)
            .OrderBy(index => Distance(origin, ring[index]))
            .First();
        var points = Enumerable.Range(1, 7)
            .Select(offset => ring[(nearestIndex + offset) % ring.Count])
            .ToList();
        var route = BuildVariablePaceRoute(
            origin,
            points,
            totalDuration,
            ResolveCarRideDirection,
            random,
            fastSpeedRange: (280, 440),
            slowSpeedRange: (105, 180),
            slowOnDirectionChange: true);
        if (random.NextDouble() < 0.20)
            InsertOffscreenExcursion(route, workArea, width, height, random, ResolveCarRideDirection);
        NormalizeRouteDuration(route, totalDuration);
        return route;
    }

    private static List<MotionRouteSegment> BuildVariablePaceRoute(
        Point origin,
        IReadOnlyList<Point> points,
        TimeSpan totalDuration,
        Func<Point, Point, string> resolveDirection,
        Random random,
        (double Min, double Max) fastSpeedRange,
        (double Min, double Max) slowSpeedRange,
        bool slowOnDirectionChange)
    {
        var route = new List<MotionRouteSegment>(points.Count);
        var current = origin;
        string? previousDirection = null;
        for (var index = 0; index < points.Count; index++)
        {
            var target = points[index];
            var direction = resolveDirection(current, target);
            var turning = previousDirection is not null && !string.Equals(previousDirection, direction, StringComparison.OrdinalIgnoreCase);
            var speedRange = turning && slowOnDirectionChange ? slowSpeedRange : fastSpeedRange;
            var speed = speedRange.Min + random.NextDouble() * (speedRange.Max - speedRange.Min);
            var seconds = Math.Max(0.55, Distance(current, target) / Math.Max(1, speed));
            route.Add(new MotionRouteSegment(
                target,
                direction,
                TimeSpan.FromSeconds(seconds),
                index == 0 ? MotionEasing.Accelerate : turning ? MotionEasing.EaseInOut : MotionEasing.Linear));
            current = target;
            previousDirection = direction;
        }

        if (route.Count > 0)
            route[^1] = route[^1] with { Easing = MotionEasing.Decelerate };
        NormalizeRouteDuration(route, totalDuration);
        return route;
    }

    private static void InsertOffscreenExcursion(
        List<MotionRouteSegment> route,
        Rect workArea,
        double width,
        double height,
        Random random,
        Func<Point, Point, string> resolveDirection)
    {
        if (route.Count < 4)
            return;

        var insertAt = random.Next(1, route.Count - 1);
        var prior = route[insertAt - 1].Target;
        var exitRight = prior.X >= workArea.Left + workArea.Width / 2;
        var exitTarget = new Point(
            exitRight ? workArea.Right + 24 : workArea.Left - width - 24,
            Math.Clamp(prior.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height)));
        var entryStart = new Point(
            exitRight ? workArea.Left - width - 24 : workArea.Right + 24,
            Math.Clamp(workArea.Top + random.NextDouble() * Math.Max(1, workArea.Height - height), workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height)));
        var inset = Math.Min(90, Math.Max(12, workArea.Width * 0.05));
        var entryTarget = new Point(
            exitRight ? workArea.Left + inset : Math.Max(workArea.Left, workArea.Right - width - inset),
            entryStart.Y);

        route.Insert(insertAt, new MotionRouteSegment(
            exitTarget,
            resolveDirection(prior, exitTarget),
            TimeSpan.FromMilliseconds(random.Next(700, 1101)),
            MotionEasing.Accelerate,
            MotionRouteVisibility.ExitOffscreen));
        route.Insert(insertAt + 1, new MotionRouteSegment(
            entryTarget,
            resolveDirection(entryStart, entryTarget),
            TimeSpan.FromMilliseconds(random.Next(800, 1251)),
            MotionEasing.Decelerate,
            MotionRouteVisibility.ReenterFromOffscreen,
            entryStart,
            TimeSpan.FromMilliseconds(random.Next(150, 451))));
        if (insertAt + 2 < route.Count)
        {
            var next = route[insertAt + 2];
            route[insertAt + 2] = next with { Direction = resolveDirection(entryTarget, next.Target) };
        }
    }

    private static void NormalizeRouteDuration(List<MotionRouteSegment> route, TimeSpan totalDuration)
    {
        if (route.Count == 0)
            return;
        var currentTotal = route.Sum(x => x.Duration.TotalMilliseconds);
        if (currentTotal <= 0)
            return;
        var scale = totalDuration.TotalMilliseconds / currentTotal;
        for (var index = 0; index < route.Count; index++)
            route[index] = route[index] with { Duration = TimeSpan.FromMilliseconds(Math.Max(300, route[index].Duration.TotalMilliseconds * scale)) };

        var drift = totalDuration - TimeSpan.FromMilliseconds(route.Sum(x => x.Duration.TotalMilliseconds));
        route[^1] = route[^1] with { Duration = route[^1].Duration + drift };
    }

    public static bool IsFullyOutsideWorkArea(Point topLeft, Rect workArea, double width, double height) =>
        topLeft.X + width <= workArea.Left ||
        topLeft.X >= workArea.Right ||
        topLeft.Y + height <= workArea.Top ||
        topLeft.Y >= workArea.Bottom;

    private async Task PlayCarRideTurnAsync(PlayableMotion motion, string from, string to, CancellationToken token)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return;
        var key = $"turn/{from}-to-{to}";
        if (motion.NamedSequences?.TryGetValue(key, out var frames) != true || frames.Count == 0)
            return;
        await PlayFramesAsync(motion, $"turn:{from}-to-{to}", frames, token);
    }

    private async Task PlayCarRideTurnPathAsync(PlayableMotion motion, string from, string to, CancellationToken token)
    {
        var ring = new[] { "right", "front-right", "front", "front-left", "left", "rear-left", "rear", "rear-right" };
        var fromIndex = Array.IndexOf(ring, from);
        var toIndex = Array.IndexOf(ring, to);
        if (fromIndex < 0 || toIndex < 0)
            return;

        var clockwise = (toIndex - fromIndex + ring.Length) % ring.Length;
        var counterClockwise = (fromIndex - toIndex + ring.Length) % ring.Length;
        var step = clockwise <= counterClockwise ? 1 : -1;
        var currentIndex = fromIndex;
        while (currentIndex != toIndex)
        {
            var nextIndex = (currentIndex + step + ring.Length) % ring.Length;
            await PlayCarRideTurnAsync(motion, ring[currentIndex], ring[nextIndex], token);
            currentIndex = nextIndex;
        }
    }

    private async Task PlayCarRideBrakeAsync(PlayableMotion motion, string direction, CancellationToken token)
    {
        var key = $"brake/{direction}";
        if (motion.NamedSequences?.TryGetValue(key, out var frames) == true && frames.Count > 0)
        {
            await PlayFramesAsync(motion, $"brake:{direction}", frames, token);
            return;
        }
        await PlayNamedMotionPhaseAsync(motion, "exit", token);
    }

    private async Task PlayNamedMotionPhaseAsync(PlayableMotion motion, string phaseName, CancellationToken token)
    {
        var phase = motion.Phases.FirstOrDefault(x => string.Equals(x.Name, phaseName, StringComparison.OrdinalIgnoreCase));
        if (phase?.Frames.Count > 0)
            await PlayFramesAsync(motion, phaseName, phase.Frames, token, phase);
    }

    private async Task PlayFramesAsync(
        PlayableMotion motion,
        string phaseName,
        IReadOnlyList<string> frames,
        CancellationToken token,
        MotionPhase? phase = null)
    {
        _suspendAnimationFrames = true;
        try
        {
            for (var index = 0; index < frames.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                SetFrame(frames[index], phaseName);
                var duration = phase?.DurationForFrame(index, motion.FrameDurationMs) ?? motion.FrameDurationMs;
                await Task.Delay(Math.Max(16, duration), token);
            }
        }
        finally
        {
            _suspendAnimationFrames = false;
        }
    }

    private EffectDisplaySnapshot CaptureEffectDisplaySnapshot()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        return new EffectDisplaySnapshot(
            _petScale,
            _lockedFrameScale,
            Opacity,
            Left,
            Top,
            width,
            height,
            new Point(Left + width / 2, Top + height),
            _runtime.CurrentStablePosture.ToString(),
            WindowPlacement.CurrentWorkingArea(this));
    }

    private void RestoreWindowAfterEffect()
    {
        var snapshot = _effectDisplaySnapshot;
        Opacity = snapshot?.Opacity is > 0 ? snapshot.Opacity : _savedOpacity <= 0 ? 1.0 : _savedOpacity;
        if (snapshot is not null)
        {
            var currentAnchor = CurrentGroundAnchor();
            _petScale = RuntimeVisualScale.ClampUserScale(snapshot.UserScale);
            _lockedFrameScale = snapshot.ActionLocalScale;
            ApplyLockedVisualScale(currentAnchor);
        }
        else
        {
            ApplyVisiblePlacement(new Point(Left, Top));
        }
        _effectDisplaySnapshot = null;
    }

    private static double ElapsedMilliseconds(long startedTimestamp) =>
        startedTimestamp <= 0 ? 0 : Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    public static Point ChooseApparateTarget(Point current, Rect workArea, double width, double height)
    {
        var maxX = Math.Max(workArea.Left, workArea.Right - width);
        var maxY = Math.Max(workArea.Top, workArea.Bottom - height);
        var minDistance = Math.Max(Math.Min(width, height) * 0.75, 120);

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var candidate = new Point(
                Random.Shared.NextDouble() * (maxX - workArea.Left) + workArea.Left,
                Random.Shared.NextDouble() * (maxY - workArea.Top) + workArea.Top);
            if (Distance(current, candidate) >= minDistance)
                return ClampToWorkArea(candidate, workArea, width, height);
        }

        return ClampToWorkArea(new Point(maxX, maxY), workArea, width, height);
    }

    private static Point ClampToWorkArea(Point preferred, Rect workArea, double width, double height) => new(
        Math.Min(Math.Max(preferred.X, workArea.Left), Math.Max(workArea.Left, workArea.Right - width)),
        Math.Min(Math.Max(preferred.Y, workArea.Top), Math.Max(workArea.Top, workArea.Bottom - height)));

    public static IReadOnlyList<Point> BuildCarRidePreviewPath(Point start, Rect workArea, double width, double height)
    {
        var horizontal = Math.Max(width * 1.3, workArea.Width * 0.12);
        var vertical = Math.Max(height * 0.62, workArea.Height * 0.09);
        var raw = new[]
        {
            new Point(start.X + horizontal, start.Y),
            new Point(start.X + horizontal * 1.45, start.Y + vertical),
            new Point(start.X + horizontal * 1.45, start.Y + vertical * 1.85),
            new Point(start.X + horizontal * 1.02, start.Y + vertical * 2.35),
            new Point(start.X - horizontal * 0.68, start.Y + vertical * 2.25),
            new Point(start.X - horizontal * 1.45, start.Y + vertical * 1.25),
            new Point(start.X - horizontal * 1.22, start.Y - vertical * 0.55),
            new Point(start.X - horizontal * 0.18, start.Y - vertical * 1.32),
            new Point(start.X + horizontal * 0.92, start.Y - vertical * 0.52),
            new Point(start.X + horizontal * 1.18, start.Y + vertical * 0.18)
        };

        return raw.Select(point => ClampToWorkArea(point, workArea, width, height)).ToArray();
    }
    private static double EaseInOut(double value) =>
        value < 0.5 ? 2 * value * value : 1 - Math.Pow(-2 * value + 2, 2) / 2;

    public static string ResolveEightWayDirection(Point from, Point to)
    {
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;
        return angle switch
        {
            >= -22.5 and < 22.5 => "right",
            >= 22.5 and < 67.5 => "down-right",
            >= 67.5 and < 112.5 => "down",
            >= 112.5 and < 157.5 => "down-left",
            >= 157.5 or < -157.5 => "left",
            >= -157.5 and < -112.5 => "up-left",
            >= -112.5 and < -67.5 => "up",
            _ => "up-right"
        };
    }

    public static string ResolveCarRideDirection(Point from, Point to)
    {
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;
        return angle switch
        {
            >= -22.5 and < 22.5 => "right",
            >= 22.5 and < 67.5 => "front-right",
            >= 67.5 and < 112.5 => "front",
            >= 112.5 and < 157.5 => "front-left",
            >= 157.5 or < -157.5 => "left",
            >= -157.5 and < -112.5 => "rear-left",
            >= -112.5 and < -67.5 => "rear",
            _ => "rear-right"
        };
    }
    private void ShowFirstAvailableFrame(PlayableMotion motion)
    {
        var frame = motion.Phases.SelectMany(x => x.Frames).FirstOrDefault();
        if (frame is null)
        {
            ShowFallback("missing first frame");
            return;
        }

        SetFrame(frame, motion.Phases.First(x => x.Frames.Count > 0).Name);
    }

    private void SetFrame(string path, string phase, double? visualScale = null)
    {
        try
        {
            PetImage.Source = LoadImage(path);
            _currentFramePath = path;
            FallbackBadge.Visibility = Visibility.Collapsed;
            PhaseBadge.Visibility = Visibility.Collapsed;
            _runtime.MarkPhase(phase, path);
            RepositionChat();
        }
        catch (Exception ex)
        {
            BootstrapLog.WriteRaw($"frame_decode_failed_{ex.GetType().Name}");
            BootstrapLog.Write("Frame decode failed", ex);
            _runtime.ReportError($"frame_decode_failed:{Path.GetFileName(path)}:{ex.GetType().Name}");
            ShowFallback(ex.GetType().Name);
        }
    }

    private BitmapImage LoadImage(string path)
    {
        lock (_imageCacheSync)
        {
            if (_imageCache.TryGetValue(path, out var cached))
                return cached;
        }

        var image = DecodeFrozenBitmap(path);
        lock (_imageCacheSync)
        {
            if (_imageCache.TryGetValue(path, out var existing))
                return existing;
            _imageCache[path] = image;
            _imageCacheOrder.Enqueue(path);
            TrimImageCache();
        }
        return image;
    }

    private static BitmapImage DecodeFrozenBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void PrefetchCarRideStartupFrames(PlayableMotion motion)
    {
        var intro = motion.Phases.FirstOrDefault(x => x.Frames.Count > 0)?.Frames ?? Array.Empty<string>();
        var directional = motion.DirectionalFrames?.TryGetValue(_carRideDirection, out var currentDirectionFrames) == true
            ? currentDirectionFrames
            : Array.Empty<string>();
        var frames = intro
            .Concat(directional)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        _ = Task.Run(() =>
        {
            foreach (var frame in frames)
            {
                try
                {
                    LoadImage(frame);
                }
                catch (Exception ex)
                {
                    BootstrapLog.WriteRaw($"car_ride_prefetch_failed_{ex.GetType().Name}");
                }
            }
        });
    }

    private void TrimImageCache()
    {
        while (_imageCache.Count > MaxDecodedFrameCache && _imageCacheOrder.Count > 0)
        {
            var candidate = _imageCacheOrder.Dequeue();
            if (!_imageCache.ContainsKey(candidate))
                continue;
            if (string.Equals(candidate, _currentFramePath, StringComparison.OrdinalIgnoreCase))
            {
                _imageCacheOrder.Enqueue(candidate);
                break;
            }
            _imageCache.Remove(candidate);
            _imageCacheEvictions++;
        }
    }

    public int DecodedFrameCacheCount
    {
        get { lock (_imageCacheSync) return _imageCache.Count; }
    }

    public long EstimatedDecodedFrameBytes
    {
        get { lock (_imageCacheSync) return _imageCache.Values.Sum(x => (long)x.PixelWidth * x.PixelHeight * 4); }
    }

    public int DecodedFrameCacheEvictions => _imageCacheEvictions;
    private void ShowFallback(string reason)
    {
        PetImage.Source = null;
        _currentFramePath = string.Empty;
        _lockedFrameScale = 1.0;
        ApplyLockedVisualScale();
        FallbackBadge.Visibility = Visibility.Visible;
        PhaseBadge.Visibility = Visibility.Visible;
        PhaseText.Text = $"Fallback: {reason}";
        _runtime.ReportError($"fallback:{reason}");
        RepositionChat();
    }

    private bool HitVisibleBody(Point point, byte alphaThreshold = 18)
    {
        var source = PetImage.Source as BitmapSource;
        if (source is null || PetImage.ActualWidth <= 0 || PetImage.ActualHeight <= 0)
            return false;

        var imagePoint = TranslatePoint(point, PetImage);
        if (imagePoint.X < 0 || imagePoint.Y < 0 || imagePoint.X > PetImage.ActualWidth || imagePoint.Y > PetImage.ActualHeight)
            return false;

        var pixelX = (int)Math.Clamp(imagePoint.X / PetImage.ActualWidth * source.PixelWidth, 0, source.PixelWidth - 1);
        var pixelY = (int)Math.Clamp(imagePoint.Y / PetImage.ActualHeight * source.PixelHeight, 0, source.PixelHeight - 1);
        var pixel = new byte[4];
        source.CopyPixels(new Int32Rect(pixelX, pixelY, 1, 1), pixel, 4, 0);
        return pixel[3] > alphaThreshold;
    }

    private void OpenControlPanel()
    {
        if (_controlPanel is { IsLoaded: true })
        {
            _controlPanel.Show();
            _controlPanel.Activate();
            return;
        }

        _controlPanel = new ControlPanelWindow(_runtime, _agentRuntime);
        _controlPanel.Closed += (_, _) => _controlPanel = null;
        _controlPanel.Show();
    }

    private bool IsChatSensorPoint(Point point) => IsChatSensorPoint(
        new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height),
        point);

    public static bool IsChatSensorPoint(Size size, Point point) =>
        point.Y >= Math.Max(0, size.Height - 42) &&
        point.X >= size.Width * 0.28 &&
        point.X <= size.Width * 0.72;

    public static bool OpensChatOnGesture(PetGestureKind gesture) => gesture == PetGestureKind.DoubleClick;

    private void ToggleChat()
    {
        EnsureChatWindow();
        if (_chatWindow!.IsExpanded)
            _chatWindow.Collapse();
        else
            OpenChatForInput();
    }

    private void OpenChatForInput()
    {
        EnsureChatWindow();
        var workArea = SystemParameters.WorkArea;
        var visiblePet = CurrentVisiblePetBounds();
        var adjusted = DesktopChatPlacement.MakeRoomBelow(
            workArea,
            visiblePet,
            new Size(_chatWindow!.Width, _chatWindow.Height));
        var verticalShift = adjusted.Top - visiblePet.Top;
        if (Math.Abs(verticalShift) > 0.01)
            ApplyVisiblePlacement(new Point(Left, Top + verticalShift));
        _chatWindow.ShowForInput(workArea, CurrentVisiblePetBounds());
    }

    private void EnsureChatWindow()
    {
        if (_chatWindow is not null)
            return;
        _chatWindow = new DesktopChatWindow(_agentRuntime);
        _chatWindow.AssistantReplyAvailable += (_, text) => ShowSpeechBubble(text);
    }

    private void ShowSpeechBubble(string text)
    {
        _speechBubbleWindow ??= new DesktopSpeechBubbleWindow();
        _speechBubbleWindow.ShowMessage(text, SystemParameters.WorkArea, CurrentVisiblePetBounds());
    }

    private void InitiativeSpeechTimer_Tick(object? sender, EventArgs e)
    {
        _initiativeSpeechTimer.Stop();
        var nextCheck = InitiativeSpeechSchedule.NextInterval(_initiativeSpeechRandom);
        try
        {
            var decision = _runtime.DecideInitiativeSpeech(_chatWindow?.IsExpanded == true);
            nextCheck = decision.NextCheck;
            if (!decision.ShouldSpeak)
                return;

            var text = InitiativeSpeechSchedule.SelectMessage(_initiativeSpeechRandom, decision.Topic, _runtime.CurrentStablePosture);
            _runtime.RecordInitiativeSpeech(decision.Topic);
            ShowSpeechBubble(text);
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("initiative_speech_failed", ex);
        }
        finally
        {
            if (IsLoaded)
                ScheduleNextInitiativeSpeech(nextCheck);
        }
    }

    private void ScheduleNextInitiativeSpeech(TimeSpan? interval = null)
    {
        _initiativeSpeechTimer.Stop();
        _initiativeSpeechTimer.Interval = interval ?? InitiativeSpeechSchedule.NextInterval(_initiativeSpeechRandom);
        _initiativeSpeechTimer.Start();
    }

    private void RepositionChat()
    {
        if (_chatWindow is null && _speechBubbleWindow is null)
            return;
        var workArea = SystemParameters.WorkArea;
        var bounds = CurrentVisiblePetBounds();
        _chatWindow?.Reposition(workArea, bounds);
        _speechBubbleWindow?.Reposition(workArea, bounds);
    }

    private Rect CurrentVisiblePetBounds()
    {
        var windowBounds = CurrentBounds();
        if (PetImage.Source is not BitmapSource source ||
            PetImage.ActualWidth <= 0 || PetImage.ActualHeight <= 0)
            return windowBounds;

        try
        {
            var origin = PetImage.TranslatePoint(new Point(0, 0), this);
            var imageBounds = new Rect(
                windowBounds.Left + origin.X,
                windowBounds.Top + origin.Y,
                PetImage.ActualWidth,
                PetImage.ActualHeight);
            var metrics = string.IsNullOrWhiteSpace(_currentFramePath)
                ? new MotionVisibleMetrics(source.PixelWidth, source.PixelHeight, new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight))
                : MotionVisualSizer.Measure(_currentFramePath);
            return DesktopChatPlacement.VisibleSubjectBounds(imageBounds, metrics);
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("visible_pet_bounds_failed", ex);
            return windowBounds;
        }
    }

    private Rect CurrentBounds() => new(
        Left,
        Top,
        ActualWidth > 0 ? ActualWidth : Width,
        ActualHeight > 0 ? ActualHeight : Height);

    private PetRuntimeStateSnapshot BuildConversationRuntimeState() => new(
        _runtime.CurrentBehaviorId,
        Math.Clamp((_runtime.Energy + _runtime.Curiosity) / 2, 0, 1),
        _runtime.Stress,
        _runtime.Social,
        _runtime.Curiosity,
        _runtime.Curiosity,
        1.0 - _runtime.Energy,
        _runtime.Comfort)
    {
        CurrentPosture = _runtime.CurrentStablePosture.ToString(),
        CurrentAction = _runtime.CurrentAction,
        MoodValence = _runtime.Mood
    };

    private void ApplyVisiblePlacement(Point preferred)
    {
        var workArea = WindowPlacement.CurrentWorkingArea(this);
        var width = Math.Max(ActualWidth > 0 ? ActualWidth : Width, MinWidth);
        var height = Math.Max(ActualHeight > 0 ? ActualHeight : Height, MinHeight);
        Left = Math.Min(Math.Max(preferred.X, workArea.Left), Math.Max(workArea.Left, workArea.Right - width));
        Top = Math.Min(Math.Max(preferred.Y, workArea.Top), Math.Max(workArea.Top, workArea.Bottom - height));
    }

    private void ApplyPetScale(double scale, bool persist)
    {
        var anchor = CurrentGroundAnchorOrNull();
        _petScale = RuntimeVisualScale.ClampUserScale(Math.Clamp(scale, MinPetScale, MaxPetScale));
        ApplyLockedVisualScale(anchor);
        if (persist)
            File.WriteAllText(PetScalePath(), _petScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ApplyMotionVisualScale(PlayableMotion motion)
    {
        _lockedFrameScale = MotionVisualSizer.RenderScaleForMotion(motion, _runtime.ReferenceVisualFramePath);
        ApplyLockedVisualScale(CurrentGroundAnchorOrNull());
    }

    private void ApplyMotionVisualScale(PlayableMotion motion, MotionPhase phase, int phaseIndex)
    {
        if (_visualScalePhaseIndex == phaseIndex)
            return;
        _lockedFrameScale = MotionVisualSizer.RenderScaleForPhase(motion, phase, _runtime.ReferenceVisualFramePath);
        _visualScalePhaseIndex = phaseIndex;
        ApplyLockedVisualScale(CurrentGroundAnchorOrNull());
    }

    private void ApplyLockedVisualScale(Point? groundAnchor = null)
    {
        var frameScale = _lockedFrameScale;
        var windowScale = Math.Max(1.0, frameScale);
        Width = BaseWindowSize * _petScale * windowScale;
        Height = BaseWindowSize * _petScale * windowScale;
        MinWidth = 240 * _petScale * windowScale;
        MinHeight = 240 * _petScale * windowScale;
        PetImage.Width = BasePetImageSize * _petScale * frameScale;
        PetImage.Height = BasePetImageSize * _petScale * frameScale;
        FallbackBadge.Width = BaseFallbackSize * _petScale;
        FallbackBadge.Height = BaseFallbackSize * _petScale;
        FallbackBadge.CornerRadius = new CornerRadius((BaseFallbackSize * _petScale) / 2);
        if (groundAnchor is { } anchor)
            ApplyVisiblePlacement(new Point(anchor.X - Width / 2, anchor.Y - Height));
    }

    public double PetScale => _petScale;
    public double ActionLocalScale => _lockedFrameScale;
    public double EffectivePetScale => RuntimeVisualScale.EffectiveScale(_petScale, _lockedFrameScale);

    private Point CurrentGroundAnchor() =>
        new(Left + (ActualWidth > 0 ? ActualWidth : Width) / 2, Top + (ActualHeight > 0 ? ActualHeight : Height));

    private Point? CurrentGroundAnchorOrNull() =>
        double.IsNaN(Left) || double.IsNaN(Top) || double.IsInfinity(Left) || double.IsInfinity(Top)
            ? null
            : CurrentGroundAnchor();

    public void SetPetScaleForTest(double scale) => ApplyPetScale(scale, persist: false);

    private double LoadPetScale()
    {
        var path = PetScalePath();
        return File.Exists(path) &&
               double.TryParse(File.ReadAllText(path), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            ? scale
            : 1.0;
    }

    private string PetScalePath()
    {
        var dir = _agentRuntime.DataPaths.ProfileDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "pet-scale.txt");
    }

    internal object Snapshot() => new
    {
        Handle = new WindowInteropHelper(this).Handle.ToString(),
        Width,
        Height,
        ActualWidth,
        ActualHeight,
        Left,
        Top,
        Visibility = Visibility.ToString(),
        Opacity,
        WindowState = WindowState.ToString(),
        ShowInTaskbar,
        Topmost
    };

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
