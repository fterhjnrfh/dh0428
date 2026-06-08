using Avalonia;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DashCapture.Core.Acquisition;
using DashCapture.Core.Configuration;
using DashCapture.Core.Models;
using DashCapture.Display;
using DashCapture.Storage;

namespace DashCapture.App;

public sealed class MainWindow : Window
{
    private const int MaxMonitorViews = 64;
    private const int MaxChannelsPerMonitorView = 64;
    private const double ButtonMinHeight = 34;
    private const double FieldMinHeight = 36;
    private const double PanelRadius = 8;

    private static readonly IBrush PageBackground = new SolidColorBrush(Color.FromRgb(242, 246, 251));
    private static readonly IBrush PanelBackground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush PanelBackground2 = new SolidColorBrush(Color.FromRgb(236, 243, 252));
    private static readonly IBrush BorderBrushSoft = new SolidColorBrush(Color.FromRgb(199, 211, 228));
    private static readonly IBrush TextPrimary = new SolidColorBrush(Color.FromRgb(24, 35, 52));
    private static readonly IBrush TextSecondary = new SolidColorBrush(Color.FromRgb(91, 108, 132));
    private static readonly IBrush AccentBlue = new SolidColorBrush(Color.FromRgb(38, 119, 220));
    private static readonly IBrush AccentGreen = new SolidColorBrush(Color.FromRgb(35, 153, 100));
    private static readonly IBrush AccentRed = new SolidColorBrush(Color.FromRgb(207, 71, 71));

    private readonly CaptureSettings _settings;
    private readonly AcquisitionService _acquisition;
    private readonly WaveformStore _waveformStore;
    private readonly DisplayPipeline _displayPipeline;
    private TdmsStorageService? _storageService;

    private readonly WrapPanel _viewNavPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _monitorGrid = new();
    private readonly Border _selectionDrawerHost = new() { IsVisible = false };
    private readonly StackPanel _selectionTreePanel = new() { Spacing = 6 };
    private readonly Button _addViewButton = new() { Content = "+" };
    private readonly Button _removeViewButton = new() { Content = "-" };
    private readonly Button _showSelectionButton = new() { Content = "\u901a\u9053" };
    private readonly Button _closeSelectionButton = new() { Content = "\u6536\u8d77" };
    private readonly Button _selectAllChannelsButton = new() { Content = "\u5168\u9009" };
    private readonly Button _clearChannelsButton = new() { Content = "\u6e05\u7a7a" };
    private readonly CheckBox _activeViewVisibleCheck = new() { Content = "\u663e\u793a\u8be5\u89c6\u56fe" };
    private readonly TextBlock _activeViewText = new();
    private readonly TextBlock _selectionTitle = new();
    private readonly TextBlock _selectionHint = new();
    private readonly RadioButton _storageAllChannelsRadio = new() { Content = "\u5168\u90e8\u901a\u9053", GroupName = "StorageChannelMode" };
    private readonly RadioButton _storageSelectedChannelsRadio = new() { Content = "\u4ec5\u4fdd\u5b58\u9009\u4e2d\u901a\u9053", GroupName = "StorageChannelMode" };
    private readonly Button _storageSelectAllChannelsButton = new() { Content = "\u5168\u9009" };
    private readonly Button _storageClearChannelsButton = new() { Content = "\u6e05\u7a7a" };
    private readonly Button _storageOnlineChannelsButton = new() { Content = "\u4ec5\u5728\u7ebf\u901a\u9053" };
    private readonly Button _storageUseMonitorChannelsButton = new() { Content = "\u4f7f\u7528\u5f53\u524d\u76d1\u63a7\u89c6\u56fe" };
    private readonly TextBox _storageSampleRateMin = new() { Width = 120, Watermark = "\u6700\u4f4e Hz" };
    private readonly TextBox _storageSampleRateMax = new() { Width = 120, Watermark = "\u6700\u9ad8 Hz" };
    private readonly StackPanel _storageChannelTreePanel = new() { Spacing = 6 };
    private readonly TextBlock _storageChannelSummary = new();
    private readonly TextBlock _storageChannelHint = new();
    private readonly StackPanel _deviceInfoPanel = new();
    private readonly TextBox _storagePath = new();
    private readonly TextBox _customFileName = new();
    private readonly ComboBox _namingMode = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _metrics = new();
    private readonly TextBlock _storagePreview = new();
    private readonly TextBlock _storageRawSizeValue = new();
    private readonly TextBlock _storageWrittenSizeValue = new();
    private readonly TextBlock _storagePayloadSizeValue = new();
    private readonly TextBlock _storageRatioValue = new();
    private readonly TextBlock _storageBlockStateValue = new();
    private readonly TextBlock _storageCodecValue = new();
    private readonly TextBlock _storagePreprocessorValue = new();
    private readonly TextBlock _storageWriteThroughputValue = new();
    private readonly TextBlock _storageDurabilityValue = new();
    private readonly TextBlock _captureTimerText = new();
    private readonly Button _connectButton = new() { Content = "\u8fde\u63a5\u8bbe\u5907" };
    private readonly Button _startButton = new() { Content = "\u5f00\u59cb\u91c7\u96c6", IsEnabled = false };
    private readonly Button _stopButton = new() { Content = "\u505c\u6b62\u91c7\u96c6", IsEnabled = false };
    private readonly Button _browseButton = new() { Content = "\u6d4f\u89c8" };
    private readonly CheckBox _storageEnabledCheck = new() { Content = "\u4fdd\u5b58\u6570\u636e" };
    private readonly CheckBox _storageTabEnabledCheck = new() { Content = "\u4fdd\u5b58\u6570\u636e" };
    private readonly CheckBox _compressionEnabledCheck = new() { Content = "\u542f\u7528\u65e0\u635f\u538b\u7f29" };
    private readonly ComboBox _compressionAlgorithmCombo = new();
    private readonly ComboBox _compressionPreprocessorCombo = new();
    private readonly WrapPanel _compressionAlgorithmParams = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _compressionPreprocessorParams = new() { Orientation = Orientation.Horizontal };
    private readonly Slider _compressionZstdLevel = new() { Minimum = -5, Maximum = 22, Width = 150 };
    private readonly Slider _compressionZstdWindowLog = new() { Minimum = 0, Maximum = 31, Width = 150 };
    private readonly Slider _compressionLz4HcLevel = new() { Minimum = 3, Maximum = 12, Width = 150 };
    private readonly Slider _compressionZlibLevel = new() { Minimum = 0, Maximum = 9, Width = 150 };
    private readonly Slider _compressionBZip2BlockSize = new() { Minimum = 1, Maximum = 9, Width = 150 };
    private readonly Slider _compressionLpcOrder = new() { Minimum = 1, Maximum = 4, Width = 150 };
    private readonly TextBlock _compressionZstdLevelValue = new();
    private readonly TextBlock _compressionZstdWindowLogValue = new();
    private readonly TextBlock _compressionLz4HcLevelValue = new();
    private readonly TextBlock _compressionZlibLevelValue = new();
    private readonly TextBlock _compressionBZip2BlockSizeValue = new();
    private readonly TextBlock _compressionLpcOrderValue = new();
    private Control? _compressionZstdLevelField;
    private Control? _compressionZstdWindowLogField;
    private Control? _compressionLz4HcField;
    private Control? _compressionZlibField;
    private Control? _compressionBZip2Field;
    private Control? _compressionLpcField;
    private readonly TdmsViewerControl _tdmsViewer;
    private CaptureStorageStatistics? _lastStorageStats;
    private readonly DispatcherTimer _captureTimer;
    private readonly DispatcherTimer _runtimeStatsTimer;
    private readonly RuntimeUsageSampler _runtimeUsageSampler = new();
    private readonly List<MonitorViewState> _monitorViews = new();
    private readonly HashSet<ChannelKey> _storageSelectedKeys = new();
    private DateTimeOffset _captureStartedAt;
    private DateTimeOffset _lastRuntimeStatsAt = DateTimeOffset.UtcNow;
    private int _activeViewIndex;
    private int _displayFrameCounter;
    private bool _captureUiRunning;
    private bool _captureCleanupInProgress;
    private bool _closeConfirmed;
    private bool _shutdownStarted;
    private bool _monitorViewsLoadedFromSettings;
    private bool _updatingSelectionPanel;
    private bool _updatingStorageChannelPanel;
    private string? _lastFaultMessage;

