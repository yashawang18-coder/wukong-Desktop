using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Wukong.Desktop;

public partial class ControlPanelWindow : Window
{
    private readonly DesktopRuntimeHost _runtime;
    private readonly DispatcherTimer _previewTimer;
    private readonly ObservableCollection<AlbumFolderItem> _albumFolders = new();
    private readonly ObservableCollection<string> _albumMediaBindings = new();
    private PlayableMotion? _previewMotion;
    private MotionPhase? _previewPhase;
    private IReadOnlyList<string> _previewFrames = Array.Empty<string>();
    private AlbumFolderItem? _selectedAlbum;
    private string _albumRoot = AlbumFolderItem.GetDefaultAlbumRoot();
    private int _previewIndex;
    private bool _previewPaused;
    private bool _previewDark;

    public ControlPanelWindow(DesktopRuntimeHost runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        DataContext = _runtime;
        TraceList.ItemsSource = _runtime.TraceLines;
        AssetList.ItemsSource = _runtime.Motions;
        AlbumList.ItemsSource = _albumFolders;
        AlbumMediaList.ItemsSource = _albumMediaBindings;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _previewTimer.Tick += (_, _) => AdvancePreview();
        RefreshAlbumView();
    }

    private async void FakeModelButton_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitFakeModelMessageAsync(ModelInput.Text);

