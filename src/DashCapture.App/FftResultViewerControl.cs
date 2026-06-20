using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DashCapture.Analysis;

namespace DashCapture.App;

public sealed class FftResultViewerControl : UserControl, IDisposable
{
    private const double ButtonMinHeight = 32;
    private const double FieldMinHeight = 34;
    private const double PanelRadius = 4;

    private static readonly IBrush PageBackground = new SolidColorBrush(Color.FromRgb(232, 236, 241));
    private static readonly IBrush PanelBackground = Brushes.White;
    private static readonly IBrush PanelBackground2 = new SolidColorBrush(Color.FromRgb(244, 246, 248));
    private static readonly IBrush BorderBrushSoft = new SolidColorBrush(Color.FromRgb(190, 198, 208));
    private static readonly IBrush TextPrimary = new SolidColorBrush(Color.FromRgb(20, 29, 39));
    private static readonly IBrush TextSecondary = new SolidColorBrush(Color.FromRgb(82, 92, 106));
    private static readonly IBrush AccentBlue = new SolidColorBrush(Color.FromRgb(31, 91, 140));
    private static readonly IBrush AccentGreen = new SolidColorBrush(Color.FromRgb(35, 117, 80));

    private readonly string _defaultFolder;
    private readonly Button _openFileButton = new() { Content = "打开FFT" };
    private readonly Button _openFolderButton = new() { Content = "打开文件夹" };
    private readonly Button _openLatestButton = new() { Content = "打开最新" };
    private readonly Button _loadSpectrumButton = new() { Content = "加载频谱", IsEnabled = false };
    private readonly Button _loadTrendButton = new() { Content = "计算趋势", IsEnabled = false };
    private readonly Button _exportSpectrumButton = new() { Content = "导出频谱", IsEnabled = false };
    private readonly Button _exportTrendButton = new() { Content = "导出趋势", IsEnabled = false };
    private readonly Button _exportPeaksButton = new() { Content = "导出全部峰值", IsEnabled = false };
    private readonly ComboBox _devicePicker = new() { Width = 220, IsEnabled = false };
    private readonly ComboBox _channelPicker = new() { Width = 250, IsEnabled = false };
    private readonly TextBox _frameIndexText = new() { Text = "0", Width = 90 };
    private readonly ComboBox _metricPicker = new() { Width = 150 };
    private readonly TextBlock _fileText = new() { Text = "未打开 FFT 结果文件。" };
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _channelText = new();
    private readonly FftResultPlotControl _plot = new();

    private FftFileOverview? _overview;
    private FftChannelTrend? _trend;
    private FftResultFrame? _spectrum;
    private CancellationTokenSource? _operationCts;
    private string? _currentPath;
    private bool _busy;

    public FftResultViewerControl(string defaultFolder)
    {
        _defaultFolder = defaultFolder;
        Background = PageBackground;
        Content = BuildContent();

        _openFileButton.Click += async (_, _) => await OpenFileAsync();
        _openFolderButton.Click += async (_, _) => await OpenFolderAsync();
        _openLatestButton.Click += async (_, _) => await OpenLatestAsync();
        _loadSpectrumButton.Click += async (_, _) => await LoadSpectrumAsync(showBusy: true);
        _loadTrendButton.Click += async (_, _) => await LoadTrendAsync(showBusy: true);
        _exportSpectrumButton.Click += async (_, _) => await ExportSpectrumAsync();
        _exportTrendButton.Click += async (_, _) => await ExportTrendAsync();
        _exportPeaksButton.Click += async (_, _) => await ExportPeaksAsync();
        _devicePicker.SelectionChanged += (_, _) => RefreshChannelPicker();
        _channelPicker.SelectionChanged += (_, _) => OnChannelSelectionChanged();
        _metricPicker.SelectionChanged += (_, _) => RefreshPlot();

        _metricPicker.ItemsSource = new[]
        {
            new ComboItem<FftTrendMetric>("峰值频率", FftTrendMetric.PeakFrequency),
            new ComboItem<FftTrendMetric>("峰值幅值", FftTrendMetric.PeakMagnitude)
        };
        _metricPicker.SelectedIndex = 0;

        StyleButton(_openFileButton, AccentBlue);
        StyleButton(_openFolderButton, AccentBlue);
        StyleButton(_openLatestButton, AccentBlue);
        StyleButton(_loadSpectrumButton, AccentGreen);
        StyleButton(_loadTrendButton, AccentGreen);
        StyleButton(_exportSpectrumButton, AccentGreen);
        StyleButton(_exportTrendButton, AccentGreen);
        StyleButton(_exportPeaksButton, AccentGreen);
        StyleInput(_frameIndexText);
        StyleComboBox(_devicePicker);
        StyleComboBox(_channelPicker);
        StyleComboBox(_metricPicker);
        SetStatus("打开 .dhfft 文件后可查看频谱和峰值趋势。");
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10
        };

