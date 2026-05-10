using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DashCapture.Storage;

namespace DashCapture.App;

public sealed class TdmsViewerControl : UserControl, IDisposable
{
    private static readonly IBrush PageBackground = new SolidColorBrush(Color.FromRgb(242, 246, 251));
    private static readonly IBrush PanelBackground = Brushes.White;
    private static readonly IBrush PanelBackground2 = new SolidColorBrush(Color.FromRgb(236, 243, 252));
    private static readonly IBrush BorderBrushSoft = new SolidColorBrush(Color.FromRgb(199, 211, 228));
    private static readonly IBrush TextPrimary = new SolidColorBrush(Color.FromRgb(24, 35, 52));
    private static readonly IBrush TextSecondary = new SolidColorBrush(Color.FromRgb(91, 108, 132));
    private static readonly IBrush AccentBlue = new SolidColorBrush(Color.FromRgb(38, 119, 220));
    private static readonly IBrush AccentGreen = new SolidColorBrush(Color.FromRgb(35, 153, 100));

    private readonly string _tdmRuntimeDir;
    private readonly Button _openFileButton = new() { Content = "Open File" };
    private readonly Button _openFolderButton = new() { Content = "Open Folder" };
    private readonly Button _exportTdmsButton = new() { Content = "Export TDMS", IsEnabled = false };
    private readonly Button _loadButton = new() { Content = "Load", IsEnabled = false };
    private readonly Button _selectAllButton = new() { Content = "All" };
    private readonly Button _clearSelectionButton = new() { Content = "None" };
    private readonly Button _addChannelButton = new() { Content = "Add", IsEnabled = false };
    private readonly Button _fullRangeButton = new() { Content = "Full Range", IsEnabled = false };
    private readonly TextBlock _fileText = new() { Text = "No data file or folder opened." };
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _exportProgressText = new();
    private readonly TextBlock _selectedChannelsText = new() { Text = "No channel selected." };
    private readonly ComboBox _devicePicker = new() { Width = 210, IsEnabled = false };
    private readonly ComboBox _channelPicker = new() { Width = 170, IsEnabled = false };
    private readonly TextBox _startSeconds = new() { Text = "0", Width = 110 };
    private readonly TextBox _windowSeconds = new() { Text = "10", Width = 110 };
    private readonly Slider _timeSlider = new() { Minimum = 0, Maximum = 0, Value = 0, IsEnabled = false };
    private readonly TextBlock _rangeText = new();
    private readonly TextBox _yMin = new() { Width = 110, IsEnabled = false };
    private readonly TextBox _yMax = new() { Width = 110, IsEnabled = false };
    private readonly CheckBox _autoY = new() { Content = "Auto Y", IsChecked = true };
    private readonly TdmsWaveformControl _waveform = new();
    private readonly List<TdmsChannelInfo> _channels = new();
    private readonly HashSet<TdmsChannelKey> _selectedChannelKeys = new();
    private readonly Dictionary<EnvelopeCacheKey, TdmsChannelEnvelope> _envelopeCache = new();
    private TdmsFileReader? _reader;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _rangeCts;
    private CancellationTokenSource? _exportCts;
    private double _fullDurationSeconds = 10;
    private bool _suppressAutoLoad;
    private bool _suppressSliderChange;