    private async void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string command })
            await _runtime.SubmitOwnerCommandAsync(command);
    }

    private void DeveloperToggle_Changed(object sender, RoutedEventArgs e) =>
        TraceList.Visibility = DeveloperToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page })
            return;

        OwnerPage.Visibility = page == "Owner" ? Visibility.Visible : Visibility.Collapsed;
        ProfilePage.Visibility = page == "Profile" ? Visibility.Visible : Visibility.Collapsed;
        AlbumPage.Visibility = page == "Album" ? Visibility.Visible : Visibility.Collapsed;
        ModelPage.Visibility = page == "Model" ? Visibility.Visible : Visibility.Collapsed;
        AssetsPage.Visibility = page == "Assets" ? Visibility.Visible : Visibility.Collapsed;
        DeveloperPage.Visibility = page == "Developer" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseAlbumRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "\u9009\u62e9\u609f\u7a7a\u76f8\u518c\u76ee\u5f55",
            InitialDirectory = Directory.Exists(_albumRoot)
                ? _albumRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog(this) == true)
        {
            _albumRoot = dialog.FolderName;
            AlbumFolderItem.SaveAlbumRootPreference(_albumRoot);
            RefreshAlbumView();
        }
    }

    private void RefreshAlbum_Click(object sender, RoutedEventArgs e) => RefreshAlbumView();

    private void OpenAlbumRoot_Click(object sender, RoutedEventArgs e) => OpenFolder(_albumRoot);

    private void AlbumItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AlbumFolderItem item })
            SelectAlbum(item);
    }

    private void SaveAlbumDescription_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedAlbumMarkdown();
    }

    private void OpenSelectedAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbum is not null)
            OpenFolder(_selectedAlbum.DirectoryPath);
    }

    private void AddAlbumMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbum is null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "\u65b0\u589e\u76f8\u518c\u56fe\u7247\u7d20\u6750",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        Directory.CreateDirectory(_selectedAlbum.DirectoryPath);
        foreach (var source in dialog.FileNames)
        {
            var fileName = MakeUniqueFileName(_selectedAlbum.DirectoryPath, Path.GetFileName(source));
            var target = Path.Combine(_selectedAlbum.DirectoryPath, fileName);
            if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target);
            if (!_albumMediaBindings.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                _albumMediaBindings.Add(fileName);
        }

        SaveSelectedAlbumMarkdown();
    }

    private void UnbindAlbumMedia_Click(object sender, RoutedEventArgs e)
    {
        if (AlbumMediaList.SelectedItem is not string fileName)
            return;

        _albumMediaBindings.Remove(fileName);
        SaveSelectedAlbumMarkdown();
    }

    private void UploadPetAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "\u9009\u62e9\u609f\u7a7a\u5934\u50cf\u622a\u56fe",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wukong",
            "profile");
        Directory.CreateDirectory(profileDir);
        var target = Path.Combine(profileDir, "pet-avatar" + Path.GetExtension(dialog.FileName));
        File.Copy(dialog.FileName, target, overwrite: true);
        OwnerAvatarImage.Source = LoadBitmap(target);
        OwnerAvatarFallback.Visibility = Visibility.Collapsed;
    }

    private void ProfileTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tab })
            return;

        ProfilePetPanel.Visibility = tab == "Pet" ? Visibility.Visible : Visibility.Collapsed;
        ProfileOwnerPanel.Visibility = tab == "Owner" ? Visibility.Visible : Visibility.Collapsed;
        ProfileRelationPanel.Visibility = tab == "Relation" ? Visibility.Visible : Visibility.Collapsed;
        ProfileMemoryPanel.Visibility = tab == "Memory" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SavePetPrompt_Click(object sender, RoutedEventArgs e)
    {
        var profileDir = ProfileDirectory();
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "pet-prompt.txt"), PetPromptText.Text);
    }

    private void SaveOwnerProfile_Click(object sender, RoutedEventArgs e)
    {
        var profileDir = ProfileDirectory();
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(
            Path.Combine(profileDir, "owner-profile.txt"),
            string.Join(Environment.NewLine, new[]
            {
                $"call_name={OwnerCallNameText.Text}",
                $"schedule={OwnerScheduleText.Text}",
                $"preference={OwnerPreferenceText.Text}",
                $"tone={((OwnerToneCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty)}",
                $"notes={OwnerNotesText.Text}"
            }));
    }

    private void SaveModelConfig_Click(object sender, RoutedEventArgs e)
    {
        var profileDir = ProfileDirectory();
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(
            Path.Combine(profileDir, "model-config.txt"),
            string.Join(Environment.NewLine, new[]
            {
                $"provider={((ModelProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty)}",
                $"api_url={ModelApiUrlText.Text}",
                $"api_key_set={!string.IsNullOrWhiteSpace(ModelApiKeyBox.Password)}",
                $"model={ModelNameText.Text}",
                "backend_connected=false"
            }));
    }

    private void PreviewAsset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;

        _previewMotion = motion;
        PreviewTitle.Text = $"{motion.DisplayName} - {motion.BehaviorId}";
        PreviewMeta.Text = $"{motion.Category} - {motion.Direction} - {motion.FrameCount} frames - {motion.Fps:F2} fps - {motion.RuntimeStatus}";
        PreviewPhaseCombo.ItemsSource = motion.Phases;
        PreviewPhaseCombo.DisplayMemberPath = nameof(MotionPhase.Name);
        PreviewPhaseCombo.SelectedIndex = 0;
        SelectPreviewPhase(motion.Phases.FirstOrDefault());
    }

    private void PreviewPhaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewPhaseCombo.SelectedItem is MotionPhase phase)
            SelectPreviewPhase(phase);
    }

    private void PreviewPause_Click(object sender, RoutedEventArgs e) =>
        _previewPaused = !_previewPaused;

    private void PreviewPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_previewFrames.Count == 0)
            return;
        _previewIndex = (_previewIndex - 1 + _previewFrames.Count) % _previewFrames.Count;
        ShowPreviewFrame();
    }

    private void PreviewNext_Click(object sender, RoutedEventArgs e)
    {
        if (_previewFrames.Count == 0)
            return;
        _previewIndex = (_previewIndex + 1) % _previewFrames.Count;
        ShowPreviewFrame();
    }

    private void PreviewBackground_Click(object sender, RoutedEventArgs e)
    {
        _previewDark = !_previewDark;
        PreviewStage.Background = _previewDark
            ? MakeCheckerBrush(Color.FromRgb(36, 38, 34), Color.FromRgb(70, 74, 66))
            : MakeCheckerBrush(Color.FromRgb(238, 236, 229), Color.FromRgb(250, 248, 241));
    }

    private void SelectPreviewPhase(MotionPhase? phase)
    {
        _previewPhase = phase;
        _previewFrames = phase?.Frames ?? Array.Empty<string>();
        _previewIndex = 0;
        _previewPaused = false;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(_previewMotion?.FrameDurationMs ?? 125);
        ShowPreviewFrame();
        if (_previewFrames.Count > 1)
            _previewTimer.Start();
        else
            _previewTimer.Stop();
    }

    private void AdvancePreview()
    {
        if (_previewPaused || _previewFrames.Count == 0)
            return;

        _previewIndex++;
        if (_previewIndex >= _previewFrames.Count)
        {
            if (PreviewLoopCheck.IsChecked == true || _previewPhase?.Loop == true)
                _previewIndex = 0;
            else
            {
                _previewIndex = _previewFrames.Count - 1;
                _previewPaused = true;
            }
        }
        ShowPreviewFrame();
    }

    private void ShowPreviewFrame()
    {
        if (_previewFrames.Count == 0)
        {
            PreviewImage.Source = null;
            PreviewFrame.Text = "No preview frames found.";
            return;
        }

        var path = _previewFrames[Math.Clamp(_previewIndex, 0, _previewFrames.Count - 1)];
        try
        {
            PreviewImage.Source = LoadBitmap(path);
            PreviewFrame.Text = $"{_previewIndex + 1}/{_previewFrames.Count} - {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            PreviewImage.Source = null;
            PreviewFrame.Text = $"Failed to load {Path.GetFileName(path)} - {ex.GetType().Name}";
        }
    }

    private void RefreshAlbumView()
    {
        _albumFolders.Clear();
        AlbumRootPathText.Text = Directory.Exists(_albumRoot)
            ? _albumRoot
            : $"{_albumRoot} (not found)";

        if (!Directory.Exists(_albumRoot))
        {
            AlbumStatusText.Text = "0 albums";
            SelectAlbum(null);
            return;
        }

        foreach (var directory in Directory.GetDirectories(_albumRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _albumFolders.Add(AlbumFolderItem.FromDirectory(directory));

        AlbumStatusText.Text = $"{_albumFolders.Count} albums";
        SelectAlbum(_albumFolders.FirstOrDefault());
    }

    private void SelectAlbum(AlbumFolderItem? item)
    {
        _selectedAlbum = item;
        if (item is null)
        {
            AlbumPreviewImage.Source = null;
            AlbumSelectedNameText.Text = "\u672a\u9009\u62e9\u5b50\u76f8\u518c";
            AlbumDatePicker.SelectedDate = null;
            AlbumDescriptionText.Text = string.Empty;
            _albumMediaBindings.Clear();
            AlbumMarkdownPathText.Text = "\u9009\u62e9\u672c\u5730\u76f8\u518c\u76ee\u5f55\u540e\uff0c\u4f1a\u8bfb\u53d6\u6bcf\u4e2a\u5b50\u6587\u4ef6\u5939\u7684 markdown \u63cf\u8ff0\u3002";
            return;
        }

        AlbumSelectedNameText.Text = item.Name;
        AlbumDatePicker.SelectedDate = DateTime.TryParse(item.DateText, out var date) ? date : null;
        AlbumDescriptionText.Text = item.Description;
        _albumMediaBindings.Clear();
        foreach (var fileName in item.MediaFiles)
            _albumMediaBindings.Add(fileName);
        AlbumMarkdownPathText.Text = string.IsNullOrWhiteSpace(item.MarkdownPath)
            ? "\u672a\u627e\u5230 markdown\uff0c\u4fdd\u5b58\u540e\u4f1a\u521b\u5efa album.md"
            : item.MarkdownPath;
        AlbumPreviewImage.Source = LoadBitmap(item.ThumbnailPath);
    }

    private static string ProfileDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wukong", "profile");

    private void SaveSelectedAlbumMarkdown()
    {
        if (_selectedAlbum is null)
            return;

        Directory.CreateDirectory(_selectedAlbum.DirectoryPath);
        var selectedPath = _selectedAlbum.DirectoryPath;
        var markdownPath = string.IsNullOrWhiteSpace(_selectedAlbum.MarkdownPath)
            ? Path.Combine(_selectedAlbum.DirectoryPath, "album.md")
            : _selectedAlbum.MarkdownPath;
        File.WriteAllText(markdownPath, _selectedAlbum.CreateMarkdown(CurrentAlbumDateText(), AlbumDescriptionText.Text, _albumMediaBindings));
        RefreshAlbumView();
        var updated = _albumFolders.FirstOrDefault(x => string.Equals(x.DirectoryPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (updated is not null)
            SelectAlbum(updated);
    }

    private string CurrentAlbumDateText() =>
        AlbumDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");

    private static string MakeUniqueFileName(string directory, string fileName)
    {
        var candidate = fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 1;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{stem}-{index:00}{extension}";
            index++;
        }
        return candidate;
    }

    private static BitmapImage? LoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static DrawingBrush MakeCheckerBrush(Color a, Color b)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(10, 10, 10, 10))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 20, 20),
            ViewportUnits = BrushMappingMode.Absolute
        };
    }
}

