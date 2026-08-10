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
    private readonly DispatcherTimer _autonomousTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly Dictionary<string, BitmapImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private const double BaseWindowSize = 320;
    private const double BasePetImageSize = 310;
    private const double BaseFallbackSize = 240;
    private ControlPanelWindow? _controlPanel;
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

    public MainWindow()
    {
        BootstrapLog.WriteRaw("mainwindow_ctor_entered");
        InitializeComponent();

        _runtime = new DesktopRuntimeHost();
        _runtime.MotionRequested += Runtime_MotionRequested;

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _animationTimer.Tick += (_, _) => AdvanceFrame();

        _autonomousTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autonomousTimer.Tick += async (_, _) => await _runtime.SubmitAutonomousTickAsync();

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
        var tapCandidateCount = IsTapCandidate(hitVisibleBody, duration, distance)
            ? RegisterTapCandidate(up, now)
            : 0;
        if (tapCandidateCount >= 3)
            gesture = PetGestureKind.RapidTap;
        else if (tapCandidateCount == 2)
            gesture = PetGestureKind.DoubleClick;

        if (gesture == PetGestureKind.RapidTap)
        {
            ResetTapCandidates();
            await _runtime.SubmitGestureAsync(gesture, BehaviorRequestSource.OwnerUi);
        }
        else if (gesture == PetGestureKind.DoubleClick)
        {
            ResetTapCandidates();
            await _runtime.SubmitGestureAsync(PetGestureKind.OwnerTouch, BehaviorRequestSource.OwnerUi);
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

    private async void TouchMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitContextMenuIntentAsync(new SemanticIntent(SemanticIntentKind.Touch, "wk.interaction.prone_touch"));

    private async void QuietMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitContextMenuIntentAsync(new SemanticIntent(SemanticIntentKind.Quiet, "wk.core.prone_idle"));

    private void OpenPanelMenuItem_Click(object sender, RoutedEventArgs e) => OpenControlPanel();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _animationTimer.Stop();
        _autonomousTimer.Stop();
        _controlPanel?.Close();
        Close();
        System.Windows.Application.Current?.Shutdown();
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var step = e.Delta > 0 ? 0.08 : -0.08;
        ApplyPetScale(_petScale + step, persist: true);
        e.Handled = true;
    }

    private void Runtime_MotionRequested(object? sender, PetMotionRequest request)
    {
        Dispatcher.Invoke(() =>
        {
            _activeRequest = request;
            _phaseIndex = 0;
            _frameIndex = 0;
            _loopCount = 0;
            _animationTimer.Interval = TimeSpan.FromMilliseconds(request.Motion.FrameDurationMs);
            ShowFirstAvailableFrame(request.Motion);
            _animationTimer.Start();
        });
    }

    private void AdvanceFrame()
    {
        if (_activeRequest is null)
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
        var frame = phase.Frames[Math.Clamp(_frameIndex, 0, phase.Frames.Count - 1)];
        SetFrame(frame, phase.Name);
        _frameIndex++;

        if (_frameIndex < phase.Frames.Count)
            return;

        if (phase.Loop)
        {
            _loopCount++;
            if (_activeRequest.LoopCycles == int.MaxValue || _loopCount < _activeRequest.LoopCycles)
            {
                _frameIndex = 0;
                return;
            }
        }

        _phaseIndex++;
        _frameIndex = 0;
        _loopCount = 0;
    }

    private void FinishCurrentMotion()
    {
        if (_activeRequest is null)
            return;

        var behaviorId = _activeRequest.Motion.BehaviorId;
        var phase = _runtime.CurrentPhase;
        _animationTimer.Stop();
        var returnToIdle = _activeRequest.ReturnToIdle;
        _activeRequest = null;
        if (returnToIdle)
            _runtime.CompleteMotion(behaviorId, phase);
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

    private void SetFrame(string path, string phase)
    {
        try
        {
            PetImage.Source = LoadImage(path);
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
        return image;
    }

    private void ShowFallback(string reason)
    {
        PetImage.Source = null;
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

        _controlPanel = new ControlPanelWindow(_runtime);
        _controlPanel.Closed += (_, _) => _controlPanel = null;
        _controlPanel.Show();
    }

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
        _petScale = Math.Clamp(scale, 0.75, 1.5);
        Width = BaseWindowSize * _petScale;
        Height = BaseWindowSize * _petScale;
        MinWidth = 240 * _petScale;
        MinHeight = 240 * _petScale;
        PetImage.Width = BasePetImageSize * _petScale;
        PetImage.Height = BasePetImageSize * _petScale;
        FallbackBadge.Width = BaseFallbackSize * _petScale;
        FallbackBadge.Height = BaseFallbackSize * _petScale;
        FallbackBadge.CornerRadius = new CornerRadius((BaseFallbackSize * _petScale) / 2);
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
            ApplyVisiblePlacement(new Point(Left, Top));
        if (persist)
            File.WriteAllText(PetScalePath(), _petScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
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