    public TdmsViewerControl(string tdmRuntimeDir)
    {
        _tdmRuntimeDir = tdmRuntimeDir;
        Background = PageBackground;
        Content = BuildContent();

        _openFileButton.Click += async (_, _) => await OpenFileAsync();
        _openFolderButton.Click += async (_, _) => await OpenFolderAsync();
        _exportTdmsButton.Click += async (_, _) => await ExportTdmsAsync();
        _loadButton.Click += async (_, _) => await LoadSelectedAsync();
        _fullRangeButton.Click += async (_, _) => await LoadFullRangeAsync();
        _addChannelButton.Click += async (_, _) => await AddSelectedChannelAsync();
        _selectAllButton.Click += (_, _) => SetAllChannels(true);
        _clearSelectionButton.Click += (_, _) => SetAllChannels(false);
        _devicePicker.SelectionChanged += (_, _) => RefreshChannelPicker();
        _waveform.ViewRangeRequested += QueueViewportLoad;
        _waveform.ProbeChanged += text => SetStatus(text);
        _timeSlider.PropertyChanged += (_, e) =>
        {
            if (_suppressSliderChange || e.Property != RangeBase.ValueProperty)
            {
                return;
            }

            double windowSeconds = ParseDouble(_windowSeconds.Text, Math.Min(10, _fullDurationSeconds));
            QueueViewportLoad(_timeSlider.Value, _timeSlider.Value + windowSeconds);
        };
        _autoY.IsCheckedChanged += (_, _) =>
        {
            bool auto = _autoY.IsChecked == true;
            _yMin.IsEnabled = !auto;
            _yMax.IsEnabled = !auto;
            ApplyYAxis();
        };

        StyleButton(_openFileButton, AccentBlue);
        StyleButton(_openFolderButton, AccentBlue);
        StyleButton(_exportTdmsButton, AccentGreen);
        StyleButton(_loadButton, AccentGreen);
        StyleButton(_selectAllButton, AccentBlue);
        StyleButton(_clearSelectionButton, AccentBlue);
        StyleButton(_addChannelButton, AccentGreen);
        StyleButton(_fullRangeButton, AccentBlue);
        StyleInput(_startSeconds);
        StyleInput(_windowSeconds);
        StyleInput(_yMin);
        StyleInput(_yMax);
        StyleComboBox(_devicePicker);
        StyleComboBox(_channelPicker);
        SetStatus("Open data, choose device/channel, then load or export.");
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        Control topPanel = BuildSidePanel();
        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        _rangeText.Foreground = TextSecondary;
        _rangeText.FontSize = 12;
        _rangeText.VerticalAlignment = VerticalAlignment.Center;

        var plotHost = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                _waveform
            }
        };

        var plotPanel = new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = plotHost
        };
        Grid.SetRow(plotPanel, 1);
        root.Children.Add(plotPanel);

        var statusGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1.4*,1.5*"),
            ColumnSpacing = 12,
            Children =
            {
                _summaryText,
                _selectedChannelsText,
                _statusText
            }
        };
        Grid.SetColumn(_selectedChannelsText, 1);
        Grid.SetColumn(_statusText, 2);
        var statusPanel = new Border
        {
            Padding = new Thickness(10, 6),
            Background = PanelBackground2,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = statusGrid
        };
        Grid.SetRow(statusPanel, 2);
        root.Children.Add(statusPanel);
        return root;
    }

    private Control BuildSidePanel()
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
        _selectedChannelsText.Foreground = TextPrimary;
        _selectedChannelsText.FontSize = 12;
        _selectedChannelsText.TextWrapping = TextWrapping.NoWrap;
        _selectedChannelsText.TextTrimming = TextTrimming.CharacterEllipsis;
        _exportProgressText.Foreground = AccentGreen;
        _exportProgressText.FontSize = 13;
        _exportProgressText.FontWeight = FontWeight.SemiBold;
        _exportProgressText.TextWrapping = TextWrapping.NoWrap;
        _exportProgressText.TextTrimming = TextTrimming.CharacterEllipsis;
        _exportProgressText.IsVisible = false;

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _openFileButton,
                _openFolderButton,
                _exportTdmsButton,
                _fullRangeButton,
                _loadButton,
                _selectAllButton,
                _clearSelectionButton,
                ToolbarField("Device", _devicePicker),
                ToolbarField("Channel", _channelPicker),
                _addChannelButton,
                ToolbarField("Start", _startSeconds),
                ToolbarField("Window", _windowSeconds),
                ToolbarField("Y Min", _yMin),
                ToolbarField("Y Max", _yMax),
                _autoY
            }
        };
        _autoY.Margin = new Thickness(0, 5, 8, 5);

        var sliderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                _timeSlider,
                _rangeText
            }
        };
        Grid.SetColumn(_rangeText, 1);

        var bottomLine = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                _fileText,
                _exportProgressText
            }
        };
        Grid.SetColumn(_exportProgressText, 1);

        return new Border
        {
            Padding = new Thickness(10),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { toolbar, sliderRow, bottomLine }
            }
        };
    }

    private static Control ToolbarField(string label, Control editor)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(0, 0, 8, 6),
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

    private static void AddRangeField(Grid grid, string label, Control editor, int column, int row)
    {
        var block = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, Foreground = TextSecondary, FontSize = 12 },
                editor
            }
        };
        Grid.SetColumn(block, column);
        Grid.SetRow(block, row);
        grid.Children.Add(block);
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
            Title = "Open data file",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Capture Data") { Patterns = new[] { "*.tdms", "*.tdms.dhc", "*.dhcap" } },
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
            Title = "Open capture folder"
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenPathAsync(path);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        SetBusy(true, "Opening data source...");
        try
        {
            TdmsFileReader reader = await Task.Run(() => TdmsFileReader.Open(path, _tdmRuntimeDir));
            _reader?.Dispose();
            _reader = reader;
            _envelopeCache.Clear();
            PopulateChannels(reader.FileInfo);
            _fileText.Text = reader.Path;
            _loadButton.IsEnabled = _channels.Count > 0;
            _fullRangeButton.IsEnabled = _channels.Count > 0;
            _exportTdmsButton.IsEnabled = _channels.Count > 0;
            SetDefaultRange(reader.FileInfo);
            SetStatus("Data source opened.");
            await LoadSelectedAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Open failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateChannels(TdmsFileInfo fileInfo)
    {
        _channels.Clear();
        _selectedChannelKeys.Clear();

        ulong maxSamples = 0;
        double maxDuration = 0;
        foreach (TdmsGroupInfo group in fileInfo.Groups)
        {
            foreach (TdmsChannelInfo channel in group.Channels)
            {
                _channels.Add(channel);
                maxSamples = Math.Max(maxSamples, channel.SampleCount);
                maxDuration = Math.Max(maxDuration, channel.DurationSeconds);
            }
        }

        foreach (TdmsChannelInfo channel in _channels.Take(4))
        {
            _selectedChannelKeys.Add(channel.Key);
        }

        _devicePicker.ItemsSource = fileInfo.Groups
            .Select(group => new ComboItem<TdmsGroupInfo>($"{group.Name} ({group.Channels.Count})", group))
            .ToArray();
        _devicePicker.SelectedIndex = fileInfo.Groups.Count > 0 ? 0 : -1;
        _devicePicker.IsEnabled = fileInfo.Groups.Count > 0;
        RefreshChannelPicker();

        _fullDurationSeconds = Math.Max(0.001, maxDuration);
        _summaryText.Text = $"Group {fileInfo.Groups.Count}    Channel {fileInfo.ChannelCount}    MaxSamples {maxSamples:N0}    Duration {FormatDuration(_fullDurationSeconds)}";
        UpdateSelectedChannelsText();
        UpdateLoadButtonState();
    }

    private void RefreshChannelPicker()
    {
        if (_devicePicker.SelectedItem is not ComboItem<TdmsGroupInfo> item)
        {
            _channelPicker.ItemsSource = Array.Empty<ComboItem<TdmsChannelInfo>>();
            _channelPicker.SelectedIndex = -1;
            _channelPicker.IsEnabled = false;
            _addChannelButton.IsEnabled = false;
            return;
        }

        _channelPicker.ItemsSource = item.Value.Channels
            .Select(channel => new ComboItem<TdmsChannelInfo>($"{channel.Name}  {channel.SampleCount:N0}", channel))
            .ToArray();
        _channelPicker.SelectedIndex = item.Value.Channels.Count > 0 ? 0 : -1;
        _channelPicker.IsEnabled = item.Value.Channels.Count > 0;
        _addChannelButton.IsEnabled = item.Value.Channels.Count > 0;
    }

    private async Task AddSelectedChannelAsync()
    {
        if (_channelPicker.SelectedItem is not ComboItem<TdmsChannelInfo> item)
        {
            return;
        }

        _selectedChannelKeys.Add(item.Value.Key);
        UpdateSelectedChannelsText();
        UpdateLoadButtonState();
        if (!_suppressAutoLoad)
        {
            await LoadSelectedAsync();
        }
    }

    private async Task ExportTdmsAsync()
    {
        TdmsFileReader? reader = _reader;
        if (reader is null)
        {
            SetStatusNow("请先打开一个采集文件或文件夹。");
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            SetStatusNow("无法打开导出窗口：当前界面没有可用的顶层窗口。");
            return;
        }

        SetExportFeedback("正在打开导出位置选择窗口...");
        IStorageFile? file;
        try
        {
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export standard TDMS",
                SuggestedFileName = SuggestedExportName(reader.Path),
                DefaultExtension = "tdms",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("TDMS") { Patterns = new[] { "*.tdms" } },
                    FilePickerFileTypes.All
                }
            });
        }
        catch (Exception ex)
        {
            ClearExportFeedback();
            SetStatusNow("导出窗口打开失败：" + ex.Message);
            return;
        }

        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            ClearExportFeedback();
            SetStatusNow("已取消导出。");
            return;
        }

        path = EnsureExtension(path, ".tdms");
        if (!IsExportTargetAllowed(reader.Path, path, out string reason))
        {
            ClearExportFeedback();
            SetStatusNow(reason);
            return;
        }

        _exportCts?.Cancel();
        _exportCts?.Dispose();
        _exportCts = new CancellationTokenSource();
        CancellationToken token = _exportCts.Token;
        var progress = new Progress<TdmsExportProgress>(UpdateExportProgress);
        _exportTdmsButton.Content = "Exporting...";
        SetBusy(true, "正在导出标准 TDMS...");
        SetExportFeedback("正在导出标准 TDMS...");
        try
        {
            await Task.Run(() => reader.ExportToTdms(path, token, progress), token);
            SetExportFeedback("TDMS 导出完成。");
            SetStatusNow("TDMS 导出完成：" + path);
        }
        catch (OperationCanceledException)
        {
            SetStatusNow("TDMS 导出已取消。");
        }
        catch (Exception ex)
        {
            SetStatusNow("TDMS 导出失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _exportTdmsButton.Content = "Export TDMS";
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    private async Task LoadFullRangeAsync()
    {
        _startSeconds.Text = "0";
        _windowSeconds.Text = _fullDurationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        await LoadSelectedAsync();
    }

    private async Task LoadSelectedAsync()
    {
        double startSeconds = ParseDouble(_startSeconds.Text, 0);
        double windowSeconds = ParseDouble(_windowSeconds.Text, 10);
        await LoadSelectedRangeAsync(startSeconds, windowSeconds, updateInputs: true, showBusy: true);
    }

    private async Task LoadSelectedRangeAsync(double startSeconds, double windowSeconds, bool updateInputs, bool showBusy)
    {
        TdmsFileReader? reader = _reader;
        if (reader is null)
        {
            return;
        }

        List<TdmsChannelInfo> selected = SelectedChannels();
        if (selected.Count == 0)
        {
            SetStatus("No channel selected.");
            _waveform.SetSeries(Array.Empty<TdmsChannelEnvelope>(), 0, 1);
            return;
        }

        if (windowSeconds <= 0)
        {
            windowSeconds = selected.Max(channel => channel.DurationSeconds);
        }

        (startSeconds, windowSeconds) = ClampRange(startSeconds, windowSeconds);
        UpdateTimeSlider(startSeconds, windowSeconds);
        if (updateInputs)
        {
            _startSeconds.Text = startSeconds.ToString("0.######", CultureInfo.InvariantCulture);
            _windowSeconds.Text = windowSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        }

        ApplyYAxis();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        int buckets = QuantizeBuckets(_waveform.Bounds.Width > 0 ? _waveform.Bounds.Width : 1600);
        if (showBusy)
        {
            SetBusy(true, $"Loading {selected.Count} channel(s), LOD {buckets}...");
        }
        try
        {
            List<TdmsChannelEnvelope> envelopes = await Task.Run(() =>
            {
                var output = new List<TdmsChannelEnvelope>(selected.Count);
                foreach (TdmsChannelInfo channel in selected)
                {
                    token.ThrowIfCancellationRequested();
                    ulong startSample = SecondsToSample(startSeconds, channel.SampleRate);
                    ulong sampleCount = SecondsToSample(windowSeconds, channel.SampleRate);
                    output.Add(ReadEnvelopeCached(reader, channel, startSample, sampleCount, buckets, token));
                }

                return output;
            }, token);

            double endSeconds = startSeconds + windowSeconds;
            _waveform.SetSeries(envelopes, startSeconds, endSeconds);
            ulong readSamples = envelopes.Aggregate(0UL, (sum, item) => sum + item.SampleCount);
            SetStatus($"Loaded {selected.Count} channel(s), {readSamples:N0} samples. Wheel zoom, drag to zoom, Shift/right-drag to pan.");
        }
        catch (OperationCanceledException)
        {
            if (showBusy)
            {
                SetStatus("Load canceled.");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Load failed: " + ex.Message);
        }
        finally
        {
            if (showBusy)
            {
                SetBusy(false);
            }
        }
    }

    private void QueueViewportLoad(double startSeconds, double endSeconds)
    {
        double windowSeconds = Math.Max(0.000001, endSeconds - startSeconds);
        (startSeconds, windowSeconds) = ClampRange(startSeconds, windowSeconds);
        UpdateTimeSlider(startSeconds, windowSeconds);
        _startSeconds.Text = startSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        _windowSeconds.Text = windowSeconds.ToString("0.######", CultureInfo.InvariantCulture);

        _rangeCts?.Cancel();
        _rangeCts?.Dispose();
        _rangeCts = new CancellationTokenSource();
        CancellationToken token = _rangeCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(90, token).ConfigureAwait(false);
                Dispatcher.UIThread.Post(async () => await LoadSelectedRangeAsync(startSeconds, windowSeconds, updateInputs: false, showBusy: false));
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private TdmsChannelEnvelope ReadEnvelopeCached(
        TdmsFileReader reader,
        TdmsChannelInfo channel,
        ulong startSample,
        ulong sampleCount,
        int buckets,
        CancellationToken token)
    {
        var key = new EnvelopeCacheKey(channel.Key, startSample, sampleCount, buckets);
        lock (_envelopeCache)
        {
            if (_envelopeCache.TryGetValue(key, out TdmsChannelEnvelope? cached))
            {
                return cached;
            }
        }

        TdmsChannelEnvelope envelope = reader.ReadEnvelope(channel, startSample, sampleCount, buckets, token);
        lock (_envelopeCache)
        {
            if (_envelopeCache.Count > 256)
            {
                foreach (EnvelopeCacheKey oldKey in _envelopeCache.Keys.Take(64).ToArray())
                {
                    _envelopeCache.Remove(oldKey);
                }
            }

            _envelopeCache[key] = envelope;
        }

        return envelope;
    }

    private (double StartSeconds, double WindowSeconds) ClampRange(double startSeconds, double windowSeconds)
    {
        double duration = Math.Max(0.001, _fullDurationSeconds);
        windowSeconds = Math.Clamp(windowSeconds, 1.0 / 1_000_000, duration);
        startSeconds = Math.Clamp(startSeconds, 0, Math.Max(0, duration - windowSeconds));
        return (startSeconds, windowSeconds);
    }

    private void UpdateTimeSlider(double startSeconds, double windowSeconds)
    {
        double maxStart = Math.Max(0, _fullDurationSeconds - Math.Max(0.000001, windowSeconds));
        _suppressSliderChange = true;
        _timeSlider.Minimum = 0;
        _timeSlider.Maximum = maxStart;
        _timeSlider.SmallChange = Math.Max(windowSeconds / 20, 0.000001);
        _timeSlider.LargeChange = Math.Max(windowSeconds * 0.8, _timeSlider.SmallChange);
        _timeSlider.Value = Math.Clamp(startSeconds, 0, maxStart);
        _timeSlider.IsEnabled = _reader is not null && maxStart > 0.000001;
        _suppressSliderChange = false;

        double endSeconds = Math.Min(_fullDurationSeconds, startSeconds + windowSeconds);
        _rangeText.Text = $"{FormatTime(startSeconds)} - {FormatTime(endSeconds)} / {FormatTime(_fullDurationSeconds)}";
    }

    private static int QuantizeBuckets(double width)
    {
        int target = (int)Math.Clamp(width * 1.35, 512, 8192);
        int power = 512;
        while (power < target && power < 8192)
        {
            power <<= 1;
        }

        return power;
    }

    private void SetDefaultRange(TdmsFileInfo fileInfo)
    {
        double duration = fileInfo.Groups
            .SelectMany(group => group.Channels)
            .Select(channel => channel.DurationSeconds)
            .DefaultIfEmpty(10)
            .Max();

        _fullDurationSeconds = Math.Max(0.001, duration);
        _startSeconds.Text = "0";
        _windowSeconds.Text = _fullDurationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private void ApplyYAxis()
    {
        if (_autoY.IsChecked == true)
        {
            _waveform.FixedYMin = null;
            _waveform.FixedYMax = null;
        }
        else
        {
            _waveform.FixedYMin = TryParseDouble(_yMin.Text);
            _waveform.FixedYMax = TryParseDouble(_yMax.Text);
        }

        _waveform.InvalidateVisual();
    }

    private List<TdmsChannelInfo> SelectedChannels()
    {
        return _channels
            .Where(channel => _selectedChannelKeys.Contains(channel.Key))
            .ToList();
    }

    private void SetAllChannels(bool selected)
    {
        _suppressAutoLoad = true;
        _selectedChannelKeys.Clear();
        if (selected)
        {
            foreach (TdmsChannelInfo channel in _channels)
            {
                _selectedChannelKeys.Add(channel.Key);
            }
        }

        _suppressAutoLoad = false;
        UpdateSelectedChannelsText();
        UpdateLoadButtonState();
        _ = LoadSelectedAsync();
    }

    private void UpdateLoadButtonState()
    {
        _loadButton.IsEnabled = _reader is not null && _selectedChannelKeys.Count > 0;
    }

    private void UpdateSelectedChannelsText()
    {
        if (_selectedChannelKeys.Count == 0)
        {
            _selectedChannelsText.Text = "Selected 0";
            return;
        }

        string preview = string.Join(", ", SelectedChannels().Take(4).Select(channel => channel.DisplayName));
        if (_selectedChannelKeys.Count > 4)
        {
            preview += $", +{_selectedChannelKeys.Count - 4}";
        }

        _selectedChannelsText.Text = $"Selected {_selectedChannelKeys.Count}: {preview}";
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _openFileButton.IsEnabled = !busy;
        _openFolderButton.IsEnabled = !busy;
        _exportTdmsButton.IsEnabled = !busy && _reader is not null;
        _loadButton.IsEnabled = !busy && _reader is not null && _selectedChannelKeys.Count > 0;
        _fullRangeButton.IsEnabled = !busy && _reader is not null;
        _devicePicker.IsEnabled = !busy && _reader is not null && _devicePicker.SelectedIndex >= 0;
        _channelPicker.IsEnabled = !busy && _reader is not null && _channelPicker.SelectedIndex >= 0;
        _addChannelButton.IsEnabled = !busy && _reader is not null && _channelPicker.SelectedIndex >= 0;
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatusNow(message);
        }
    }

    public void Dispose()
    {
        _rangeCts?.Cancel();
        _rangeCts?.Dispose();
        _rangeCts = null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _exportCts?.Cancel();
        _exportCts?.Dispose();
        _exportCts = null;
        _reader?.Dispose();
        _reader = null;
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => _statusText.Text = text);
    }

    private void SetStatusNow(string text)
    {
        _statusText.Text = text;
    }

    private void SetExportFeedback(string text)
    {
        _exportProgressText.Text = text;
        _exportProgressText.IsVisible = true;
        _statusText.Text = text;
    }

    private void ClearExportFeedback()
    {
        _exportProgressText.Text = string.Empty;
        _exportProgressText.IsVisible = false;
    }

    private void UpdateExportProgress(TdmsExportProgress progress)
    {
        double channelPercent = progress.ChannelSamplesTotal == 0
            ? 100
            : Math.Clamp((double)progress.ChannelSamplesDone / progress.ChannelSamplesTotal * 100, 0, 100);
        string text = $"正在导出 TDMS：{progress.CompletedChannels}/{progress.TotalChannels} 通道    {progress.ChannelName} {channelPercent:0.#}%";
        SetExportFeedback(text);
    }

    private static ulong SecondsToSample(double seconds, double sampleRate)
    {
        if (seconds <= 0 || sampleRate <= 0)
        {
            return 0;
        }

        double samples = seconds * sampleRate;
        if (samples >= ulong.MaxValue)
        {
            return ulong.MaxValue;
        }

        return (ulong)Math.Round(samples, MidpointRounding.AwayFromZero);
    }

    private static double ParseDouble(string? text, double fallback)
    {
        return TryParseDouble(text) ?? fallback;
    }

    private static double? TryParseDouble(string? text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
        {
            return current;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
        {
            return invariant;
        }

        return null;
    }

    private static Border GroupHeader(TdmsGroupInfo group)
    {
        return new Border
        {
            Margin = new Thickness(8, 8, 8, 2),
            Padding = new Thickness(10, 7),
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 52)),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = $"{group.Name}    {group.SampleRate:0.##} Hz",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold
            }
        };
    }

    private static Border InfoBlock(Control child)
    {
        return new Border
        {
            Padding = new Thickness(10),
            Background = PanelBackground2,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = child
        };
    }

    private static TextBlock SectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TextPrimary,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold
        };
    }

    private static void StyleButton(Button button, IBrush background)
    {
        button.Background = background;
        button.Foreground = Brushes.White;
        button.Padding = new Thickness(12, 7);
        button.Margin = new Thickness(0, 0, 8, 8);
        button.FontSize = 13;
        button.FontWeight = FontWeight.SemiBold;
    }

    private static void StyleInput(TextBox textBox)
    {
        textBox.Background = Brushes.White;
        textBox.Foreground = TextPrimary;
        textBox.BorderBrush = BorderBrushSoft;
        textBox.FontSize = 13;
        textBox.Padding = new Thickness(8, 5);
    }

    private static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.Background = Brushes.White;
        comboBox.Foreground = TextPrimary;
        comboBox.BorderBrush = BorderBrushSoft;
        comboBox.FontSize = 13;
        comboBox.Padding = new Thickness(7, 4);
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds >= 3600)
        {
            return (seconds / 3600).ToString("0.## h", CultureInfo.InvariantCulture);
        }

        if (seconds >= 60)
        {
            return (seconds / 60).ToString("0.## min", CultureInfo.InvariantCulture);
        }

        return seconds.ToString("0.### s", CultureInfo.InvariantCulture);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds >= 3600)
        {
            return (seconds / 3600).ToString("0.###h", CultureInfo.InvariantCulture);
        }

        if (seconds >= 60)
        {
            return (seconds / 60).ToString("0.###m", CultureInfo.InvariantCulture);
        }

        return seconds.ToString(seconds < 1 ? "0.######s" : "0.###s", CultureInfo.InvariantCulture);
    }

    private static string SuggestedExportName(string sourcePath)
    {
        string sourceName = Directory.Exists(sourcePath)
            ? new DirectoryInfo(sourcePath).Name
            : Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "DashCapture";
        }

        return sourceName + "_export.tdms";
    }

    private static string EnsureExtension(string path, string extension)
    {
        return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : path + extension;
    }

    private static bool IsExportTargetAllowed(string sourcePath, string targetPath, out string reason)
    {
        string fullTarget = Path.GetFullPath(targetPath);
        string? targetDirectory = Path.GetDirectoryName(fullTarget);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            reason = "导出目标无效。";
            return false;
        }

        if (File.Exists(sourcePath) &&
            string.Equals(Path.GetFullPath(sourcePath), fullTarget, StringComparison.OrdinalIgnoreCase))
        {
            reason = "导出目标不能覆盖当前打开的源文件。";
            return false;
        }

        bool sourceIsCaptureSet = Directory.Exists(sourcePath) ||
                                  sourcePath.EndsWith(".dhcap", StringComparison.OrdinalIgnoreCase) ||
                                  sourcePath.EndsWith(".tdms.dhc", StringComparison.OrdinalIgnoreCase) ||
                                  IsDashCaptureContainerFile(sourcePath);
        string sourceDirectory = Directory.Exists(sourcePath)
            ? Path.GetFullPath(sourcePath)
            : Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? string.Empty;
        if (sourceIsCaptureSet &&
            !string.IsNullOrWhiteSpace(sourceDirectory) &&
            string.Equals(Path.GetFullPath(targetDirectory), sourceDirectory, StringComparison.OrdinalIgnoreCase))
        {
            reason = "请把 TDMS 导出到采集源文件夹外，避免导出的 TDMS 与源分段混在一起。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsDashCaptureContainerFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            Span<byte> magic = stackalloc byte[8];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Read(magic) != magic.Length)
            {
                return false;
            }

            return magic.SequenceEqual(new byte[] { (byte)'D', (byte)'H', (byte)'C', (byte)'A', (byte)'P', (byte)'0', (byte)'1', 0 });
        }
        catch
        {
            return false;
        }
    }

    private sealed record ComboItem<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private readonly record struct EnvelopeCacheKey(TdmsChannelKey Channel, ulong StartSample, ulong SampleCount, int Buckets);
}