        Control topPanel = BuildToolbar();
        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        var plotPanel = new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = _plot
        };
        Grid.SetRow(plotPanel, 1);
        root.Children.Add(plotPanel);

        var statusGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,1.3*,1.5*"),
            ColumnSpacing = 14,
            Children =
            {
                _summaryText,
                _channelText,
                _statusText
            }
        };
        Grid.SetColumn(_channelText, 1);
        Grid.SetColumn(_statusText, 2);

        var statusPanel = new Border
        {
            Padding = new Thickness(12, 7),
            Background = PanelBackground2,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = statusGrid
        };
        Grid.SetRow(statusPanel, 2);
        root.Children.Add(statusPanel);
        return root;
    }

    private Control BuildToolbar()
    {
        _fileText.Foreground = TextPrimary;
        _fileText.FontSize = 13;
        _fileText.TextWrapping = TextWrapping.NoWrap;
        _fileText.TextTrimming = TextTrimming.CharacterEllipsis;
        _summaryText.Foreground = TextSecondary;
        _summaryText.FontSize = 12;
        _summaryText.TextWrapping = TextWrapping.NoWrap;
        _summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusText.Foreground = TextSecondary;
        _statusText.FontSize = 12;
        _statusText.TextWrapping = TextWrapping.NoWrap;
        _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        _channelText.Foreground = TextPrimary;
        _channelText.FontSize = 12;
        _channelText.TextWrapping = TextWrapping.NoWrap;
        _channelText.TextTrimming = TextTrimming.CharacterEllipsis;

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _openFileButton,
                _openFolderButton,
                _openLatestButton,
                ToolbarField("设备", _devicePicker),
                ToolbarField("通道", _channelPicker),
                ToolbarField("帧号", _frameIndexText),
                ToolbarField("趋势", _metricPicker),
                _loadSpectrumButton,
                _loadTrendButton,
                _exportSpectrumButton,
                _exportTrendButton,
                _exportPeaksButton
            }
        };

        return new Border
        {
            Padding = new Thickness(12),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { toolbar, _fileText }
            }
        };
    }

    private static Control ToolbarField(string label, Control editor)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 10, 8),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = TextSecondary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                },
                editor
            }
        };
    }

    private async Task OpenFileAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "打开 FFT 结果文件",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("FFT 结果文件") { Patterns = new[] { "*.dhfft" } },
                FilePickerFileTypes.All
            }
        });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenPathAsync(path);
        }
    }

    private async Task OpenFolderAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "打开 FFT 结果文件夹"
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenPathAsync(path);
        }
    }

    private async Task OpenLatestAsync()
    {
        await OpenPathAsync(_defaultFolder);
    }

    private async Task OpenPathAsync(string path)
    {
        string? filePath = ResolveFftPath(path);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            SetStatus("没有找到 .dhfft 文件。");
            return;
        }

        CancellationToken token = ResetOperation();
        SetBusy(true, "正在打开 FFT 结果...");
        try
        {
            var progress = new Progress<FftReadProgress>(item => SetStatus($"正在扫描文件头 {item.Percent:0.0}%    已读帧 {item.FramesRead:N0}"));
            FftFileOverview overview = await Task.Run(() => FftTrendCalculator.ReadOverview(filePath, token, progress), token);
            token.ThrowIfCancellationRequested();
            _currentPath = filePath;
            _overview = overview;
            _trend = null;
            _spectrum = null;
            PopulateChannels(overview);
            _fileText.Text = filePath;
            _summaryText.Text = FormatOverview(overview);
            SetStatus($"已打开 {overview.Channels.Count:N0} 个 FFT 通道。");
            await LoadSpectrumAsync(showBusy: false);
            await LoadTrendAsync(showBusy: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("打开已取消。");
        }
        catch (Exception ex)
        {
            ClearLoadedFile();
            SetStatus("打开失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateChannels(FftFileOverview overview)
    {
        var groups = overview.Channels
            .GroupBy(channel => new FftDeviceKey(channel.Key.DeviceId, channel.Key.DeviceIp))
            .OrderBy(group => group.Key.DeviceId)
            .Select(group => new FftDeviceGroup(group.Key, group.OrderBy(channel => channel.Key.ChannelId).ToArray()))
            .ToArray();

        _devicePicker.ItemsSource = groups
            .Select(group => new ComboItem<FftDeviceGroup>($"设备 {group.Key.DeviceId + 1} ({group.Channels.Count})", group))
            .ToArray();
        _devicePicker.SelectedIndex = groups.Length > 0 ? 0 : -1;
        _devicePicker.IsEnabled = groups.Length > 0;
        RefreshChannelPicker();
        UpdateButtons();
    }

    private void RefreshChannelPicker()
    {
        if (_devicePicker.SelectedItem is not ComboItem<FftDeviceGroup> item)
        {
            _channelPicker.ItemsSource = Array.Empty<ComboItem<FftChannelOverview>>();
            _channelPicker.SelectedIndex = -1;
            _channelPicker.IsEnabled = false;
            RefreshChannelSummary();
            UpdateButtons();
            return;
        }

        _channelPicker.ItemsSource = item.Value.Channels
            .Select(channel => new ComboItem<FftChannelOverview>($"{channel.ChannelName}  {channel.FrameCount:N0} 帧", channel))
            .ToArray();
        _channelPicker.SelectedIndex = item.Value.Channels.Count > 0 ? 0 : -1;
        _channelPicker.IsEnabled = item.Value.Channels.Count > 0;
        RefreshChannelSummary();
        UpdateButtons();
    }

    private void OnChannelSelectionChanged()
    {
        _trend = null;
        _spectrum = null;
        RefreshChannelSummary();
        RefreshPlot();
        UpdateButtons();
    }

    private async Task LoadSpectrumAsync(bool showBusy)
    {
        string? path = _currentPath;
        FftChannelOverview? channel = SelectedChannel();
        if (string.IsNullOrWhiteSpace(path) || channel is null)
        {
            return;
        }

        if (!TryReadFrameIndex(out int index))
        {
            SetStatus("帧号请输入数字，例如 0、1、25。");
            return;
        }

        CancellationToken token = ResetOperation();
        if (showBusy)
        {
            SetBusy(true, "正在加载 FFT 频谱...");
        }

        try
        {
            var progress = new Progress<FftReadProgress>(item => SetStatus($"正在加载频谱 {item.Percent:0.0}%    已匹配 {item.MatchedFrames:N0} 帧"));
            FftResultFrame? frame = await Task.Run(() => FftTrendCalculator.ReadChannelFrame(path, channel.Key, index, token, progress), token);
            token.ThrowIfCancellationRequested();
            if (frame is null)
            {
                SetStatus("没有找到这个通道的指定帧。");
                return;
            }

            _spectrum = frame;
            RefreshPlot();
            _exportSpectrumButton.IsEnabled = true;
            FftPeak peak = frame.FindPeak(ignoreDc: true);
            string peakText = peak.BinIndex >= 0
                ? $"主峰 {peak.FrequencyHz:0.###} Hz，幅值 {peak.Magnitude:0.######}"
                : "未找到明显主峰";
            SetStatus($"频谱已加载：通道第 {index:N0} 帧，FFT {frame.FftSize:N0} 点，频点 {frame.BinCount:N0} 个，{peakText}。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("频谱加载已取消。");
        }
        catch (Exception ex)
        {
            SetStatus("频谱加载失败：" + ex.Message);
        }
        finally
        {
            if (showBusy)
            {
                SetBusy(false);
            }
        }
    }

    private async Task LoadTrendAsync(bool showBusy)
    {
        string? path = _currentPath;
        FftChannelOverview? channel = SelectedChannel();
        if (string.IsNullOrWhiteSpace(path) || channel is null)
        {
            return;
        }

        CancellationToken token = ResetOperation();
        if (showBusy)
        {
            SetBusy(true, "正在计算 FFT 峰值趋势...");
        }

        try
        {
            var progress = new Progress<FftReadProgress>(item => SetStatus($"正在计算趋势 {item.Percent:0.0}%    趋势点 {item.MatchedFrames:N0}"));
            FftChannelTrend trend = await Task.Run(() => FftTrendCalculator.CalculateTrend(path, channel.Key, token, progress), token);
            token.ThrowIfCancellationRequested();
            _trend = trend;
            RefreshPlot();
            _exportTrendButton.IsEnabled = trend.Points.Count > 0;
            SetStatus($"趋势已计算：{trend.Points.Count:N0} 个点，平均峰值频率 {trend.AveragePeakFrequencyHz:0.###} Hz。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("趋势计算已取消。");
        }
        catch (Exception ex)
        {
            SetStatus("趋势计算失败：" + ex.Message);
        }
        finally
        {
            if (showBusy)
            {
                SetBusy(false);
            }
        }
    }

    private async Task ExportSpectrumAsync()
    {
        FftResultFrame? spectrum = _spectrum;
        if (spectrum is null)
        {
            SetStatus("请先加载频谱。");
            return;
        }

        string? path = await PickSavePathAsync("导出频谱 CSV", SuggestedName("_频谱.csv"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = EnsureExtension(path, ".csv");
        try
        {
            FftTrendCalculator.ExportSpectrumCsv(spectrum, path);
            SetStatus("频谱已导出：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("频谱导出失败：" + ex.Message);
        }
    }

    private async Task ExportTrendAsync()
    {
        FftChannelTrend? trend = _trend;
        if (trend is null || trend.Points.Count == 0)
        {
            await LoadTrendAsync(showBusy: true);
            trend = _trend;
            if (trend is null || trend.Points.Count == 0)
            {
                return;
            }
        }

        string? path = await PickSavePathAsync("导出趋势 CSV", SuggestedName("_趋势.csv"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = EnsureExtension(path, ".csv");
        try
        {
            FftTrendCalculator.ExportTrendCsv(trend, path);
            SetStatus("趋势已导出：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("趋势导出失败：" + ex.Message);
        }
    }

    private async Task ExportPeaksAsync()
    {
        string? source = _currentPath;
        if (string.IsNullOrWhiteSpace(source))
        {
            SetStatus("请先打开 .dhfft 文件。");
            return;
        }

        string? path = await PickSavePathAsync("导出全部 FFT 峰值 CSV", SuggestedName("_全部峰值.csv"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = EnsureExtension(path, ".csv");
        CancellationToken token = ResetOperation();
        SetBusy(true, "正在导出全部 FFT 峰值...");
        try
        {
            var progress = new Progress<FftReadProgress>(item => SetStatus($"正在导出峰值 {item.Percent:0.0}%    已写入 {item.MatchedFrames:N0} 行"));
            await Task.Run(() => FftTrendCalculator.ExportPeaksCsv(source, path, null, token, progress), token);
            SetStatus("全部峰值已导出：" + path);
        }
        catch (OperationCanceledException)
        {
            SetStatus("峰值导出已取消。");
        }
        catch (Exception ex)
        {
            SetStatus("峰值导出失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<string?> PickSavePathAsync(string title, string suggestedName)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            SetStatus("无法打开保存窗口。");
            return null;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                FilePickerFileTypes.All
            }
        });

        return file?.TryGetLocalPath();
    }

    private void RefreshChannelSummary()
    {
        FftChannelOverview? channel = SelectedChannel();
        if (channel is null)
        {
            _channelText.Text = "未选择通道。";
            return;
        }

        _channelText.Text = $"{FormatChannelDisplayName(channel)}    采样率 {channel.SampleRate:0.###} Hz    FFT {channel.FftSize:N0} 点    {channel.FrequencyResolution:0.######} Hz/点    {channel.FrameCount:N0} 帧";
    }

    private void RefreshPlot()
    {
        FftTrendMetric metric = _metricPicker.SelectedItem is ComboItem<FftTrendMetric> item
            ? item.Value
            : FftTrendMetric.PeakFrequency;
        _plot.SetData(_spectrum, _trend, metric);
    }

    private FftChannelOverview? SelectedChannel()
    {
        return _channelPicker.SelectedItem is ComboItem<FftChannelOverview> item ? item.Value : null;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        UpdateButtons();
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
        }
    }

    private void UpdateButtons()
    {
        bool hasFile = _overview is not null && _currentPath is not null;
        bool hasChannel = SelectedChannel() is not null;
        _openFileButton.IsEnabled = !_busy;
        _openFolderButton.IsEnabled = !_busy;
        _openLatestButton.IsEnabled = !_busy;
        _devicePicker.IsEnabled = !_busy && hasFile && _devicePicker.SelectedIndex >= 0;
        _channelPicker.IsEnabled = !_busy && hasFile && _channelPicker.SelectedIndex >= 0;
        _loadSpectrumButton.IsEnabled = !_busy && hasChannel;
        _loadTrendButton.IsEnabled = !_busy && hasChannel;
        _exportSpectrumButton.IsEnabled = !_busy && _spectrum is not null;
        _exportTrendButton.IsEnabled = !_busy && _trend is not null && _trend.Points.Count > 0;
        _exportPeaksButton.IsEnabled = !_busy && hasFile;
    }

    private CancellationToken ResetOperation()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        return _operationCts.Token;
    }

    private void ClearLoadedFile()
    {
        _overview = null;
        _trend = null;
        _spectrum = null;
        _currentPath = null;
        _fileText.Text = "未打开 FFT 结果文件。";
        _summaryText.Text = string.Empty;
        _channelText.Text = string.Empty;
        _devicePicker.ItemsSource = Array.Empty<object>();
        _channelPicker.ItemsSource = Array.Empty<object>();
        _devicePicker.SelectedIndex = -1;
        _channelPicker.SelectedIndex = -1;
        RefreshPlot();
        UpdateButtons();
    }

    private void SetStatus(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _statusText.Text = text;
            return;
        }

        Dispatcher.UIThread.Post(() => _statusText.Text = text);
    }

    public void Dispose()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private static string? ResolveFftPath(string path)
    {
        if (File.Exists(path))
        {
            return path.EndsWith(".dhfft", StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return Directory.EnumerateFiles(path, "*.dhfft", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string FormatOverview(FftFileOverview overview)
    {
        string window = overview.FileInfo.FormatVersion >= 3 &&
            string.Equals(overview.FileInfo.WindowMode, "sample_rate_resolution", StringComparison.OrdinalIgnoreCase)
            ? $"{overview.FileInfo.TargetResolutionHz:0.###} Hz / {overview.FileInfo.OverlapRatio:P0}"
            : $"{overview.FileInfo.WindowSampleCount:N0}/{overview.FileInfo.HopSampleCount:N0}";
        return $"格式 v{overview.FileInfo.FormatVersion}    通道 {overview.Channels.Count:N0}    帧 {overview.FrameCount:N0}    窗口 {window}";
    }

    private static string FormatChannelDisplayName(FftChannelOverview channel)
    {
        return string.IsNullOrWhiteSpace(channel.ChannelName)
            ? $"设备 {channel.Key.DeviceId + 1}/通道 {channel.Key.ChannelId}"
            : $"设备 {channel.Key.DeviceId + 1}/{channel.ChannelName}";
    }

    private string SuggestedName(string suffix)
    {
        string baseName = string.IsNullOrWhiteSpace(_currentPath)
            ? "DashCaptureFft"
            : Path.GetFileNameWithoutExtension(_currentPath);
        return baseName + suffix;
    }

    private static int ParseInt(string? text, int fallback)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) ||
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? value
            : fallback;
    }

    private bool TryReadFrameIndex(out int frameIndex)
    {
        if (int.TryParse(_frameIndexText.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out frameIndex) ||
            int.TryParse(_frameIndexText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out frameIndex))
        {
            frameIndex = Math.Max(0, frameIndex);
            _frameIndexText.Text = frameIndex.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        frameIndex = 0;
        return false;
    }

    private static string EnsureExtension(string path, string extension)
    {
        return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
    }

    private static void StyleButton(Button button, IBrush background)
    {
        button.Background = background;
        button.Foreground = Brushes.White;
        button.BorderBrush = background;
        button.BorderThickness = new Thickness(1);
        button.Padding = new Thickness(11, 6);
        button.Margin = new Thickness(0, 0, 10, 8);
        button.MinHeight = ButtonMinHeight;
        button.MinWidth = 64;
        button.FontSize = 12;
        button.FontWeight = FontWeight.SemiBold;
    }

    private static void StyleInput(TextBox textBox)
    {
        textBox.Background = Brushes.White;
        textBox.Foreground = TextPrimary;
        textBox.BorderBrush = BorderBrushSoft;
        textBox.FontSize = 12;
        textBox.Padding = new Thickness(8, 5);
        textBox.MinHeight = FieldMinHeight;
    }

    private static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.Background = Brushes.White;
        comboBox.Foreground = TextPrimary;
        comboBox.BorderBrush = BorderBrushSoft;
        comboBox.FontSize = 12;
        comboBox.Padding = new Thickness(7, 4);
        comboBox.MinHeight = FieldMinHeight;
    }

    private sealed record ComboItem<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private readonly record struct FftDeviceKey(int DeviceId, string DeviceIp);

    private sealed record FftDeviceGroup(FftDeviceKey Key, IReadOnlyList<FftChannelOverview> Channels);
}