public sealed record AlbumFolderItem(
    string Name,
    string DirectoryPath,
    string DateText,
    int PhotoCount,
    string Description,
    string MarkdownPath,
    string ThumbnailPath,
    string Status,
    IReadOnlyList<string> MediaFiles)
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly string[] MarkdownNames = { "album.md", "README.md", "readme.md", "description.md", "\u63cf\u8ff0.md" };

    public static string GetDefaultAlbumRoot()
    {
        var configured = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var preference = AlbumRootPreferencePath();
        if (File.Exists(preference))
        {
            var path = File.ReadAllText(preference).Trim();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
        }

        const string xhsRoot = "D:\\\u3010ZS\u3011\\\u3010\u684c\u9762\u5ba0\u7269\u3011\\images_wk\\images_xhs";
        return Directory.Exists(xhsRoot)
            ? xhsRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Wukong");
    }

    public static void SaveAlbumRootPreference(string path)
    {
        Directory.CreateDirectory(ProfileDirectory());
        File.WriteAllText(AlbumRootPreferencePath(), path);
    }

    public static AlbumFolderItem FromDirectory(string directory)
    {
        var images = Directory.GetFiles(directory)
            .Where(x => ImageExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var markdown = FindMarkdown(directory);
        var metadata = string.IsNullOrWhiteSpace(markdown)
            ? MarkdownAlbumMetadata.Empty
            : MarkdownAlbumMetadata.Read(markdown);
        var orderedImages = metadata.MediaFiles.Count == 0
            ? images
            : metadata.MediaFiles
                .Select(x => Path.Combine(directory, x))
                .Where(File.Exists)
                .Concat(images.Where(x => !metadata.MediaFiles.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)))
                .ToList();
        var description = string.IsNullOrWhiteSpace(markdown)
            ? "\u672a\u627e\u5230 markdown \u63cf\u8ff0"
            : metadata.Description;
        var status = string.IsNullOrWhiteSpace(markdown)
            ? "\u5f85\u8865\u63cf\u8ff0"
            : "\u5df2\u8bfb\u53d6\u63cf\u8ff0";

        return new AlbumFolderItem(
            string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileName(directory) : metadata.Title,
            directory,
            NormalizeDateText(string.IsNullOrWhiteSpace(metadata.TimeText) ? Directory.GetLastWriteTime(directory).ToString("yyyy-MM-dd") : metadata.TimeText),
            images.Count,
            description,
            markdown,
            orderedImages.FirstOrDefault() ?? string.Empty,
            status,
            orderedImages.Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray());
    }

    public string CreateMarkdown(string timeText, string description) =>
        CreateMarkdown(timeText, description, MediaFiles);

    public string CreateMarkdown(string timeText, string description, IReadOnlyList<string> mediaFiles)
    {
        var title = string.IsNullOrWhiteSpace(Name) ? Path.GetFileName(DirectoryPath) : Name;
        return File.Exists(MarkdownPath)
            ? MarkdownAlbumMetadata.UpdateExisting(MarkdownPath, title, timeText, description, mediaFiles)
            : MarkdownAlbumMetadata.CreateNew(title, timeText, description, mediaFiles);
    }

    private static string ProfileDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wukong", "profile");

    private static string AlbumRootPreferencePath() => Path.Combine(ProfileDirectory(), "album-root.txt");

    internal static string BuildMarkdown(string title, string timeText, string description, IReadOnlyList<string> mediaFiles, IReadOnlyList<string>? preservedFrontMatter = null, IReadOnlyList<string>? preservedBodySections = null)
    {
        var mediaLines = mediaFiles.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, mediaFiles.Select(x => $"  - \"{x}\""));
        var body = description.Trim();
        var frontMatter = preservedFrontMatter is { Count: > 0 }
            ? string.Join(Environment.NewLine, preservedFrontMatter.Where(x => !string.IsNullOrWhiteSpace(x))) + Environment.NewLine
            : string.Empty;
        var preserved = preservedBodySections is { Count: > 0 }
            ? $"{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, preservedBodySections).Trim()}"
            : string.Empty;
        return
            $"---{Environment.NewLine}" +
            frontMatter +
            $"title: \"{title}\"{Environment.NewLine}" +
            $"time: \"{NormalizeDateText(timeText)}\"{Environment.NewLine}" +
            $"media:{Environment.NewLine}{mediaLines}{Environment.NewLine}" +
            $"---{Environment.NewLine}{Environment.NewLine}" +
            $"# {title}{Environment.NewLine}{Environment.NewLine}" +
            $"\u65f6\u95f4: {NormalizeDateText(timeText)}{Environment.NewLine}{Environment.NewLine}" +
            $"## \u6b63\u6587{Environment.NewLine}{Environment.NewLine}" +
            $"{body}{preserved}{Environment.NewLine}{Environment.NewLine}" +
            $"## \u7d20\u6750{Environment.NewLine}{Environment.NewLine}" +
            string.Join(Environment.NewLine, mediaFiles.Select(x => $"- `{x}`")) +
            Environment.NewLine;
    }

    private static string FindMarkdown(string directory)
    {
        var preferred = MarkdownNames
            .Select(x => Path.Combine(directory, x))
            .FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        return Directory.GetFiles(directory, "*.md")
            .Concat(Directory.GetFiles(directory, "*.markdown"))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string NormalizeDateText(string value)
    {
        value = value.Trim();
        if (value.Length >= 10 &&
            char.IsDigit(value[0]) &&
            char.IsDigit(value[1]) &&
            char.IsDigit(value[2]) &&
            char.IsDigit(value[3]) &&
            value[4] == '-' &&
            char.IsDigit(value[5]) &&
            char.IsDigit(value[6]) &&
            value[7] == '-' &&
            char.IsDigit(value[8]) &&
            char.IsDigit(value[9]))
        {
            return value[..10];
        }

        return DateTime.TryParse(value, out var date)
            ? date.ToString("yyyy-MM-dd")
            : value;
    }
}

