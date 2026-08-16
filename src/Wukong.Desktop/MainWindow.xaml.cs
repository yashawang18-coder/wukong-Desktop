using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wukong.Domain;

namespace Wukong.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopRuntimeHost _runtime;
    private readonly DesktopAgentRuntime _agentRuntime;
    private readonly DispatcherTimer _autonomousTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _coinStateTimer;
    private readonly DispatcherTimer _coinSingleClickTimer;
    private readonly Dictionary<string, BitmapImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _imageCacheOrder = new();
    private int _imageCacheEvictions;
    private const int MaxDecodedFrameCache = 36;
    private const double BaseWindowSize = 320;
    private const double BasePetImageSize = 310;
    private const double BaseFallbackSize = 240;
    private const double MinPetScale = 0.5;
    private const double MaxPetScale = 1.5;
    private const double PetScaleStep = 0.08;
    private ControlPanelWindow? _controlPanel;
    private DesktopChatWindow? _chatWindow;
    private PetMotionRequest? _activeRequest;
    private int _phaseIndex;
    private int _frameIndex;
    private int _loopCount;
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

    public MainWindow()
    {
        BootstrapLog.WriteRaw("mainwindow_ctor_entered");
        InitializeComponent();

        _runtime = new DesktopRuntimeHost();
        _agentRuntime = DesktopAgentRuntime.CreateDefault();
        _runtime.MotionRequested += Runtime_MotionRequested;
        _runtime.PetPixelSizeRequested += Runtime_PetPixelSizeRequested;
        LocationChanged += (_, _) => RepositionChat();
        SizeChanged += (_, _) => RepositionChat();
        Closed += (_, _) =>
        {
            _effectCancellation?.Cancel();
            _coinStateTimer.Stop();
            _coinSingleClickTimer.Stop();
            var chatWindow = _chatWindow;
            _chatWindow = null;
            chatWindow?.Close();
            _imageCache.Clear();
            _imageCacheOrder.Clear();
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
        var hitVisibleBody = HitVisibleBody(up);
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
            ResetTapCandidates();
            await _runtime.SubmitGestureAsync(gesture, BehaviorRequestSource.OwnerUi);
        }
        else if (gesture == PetGestureKind.DoubleClick)
        {
            ResetTapCandidates();
            await _runtime.SubmitGestureAsync(PetGestureKind.OwnerTouch, BehaviorRequestSource.OwnerUi);
            OpenControlPanel();
        }
        else
        {
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
            _activeRequest = request;
            _phaseIndex = 0;
            _frameIndex = 0;
            _loopCount = 0;
            BeginMotionEffect(request);
            SetAnimationIntervalForCurrentFrame(request.Motion, phaseIndex: 0, frameIndex: 0, useDirectionalFrames: false);
            ShowFirstAvailableFrame(request.Motion);
            _animationTimer.Start();
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
        _suspendAnimationFrames = false;
        _activeRequest = null;
        _autonomousTimer.Start();
        await _runtime.StopAsync(reason);
    }

    private void BeginMotionEffect(PetMotionRequest request)
    {
        _effectCancellation?.Cancel();
        _effectCancellation?.Dispose();
        _effectCancellation = new CancellationTokenSource();
        RestoreWindowAfterEffect();

        if (request.Motion.Effect == DesktopMotionEffect.Petrify)
            _autonomousTimer.Stop();
        else if (request.Motion.Effect == DesktopMotionEffect.PetrifyRelease)
            _autonomousTimer.Start();

        _ = request.Motion.Effect switch
        {
            DesktopMotionEffect.BroomFlight => RunBroomFlightAsync(_effectCancellation.Token),
            DesktopMotionEffect.Apparate => RunApparateAsync(request, _effectCancellation.Token),
            DesktopMotionEffect.Scourgify => RunScourgifyAsync(_effectCancellation.Token),
            DesktopMotionEffect.CarRide => RunCarRideAsync(_effectCancellation.Token),
            _ => Task.CompletedTask
        };
    }

    private async Task RunBroomFlightAsync(CancellationToken token)
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var path = new[]
            {
                new Point(workArea.Left + workArea.Width * 0.18, workArea.Top + workArea.Height * 0.72),
                new Point(workArea.Left + workArea.Width * 0.36, workArea.Top + workArea.Height * 0.44),
                new Point(workArea.Left + workArea.Width * 0.62, workArea.Top + workArea.Height * 0.38),
                new Point(workArea.Left + workArea.Width * 0.78, workArea.Top + workArea.Height * 0.64),
                new Point(workArea.Left + workArea.Width * 0.48, workArea.Top + workArea.Height * 0.76)
            };

            var start = new Point(Left, Top);
            foreach (var target in path.Select(x => ClampToWorkArea(x, workArea, width, height)))
            {
                await MoveWindowAsync(start, target, TimeSpan.FromMilliseconds(620), token);
                start = target;
            }
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
            ApplyVisiblePlacement(new Point(Left, Top));
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

            var workArea = SystemParameters.WorkArea;
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
            var workArea = SystemParameters.WorkArea;
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
            ApplyVisiblePlacement(new Point(Left, Top));
        }
    }

    private async Task RunCarRideAsync(CancellationToken token)
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var start = ClampToWorkArea(new Point(Left, Top), workArea, width, height);
            var path = BuildCarRidePreviewPath(start, workArea, width, height);

            var current = start;
            foreach (var target in path)
            {
                await MoveWindowAsync(current, target, TimeSpan.FromMilliseconds(1350), token);
                current = target;
            }
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
            ApplyVisiblePlacement(new Point(Left, Top));
        }
    }
    private async Task MoveWindowAsync(Point from, Point to, TimeSpan duration, CancellationToken token)
    {
        if (_activeRequest?.Motion.Effect == DesktopMotionEffect.BroomFlight)
            _broomDirection = ResolveEightWayDirection(from, to);
        else if (_activeRequest?.Motion.Effect == DesktopMotionEffect.CarRide)
            _carRideDirection = ResolveCarRideDirection(from, to);
        var steps = Math.Max(1, (int)(duration.TotalMilliseconds / 24));
        for (var i = 1; i <= steps; i++)
        {
            token.ThrowIfCancellationRequested();
            var t = EaseInOut(i / (double)steps);
            Left = from.X + (to.X - from.X) * t;
            Top = from.Y + (to.Y - from.Y) * t;
            await Task.Delay(24, token);
        }
    }

    private void RestoreWindowAfterEffect()
    {
        Opacity = _savedOpacity <= 0 ? 1.0 : _savedOpacity;
        ApplyVisiblePlacement(new Point(Left, Top));
    }

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
            ApplyFrameVisualScale(path, visualScale ?? _activeRequest?.Motion.VisualScale ?? 1.0);
            FallbackBadge.Visibility = Visibility.Collapsed;
            PhaseBadge.Visibility = Visibility.Collapsed;
            _runtime.MarkPhase(phase, path);
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
        if (_imageCache.TryGetValue(path, out var cached))
            return cached;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        _imageCache[path] = image;
        _imageCacheOrder.Enqueue(path);
        TrimImageCache();
        return image;
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

    public int DecodedFrameCacheCount => _imageCache.Count;

    public long EstimatedDecodedFrameBytes => _imageCache.Values.Sum(x => (long)x.PixelWidth * x.PixelHeight * 4);

    public int DecodedFrameCacheEvictions => _imageCacheEvictions;
    private void ShowFallback(string reason)
    {
        PetImage.Source = null;
        _currentFramePath = string.Empty;
        ApplyFrameVisualScale(null, 1.0);
        FallbackBadge.Visibility = Visibility.Visible;
        PhaseBadge.Visibility = Visibility.Visible;
        PhaseText.Text = $"Fallback: {reason}";
        _runtime.ReportError($"fallback:{reason}");
    }

    private bool HitVisibleBody(Point point)
    {
        var source = PetImage.Source as BitmapSource;
        if (source is null || PetImage.ActualWidth <= 0 || PetImage.ActualHeight <= 0)
            return false;

        var imagePoint = PetImage.TranslatePoint(point, PetImage);
        if (imagePoint.X < 0 || imagePoint.Y < 0 || imagePoint.X > PetImage.ActualWidth || imagePoint.Y > PetImage.ActualHeight)
            return false;

        var pixelX = (int)Math.Clamp(imagePoint.X / PetImage.ActualWidth * source.PixelWidth, 0, source.PixelWidth - 1);
        var pixelY = (int)Math.Clamp(imagePoint.Y / PetImage.ActualHeight * source.PixelHeight, 0, source.PixelHeight - 1);
        var pixel = new byte[4];
        source.CopyPixels(new Int32Rect(pixelX, pixelY, 1, 1), pixel, 4, 0);
        return pixel[3] > 18;
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

    private void ToggleChat()
    {
        _chatWindow ??= new DesktopChatWindow(_agentRuntime) { Owner = this };
        _chatWindow.Toggle(SystemParameters.WorkArea, CurrentBounds());
    }

    private void RepositionChat() =>
        _chatWindow?.Reposition(SystemParameters.WorkArea, CurrentBounds());

    private Rect CurrentBounds() => new(
        Left,
        Top,
        ActualWidth > 0 ? ActualWidth : Width,
        ActualHeight > 0 ? ActualHeight : Height);

    private void ApplyVisiblePlacement(Point preferred)
    {
        var workArea = SystemParameters.WorkArea;
        var width = Math.Max(ActualWidth > 0 ? ActualWidth : Width, MinWidth);
        var height = Math.Max(ActualHeight > 0 ? ActualHeight : Height, MinHeight);
        Left = Math.Min(Math.Max(preferred.X, workArea.Left), Math.Max(workArea.Left, workArea.Right - width));
        Top = Math.Min(Math.Max(preferred.Y, workArea.Top), Math.Max(workArea.Top, workArea.Bottom - height));
    }

    private void ApplyPetScale(double scale, bool persist)
    {
        _petScale = Math.Clamp(scale, MinPetScale, MaxPetScale);
        Width = BaseWindowSize * _petScale;
        Height = BaseWindowSize * _petScale;
        MinWidth = 240 * _petScale;
        MinHeight = 240 * _petScale;
        ApplyFrameVisualScale(string.IsNullOrWhiteSpace(_currentFramePath) ? null : _currentFramePath, _activeRequest?.Motion.VisualScale ?? 1.0);
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
            ApplyVisiblePlacement(new Point(Left, Top));
        if (persist)
            File.WriteAllText(PetScalePath(), _petScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ApplyFrameVisualScale(string? framePath, double targetVisibleRatio)
    {
        var frameScale = MotionVisualSizer.RenderScaleFor(framePath, _runtime.ReferenceVisualFramePath, targetVisibleRatio);
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
    }

    public double PetScale => _petScale;

    public void SetPetScaleForTest(double scale) => ApplyPetScale(scale, persist: false);

    private static double LoadPetScale()
    {
        var path = PetScalePath();
        return File.Exists(path) &&
               double.TryParse(File.ReadAllText(path), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            ? scale
            : 1.0;
    }

    private static string PetScalePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wukong", "profile");
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