    public MainWindow()
    {
        _settings = AppSettingsLoader.Load();
        _acquisition = new AcquisitionService(_settings);
        _waveformStore = new WaveformStore(DisplayCapacity());
        _displayPipeline = new DisplayPipeline(
            _acquisition,
            _waveformStore,
            () => _acquisition.Devices,
            _settings.Display.MaxDisplayPointsPerSecond);
        _tdmsViewer = new TdmsViewerControl(_settings.Storage.TdmRuntimeDir);
        LoadMonitorViewsFromSettings();
        LoadStorageChannelSelectionFromSettings();
        if (_monitorViews.Count == 0)
        {
            AddMonitorView();
        }

        _storagePath.Text = _settings.Storage.RootPath;
        _customFileName.Text = _settings.Storage.CustomFileName;
        _storageEnabledCheck.IsChecked = _settings.Storage.Enabled;
        _storageTabEnabledCheck.IsChecked = _settings.Storage.Enabled;
        _storageAllChannelsRadio.IsChecked = _settings.Storage.ChannelSelection.Mode != StorageChannelSelectionMode.SelectedChannels;
        _storageSelectedChannelsRadio.IsChecked = _settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.SelectedChannels;
        _storageSampleRateMin.Text = FormatNullableSampleRate(_settings.Storage.ChannelSelection.SampleRateMinHz);
        _storageSampleRateMax.Text = FormatNullableSampleRate(_settings.Storage.ChannelSelection.SampleRateMaxHz);
        _namingMode.ItemsSource = new[] { "\u6309\u65f6\u95f4\u547d\u540d", "\u81ea\u5b9a\u4e49\u547d\u540d" };
        _namingMode.SelectedIndex = _settings.Storage.NamingMode == FileNamingMode.Time ? 0 : 1;
        InitializeCompressionControls();
        StyleInput(_storagePath);
        StyleInput(_customFileName);
        StyleInput(_storageSampleRateMin);
        StyleInput(_storageSampleRateMax);
        StyleComboBox(_namingMode);
        StyleComboBox(_compressionAlgorithmCombo);
        StyleComboBox(_compressionPreprocessorCombo);
        StyleControlButton(_addViewButton, AccentBlue);
        StyleControlButton(_removeViewButton, AccentRed);
        StyleControlButton(_showSelectionButton, AccentBlue);
        StyleControlButton(_closeSelectionButton, AccentBlue);
        StyleControlButton(_selectAllChannelsButton, AccentBlue);
        StyleControlButton(_clearChannelsButton, AccentBlue);
        StyleControlButton(_storageSelectAllChannelsButton, AccentBlue);
        StyleControlButton(_storageClearChannelsButton, AccentBlue);
        StyleControlButton(_storageOnlineChannelsButton, AccentGreen);
        StyleControlButton(_storageUseMonitorChannelsButton, AccentBlue);

        Title = "DASH Capture";
        Background = PageBackground;
        MinWidth = 1120;
        MinHeight = 720;
        Width = 1360;
        Height = 820;
        _captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _captureTimer.Tick += (_, _) => UpdateCaptureTimerText();
        ResetCaptureTimer();
        Content = BuildContent();

        _connectButton.Click += async (_, _) => await ConnectAsync();
        _startButton.Click += async (_, _) => await StartAsync();
        _stopButton.Click += async (_, _) => await StopAsync();
        _browseButton.Click += async (_, _) => await BrowseStorageFolderAsync();
        _addViewButton.Click += (_, _) =>
        {
            if (_monitorViews.Count < MaxMonitorViews)
            {
                AddMonitorView();
                SelectMonitorView(_monitorViews.Count - 1, showSelection: true);
                RebuildMonitorGrid();
                RebuildViewNav();
                ApplyMonitorSelectionsToStore();
                PersistMonitorViewSettings();
            }
        };
        _removeViewButton.Click += async (_, _) => await RemoveActiveMonitorViewAsync();
        _showSelectionButton.Click += (_, _) => ShowSelectionDrawer();
        _closeSelectionButton.Click += (_, _) => HideSelectionDrawer();
        _selectAllChannelsButton.Click += (_, _) => SetAllChannelsForActiveView(true);
        _clearChannelsButton.Click += (_, _) => SetAllChannelsForActiveView(false);
        _activeViewVisibleCheck.IsCheckedChanged += (_, _) =>
        {
            if (_updatingSelectionPanel || _monitorViews.Count == 0)
            {
                return;
            }

            MonitorViewState view = _monitorViews[_activeViewIndex];
            view.Visible = _activeViewVisibleCheck.IsChecked == true;
            RebuildMonitorGrid();
            ApplyMonitorSelectionsToStore();
            RebuildViewNav();
            RebuildSelectionTree();
            PersistMonitorViewSettings();
        };
        _namingMode.SelectionChanged += (_, _) => UpdateStoragePreview();
        _customFileName.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                UpdateStoragePreview();
            }
        };
        _storagePath.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                UpdateStoragePreview();
            }
        };
        _storageEnabledCheck.IsCheckedChanged += (_, _) =>
        {
            if (_storageTabEnabledCheck.IsChecked != _storageEnabledCheck.IsChecked)
            {
                _storageTabEnabledCheck.IsChecked = _storageEnabledCheck.IsChecked;
            }

            UpdateStoragePreview();
        };
        _storageTabEnabledCheck.IsCheckedChanged += (_, _) =>
        {
            if (_storageEnabledCheck.IsChecked != _storageTabEnabledCheck.IsChecked)
            {
                _storageEnabledCheck.IsChecked = _storageTabEnabledCheck.IsChecked;
            }

            UpdateStoragePreview();
        };
        _storageAllChannelsRadio.IsCheckedChanged += (_, _) =>
        {
            if (_updatingStorageChannelPanel || _storageAllChannelsRadio.IsChecked != true)
            {
                return;
            }

            _settings.Storage.ChannelSelection.Mode = StorageChannelSelectionMode.AllChannels;
            CommitStorageChannelSelectionChange();
        };
        _storageSelectedChannelsRadio.IsCheckedChanged += (_, _) =>
        {
            if (_updatingStorageChannelPanel || _storageSelectedChannelsRadio.IsChecked != true)
            {
                return;
            }

            _settings.Storage.ChannelSelection.Mode = StorageChannelSelectionMode.SelectedChannels;
            CommitStorageChannelSelectionChange();
        };
        _storageSelectAllChannelsButton.Click += (_, _) => SetAllStorageChannels();
        _storageClearChannelsButton.Click += (_, _) => ClearStorageChannels();
        _storageOnlineChannelsButton.Click += (_, _) => SetOnlineStorageChannels();
        _storageUseMonitorChannelsButton.Click += (_, _) => UseActiveMonitorViewForStorage();
        _storageSampleRateMin.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                ApplyStorageSampleRateRangeFromUi();
            }
        };
        _storageSampleRateMax.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                ApplyStorageSampleRateRangeFromUi();
            }
        };
        _compressionEnabledCheck.IsCheckedChanged += (_, _) =>
        {
            UpdateCompressionParameterVisibility();
            UpdateStoragePreview();
        };
        _compressionAlgorithmCombo.SelectionChanged += (_, _) =>
        {
            UpdateCompressionParameterVisibility();
            UpdateStoragePreview();
        };
        _compressionPreprocessorCombo.SelectionChanged += (_, _) =>
        {
            UpdateCompressionParameterVisibility();
            UpdateStoragePreview();
        };
        foreach (Slider slider in CompressionSliders())
        {
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    UpdateCompressionSliderTexts();
                    UpdateStoragePreview();
                }
            };
        }

        _acquisition.Faulted += fault => Dispatcher.UIThread.Post(() =>
        {
            _lastFaultMessage = fault.Message;
            _status.Text = fault.Message;
        });
        _acquisition.TelemetryUpdated += telemetry => Dispatcher.UIThread.Post(() => UpdateTelemetry(telemetry));

        _runtimeStatsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _runtimeStatsTimer.Tick += (_, _) => UpdateRuntimeTitle();
        _runtimeStatsTimer.Start();
        UpdateRuntimeTitle();

        Closing += OnWindowClosing;

        UpdateStoragePreview();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _status.Text = "\u6b63\u5728\u505c\u6b62\u91c7\u96c6\u5e76\u5b8c\u6210\u843d\u76d8...";
        try
        {
            await ShutdownAsync();
        }
        catch (Exception ex)
        {
            _status.Text = $"\u5173\u95ed\u65f6\u843d\u76d8\u5f02\u5e38\uff1a{ex.Message}";
        }
        finally
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private async Task ShutdownAsync()
    {
        await StopAsync();
        await _displayPipeline.DisposeAsync();
        if (_storageService is not null)
        {
            await _storageService.DisposeAsync();
            _storageService = null;
        }

        _tdmsViewer.Dispose();
        await DisposeMonitorViewsAsync();
        _captureTimer.Stop();
        _runtimeStatsTimer.Stop();
        _runtimeUsageSampler.Dispose();
        await _acquisition.DisposeAsync();
    }

    private void InitializeCompressionControls()
    {
        CompressionSettings compression = _settings.Storage.Compression;
        _compressionEnabledCheck.IsChecked = compression.Enabled;

        OptionItem<CompressionAlgorithm>[] algorithms =
        {
            new(CompressionAlgorithm.None, "None"),
            new(CompressionAlgorithm.Zstd, "ZSTD"),
            new(CompressionAlgorithm.Lz4, "LZ4"),
            new(CompressionAlgorithm.Snappy, "Snappy"),
            new(CompressionAlgorithm.Zlib, "Zlib"),
            new(CompressionAlgorithm.Lz4Hc, "LZ4 HC"),
            new(CompressionAlgorithm.BZip2, "BZip2")
        };
        _compressionAlgorithmCombo.ItemsSource = algorithms;
        _compressionAlgorithmCombo.SelectedItem = algorithms.FirstOrDefault(item => item.Value == compression.Algorithm) ?? algorithms[0];

        OptionItem<CompressionPreprocessor>[] preprocessors =
        {
            new(CompressionPreprocessor.None, "\u65e0"),
            new(CompressionPreprocessor.Delta1, "\u4e00\u9636\u5dee\u5206"),
            new(CompressionPreprocessor.Delta2, "\u4e8c\u9636\u5dee\u5206"),
            new(CompressionPreprocessor.Lpc, "LPC"),
            new(CompressionPreprocessor.ByteShuffle, "\u5b57\u8282\u91cd\u6392"),
            new(CompressionPreprocessor.FloatXorDelta, "\u6d6e\u70b9 XOR \u5dee\u5206"),
            new(CompressionPreprocessor.DeltaFloatPredictor, "\u6d6e\u70b9\u7ebf\u6027\u9884\u6d4b"),
            new(CompressionPreprocessor.IntDeltaZigZag, "\u6574\u6570\u5dee\u5206 ZigZag")
        };
        _compressionPreprocessorCombo.ItemsSource = preprocessors;
        _compressionPreprocessorCombo.SelectedItem = preprocessors.FirstOrDefault(item => item.Value == compression.Preprocessor) ?? preprocessors[0];

        _compressionAlgorithmCombo.Width = 150;
        _compressionPreprocessorCombo.Width = 150;
        _compressionZstdLevel.Value = Math.Clamp(compression.ZstdLevel, -5, 22);
        _compressionZstdWindowLog.Value = Math.Clamp(compression.ZstdWindowLog, 0, 31);
        _compressionLz4HcLevel.Value = Math.Clamp(compression.Lz4HcLevel, 3, 12);
        _compressionZlibLevel.Value = Math.Clamp(compression.ZlibLevel, 0, 9);
        _compressionBZip2BlockSize.Value = Math.Clamp(compression.BZip2BlockSize, 1, 9);
        _compressionLpcOrder.Value = Math.Clamp(compression.LpcOrder, 1, 4);
        UpdateCompressionSliderTexts();
    }

    private IEnumerable<Slider> CompressionSliders()
    {
        yield return _compressionZstdLevel;
        yield return _compressionZstdWindowLog;
        yield return _compressionLz4HcLevel;
        yield return _compressionZlibLevel;
        yield return _compressionBZip2BlockSize;
        yield return _compressionLpcOrder;
    }

    private Control BuildContent()
    {
        var root = new DockPanel();

        Control topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        Control statusBar = BuildStatusBar();
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        root.Children.Add(BuildTabs());
        return root;
    }

    private Control BuildTopBar()
    {
        StyleControlButton(_connectButton, AccentBlue);
        StyleControlButton(_startButton, AccentGreen);
        StyleControlButton(_stopButton, AccentRed);

        _status.Text = "\u672a\u8fde\u63a5";
        _status.Foreground = TextPrimary;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _storageEnabledCheck.Foreground = TextPrimary;
        _storageEnabledCheck.VerticalAlignment = VerticalAlignment.Center;
        _storageTabEnabledCheck.Foreground = TextPrimary;
        _storageTabEnabledCheck.VerticalAlignment = VerticalAlignment.Center;

        var bar = new Grid
        {
            Margin = new Thickness(16, 12),
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            ColumnSpacing = 12
        };

        Control deviceGroup = AddControlGroup("\u8bbe\u5907", _connectButton);
        Grid.SetColumn(deviceGroup, 0);
        bar.Children.Add(deviceGroup);

        var sampleGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _startButton, _stopButton, _storageEnabledCheck }
        };
        Control acquisitionGroup = AddControlGroup("\u91c7\u96c6", sampleGroup);
        Grid.SetColumn(acquisitionGroup, 1);
        bar.Children.Add(acquisitionGroup);

        Control statusPill = AddStatusPill();
        Grid.SetColumn(statusPill, 2);
        bar.Children.Add(statusPill);

        _captureTimerText.Foreground = TextSecondary;
        _captureTimerText.FontSize = 15;
        _captureTimerText.FontWeight = FontWeight.SemiBold;
        _captureTimerText.VerticalAlignment = VerticalAlignment.Center;
        _captureTimerText.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_captureTimerText, 3);
        bar.Children.Add(_captureTimerText);

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar
        };
    }

    private Control BuildTabs()
    {
        return new TabControl
        {
            Margin = new Thickness(14, 8, 14, 0),
            FontSize = 15,
            Items =
            {
                new TabItem { Header = "\u4e3b\u76d1\u63a7", Content = BuildMonitorTab() },
                new TabItem { Header = "\u8bbe\u5907\u901a\u9053", Content = BuildDeviceTab() },
                new TabItem { Header = "\u6570\u636e\u67e5\u770b", Content = _tdmsViewer },
                new TabItem { Header = "\u5b58\u50a8", Content = BuildStorageTab() }
            }
        };
    }

    private Control BuildMonitorTab()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };

        var scroll = new ScrollViewer
        {
            Content = _monitorGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 10, 0, 10)
        };

        Grid.SetColumn(scroll, 0);
        root.Children.Add(scroll);

        Control commands = BuildMonitorCommandPanel();
        Grid.SetColumn(commands, 1);
        root.Children.Add(commands);

        RebuildMonitorGrid();
        RebuildViewNav();
        RebuildSelectionTree();
        return root;
    }

    private Control BuildMonitorCommandPanel()
    {
        _activeViewText.Foreground = TextSecondary;
        _activeViewText.FontSize = 13;
        _activeViewText.TextWrapping = TextWrapping.Wrap;
        _activeViewText.Width = 108;

        _addViewButton.MinWidth = 88;
        _removeViewButton.MinWidth = 88;
        _showSelectionButton.MinWidth = 88;
        _selectionDrawerHost.Width = 380;
        _selectionDrawerHost.Margin = new Thickness(10, 10, 0, 10);
        _selectionDrawerHost.Background = PanelBackground;
        _selectionDrawerHost.BorderBrush = BorderBrushSoft;
        _selectionDrawerHost.BorderThickness = new Thickness(1);
        _selectionDrawerHost.CornerRadius = new CornerRadius(PanelRadius);
        _selectionDrawerHost.Child = BuildSelectionDrawer();

        var rail = new Border
        {
            Margin = new Thickness(0, 10, 0, 10),
            Padding = new Thickness(10),
            Width = 128,
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    _addViewButton,
                    _removeViewButton,
                    _showSelectionButton,
                    _activeViewText
                }
            }
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { rail, _selectionDrawerHost }
        };
    }

    private Control BuildSelectionDrawer()
    {
        _selectionTitle.Foreground = TextPrimary;
        _selectionTitle.FontSize = 17;
        _selectionTitle.FontWeight = FontWeight.SemiBold;
        _selectionTitle.TextWrapping = TextWrapping.Wrap;
        _selectionHint.Foreground = TextSecondary;
        _selectionHint.FontSize = 13;
        _selectionHint.TextWrapping = TextWrapping.Wrap;
        _activeViewVisibleCheck.Foreground = TextPrimary;
        _activeViewVisibleCheck.FontSize = 14;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        header.Children.Add(_selectionTitle);
        Grid.SetColumn(_closeSelectionButton, 1);
        header.Children.Add(_closeSelectionButton);

        var viewScroll = new ScrollViewer
        {
            Content = _viewNavPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = 40
        };

        var treeScroll = new ScrollViewer
        {
            Content = _selectionTreePanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var actionRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10
        };
        actionRow.Children.Add(_selectAllChannelsButton);
        Grid.SetColumn(_clearChannelsButton, 1);
        actionRow.Children.Add(_clearChannelsButton);

        var panel = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                header,
                viewScroll,
                _activeViewVisibleCheck,
                _selectionHint,
                actionRow,
                treeScroll
            }
        };

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(viewScroll, Dock.Top);
        DockPanel.SetDock(_activeViewVisibleCheck, Dock.Top);
        DockPanel.SetDock(_selectionHint, Dock.Top);
        DockPanel.SetDock(actionRow, Dock.Bottom);

        header.Margin = new Thickness(14, 14, 14, 10);
        viewScroll.Margin = new Thickness(14, 0, 14, 10);
        _activeViewVisibleCheck.Margin = new Thickness(14, 0, 14, 8);
        _selectionHint.Margin = new Thickness(14, 0, 14, 12);
        actionRow.Margin = new Thickness(14, 10, 14, 14);
        treeScroll.Margin = new Thickness(14, 0, 14, 0);

        return panel;
    }

    private void ShowSelectionDrawer()
    {
        _selectionDrawerHost.IsVisible = true;
        RebuildViewNav();
        RebuildSelectionTree();
    }

    private void HideSelectionDrawer()
    {
        _selectionDrawerHost.IsVisible = false;
    }

    private void RebuildSelectionTree()
    {
        _selectionTreePanel.Children.Clear();
        _updatingSelectionPanel = true;
        try
        {
            if (_monitorViews.Count == 0)
            {
                _selectionTitle.Text = "\u672a\u521b\u5efa\u89c6\u56fe";
                _selectionHint.Text = "\u70b9\u51fb + \u521b\u5efa\u65b0\u89c6\u56fe";
                _activeViewVisibleCheck.IsChecked = false;
                return;
            }

            MonitorViewState view = _monitorViews[_activeViewIndex];
            _selectionTitle.Text = $"{view.Name} \u901a\u9053\u9009\u62e9";
            _selectionHint.Text = _acquisition.Devices.Count == 0
                ? "\u672a\u8fde\u63a5\u8bbe\u5907\uff0c\u8fde\u63a5\u540e\u5c06\u663e\u793a\u8bbe\u5907\u6811"
                : $"\u5df2\u9009 {view.SelectedKeys.Count}/{MaxChannelsPerMonitorView} \u901a\u9053\uff0c\u53ef\u8de8\u8bbe\u5907\u53e0\u52a0";
            _activeViewVisibleCheck.IsChecked = view.Visible;

            if (_acquisition.Devices.Count == 0)
            {
                _selectionTreePanel.Children.Add(new TextBlock
                {
                    Text = "\u6682\u65e0\u8bbe\u5907",
                    Foreground = TextSecondary,
                    FontSize = 14,
                    Margin = new Thickness(2, 6)
                });
                return;
            }

            int deviceIndex = 0;
            foreach (DeviceDescriptor device in _acquisition.Devices)
            {
                _selectionTreePanel.Children.Add(BuildDeviceSelectionNode(device, deviceIndex + 1, view));
                deviceIndex++;
            }
        }
        finally
        {
            _updatingSelectionPanel = false;
        }
    }

    private Control BuildDeviceSelectionNode(DeviceDescriptor device, int displayIndex, MonitorViewState view)
    {
        ChannelDescriptor[] channels = device.Channels.ToArray();
        ChannelKey[] keys = channels.Select(channel => new ChannelKey(channel)).ToArray();
        int selectedCount = keys.Count(view.SelectedKeys.Contains);
        bool? checkedState = selectedCount == 0
            ? false
            : selectedCount == keys.Length ? true : null;

        var deviceCheck = new CheckBox
        {
            IsThreeState = true,
            IsChecked = checkedState,
            Content = $"{FormatDeviceName(device, displayIndex)}    {selectedCount}/{channels.Length}",
            Foreground = device.Online ? TextPrimary : TextSecondary,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        deviceCheck.IsCheckedChanged += (_, _) =>
        {
            if (_updatingSelectionPanel)
            {
                return;
            }

            if (deviceCheck.IsChecked == true)
            {
                SetDeviceChannelsForActiveView(device, selected: true);
            }
            else if (deviceCheck.IsChecked == false)
            {
                SetDeviceChannelsForActiveView(device, selected: false);
            }
        };

        var channelPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(22, 6, 0, 8)
        };
        foreach (ChannelDescriptor channel in channels)
        {
            ChannelKey key = new(channel);
            var channelCheck = new CheckBox
            {
                IsChecked = view.SelectedKeys.Contains(key),
                Content = FormatChannelSelectionName(channel),
                Foreground = channel.Online ? TextPrimary : TextSecondary,
                FontSize = 13
            };
            channelCheck.IsCheckedChanged += (_, _) =>
            {
                if (_updatingSelectionPanel)
                {
                    return;
                }

                if (!TrySetChannelForActiveView(channel, channelCheck.IsChecked == true))
                {
                    _updatingSelectionPanel = true;
                    channelCheck.IsChecked = false;
                    _updatingSelectionPanel = false;
                    return;
                }

                CommitMonitorSelectionChange(_monitorViews[_activeViewIndex]);
            };
            channelPanel.Children.Add(channelCheck);
        }

        return new Expander
        {
            Header = deviceCheck,
            IsExpanded = selectedCount > 0 || displayIndex == 1,
            Content = channelPanel,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            Background = PanelBackground2,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 5)
        };
    }

    private Control BuildDeviceTab()
    {
        _deviceInfoPanel.Margin = new Thickness(16);
        _deviceInfoPanel.Spacing = 14;
        RefreshDeviceInfoPanel();

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _deviceInfoPanel
        };
    }

    private Control BuildStorageTab()
    {
        _storagePath.Width = double.NaN;
        _storagePath.MinWidth = 260;
        _storagePath.Watermark = "\u9009\u62e9\u6570\u636e\u4fdd\u5b58\u76ee\u5f55";
        _customFileName.Width = double.NaN;
        _customFileName.Watermark = "\u4f8b\u5982 TestRun_A";
        _namingMode.Width = double.NaN;
        StyleControlButton(_browseButton, AccentBlue);

        var root = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("1.05*,0.95*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var pathGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                _storagePath,
                _browseButton
            }
        };
        Grid.SetColumn(_browseButton, 1);

        var storageGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 12,
            Children =
            {
                StorageField("\u91c7\u96c6\u65f6\u5199\u5165", _storageTabEnabledCheck),
                StorageField("\u4fdd\u5b58\u4f4d\u7f6e", pathGrid),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 14,
                    Children =
                    {
                        StorageField("\u547d\u540d\u65b9\u5f0f", _namingMode),
                        StorageField("\u81ea\u5b9a\u4e49\u540d\u79f0", _customFileName)
                    }
                }
            }
        };
        Grid.SetRow(storageGrid.Children[1], 1);
        Grid.SetRow(storageGrid.Children[2], 2);
        Grid.SetColumn(((Grid)storageGrid.Children[2]).Children[1], 1);

        _storagePreview.TextWrapping = TextWrapping.Wrap;
        _storagePreview.Foreground = TextPrimary;
        _storagePreview.FontSize = 13;
        var storageContent = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                storageGrid,
                StorageField("\u4fdd\u5b58\u9884\u89c8", _storagePreview)
            }
        };

        Control storageModule = StorageModule("\u5b58\u50a8", storageContent);
        Grid.SetColumn(storageModule, 0);
        Grid.SetRow(storageModule, 0);
        root.Children.Add(storageModule);

        Control compressionModule = StorageModule("\u65e0\u635f\u538b\u7f29", BuildCompressionSettingsPanel());
        Grid.SetColumn(compressionModule, 1);
        Grid.SetRow(compressionModule, 0);
        root.Children.Add(compressionModule);

        Control channelModule = StorageModule("\u5b58\u50a8\u901a\u9053", BuildStorageChannelSelectionPanel());
        Grid.SetColumn(channelModule, 0);
        Grid.SetRow(channelModule, 1);
        Grid.SetColumnSpan(channelModule, 2);
        root.Children.Add(channelModule);

        UpdateStorageStatsFields(null);
        Control runtimeModule = StorageModule("\u8fd0\u884c\u53c2\u6570", BuildRuntimeParametersPanel());
        Grid.SetColumn(runtimeModule, 0);
        Grid.SetRow(runtimeModule, 2);
        Grid.SetColumnSpan(runtimeModule, 2);
        root.Children.Add(runtimeModule);

        return new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private Control BuildStorageChannelSelectionPanel()
    {
        _storageChannelSummary.Foreground = TextPrimary;
        _storageChannelSummary.FontSize = 14;
        _storageChannelSummary.TextWrapping = TextWrapping.Wrap;
        _storageChannelHint.Foreground = TextSecondary;
        _storageChannelHint.FontSize = 13;
        _storageChannelHint.TextWrapping = TextWrapping.Wrap;

        var rangeControl = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _storageSampleRateMin,
                new TextBlock
                {
                    Text = "\u81f3",
                    Foreground = TextSecondary,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                },
                _storageSampleRateMax,
                new TextBlock
                {
                    Text = "Hz",
                    Foreground = TextSecondary,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var actionRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _storageSelectAllChannelsButton,
                _storageClearChannelsButton,
                StorageField("\u91c7\u6837\u7387\u533a\u95f4", rangeControl)
            }
        };
        foreach (Control child in actionRow.Children)
        {
            child.Margin = new Thickness(0, 0, 10, 10);
        }

        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                actionRow,
                _storageChannelSummary,
                _storageChannelHint
            }
        };

        RefreshStorageChannelPanel();
        return panel;
    }

    private void LoadStorageChannelSelectionFromSettings()
    {
        _storageSelectedKeys.Clear();
        foreach (MonitorChannelSettings channel in _settings.Storage.ChannelSelection.Channels)
        {
            _storageSelectedKeys.Add(new ChannelKey(channel.DeviceIp, channel.DeviceId, channel.ChannelId));
        }
    }

    private void RefreshStorageChannelPanel()
    {
        _updatingStorageChannelPanel = true;
        try
        {
            NormalizeStorageSelectedKeys();
            _storageAllChannelsRadio.IsChecked = _settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.AllChannels;
            _storageSelectedChannelsRadio.IsChecked = _settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.SelectedChannels;
        }
        finally
        {
            _updatingStorageChannelPanel = false;
            UpdateStorageChannelSummary();
            UpdateStorageChannelControlState();
        }
    }

    private void RebuildStorageChannelTree()
    {
        RefreshStorageChannelPanel();
    }

    private Control BuildStorageDeviceSelectionNode(DeviceDescriptor device, int displayIndex)
    {
        bool selectedMode = _settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.SelectedChannels;
        ChannelDescriptor[] channels = device.Channels.ToArray();
        int selectedCount = selectedMode
            ? channels.Count(IsStorageChannelSelected)
            : channels.Length;
        bool? checkedState = selectedCount == 0
            ? false
            : selectedCount == channels.Length ? true : null;

        var deviceCheck = new CheckBox
        {
            IsThreeState = true,
            IsChecked = checkedState,
            Content = $"{FormatDeviceName(device, displayIndex)}    {selectedCount}/{channels.Length}",
            Foreground = device.Online ? TextPrimary : TextSecondary,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            IsEnabled = selectedMode
        };
        deviceCheck.IsCheckedChanged += (_, _) =>
        {
            if (_updatingStorageChannelPanel)
            {
                return;
            }

            if (deviceCheck.IsChecked == true)
            {
                SetStorageDeviceChannels(device, selected: true);
            }
            else if (deviceCheck.IsChecked == false)
            {
                SetStorageDeviceChannels(device, selected: false);
            }
        };

        var channelPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(22, 6, 0, 8)
        };
        foreach (ChannelDescriptor channel in channels)
        {
            var channelCheck = new CheckBox
            {
                IsChecked = !selectedMode || IsStorageChannelSelected(channel),
                Content = FormatChannelSelectionName(channel),
                Foreground = channel.Online ? TextPrimary : TextSecondary,
                FontSize = 13,
                IsEnabled = selectedMode
            };
            channelCheck.IsCheckedChanged += (_, _) =>
            {
                if (_updatingStorageChannelPanel)
                {
                    return;
                }

                TrySetStorageChannel(channel, channelCheck.IsChecked == true);
                CommitStorageChannelSelectionChange();
            };
            channelPanel.Children.Add(channelCheck);
        }

        return new Expander
        {
            Header = deviceCheck,
            IsExpanded = selectedCount > 0 || displayIndex == 1,
            Content = channelPanel,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            Background = PanelBackground2,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 5)
        };
    }

    private void SetAllStorageChannels()
    {
        SetStorageChannelMode(StorageChannelSelectionMode.AllChannels);
        _storageSelectedKeys.Clear();
        _updatingStorageChannelPanel = true;
        try
        {
            _storageSampleRateMin.Text = string.Empty;
            _storageSampleRateMax.Text = string.Empty;
            _settings.Storage.ChannelSelection.SampleRateMinHz = null;
            _settings.Storage.ChannelSelection.SampleRateMaxHz = null;
        }
        finally
        {
            _updatingStorageChannelPanel = false;
        }

        CommitStorageChannelSelectionChange();
    }

    private void ClearStorageChannels()
    {
        SetStorageChannelMode(StorageChannelSelectionMode.SelectedChannels);
        _storageSelectedKeys.Clear();
        _updatingStorageChannelPanel = true;
        try
        {
            _storageSampleRateMin.Text = string.Empty;
            _storageSampleRateMax.Text = string.Empty;
            _settings.Storage.ChannelSelection.SampleRateMinHz = null;
            _settings.Storage.ChannelSelection.SampleRateMaxHz = null;
        }
        finally
        {
            _updatingStorageChannelPanel = false;
        }

        CommitStorageChannelSelectionChange();
    }

    private void ApplyStorageSampleRateRangeFromUi()
    {
        if (_updatingStorageChannelPanel)
        {
            return;
        }

        _settings.Storage.ChannelSelection.SampleRateMinHz = TryParseDouble(_storageSampleRateMin.Text);
        _settings.Storage.ChannelSelection.SampleRateMaxHz = TryParseDouble(_storageSampleRateMax.Text);
        SetStorageChannelMode(StorageChannelSelectionMode.SampleRateRange);
        _storageSelectedKeys.Clear();
        CommitStorageChannelSelectionChange();
    }

    private void SetOnlineStorageChannels()
    {
        if (_acquisition.Devices.Count == 0)
        {
            return;
        }

        SetStorageChannelMode(StorageChannelSelectionMode.SelectedChannels);
        _storageSelectedKeys.Clear();
        foreach (ChannelDescriptor channel in _acquisition.Devices
                     .Where(device => device.Online)
                     .SelectMany(device => device.Channels)
                     .Where(channel => channel.Online))
        {
            _storageSelectedKeys.Add(new ChannelKey(channel));
        }

        CommitStorageChannelSelectionChange();
    }

    private void UseActiveMonitorViewForStorage()
    {
        if (_monitorViews.Count == 0)
        {
            return;
        }

        SetStorageChannelMode(StorageChannelSelectionMode.SelectedChannels);
        _storageSelectedKeys.Clear();
        foreach (ChannelKey key in _monitorViews[_activeViewIndex].SelectedKeys)
        {
            _storageSelectedKeys.Add(key);
        }

        CommitStorageChannelSelectionChange();
    }

    private void SetStorageDeviceChannels(DeviceDescriptor device, bool selected)
    {
        SetStorageChannelMode(StorageChannelSelectionMode.SelectedChannels);
        foreach (ChannelDescriptor channel in device.Channels)
        {
            TrySetStorageChannel(channel, selected);
        }

        CommitStorageChannelSelectionChange();
    }

    private bool TrySetStorageChannel(ChannelDescriptor channel, bool selected)
    {
        ChannelKey key = new(channel);
        RemoveStorageChannelKey(channel);
        if (selected)
        {
            _storageSelectedKeys.Add(key);
        }

        return true;
    }

    private void RemoveStorageChannelKey(ChannelDescriptor channel)
    {
        ChannelKey key = new(channel);
        _storageSelectedKeys.RemoveWhere(item =>
            item == key ||
            (item.DeviceId == channel.DeviceId && item.ChannelId == channel.ChannelId));
    }

    private bool IsStorageChannelSelected(ChannelDescriptor channel)
    {
        ChannelKey key = new(channel);
        return _storageSelectedKeys.Contains(key) ||
               _storageSelectedKeys.Any(item => item.DeviceId == channel.DeviceId && item.ChannelId == channel.ChannelId);
    }

    private void SetStorageChannelMode(StorageChannelSelectionMode mode)
    {
        _settings.Storage.ChannelSelection.Mode = mode;
        _updatingStorageChannelPanel = true;
        try
        {
            _storageAllChannelsRadio.IsChecked = mode == StorageChannelSelectionMode.AllChannels;
            _storageSelectedChannelsRadio.IsChecked = mode == StorageChannelSelectionMode.SelectedChannels;
        }
        finally
        {
            _updatingStorageChannelPanel = false;
        }
    }

    private void CommitStorageChannelSelectionChange()
    {
        NormalizeStorageSelectedKeys();
        PersistStorageChannelSelectionSettings();
        RebuildStorageChannelTree();
        UpdateStoragePreview();
    }

    private void PersistStorageChannelSelectionSettings()
    {
        try
        {
            var selection = new StorageChannelSelectionSettings
            {
                Mode = _settings.Storage.ChannelSelection.Mode,
                SampleRateMinHz = _settings.Storage.ChannelSelection.SampleRateMinHz,
                SampleRateMaxHz = _settings.Storage.ChannelSelection.SampleRateMaxHz,
                Channels = _storageSelectedKeys
                    .OrderBy(key => key.DeviceId)
                    .ThenBy(key => key.ChannelId)
                    .ThenBy(key => key.DeviceIp, StringComparer.OrdinalIgnoreCase)
                    .Select(key => new MonitorChannelSettings
                    {
                        DeviceIp = key.DeviceIp,
                        DeviceId = key.DeviceId,
                        ChannelId = key.ChannelId
                    })
                    .ToList()
            };

            _settings.Storage.ChannelSelection = selection;
            AppSettingsLoader.SaveStorageChannelSelection(selection);
        }
        catch
        {
        }
    }

    private void NormalizeStorageSelectedKeys()
    {
        if (_acquisition.Devices.Count == 0 || _storageSelectedKeys.Count == 0)
        {
            return;
        }

        Dictionary<ChannelKey, ChannelDescriptor> exactLookup = _acquisition.Devices
            .SelectMany(device => device.Channels)
            .GroupBy(channel => new ChannelKey(channel))
            .ToDictionary(group => group.Key, group => group.First());
        Dictionary<(int DeviceId, int ChannelId), ChannelDescriptor> fallbackLookup = _acquisition.Devices
            .SelectMany(device => device.Channels)
            .GroupBy(channel => (channel.DeviceId, channel.ChannelId))
            .ToDictionary(group => group.Key, group => group.First());

        var normalizedKeys = new HashSet<ChannelKey>();
        foreach (ChannelKey selectedKey in _storageSelectedKeys.ToArray())
        {
            if (exactLookup.TryGetValue(selectedKey, out ChannelDescriptor? channel) ||
                fallbackLookup.TryGetValue((selectedKey.DeviceId, selectedKey.ChannelId), out channel))
            {
                normalizedKeys.Add(new ChannelKey(channel));
            }
            else
            {
                normalizedKeys.Add(selectedKey);
            }
        }

        _storageSelectedKeys.Clear();
        foreach (ChannelKey key in normalizedKeys)
        {
            _storageSelectedKeys.Add(key);
        }
    }

    private IReadOnlyList<DeviceDescriptor> ResolveStorageDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        if (_settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.AllChannels)
        {
            return devices;
        }

        if (devices.Count == 0)
        {
            return Array.Empty<DeviceDescriptor>();
        }

        var selectedDevices = new List<DeviceDescriptor>();
        foreach (DeviceDescriptor device in devices)
        {
            ChannelDescriptor[] selectedChannels = _settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.SampleRateRange
                ? device.Channels.Where(IsChannelInStorageSampleRateRange).ToArray()
                : device.Channels.Where(IsStorageChannelSelected).ToArray();
            if (selectedChannels.Length > 0)
            {
                selectedDevices.Add(device with { Channels = selectedChannels });
            }
        }

        return selectedDevices;
    }

    private bool IsChannelInStorageSampleRateRange(ChannelDescriptor channel)
    {
        double sampleRate = channel.SampleRate;
        if (sampleRate <= 0 || double.IsNaN(sampleRate) || double.IsInfinity(sampleRate))
        {
            return false;
        }

        double? min = _settings.Storage.ChannelSelection.SampleRateMinHz;
        double? max = _settings.Storage.ChannelSelection.SampleRateMaxHz;
        if (min.HasValue && max.HasValue && min > max)
        {
            (min, max) = (max, min);
        }

        return (!min.HasValue || sampleRate >= min.Value) &&
               (!max.HasValue || sampleRate <= max.Value);
    }

    private void UpdateStorageChannelSummary()
    {
        int totalChannels = _acquisition.Devices.Sum(device => device.Channels.Count);
        int selectedChannels = ResolveStorageDevices(_acquisition.Devices).Sum(device => device.Channels.Count);
        StorageChannelSelectionMode mode = _settings.Storage.ChannelSelection.Mode;

        if (totalChannels == 0)
        {
            _storageChannelSummary.Text = mode == StorageChannelSelectionMode.SampleRateRange
                ? $"\u672a\u8fde\u63a5\u8bbe\u5907\uff1b\u5df2\u8bbe\u7f6e\u91c7\u6837\u7387\u533a\u95f4 {StorageSampleRateRangeText()}"
                : mode == StorageChannelSelectionMode.SelectedChannels
                    ? "\u672a\u8fde\u63a5\u8bbe\u5907\uff1b\u5f53\u524d\u4e0d\u4fdd\u5b58\u4efb\u4f55\u901a\u9053"
                    : "\u672a\u8fde\u63a5\u8bbe\u5907\uff1b\u8fde\u63a5\u540e\u9ed8\u8ba4\u4fdd\u5b58\u5168\u90e8\u901a\u9053";
        }
        else
        {
            _storageChannelSummary.Text = mode == StorageChannelSelectionMode.AllChannels
                ? $"\u672c\u6b21\u5c06\u4fdd\u5b58\u5168\u90e8 {totalChannels} \u4e2a\u901a\u9053"
                : mode == StorageChannelSelectionMode.SampleRateRange
                    ? $"\u91c7\u6837\u7387 {StorageSampleRateRangeText()}\uff1a\u5c06\u4fdd\u5b58 {selectedChannels}/{totalChannels} \u4e2a\u901a\u9053"
                    : "\u5f53\u524d\u5df2\u6e05\u7a7a\uff1b\u672c\u6b21\u4e0d\u4fdd\u5b58\u4efb\u4f55\u901a\u9053";
        }

        if (_captureUiRunning)
        {
            _storageChannelHint.Text = "\u91c7\u96c6\u8fd0\u884c\u4e2d\uff0c\u5b58\u50a8\u901a\u9053\u5df2\u9501\u5b9a\uff0c\u4fee\u6539\u5c06\u5728\u4e0b\u6b21\u91c7\u96c6\u524d\u751f\u6548";
        }
        else if (mode == StorageChannelSelectionMode.AllChannels)
        {
            _storageChannelHint.Text = "\u5168\u9009\u540e\u4f1a\u5199\u5165\u5f53\u524d\u8bbe\u5907\u7684\u6240\u6709\u901a\u9053";
        }
        else if (selectedChannels == 0)
        {
            _storageChannelHint.Text = "\u5f00\u59cb\u91c7\u96c6\u524d\u81f3\u5c11\u9700\u8981\u901a\u8fc7\u5168\u9009\u6216\u91c7\u6837\u7387\u533a\u95f4\u5339\u914d\u5230\u4e00\u4e2a\u901a\u9053";
        }
        else
        {
            _storageChannelHint.Text = "\u4fee\u6539\u91c7\u6837\u7387\u533a\u95f4\u540e\u4f1a\u81ea\u52a8\u7b5b\u9009\u5339\u914d\u901a\u9053\u5e76\u4fdd\u5b58\u914d\u7f6e";
        }
    }

    private void UpdateStorageChannelControlState()
    {
        bool canEdit = !_captureUiRunning && !_captureCleanupInProgress;

        _storageAllChannelsRadio.IsEnabled = canEdit;
        _storageSelectedChannelsRadio.IsEnabled = canEdit;
        _storageChannelTreePanel.IsEnabled = canEdit;
        _storageSelectAllChannelsButton.IsEnabled = canEdit;
        _storageClearChannelsButton.IsEnabled = canEdit;
        _storageSampleRateMin.IsEnabled = canEdit;
        _storageSampleRateMax.IsEnabled = canEdit;
        _storageOnlineChannelsButton.IsEnabled = false;
            _storageUseMonitorChannelsButton.IsEnabled = false;
    }

    private string StorageSampleRateRangeText()
    {
        double? min = _settings.Storage.ChannelSelection.SampleRateMinHz;
        double? max = _settings.Storage.ChannelSelection.SampleRateMaxHz;
        if (min.HasValue && max.HasValue && min > max)
        {
            (min, max) = (max, min);
        }

        string minText = min.HasValue ? min.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-\u221e";
        string maxText = max.HasValue ? max.Value.ToString("0.###", CultureInfo.InvariantCulture) : "+\u221e";
        return $"{minText} - {maxText} Hz";
    }

    private Control BuildCompressionSettingsPanel()
    {
        var switches = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _compressionEnabledCheck
            }
        };
        _compressionEnabledCheck.Margin = new Thickness(0, 0, 18, 8);
        _compressionEnabledCheck.Foreground = TextPrimary;

        var options = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                CompressionField("\u7b97\u6cd5", _compressionAlgorithmCombo),
                CompressionField("\u9884\u5904\u7406", _compressionPreprocessorCombo)
            }
        };

        _compressionZstdLevelField = SliderField("ZSTD \u7b49\u7ea7", _compressionZstdLevel, _compressionZstdLevelValue, "\u9ed8\u8ba4 3\uff0c\u8303\u56f4 -5-22");
        _compressionZstdWindowLogField = SliderField("ZSTD \u7a97\u53e3", _compressionZstdWindowLog, _compressionZstdWindowLogValue, "0 \u4e3a\u81ea\u52a8\uff0c\u8303\u56f4 0-31");
        _compressionLz4HcField = SliderField("LZ4 HC \u7b49\u7ea7", _compressionLz4HcLevel, _compressionLz4HcLevelValue, "\u9ed8\u8ba4 9\uff0c\u8303\u56f4 3-12");
        _compressionZlibField = SliderField("Zlib \u7b49\u7ea7", _compressionZlibLevel, _compressionZlibLevelValue, "\u9ed8\u8ba4 6\uff0c\u8303\u56f4 0-9");
        _compressionBZip2Field = SliderField("BZip2 \u5757\u5927\u5c0f", _compressionBZip2BlockSize, _compressionBZip2BlockSizeValue, "\u9ed8\u8ba4 9\uff0c\u8303\u56f4 1-9");
        _compressionLpcField = SliderField("LPC \u9636\u6570", _compressionLpcOrder, _compressionLpcOrderValue, "\u9ed8\u8ba4 2\uff0c\u8303\u56f4 1-4");
        _compressionAlgorithmParams.Children.Clear();
        _compressionAlgorithmParams.Children.Add(_compressionZstdLevelField);
        _compressionAlgorithmParams.Children.Add(_compressionZstdWindowLogField);
        _compressionAlgorithmParams.Children.Add(_compressionLz4HcField);
        _compressionAlgorithmParams.Children.Add(_compressionZlibField);
        _compressionAlgorithmParams.Children.Add(_compressionBZip2Field);
        _compressionPreprocessorParams.Children.Clear();
        _compressionPreprocessorParams.Children.Add(_compressionLpcField);
        UpdateCompressionParameterVisibility();

        return new Border
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Spacing = 12,
                Children = { switches, options, _compressionAlgorithmParams, _compressionPreprocessorParams }
            }
        };
    }

    private Control BuildRuntimeParametersPanel()
    {
        Control[] parameters =
        {
            RuntimeParameter("\u5206\u6587\u4ef6\u5927\u5c0f", FormatFileSplitSize()),
            RuntimeParameter("\u5237\u76d8\u95f4\u9694", $"{Math.Clamp(_settings.Storage.FlushIntervalMs, 100, 1000)} \u6beb\u79d2"),
            RuntimeParameter("\u805a\u5408\u5757\u5927\u5c0f", $"{Math.Clamp(_settings.Storage.Compression.ChunkSizeMb, 1, 256)} MB"),
            RuntimeParameter("TDMS \u5bfc\u51fa\u5e93", _settings.Storage.TdmRuntimeDir),
            RuntimeParameter("\u539f\u59cb\u5927\u5c0f", _storageRawSizeValue),
            RuntimeParameter("\u5199\u5165\u5927\u5c0f", _storageWrittenSizeValue),
            RuntimeParameter("\u8f7d\u8377\u5927\u5c0f", _storagePayloadSizeValue),
            RuntimeParameter("\u538b\u7f29\u500d\u7387", _storageRatioValue),
            RuntimeParameter("\u5757\u72b6\u6001", _storageBlockStateValue),
            RuntimeParameter("\u5199\u5165\u541e\u5410", _storageWriteThroughputValue),
            RuntimeParameter("\u65ad\u7535\u4fdd\u62a4", _storageDurabilityValue),
            RuntimeParameter("\u4fdd\u62a4\u7a97\u53e3", "< 2 \u79d2"),
            RuntimeParameter("\u5f53\u524d\u7b97\u6cd5", _storageCodecValue),
            RuntimeParameter("\u5f53\u524d\u9884\u5904\u7406", _storagePreprocessorValue)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 16,
            RowSpacing = 10
        };

        for (int index = 0; index < parameters.Length; index++)
        {
            if (index % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Control parameter = parameters[index];
            Grid.SetRow(parameter, index / 2);
            Grid.SetColumn(parameter, index % 2);
            grid.Children.Add(parameter);
        }

        return grid;
    }

    private static Control RuntimeParameter(string label, string value)
    {
        return RuntimeParameter(label, new TextBlock { Text = value });
    }

    private static Control RuntimeParameter(string label, TextBlock valueBlock)
    {
        valueBlock.Foreground = TextPrimary;
        valueBlock.FontSize = 13;
        valueBlock.TextWrapping = TextWrapping.Wrap;
        valueBlock.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("112,*"),
            ColumnSpacing = 10,
            MinHeight = FieldMinHeight,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = TextSecondary,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                },
                valueBlock
            }
        };
        Grid.SetColumn(valueBlock, 1);
        return grid;
    }

    private static Control StorageModule(string title, Control content)
    {
        return new Border
        {
            Padding = new Thickness(14),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = TextPrimary,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold
                    },
                    content
                }
            }
        };
    }

    private static Control StorageField(string label, Control editor)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = TextSecondary,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("104,*"),
            ColumnSpacing = 10,
            MinHeight = FieldMinHeight,
            Children =
            {
                labelBlock,
                editor
            }
        };
        Grid.SetColumn(editor, 1);
        return grid;
    }

    private static Control StorageValue(string label, string value)
    {
        return StorageField(label, new TextBlock
        {
            Text = value,
            Foreground = TextPrimary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    private static Control CompressionField(string label, Control editor)
    {
        var block = FieldBlock(label, editor);
        block.Margin = new Thickness(0, 0, 16, 12);
        return block;
    }

    private static Control SliderField(string label, Slider slider, TextBlock valueBlock, string hint)
    {
        valueBlock.Width = 42;
        valueBlock.Foreground = TextPrimary;
        valueBlock.FontSize = 13;
        valueBlock.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            Children = { slider, valueBlock }
        };
        Grid.SetColumn(valueBlock, 1);

        return new StackPanel
        {
            Width = 240,
            Margin = new Thickness(0, 0, 16, 12),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, Foreground = TextPrimary, FontSize = 13, FontWeight = FontWeight.SemiBold },
                row,
                new TextBlock { Text = hint, Foreground = TextSecondary, FontSize = 11 }
            }
        };
    }

    private string FormatFileSplitSize()
    {
        if (_settings.Storage.FileSplitMb > 0)
        {
            return $"{_settings.Storage.FileSplitMb} MB";
        }

        return $"{Math.Max(1, _settings.Storage.FileSplitGb)} GB";
    }

    private Control BuildStatusBar()
    {
        _metrics.Margin = new Thickness(16, 7);
        _metrics.Foreground = TextSecondary;
        _metrics.FontSize = 13;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 24, 33)),
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _metrics
        };
    }

    private async Task BrowseStorageFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "\u9009\u62e9\u6570\u636e\u4fdd\u5b58\u76ee\u5f55"
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _storagePath.Text = path;
        }
    }

    private async Task ConnectAsync()
    {
        SetButtons(connect: false, start: false, stop: false);
        _status.Text = "\u6b63\u5728\u8fde\u63a5";
        try
        {
            await _acquisition.ConnectAsync(CancellationToken.None);
            RefreshDevicesFromAcquisition(seedDefault: true);
            _startButton.IsEnabled = _acquisition.Devices.Count > 0;
            _connectButton.IsEnabled = true;
            _status.Text = _acquisition.Devices.Count > 0 ? "\u5df2\u8fde\u63a5" : "\u672a\u53d1\u73b0\u8bbe\u5907";
        }
        catch (Exception ex)
        {
            _status.Text = "\u8fde\u63a5\u5931\u8d25";
            await ShowConnectionFailureAsync(ex);
            SetButtons(connect: true, start: false, stop: false);
        }
    }

    private void RefreshDevicesFromAcquisition(bool seedDefault)
    {
        if (seedDefault)
        {
            SeedDefaultMonitorSelection();
        }

        ApplyMonitorSelectionsToStore();
        RebuildSelectionTree();
        RebuildStorageChannelTree();
        RefreshDeviceInfoPanel();
    }

    private async Task ShowConnectionFailureAsync(Exception ex)
    {
        string message = _settings.Acquisition.Source == AcquisitionSourceMode.RemoteDataSource
            ? "\u8fdc\u7a0b\u6570\u636e\u6e90\u8fde\u63a5\u5931\u8d25\u3002\n\n" +
              $"\u9519\u8bef: {ex.Message}\n\n" +
              $"Host: {_settings.DataSource.Host}\n" +
              $"Port: {_settings.DataSource.Port}\n\n" +
              "\u8bf7\u5148\u542f\u52a8 SyntheticDataSource\uff0c\u5e76\u786e\u8ba4 Host/Port \u4e0e appsettings.json \u4e00\u81f4\u3002"
            : "\u8bbe\u5907\u8fde\u63a5\u5931\u8d25\u3002\n\n" +
              $"\u9519\u8bef: {ex.Message}\n\n" +
              $"DashRoot: {_settings.Sdk.DashRoot}\n" +
              $"ConfigDir: {_settings.Sdk.ConfigDir}\n\n" +
              "\u8bf7\u786e\u8ba4 DASH \u76ee\u5f55\u3001ConfigDir\u3001Serial \u914d\u7f6e\u3001\u8bbe\u5907\u7535\u6e90\u548c\u7f51\u7edc\u8fde\u63a5\u3002";

        await new Window
        {
            Title = "\u8fde\u63a5\u5931\u8d25",
            Width = 640,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(18),
                Background = Brushes.White,
                Child = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TextPrimary,
                    FontSize = 14
                }
            }
        }.ShowDialog(this);
    }

    private async Task ShowStorageChannelSelectionRequiredAsync()
    {
        await new Window
        {
            Title = "\u5b58\u50a8\u901a\u9053\u672a\u9009\u62e9",
            Width = 520,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(18),
                Background = Brushes.White,
                Child = new TextBlock
                {
                    Text = "\u5f53\u524d\u6ca1\u6709\u5339\u914d\u5230\u53ef\u5199\u5165\u7684\u5b58\u50a8\u901a\u9053\u3002\n\n\u8bf7\u5728\u201c\u5b58\u50a8\u201d\u9875\u9762\u70b9\u51fb\u201c\u5168\u9009\u201d\uff0c\u6216\u8c03\u6574\u91c7\u6837\u7387\u533a\u95f4\uff0c\u786e\u4fdd\u81f3\u5c11\u6709\u4e00\u4e2a\u901a\u9053\u88ab\u7b5b\u9009\u540e\u518d\u5f00\u59cb\u91c7\u96c6\u3002",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TextPrimary,
                    FontSize = 14
                }
            }
        }.ShowDialog(this);
    }

    private async Task StartAsync()
    {
        if (!_acquisition.IsConnected)
        {
            await ConnectAsync();
        }

        if (_acquisition.Devices.Count == 0)
        {
            return;
        }

        ApplyMonitorSelectionsToStore();
        ApplyStorageSettingsFromUi();
        NormalizeStorageSelectedKeys();
        bool storageEnabled = _settings.Storage.Enabled;
        IReadOnlyList<DeviceDescriptor> storageDevices = ResolveStorageDevices(_acquisition.Devices);
        if (storageEnabled && storageDevices.Sum(device => device.Channels.Count) == 0)
        {
            await ShowStorageChannelSelectionRequiredAsync();
            RebuildStorageChannelTree();
            return;
        }

        _acquisition.SetStorageEnabled(storageEnabled);
        _waveformStore.Clear();
        _lastStorageStats = null;
        _lastFaultMessage = null;
        UpdateStorageStatsFields(null);

        if (_storageService is not null)
        {
            await _storageService.DisposeAsync();
            _storageService = null;
        }

        if (storageEnabled)
        {
            _storageService = new TdmsStorageService(_acquisition, _settings.Storage);
            _storageService.Faulted += fault => Dispatcher.UIThread.Post(() => _status.Text = fault.Message);
            await _storageService.StartAsync(storageDevices, _acquisition.Devices, CancellationToken.None);
        }

        await _displayPipeline.StartAsync(CancellationToken.None);
        await _acquisition.StartAsync(CancellationToken.None);
        StartCaptureTimer();
        _captureUiRunning = true;
        SetButtons(connect: false, start: false, stop: true);
        _storageEnabledCheck.IsEnabled = false;
        _storageTabEnabledCheck.IsEnabled = false;
        UpdateStorageChannelControlState();
        UpdateStorageChannelSummary();
        _status.Text = storageEnabled
            ? (_settings.Storage.Compression.Enabled && _settings.Storage.Compression.Algorithm != CompressionAlgorithm.None ? "\u91c7\u96c6\u4e2d\uff0c\u6b63\u5728\u5199\u5165\u538b\u7f29 .dhcap" : "\u91c7\u96c6\u4e2d\uff0c\u6b63\u5728\u5199\u5165 Codec=None \u539f\u59cb .dhcap")
            : "\u91c7\u96c6\u4e2d\uff0c\u4ec5\u663e\u793a\u4e0d\u4fdd\u5b58";
    }

    private async Task StopAsync()
    {
        await CompleteCaptureStopAsync(requestAcquisitionStop: true, null);
    }

    private async Task CompleteCaptureStopAsync(bool requestAcquisitionStop, string? stopReason)
    {
        _stopButton.IsEnabled = false;
        if (_captureCleanupInProgress)
        {
            while (_captureCleanupInProgress)
            {
                await Task.Delay(50);
            }

            return;
        }

        _captureCleanupInProgress = true;
        StopCaptureTimer();
        try
        {
            if (requestAcquisitionStop && _acquisition.IsRunning)
            {
                await _acquisition.StopAsync(CancellationToken.None);
            }

            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while ((_acquisition.GetTelemetry().StorageQueueDepth > 0 || _acquisition.GetTelemetry().DisplayQueueDepth > 0) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            await _displayPipeline.StopAsync();
            if (_storageService is not null)
            {
                await _storageService.StopAsync();
                _lastStorageStats = _storageService.GetStatistics();
                UpdateStorageStatsFields(_lastStorageStats);
                _storageService = null;
            }
            _acquisition.ReleaseQueuedBlocks();

            SetButtons(connect: true, start: _acquisition.Devices.Count > 0, stop: false);
            _storageEnabledCheck.IsEnabled = true;
            _storageTabEnabledCheck.IsEnabled = true;
            _captureUiRunning = false;
            if (_acquisition.Devices.Count > 0)
            {
                string? reason = string.IsNullOrWhiteSpace(stopReason) ? _lastFaultMessage : stopReason;
                _status.Text = string.IsNullOrWhiteSpace(reason) || string.Equals(reason, "Stopped", StringComparison.OrdinalIgnoreCase)
                    ? "\u5df2\u505c\u6b62"
                    : $"\u5df2\u505c\u6b62\uff1a{reason}";
            }
        }
        finally
        {
            _captureCleanupInProgress = false;
            UpdateStorageChannelControlState();
            UpdateStorageChannelSummary();
        }
    }

    private void StartCaptureTimer()
    {
        _captureStartedAt = DateTimeOffset.Now;
        _captureTimerText.Text = FormatCaptureElapsed(TimeSpan.Zero);
        _captureTimer.Start();
    }

    private void StopCaptureTimer()
    {
        if (_captureTimer.IsEnabled)
        {
            UpdateCaptureTimerText();
        }

        _captureTimer.Stop();
    }

    private void ResetCaptureTimer()
    {
        _captureTimer.Stop();
        _captureTimerText.Text = FormatCaptureElapsed(TimeSpan.Zero);
    }

    private void UpdateCaptureTimerText()
    {
        _captureTimerText.Text = FormatCaptureElapsed(DateTimeOffset.Now - _captureStartedAt);
    }

    private static string FormatCaptureElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        int hours = (int)elapsed.TotalHours;
        return $"\u91c7\u96c6\u65f6\u957f {hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void LoadMonitorViewsFromSettings()
    {
        foreach (MonitorViewSettings viewSettings in _settings.Display.Views.Take(MaxMonitorViews))
        {
            AddMonitorView(viewSettings);
        }

        _monitorViewsLoadedFromSettings = _monitorViews.Count > 0;
        if (_monitorViews.Count > 0)
        {
            int firstVisible = _monitorViews.FindIndex(view => view.Visible);
            _activeViewIndex = firstVisible >= 0 ? firstVisible : 0;
        }
    }

    private void AddMonitorView(MonitorViewSettings? settings = null)
    {
        var waveform = new WaveformControl
        {
            Store = _waveformStore,
            WindowSeconds = _settings.Display.WindowSeconds,
            DefaultYAxisAmplitude = _settings.Display.DefaultYAxisAmplitude
        };

        var title = new TextBlock
        {
            Foreground = TextSecondary,
            FontSize = 12,
            Margin = new Thickness(8, 6, 8, 0),
            TextWrapping = TextWrapping.NoWrap
        };

        var host = new Border
        {
            Margin = new Thickness(5),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = new DockPanel
            {
                Children =
                {
                    title,
                    waveform
                }
            }
        };
        DockPanel.SetDock(title, Dock.Top);

        MonitorViewState? view = null;
        var renderLoop = new MonitorViewRenderLoop(
            waveform,
            MonitorFrameInterval(),
            () => view?.Visible == true,
            TrackDisplayFrame);

        string name = string.IsNullOrWhiteSpace(settings?.Name)
            ? DefaultMonitorViewName(_monitorViews.Count)
            : settings.Name.Trim();
        view = new MonitorViewState(waveform, host, title, renderLoop)
        {
            Name = name,
            Visible = settings?.Visible ?? true
        };
        foreach (MonitorChannelSettings channel in settings?.Channels ?? Enumerable.Empty<MonitorChannelSettings>())
        {
            if (!string.IsNullOrWhiteSpace(channel.DeviceIp))
            {
                view.SelectedKeys.Add(new ChannelKey(channel.DeviceIp, channel.DeviceId, channel.ChannelId));
            }
        }

        host.PointerPressed += (_, _) => SelectMonitorView(_monitorViews.IndexOf(view), showSelection: true);
        _monitorViews.Add(view);
        RefreshViewChannels(view);
    }

    private void SelectMonitorView(int index, bool showSelection = false)
    {
        if (index < 0 || index >= _monitorViews.Count)
        {
            return;
        }

        _activeViewIndex = index;
        RebuildViewNav();
        RebuildSelectionTree();
        UpdateViewSelectionChrome();
        if (showSelection)
        {
            ShowSelectionDrawer();
        }
    }

    private void RebuildViewNav()
    {
        _viewNavPanel.Children.Clear();
        for (int i = 0; i < _monitorViews.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = $"V{index + 1}",
                Padding = new Thickness(10, 5),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 13,
                Background = index == _activeViewIndex ? AccentBlue : PanelBackground2,
                Foreground = index == _activeViewIndex ? Brushes.White : TextPrimary,
                BorderBrush = BorderBrushSoft
            };
            button.Click += (_, _) => SelectMonitorView(index, showSelection: true);
            _viewNavPanel.Children.Add(button);
        }

        _addViewButton.IsEnabled = _monitorViews.Count < MaxMonitorViews;
        _removeViewButton.IsEnabled = _monitorViews.Count > 1;
        UpdateActiveViewText();
        UpdateViewSelectionChrome();
    }

    private void RebuildMonitorGrid()
    {
        _monitorGrid.Children.Clear();
        _monitorGrid.RowDefinitions.Clear();
        _monitorGrid.ColumnDefinitions.Clear();

        var visibleViews = _monitorViews
            .Select((view, index) => new { View = view, Index = index })
            .Where(item => item.View.Visible)
            .ToArray();

        if (visibleViews.Length == 0)
        {
            _monitorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star) { MinHeight = 360 });
            _monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _monitorGrid.Children.Add(new TextBlock
            {
                Text = "\u6682\u65e0\u53ef\u89c1\u89c6\u56fe",
                Foreground = TextSecondary,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            UpdateViewSelectionChrome();
            return;
        }

        int count = visibleViews.Length;
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)columns);

        for (int i = 0; i < rows; i++)
        {
            _monitorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star) { MinHeight = 220 });
        }

        for (int i = 0; i < columns; i++)
        {
            _monitorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 260 });
        }

        for (int i = 0; i < visibleViews.Length; i++)
        {
            Border host = visibleViews[i].View.Host;
            Grid.SetRow(host, i / columns);
            Grid.SetColumn(host, i % columns);
            _monitorGrid.Children.Add(host);
        }

        UpdateViewSelectionChrome();
    }

    private void UpdateViewSelectionChrome()
    {
        for (int i = 0; i < _monitorViews.Count; i++)
        {
            _monitorViews[i].Host.BorderBrush = i == _activeViewIndex ? AccentBlue : BorderBrushSoft;
            _monitorViews[i].Host.BorderThickness = new Thickness(i == _activeViewIndex ? 2 : 1);
        }
    }

    private void SeedDefaultMonitorSelection()
    {
        if (_monitorViewsLoadedFromSettings || _acquisition.Devices.Count == 0 || _monitorViews.Count == 0 || _monitorViews.Any(view => view.SelectedKeys.Count > 0))
        {
            return;
        }

        MonitorViewState firstView = _monitorViews[0];
        foreach (ChannelDescriptor channel in _acquisition.Devices[0].Channels.Take(Math.Min(4, MaxChannelsPerMonitorView)))
        {
            firstView.SelectedKeys.Add(new ChannelKey(channel));
        }

        RefreshViewChannels(firstView);
        ApplyMonitorSelectionsToStore();
        PersistMonitorViewSettings();
    }

    private void SetAllChannelsForActiveView(bool selected)
    {
        if (_monitorViews.Count == 0)
        {
            return;
        }

        MonitorViewState view = _monitorViews[_activeViewIndex];
        if (!selected)
        {
            view.SelectedKeys.Clear();
            CommitMonitorSelectionChange(view);
            return;
        }

        foreach (ChannelDescriptor channel in _acquisition.Devices.SelectMany(device => device.Channels))
        {
            if (!TrySetChannelForView(view, channel, selected: true))
            {
                break;
            }
        }

        CommitMonitorSelectionChange(view);
    }

    private void SetDeviceChannelsForActiveView(DeviceDescriptor device, bool selected)
    {
        if (_monitorViews.Count == 0)
        {
            return;
        }

        MonitorViewState view = _monitorViews[_activeViewIndex];
        foreach (ChannelDescriptor channel in device.Channels)
        {
            if (!TrySetChannelForView(view, channel, selected))
            {
                break;
            }
        }

        CommitMonitorSelectionChange(view);
    }

    private bool TrySetChannelForActiveView(ChannelDescriptor channel, bool selected)
    {
        return _monitorViews.Count > 0 && TrySetChannelForView(_monitorViews[_activeViewIndex], channel, selected);
    }

    private bool TrySetChannelForView(MonitorViewState view, ChannelDescriptor channel, bool selected)
    {
        ChannelKey key = new(channel);
        if (selected)
        {
            if (view.SelectedKeys.Count >= MaxChannelsPerMonitorView && !view.SelectedKeys.Contains(key))
            {
                _selectionHint.Text = $"\u5355\u4e2a\u89c6\u56fe\u6700\u591a\u652f\u6301 {MaxChannelsPerMonitorView} \u4e2a\u901a\u9053";
                return false;
            }

            view.SelectedKeys.Add(key);
            return true;
        }

        view.SelectedKeys.Remove(key);
        return true;
    }

    private void CommitMonitorSelectionChange(MonitorViewState view)
    {
        RefreshViewChannels(view);
        ApplyMonitorSelectionsToStore();
        RebuildSelectionTree();
        PersistMonitorViewSettings();
    }

    private async Task RemoveActiveMonitorViewAsync()
    {
        if (_monitorViews.Count <= 1)
        {
            return;
        }

        MonitorViewState removed = _monitorViews[_activeViewIndex];
        _monitorViews.RemoveAt(_activeViewIndex);
        _activeViewIndex = Math.Clamp(_activeViewIndex, 0, _monitorViews.Count - 1);
        RebuildMonitorGrid();
        RebuildViewNav();
        RebuildSelectionTree();
        ApplyMonitorSelectionsToStore();
        PersistMonitorViewSettings();
        await removed.DisposeAsync();
    }

    private async Task DisposeMonitorViewsAsync()
    {
        foreach (MonitorViewState view in _monitorViews.ToArray())
        {
            await view.DisposeAsync();
        }
    }

    private void ApplyMonitorSelectionsToStore()
    {
        foreach (MonitorViewState view in _monitorViews)
        {
            RefreshViewChannels(view);
        }

        ChannelDescriptor[] union = _monitorViews
            .Where(view => view.Visible)
            .SelectMany(view => view.Channels)
            .GroupBy(channel => new ChannelKey(channel))
            .Select(group => group.First())
            .ToArray();

        _waveformStore.SetCapacity(DisplayCapacity());
        _waveformStore.SetVisibleChannels(union);
        UpdateActiveViewText();
    }

    private int DisplayCapacity()
    {
        int pointsPerSecond = Math.Max(1, _settings.Display.MaxDisplayPointsPerSecond);
        int seconds = Math.Max(1, _settings.Display.WindowSeconds);
        return Math.Max(1000, pointsPerSecond * seconds);
    }

    private TimeSpan MonitorFrameInterval()
    {
        double milliseconds = Math.Max(1.0, 1000.0 / Math.Max(1, _settings.Display.TargetFps));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private void TrackDisplayFrame()
    {
        Interlocked.Increment(ref _displayFrameCounter);
    }

    private void RefreshViewChannels(MonitorViewState view)
    {
        Dictionary<ChannelKey, ChannelDescriptor> exactLookup = _acquisition.Devices
            .SelectMany(device => device.Channels)
            .GroupBy(channel => new ChannelKey(channel))
            .ToDictionary(group => group.Key, group => group.First());
        Dictionary<(int DeviceId, int ChannelId), ChannelDescriptor> fallbackLookup = _acquisition.Devices
            .SelectMany(device => device.Channels)
            .GroupBy(channel => (channel.DeviceId, channel.ChannelId))
            .ToDictionary(group => group.Key, group => group.First());

        var channels = new List<ChannelDescriptor>();
        var normalizedKeys = new HashSet<ChannelKey>();
        foreach (ChannelKey selectedKey in view.SelectedKeys.Take(MaxChannelsPerMonitorView).ToArray())
        {
            if (exactLookup.TryGetValue(selectedKey, out ChannelDescriptor? channel) ||
                fallbackLookup.TryGetValue((selectedKey.DeviceId, selectedKey.ChannelId), out channel))
            {
                ChannelKey normalizedKey = new(channel);
                if (normalizedKeys.Add(normalizedKey))
                {
                    channels.Add(channel);
                }
            }
            else
            {
                normalizedKeys.Add(selectedKey);
            }
        }

        view.SelectedKeys.Clear();
        foreach (ChannelKey key in normalizedKeys)
        {
            view.SelectedKeys.Add(key);
        }

        view.Channels = channels;
        int viewIndex = _monitorViews.IndexOf(view);
        view.Name = string.IsNullOrWhiteSpace(view.Name) ? DefaultMonitorViewName(viewIndex) : view.Name;
        view.Title.Text = $"{view.Name}    \u663e\u793a {view.Channels.Count} / \u5df2\u9009 {view.SelectedKeys.Count} \u901a\u9053";
        view.Waveform.Channels = view.Channels;
    }

    private void UpdateActiveViewText()
    {
        if (_monitorViews.Count == 0)
        {
            _activeViewText.Text = string.Empty;
            return;
        }

        MonitorViewState view = _monitorViews[_activeViewIndex];
        _activeViewText.Text = $"{view.Name}\n{_activeViewIndex + 1}/{_monitorViews.Count}\n{view.SelectedKeys.Count}/{MaxChannelsPerMonitorView} \u901a\u9053";
    }

    private void PersistMonitorViewSettings()
    {
        try
        {
            List<MonitorViewSettings> views = _monitorViews
                .Select((view, index) => new MonitorViewSettings
                {
                    Name = string.IsNullOrWhiteSpace(view.Name) ? DefaultMonitorViewName(index) : view.Name,
                    Visible = view.Visible,
                    Channels = view.SelectedKeys
                        .OrderBy(key => key.DeviceId)
                        .ThenBy(key => key.ChannelId)
                        .ThenBy(key => key.DeviceIp, StringComparer.OrdinalIgnoreCase)
                        .Select(key => new MonitorChannelSettings
                        {
                            DeviceIp = key.DeviceIp,
                            DeviceId = key.DeviceId,
                            ChannelId = key.ChannelId
                        })
                        .ToList()
                })
                .ToList();
            _settings.Display.Views = views;
            AppSettingsLoader.SaveDisplayViews(views);
        }
        catch (Exception ex)
        {
            _status.Text = $"\u89c6\u56fe\u914d\u7f6e\u4fdd\u5b58\u5931\u8d25\uff1a{ex.Message}";
        }
    }

    private void RefreshDeviceInfoPanel()
    {
        _deviceInfoPanel.Children.Clear();
        if (_acquisition.Devices.Count == 0)
        {
            _deviceInfoPanel.Children.Add(new TextBlock
            {
                Text = "\u672a\u8fde\u63a5\u8bbe\u5907",
                Foreground = TextSecondary,
                FontSize = 16,
                Margin = new Thickness(4)
            });
            return;
        }

        int index = 0;
        foreach (DeviceDescriptor device in _acquisition.Devices)
        {
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(new TextBlock
            {
                Text = FormatDeviceName(device, index + 1),
                Foreground = TextPrimary,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold
            });
            card.Children.Add(new TextBlock
            {
                Text = $"\u91c7\u6837\u7387 {device.SampleRate:0.##} Hz    \u901a\u9053 {device.Channels.Count}    \u72b6\u6001 {(device.Online ? "\u5728\u7ebf" : "\u79bb\u7ebf")}",
                Foreground = TextSecondary,
                FontSize = 14
            });

            var wrap = new WrapPanel();
            foreach (ChannelDescriptor channel in device.Channels.Take(64))
            {
                wrap.Children.Add(new Border
                {
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(9, 5),
                    Background = ChannelBrush(channel.ChannelId),
                    CornerRadius = new CornerRadius(14),
                    Child = new TextBlock
                    {
                        Text = channel.Name,
                        FontSize = 13,
                        Foreground = Brushes.White
                    }
                });
            }

            if (device.Channels.Count > 64)
            {
                wrap.Children.Add(new TextBlock
                {
                    Text = $"+{device.Channels.Count - 64}",
                    Foreground = TextSecondary,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            card.Children.Add(wrap);
            _deviceInfoPanel.Children.Add(new Border
            {
                Padding = new Thickness(14),
                Background = index % 2 == 0 ? PanelBackground : PanelBackground2,
                BorderBrush = BorderBrushSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = card
            });
            index++;
        }
    }

    private void UpdateSelectedChannels()
    {
        ApplyMonitorSelectionsToStore();
    }

    private void UpdateTelemetry(CaptureTelemetry telemetry)
    {
        double mb = telemetry.BytesReceived / 1024.0 / 1024.0;
        CaptureStorageStatistics? storageStats = _storageService?.GetStatistics() ?? _lastStorageStats;
        if (_storageService is not null && storageStats is not null)
        {
            _lastStorageStats = storageStats;
        }

        _metrics.Text = $"Blocks {telemetry.BlocksReceived}    Data {mb:0.0} MB    StorageQ {telemetry.StorageQueueDepth}    DisplayQ {telemetry.DisplayQueueDepth}    Drops {telemetry.DisplayDrops}    {telemetry.BackpressureLevel}    {FormatStorageStatsShort(storageStats)}";
        UpdateStorageStatsFields(storageStats);
        if (!string.IsNullOrWhiteSpace(telemetry.Status))
        {
            _status.Text = TranslateStatus(telemetry.Status);
        }

        if (_captureUiRunning && !_captureCleanupInProgress && !_acquisition.IsRunning)
        {
            string? reason = string.Equals(telemetry.Status, "Stopped", StringComparison.OrdinalIgnoreCase)
                ? _lastFaultMessage
                : telemetry.Status;
            _ = CompleteCaptureStopAsync(requestAcquisitionStop: false, reason);
        }
    }

    private void UpdateRuntimeTitle()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        double elapsedSeconds = Math.Max(0.001, (now - _lastRuntimeStatsAt).TotalSeconds);
        int displayFrames = Interlocked.Exchange(ref _displayFrameCounter, 0);
        double displayFps = displayFrames / elapsedSeconds;
        _lastRuntimeStatsAt = now;

        RuntimeUsageSnapshot usage = _runtimeUsageSampler.Sample();
        Title = $"DASH Capture | FPS {displayFps:0.0} | CPU App {FormatPercent(usage.ProcessCpuPercent)} Sys {FormatPercent(usage.SystemCpuPercent)} | {FormatGpuUsage(usage)}";
        CaptureStorageStatistics? storageStats = _storageService?.GetStatistics() ?? _lastStorageStats;
        if (_storageService is not null && storageStats is not null)
        {
            _lastStorageStats = storageStats;
        }

        UpdateStorageStatsFields(storageStats);
    }

    private static string FormatStorageStatsShort(CaptureStorageStatistics? stats)
    {
        if (stats is null || stats.RawBytes <= 0)
        {
            return "\u5b58\u50a8\u5f85\u91c7\u96c6";
        }

        double ratio = stats.WrittenBytes > 0 ? (double)stats.RawBytes / stats.WrittenBytes : 0;
        return $"\u7f16\u7801 {stats.Codec}/{stats.Preprocessor}    \u538b\u7f29 {ratio:0.00}x    \u5199\u5165 {stats.WriteThroughputMbPerSecond:0.0} MB/s";
    }

    private void UpdateStorageStatsFields(CaptureStorageStatistics? stats)
    {
        if (stats is null || stats.RawBytes <= 0)
        {
            _storageRawSizeValue.Text = "0.0 MB";
            _storageWrittenSizeValue.Text = "0.0 MB";
            _storagePayloadSizeValue.Text = "0.0 MB";
            _storageRatioValue.Text = "\u5f85\u91c7\u96c6";
            _storageBlockStateValue.Text = "\u5f85\u91c7\u96c6";
            _storageWriteThroughputValue.Text = "0.0 MB/s";
            _storageDurabilityValue.Text = "\u5f85\u91c7\u96c6";
            _storageCodecValue.Text = "\u5f85\u91c7\u96c6";
            _storagePreprocessorValue.Text = "\u5f85\u91c7\u96c6";
            return;
        }

        double ratio = stats.WrittenBytes > 0 ? (double)stats.RawBytes / stats.WrittenBytes : 0;
        double storedPercent = stats.TotalBlocks > 0 ? stats.StoredBlocks * 100.0 / stats.TotalBlocks : 0;
        double compressedPercent = stats.TotalBlocks > 0 ? stats.CompressedBlocks * 100.0 / stats.TotalBlocks : 0;
        _storageRawSizeValue.Text = FormatStorageBytes(stats.RawBytes);
        _storageWrittenSizeValue.Text = FormatStorageBytes(stats.WrittenBytes);
        _storagePayloadSizeValue.Text = FormatStorageBytes(stats.PayloadBytes);
        _storageRatioValue.Text = $"{ratio:0.00}x";
        _storageBlockStateValue.Text = $"\u603b {stats.TotalBlocks}    \u538b\u7f29 {compressedPercent:0.#}%    \u76f4\u5b58 {storedPercent:0.#}%    \u961f\u5217 {stats.CompressionQueueDepth}/{stats.WriteQueueDepth}    \u7ebf\u7a0b {stats.CompressionWorkerCount}";
        _storageWriteThroughputValue.Text = $"{stats.WriteThroughputMbPerSecond:0.0} MB/s";
        _storageDurabilityValue.Text = $"\u5df2\u843d\u76d8 {FormatStorageBytes(stats.DurableRawBytes)}    \u6ede\u540e {stats.DurableLagSeconds:0.0} s";
        _storageCodecValue.Text = stats.Codec;
        _storagePreprocessorValue.Text = stats.Preprocessor;
    }

    private static string FormatStorageBytes(long bytes)
    {
        return $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }

    private static string FormatGpuUsage(RuntimeUsageSnapshot usage)
    {
        if (!usage.GpuTotalPercent.HasValue)
        {
            return "GPU N/A";
        }

        string engines = FormatGpuEngines(usage.GpuEngines);
        string suffix = string.IsNullOrWhiteSpace(engines) ? string.Empty : $" ({engines})";
        return $"GPU App {FormatPercent(usage.GpuProcessPercent)} Sys {FormatPercent(usage.GpuTotalPercent)}{suffix}";
    }

    private static string FormatGpuEngines(IReadOnlyList<GpuEngineUsage> engines)
    {
        string[] priority = { "3D", "Compute", "Copy", "VideoDecode", "VideoEncode" };
        var selected = new List<GpuEngineUsage>();
        foreach (string engine in priority)
        {
            GpuEngineUsage? item = engines.FirstOrDefault(value => value.Engine.Equals(engine, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                selected.Add(item);
            }
        }

        foreach (GpuEngineUsage item in engines.Where(item => selected.All(selectedItem => !selectedItem.Engine.Equals(item.Engine, StringComparison.OrdinalIgnoreCase))))
        {
            if (selected.Count >= 4)
            {
                break;
            }

            selected.Add(item);
        }

        return string.Join(" ", selected
            .Where(item => item.TotalPercent >= 0.05 || item.ProcessPercent >= 0.05)
            .Take(4)
            .Select(item => $"{ShortGpuEngineName(item.Engine)} {item.TotalPercent:0.#}%"));
    }

    private static string ShortGpuEngineName(string engine)
    {
        return engine switch
        {
            "VideoDecode" => "VDec",
            "VideoEncode" => "VEnc",
            "VideoProcessing" => "VProc",
            _ => engine
        };
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%"
            : "N/A";
    }

    private static double? TryParseDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

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

    private static string FormatNullableSampleRate(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private void ApplyStorageSettingsFromUi()
    {
        _settings.Storage.RootPath = string.IsNullOrWhiteSpace(_storagePath.Text) ? _settings.Storage.RootPath : _storagePath.Text.Trim();
        _settings.Storage.Enabled = _storageEnabledCheck.IsChecked == true;
        _settings.Storage.NamingMode = _namingMode.SelectedIndex == 1 ? FileNamingMode.Custom : FileNamingMode.Time;
        _settings.Storage.CustomFileName = string.IsNullOrWhiteSpace(_customFileName.Text) ? "DashCapture" : _customFileName.Text.Trim();
        _settings.Storage.ChannelSelection.SampleRateMinHz = TryParseDouble(_storageSampleRateMin.Text);
        _settings.Storage.ChannelSelection.SampleRateMaxHz = TryParseDouble(_storageSampleRateMax.Text);
        CompressionSettings compression = _settings.Storage.Compression;
        compression.Enabled = _compressionEnabledCheck.IsChecked == true;
        compression.Algorithm = SelectedValue(_compressionAlgorithmCombo, compression.Algorithm);
        compression.Preprocessor = SelectedValue(_compressionPreprocessorCombo, compression.Preprocessor);
        compression.ZstdLevel = SliderInt(_compressionZstdLevel, -5, 22);
        compression.ZstdWindowLog = SliderInt(_compressionZstdWindowLog, 0, 31);
        compression.Lz4HcLevel = SliderInt(_compressionLz4HcLevel, 3, 12);
        compression.ZlibLevel = SliderInt(_compressionZlibLevel, 0, 9);
        compression.BZip2BlockSize = SliderInt(_compressionBZip2BlockSize, 1, 9);
        compression.LpcOrder = SliderInt(_compressionLpcOrder, 1, 4);
        UpdateStoragePreview();
    }

    private void UpdateStoragePreview()
    {
        bool custom = _namingMode.SelectedIndex == 1;
        _customFileName.IsEnabled = custom;
        string folder = string.IsNullOrWhiteSpace(_storagePath.Text) ? _settings.Storage.RootPath : _storagePath.Text.Trim();
        string baseName = string.IsNullOrWhiteSpace(_customFileName.Text) ? "DashCapture" : _customFileName.Text.Trim();
        const string extension = ".dhcap";
        string preview = custom
            ? $"{baseName}_0001{extension}\uff1b\u82e5\u91cd\u540d\u5219\u81ea\u52a8\u4f7f\u7528 {baseName}_001\\..."
            : $"DashCapture_yyyyMMdd_HHmmss_0001{extension}";
        _storagePreview.Text = $"\u4fdd\u5b58\u76ee\u5f55: {folder}\n\u6587\u4ef6\u540d: {preview}\n\u901a\u9053: {StorageChannelPreviewSummary()}\n\u538b\u7f29: {CompressionSummaryFromUi()}";
        _storagePreview.Foreground = TextPrimary;
        _storagePreview.FontSize = 14;
    }

    private string StorageChannelPreviewSummary()
    {
        int totalChannels = _acquisition.Devices.Sum(device => device.Channels.Count);
        if (_settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.AllChannels)
        {
            return totalChannels > 0 ? $"\u5168\u90e8 {totalChannels} \u901a\u9053" : "\u5168\u90e8\u901a\u9053";
        }

        int selectedChannels = ResolveStorageDevices(_acquisition.Devices).Sum(device => device.Channels.Count);
        if (_settings.Storage.ChannelSelection.Mode == StorageChannelSelectionMode.SampleRateRange)
        {
            return totalChannels > 0
                ? $"\u91c7\u6837\u7387 {StorageSampleRateRangeText()}\uff0c\u5339\u914d {selectedChannels}/{totalChannels} \u901a\u9053"
                : $"\u91c7\u6837\u7387 {StorageSampleRateRangeText()}";
        }

        return totalChannels > 0
            ? $"\u5df2\u6e05\u7a7a\uff0c\u4fdd\u5b58 0/{totalChannels} \u901a\u9053"
            : "\u5df2\u6e05\u7a7a";
    }

    private string CompressionSummaryFromUi()
    {
        if (_compressionEnabledCheck.IsChecked != true)
        {
            return ".dhcap Codec=None\uff0cPre=None\uff0c\u4fdd\u5b58\u539f\u59cb float32 \u5b57\u8282\uff0c\u53ef\u5bfc\u51fa TDMS";
        }

        if (SelectedValue(_compressionAlgorithmCombo, CompressionAlgorithm.Zstd) == CompressionAlgorithm.None)
        {
            return ".dhcap Codec=None\uff0cPre=None\uff0c\u4fdd\u5b58\u539f\u59cb float32 \u5b57\u8282";
        }

        string preprocessor = SelectedValue(_compressionPreprocessorCombo, CompressionPreprocessor.None) == CompressionPreprocessor.None
            ? "\u65e0\u9884\u5904\u7406"
            : SelectedLabel(_compressionPreprocessorCombo);
        return $".dhcap Codec={SelectedLabel(_compressionAlgorithmCombo)}\uff0cPre={preprocessor}\uff0c\u53ef\u5bfc\u51fa TDMS";
    }

    private void UpdateCompressionSliderTexts()
    {
        _compressionZstdLevelValue.Text = SliderInt(_compressionZstdLevel, -5, 22).ToString(CultureInfo.InvariantCulture);
        _compressionZstdWindowLogValue.Text = SliderInt(_compressionZstdWindowLog, 0, 31).ToString(CultureInfo.InvariantCulture);
        _compressionLz4HcLevelValue.Text = SliderInt(_compressionLz4HcLevel, 3, 12).ToString(CultureInfo.InvariantCulture);
        _compressionZlibLevelValue.Text = SliderInt(_compressionZlibLevel, 0, 9).ToString(CultureInfo.InvariantCulture);
        _compressionBZip2BlockSizeValue.Text = SliderInt(_compressionBZip2BlockSize, 1, 9).ToString(CultureInfo.InvariantCulture);
        _compressionLpcOrderValue.Text = SliderInt(_compressionLpcOrder, 1, 4).ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateCompressionParameterVisibility()
    {
        bool enabled = _compressionEnabledCheck.IsChecked == true;
        CompressionAlgorithm algorithm = SelectedValue(_compressionAlgorithmCombo, CompressionAlgorithm.Zstd);
        CompressionPreprocessor preprocessor = SelectedValue(_compressionPreprocessorCombo, CompressionPreprocessor.None);

        bool compressionActive = enabled && algorithm != CompressionAlgorithm.None;
        SetVisible(_compressionZstdLevelField, compressionActive && algorithm == CompressionAlgorithm.Zstd);
        SetVisible(_compressionZstdWindowLogField, compressionActive && algorithm == CompressionAlgorithm.Zstd);
        SetVisible(_compressionLz4HcField, compressionActive && algorithm == CompressionAlgorithm.Lz4Hc);
        SetVisible(_compressionZlibField, compressionActive && algorithm == CompressionAlgorithm.Zlib);
        SetVisible(_compressionBZip2Field, compressionActive && algorithm == CompressionAlgorithm.BZip2);
        SetVisible(_compressionLpcField, compressionActive && preprocessor == CompressionPreprocessor.Lpc);

        _compressionAlgorithmParams.IsVisible =
            compressionActive && (algorithm == CompressionAlgorithm.Zstd ||
                        algorithm == CompressionAlgorithm.Lz4Hc ||
                        algorithm == CompressionAlgorithm.Zlib ||
                        algorithm == CompressionAlgorithm.BZip2);
        _compressionPreprocessorParams.IsVisible = compressionActive && preprocessor == CompressionPreprocessor.Lpc;
    }

    private static void SetVisible(Control? control, bool visible)
    {
        if (control is not null)
        {
            control.IsVisible = visible;
        }
    }

    private static int SliderInt(Slider slider, int min, int max)
    {
        return Math.Clamp((int)Math.Round(slider.Value, MidpointRounding.AwayFromZero), min, max);
    }

    private static T SelectedValue<T>(ComboBox comboBox, T fallback)
    {
        return comboBox.SelectedItem is OptionItem<T> item ? item.Value : fallback;
    }

    private static string SelectedLabel(ComboBox comboBox)
    {
        return comboBox.SelectedItem?.ToString() ?? string.Empty;
    }

    private void SetButtons(bool connect, bool start, bool stop)
    {
        _connectButton.IsEnabled = connect;
        _startButton.IsEnabled = start;
        _stopButton.IsEnabled = stop;
    }

    private Control AddControlGroup(string label, Control control)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = TextSecondary,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                },
                control
            }
        };
    }

    private Control AddStatusPill()
    {
        return new Border
        {
            Padding = new Thickness(12, 7),
            Background = PanelBackground2,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\u72b6\u6001",
                        Foreground = TextSecondary,
                        FontSize = 13
                    },
                    _status
                }
            }
        };
    }

    private static TextBlock SectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TextPrimary,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        };
    }

    private static Control FieldBlock(string label, Control editor)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = TextSecondary,
                    FontSize = 14
                },
                editor
            }
        };
    }

    private static void StyleControlButton(Button button, IBrush background)
    {
        button.Background = background;
        button.Foreground = Brushes.White;
        button.Padding = new Thickness(14, 7);
        button.MinWidth = 88;
        button.MinHeight = ButtonMinHeight;
        button.FontSize = 14;
        button.FontWeight = FontWeight.SemiBold;
    }

    private static void StyleInput(TextBox textBox)
    {
        textBox.Background = Brushes.White;
        textBox.Foreground = TextPrimary;
        textBox.BorderBrush = BorderBrushSoft;
        textBox.FontSize = 14;
        textBox.Padding = new Thickness(10, 7);
        textBox.MinHeight = FieldMinHeight;
    }

    private static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.Background = Brushes.White;
        comboBox.Foreground = TextPrimary;
        comboBox.BorderBrush = BorderBrushSoft;
        comboBox.FontSize = 14;
        comboBox.Padding = new Thickness(8, 5);
        comboBox.MinHeight = FieldMinHeight;
    }

    private static IBrush ChannelBrush(int channelId)
    {
        Color[] colors =
        {
            Color.FromRgb(66, 133, 244),
            Color.FromRgb(52, 168, 83),
            Color.FromRgb(251, 188, 5),
            Color.FromRgb(234, 67, 53),
            Color.FromRgb(156, 102, 255),
            Color.FromRgb(0, 173, 181)
        };
        return new SolidColorBrush(colors[Math.Abs(channelId) % colors.Length]);
    }

    private static string TranslateStatus(string status)
    {
        return status switch
        {
            "Idle" => "\u7a7a\u95f2",
            "Sampling" => "\u91c7\u96c6\u4e2d",
            "Stopped" => "\u5df2\u505c\u6b62",
            var text when text.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) => "\u5df2\u8fde\u63a5",
            _ => status
        };
    }

    private static string FormatDeviceName(DeviceDescriptor device, int displayIndex)
    {
        return $"\u8bbe\u5907 {displayIndex}\uff08{device.IpAddress}\uff09";
    }

    private static string FormatChannelSelectionName(ChannelDescriptor channel)
    {
        string name = string.IsNullOrWhiteSpace(channel.Name)
            ? $"\u901a\u9053 {channel.ChannelId + 1}"
            : channel.Name;
        return $"{name}    {channel.SampleRate:0.##} Hz";
    }

    private static string DefaultMonitorViewName(int zeroBasedIndex)
    {
        return $"\u89c6\u56fe {zeroBasedIndex + 1}";
    }

    private sealed record OptionItem<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class MonitorViewRenderLoop : IAsyncDisposable
    {
        private readonly WaveformControl _waveform;
        private readonly TimeSpan _interval;
        private readonly Func<bool> _shouldRender;
        private readonly Action _frameTick;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;

        public MonitorViewRenderLoop(WaveformControl waveform, TimeSpan interval, Func<bool> shouldRender, Action frameTick)
        {
            _waveform = waveform;
            _interval = interval;
            _shouldRender = shouldRender;
            _frameTick = frameTick;
            _worker = Task.Factory.StartNew(
                () => RunAsync(_cts.Token),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cancellationToken.IsCancellationRequested || !_shouldRender())
                        {
                            return;
                        }

                        _frameTick();
                        _waveform.InvalidateVisual();
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    private sealed class MonitorViewState : IAsyncDisposable
    {
        public MonitorViewState(WaveformControl waveform, Border host, TextBlock title, MonitorViewRenderLoop renderLoop)
        {
            Waveform = waveform;
            Host = host;
            Title = title;
            RenderLoop = renderLoop;
        }

        public string Name { get; set; } = string.Empty;
        public bool Visible { get; set; } = true;
        public WaveformControl Waveform { get; }
        public Border Host { get; }
        public TextBlock Title { get; }
        public MonitorViewRenderLoop RenderLoop { get; }
        public HashSet<ChannelKey> SelectedKeys { get; } = new();
        public IReadOnlyList<ChannelDescriptor> Channels { get; set; } = Array.Empty<ChannelDescriptor>();

        public ValueTask DisposeAsync()
        {
            return RenderLoop.DisposeAsync();
        }
    }
}