public sealed record MarkdownAlbumMetadata(string Title, string TimeText, string Description, IReadOnlyList<string> MediaFiles)
{
    public static MarkdownAlbumMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty, Array.Empty<string>());

    public static string CreateNew(string title, string timeText, string description, IReadOnlyList<string> mediaFiles) =>
        AlbumFolderItem.BuildMarkdown(title, timeText, description, mediaFiles);

    public static string UpdateExisting(string path, string title, string timeText, string description, IReadOnlyList<string> mediaFiles)
    {
        var text = File.ReadAllText(path);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var frontMatter = ExtractUnknownFrontMatter(lines, out var bodyStart);
        var bodySections = ExtractUnknownBodySections(lines.Skip(bodyStart).ToList());
        return AlbumFolderItem.BuildMarkdown(title, timeText, description, mediaFiles, frontMatter, bodySections);
    }

    public static MarkdownAlbumMetadata Read(string path)
    {
        var text = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var title = string.Empty;
        var time = string.Empty;
        var media = new List<string>();
        var bodyStart = 0;

        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            for (var i = 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line == "---")
                {
                    bodyStart = i + 1;
                    break;
                }

                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    title = Unquote(line["title:".Length..].Trim());
                else if (line.StartsWith("time:", StringComparison.OrdinalIgnoreCase))
                    time = Unquote(line["time:".Length..].Trim());
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                    media.Add(Unquote(line[2..].Trim()));
            }
        }

        var bodyLines = lines.Skip(bodyStart).ToList();
        if (string.IsNullOrWhiteSpace(title))
            title = ReadHeading(bodyLines);
        if (string.IsNullOrWhiteSpace(time))
            time = ReadChineseTime(bodyLines);

        return new MarkdownAlbumMetadata(title, time, ReadBodyDescription(bodyLines), media);
    }

    private static IReadOnlyList<string> ExtractUnknownFrontMatter(IReadOnlyList<string> lines, out int bodyStart)
    {
        bodyStart = 0;
        if (lines.Count == 0 || lines[0].Trim() != "---")
            return Array.Empty<string>();

        var result = new List<string>();
        var skippingMedia = false;
        for (var i = 1; i < lines.Count; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line == "---")
            {
                bodyStart = i + 1;
                break;
            }

            if (skippingMedia)
            {
                var isMediaItem = line.StartsWith("- ", StringComparison.Ordinal) || raw.StartsWith(" ", StringComparison.Ordinal);
                if (isMediaItem)
                    continue;
                skippingMedia = false;
            }

            if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("time:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
            {
                skippingMedia = true;
                continue;
            }

            result.Add(raw);
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractUnknownBodySections(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        var skippingManagedSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal) ||
                line.StartsWith("\u65f6\u95f4:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                skippingManagedSection =
                    line.Equals("## \u6b63\u6587", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("## \u7d20\u6750", StringComparison.OrdinalIgnoreCase);
                if (!skippingManagedSection)
                    result.Add(raw);
                continue;
            }

            if (!skippingManagedSection && !string.IsNullOrWhiteSpace(raw))
                result.Add(raw);
        }

        return result;
    }

    private static string ReadBodyDescription(IReadOnlyList<string> lines)
    {
        var body = new List<string>();
        var inBody = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Equals("## \u6b63\u6587", StringComparison.OrdinalIgnoreCase))
            {
                inBody = true;
                continue;
            }
            if (inBody && trimmed.StartsWith("## ", StringComparison.Ordinal))
                break;
            if (inBody)
                body.Add(line);
        }

        if (body.Count > 0)
            return string.Join(Environment.NewLine, body).Trim();

        return string.Join(
            Environment.NewLine,
            lines
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !x.TrimStart().StartsWith("#", StringComparison.Ordinal))
            .Where(x => !x.TrimStart().StartsWith("date:", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.TrimStart().StartsWith("\u65f6\u95f4:", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.TrimStart().StartsWith("- `", StringComparison.Ordinal)))
            .Trim();
    }

    private static string ReadHeading(IEnumerable<string> lines) =>
        lines.Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith("# ", StringComparison.Ordinal))?
            .TrimStart('#', ' ') ?? string.Empty;

    private static string ReadChineseTime(IEnumerable<string> lines)
    {
        const string prefix = "\u65f6\u95f4:";
        return lines.Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            [prefix.Length..].Trim() ?? string.Empty;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }
}
