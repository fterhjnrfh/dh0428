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
    private readonly Button _openFileButton = new() { Content = "Open TDMS" };
    private readonly Button _openFolderButton = new() { Content = "Open Folder" };
    private readonly Button _loadButton = new() { Content = "Load", IsEnabled = false };
    private readonly Button _selectAllButton = new() { Content = "All" };
    private readonly Button _clearSelectionButton = new() { Content = "None" };
    private readonly Button _fullRangeButton = new() { Content = "Full Range", IsEnabled = false };
    private readonly TextBlock _fileText = new() { Text = "No TDMS file or folder opened." };
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _statusText = new();
    private readonly StackPanel _channelPanel = new() { Spacing = 6 };
    private readonly TextBox _startSeconds = new() { Text = "0", Width = 110 };
    private readonly TextBox _windowSeconds = new() { Text = "10", Width = 110 };
    private readonly Slider _timeSlider = new() { Minimum = 0, Maximum = 0, Value = 0, IsEnabled = false };
    private readonly TextBlock _rangeText = new();
    private readonly TextBox _yMin = new() { Width = 110, IsEnabled = false };
    private readonly TextBox _yMax = new() { Width = 110, IsEnabled = false };
    private readonly CheckBox _autoY = new() { Content = "Auto Y", IsChecked = true };
    private readonly TdmsWaveformControl _waveform = new();
    private readonly List<CheckBox> _channelChecks = new();
    private readonly List<TdmsChannelInfo> _channels = new();
    private readonly Dictionary<EnvelopeCacheKey, TdmsChannelEnvelope> _envelopeCache = new();
    private TdmsFileReader? _reader;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _rangeCts;
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
        _loadButton.Click += async (_, _) => await LoadSelectedAsync();
        _fullRangeButton.Click += async (_, _) => await LoadFullRangeAsync();
        _selectAllButton.Click += (_, _) => SetAllChannels(true);
        _clearSelectionButton.Click += (_, _) => SetAllChannels(false);
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
        StyleButton(_loadButton, AccentGreen);
        StyleButton(_selectAllButton, AccentBlue);
        StyleButton(_clearSelectionButton, AccentBlue);
        StyleButton(_fullRangeButton, AccentBlue);
        StyleInput(_startSeconds);
        StyleInput(_windowSeconds);
        StyleInput(_yMin);
        StyleInput(_yMax);
        SetStatus("Open a TDMS file or a capture folder. Wheel zoom, drag to zoom, Shift/right-drag to pan.");
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(10),
            ColumnDefinitions = new ColumnDefinitions("430,*")
        };

        Control sidePanel = BuildSidePanel();
        Grid.SetColumn(sidePanel, 0);
        root.Children.Add(sidePanel);

        _rangeText.Foreground = TextSecondary;
        _rangeText.FontSize = 12;
        _rangeText.VerticalAlignment = VerticalAlignment.Center;

        var rangeBar = new Grid
        {
            Margin = new Thickness(10, 6, 10, 10),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                _timeSlider,
                _rangeText
            }
        };
        Grid.SetColumn(_rangeText, 1);

        var plotHost = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                rangeBar,
                _waveform
            }
        };
        DockPanel.SetDock(rangeBar, Dock.Bottom);

        var plotPanel = new Border
        {
            Margin = new Thickness(10, 0, 0, 0),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = plotHost
        };
        Grid.SetColumn(plotPanel, 1);
        root.Children.Add(plotPanel);
        return root;
    }

    private Control BuildSidePanel()
    {
        _fileText.Foreground = TextPrimary;
        _fileText.FontSize = 13;
        _fileText.TextWrapping = TextWrapping.Wrap;
        _fileText.MaxHeight = 42;
        _summaryText.Foreground = TextSecondary;
        _summaryText.FontSize = 13;
        _summaryText.TextWrapping = TextWrapping.Wrap;
        _summaryText.MaxHeight = 36;
        _statusText.Foreground = TextSecondary;
        _statusText.FontSize = 13;
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.MaxHeight = 48;

        var fileButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _openFileButton, _openFolderButton, _fullRangeButton }
        };

        var selectionButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _selectAllButton, _clearSelectionButton, _loadButton }
        };

        var rangeGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 6
        };
        AddRangeField(rangeGrid, "Start (s)", _startSeconds, 0, 0);
        AddRangeField(rangeGrid, "Window (s)", _windowSeconds, 1, 0);
        AddRangeField(rangeGrid, "Y Min", _yMin, 0, 1);
        AddRangeField(rangeGrid, "Y Max", _yMax, 1, 1);

        var scroll = new ScrollViewer
        {
            Content = _channelPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var top = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12, 10, 12, 8),
            Children =
            {
                SectionTitle("TDMS Viewer"),
                fileButtons,
                InfoBlock(_fileText),
                InfoBlock(_summaryText),
                rangeGrid,
                _autoY,
                selectionButtons
            }
        };

        var channelHeader = new Grid
        {
            Margin = new Thickness(12, 4, 12, 6),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                SectionTitle("Channels"),
                new TextBlock
                {
                    Text = "device / channel",
                    Foreground = TextSecondary,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        Grid.SetColumn(channelHeader.Children[1], 1);

        var statusBlock = InfoBlock(_statusText);
        statusBlock.Margin = new Thickness(12, 8, 12, 12);

        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                top,
                new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        channelHeader,
                        scroll
                    }
                },
                statusBlock
            }
        };
        Grid.SetRow(panel.Children[1], 1);
        Grid.SetRow(panel.Children[2], 2);
        DockPanel.SetDock(channelHeader, Dock.Top);

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel
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
            Title = "Open TDMS file",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("TDMS") { Patterns = new[] { "*.tdms" } },
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
            Title = "Open TDMS capture folder"
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenPathAsync(path);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        SetBusy(true, "Opening TDMS source...");
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
            SetDefaultRange(reader.FileInfo);
            SetStatus("TDMS source opened.");
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
        _channelChecks.Clear();
        _channelPanel.Children.Clear();

        ulong maxSamples = 0;
        double maxDuration = 0;
        foreach (TdmsGroupInfo group in fileInfo.Groups)
        {
            _channelPanel.Children.Add(GroupHeader(group));
            foreach (TdmsChannelInfo channel in group.Channels)
            {
                _channels.Add(channel);
                maxSamples = Math.Max(maxSamples, channel.SampleCount);
                maxDuration = Math.Max(maxDuration, channel.DurationSeconds);

                var check = new CheckBox
                {
                    Tag = channel,
                    IsChecked = _channelChecks.Count < 4,
                    Content = new TextBlock
                    {
                        Text = $"{channel.Name}  {channel.SampleCount:N0}",
                        FontSize = 14,
                        Foreground = TextPrimary
                    }
                };
                check.IsCheckedChanged += (_, _) =>
                {
                    UpdateLoadButtonState();
                    if (!_suppressAutoLoad)
                    {
                        _ = LoadSelectedAsync();
                    }
                };

                _channelChecks.Add(check);
                _channelPanel.Children.Add(new Border
                {
                    Margin = new Thickness(8, 0, 8, 0),
                    Padding = new Thickness(8, 5),
                    Background = _channelChecks.Count % 2 == 0 ? PanelBackground2 : new SolidColorBrush(Color.FromRgb(247, 250, 253)),
                    BorderBrush = BorderBrushSoft,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Child = check
                });
            }
        }

        _fullDurationSeconds = Math.Max(0.001, maxDuration);
        _summaryText.Text = $"Group {fileInfo.Groups.Count}    Channel {fileInfo.ChannelCount}    MaxSamples {maxSamples:N0}    Duration {FormatDuration(_fullDurationSeconds)}";
        UpdateLoadButtonState();
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
        return _channelChecks
            .Where(check => check.IsChecked == true)
            .Select(check => check.Tag)
            .OfType<TdmsChannelInfo>()
            .ToList();
    }

    private void SetAllChannels(bool selected)
    {
        _suppressAutoLoad = true;
        foreach (CheckBox check in _channelChecks)
        {
            check.IsChecked = selected;
        }

        _suppressAutoLoad = false;
        UpdateLoadButtonState();
        _ = LoadSelectedAsync();
    }

    private void UpdateLoadButtonState()
    {
        _loadButton.IsEnabled = _reader is not null && _channelChecks.Any(check => check.IsChecked == true);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _openFileButton.IsEnabled = !busy;
        _openFolderButton.IsEnabled = !busy;
        _loadButton.IsEnabled = !busy && _reader is not null && _channelChecks.Any(check => check.IsChecked == true);
        _fullRangeButton.IsEnabled = !busy && _reader is not null;
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
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
        _reader?.Dispose();
        _reader = null;
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => _statusText.Text = text);
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

    private readonly record struct EnvelopeCacheKey(TdmsChannelKey Channel, ulong StartSample, ulong SampleCount, int Buckets);
}
