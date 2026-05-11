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

    private readonly ComboBox _deviceCombo = new();
    private readonly StackPanel _channelPanel = new();
    private readonly WrapPanel _viewNavPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _monitorGrid = new();
    private readonly Button _addViewButton = new() { Content = "+" };
    private readonly Button _removeViewButton = new() { Content = "-" };
    private readonly Button _selectAllChannelsButton = new() { Content = "\u5168\u9009" };
    private readonly Button _clearChannelsButton = new() { Content = "\u6e05\u7a7a" };
    private readonly TextBlock _activeViewText = new();
    private readonly StackPanel _deviceInfoPanel = new();
    private readonly TextBox _storagePath = new();
    private readonly TextBox _customFileName = new();
    private readonly ComboBox _namingMode = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _metrics = new();
    private readonly TextBlock _storagePreview = new();
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
    private readonly StackPanel _compressionAlgorithmParams = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private readonly StackPanel _compressionPreprocessorParams = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
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
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _captureTimer;
    private readonly DispatcherTimer _runtimeStatsTimer;
    private readonly RuntimeUsageSampler _runtimeUsageSampler = new();
    private readonly List<MonitorViewState> _monitorViews = new();
    private DateTimeOffset _captureStartedAt;
    private DateTimeOffset _lastRuntimeStatsAt = DateTimeOffset.UtcNow;
    private int _activeViewIndex;
    private int _displayFrameCounter;

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
        AddMonitorView();

        _storagePath.Text = _settings.Storage.RootPath;
        _customFileName.Text = _settings.Storage.CustomFileName;
        _storageEnabledCheck.IsChecked = _settings.Storage.Enabled;
        _storageTabEnabledCheck.IsChecked = _settings.Storage.Enabled;
        _namingMode.ItemsSource = new[] { "\u6309\u65f6\u95f4\u547d\u540d", "\u81ea\u5b9a\u4e49\u547d\u540d" };
        _namingMode.SelectedIndex = _settings.Storage.NamingMode == FileNamingMode.Time ? 0 : 1;
        InitializeCompressionControls();
        StyleInput(_storagePath);
        StyleInput(_customFileName);
        StyleComboBox(_deviceCombo);
        StyleComboBox(_namingMode);
        StyleComboBox(_compressionAlgorithmCombo);
        StyleComboBox(_compressionPreprocessorCombo);
        StyleControlButton(_addViewButton, AccentBlue);
        StyleControlButton(_removeViewButton, AccentRed);
        StyleControlButton(_selectAllChannelsButton, AccentBlue);
        StyleControlButton(_clearChannelsButton, AccentBlue);

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
        _deviceCombo.SelectionChanged += (_, _) => RebuildChannelList();
        _addViewButton.Click += (_, _) =>
        {
            if (_monitorViews.Count < MaxMonitorViews)
            {
                AddMonitorView();
                SelectMonitorView(_monitorViews.Count - 1);
                RebuildMonitorGrid();
                RebuildViewNav();
                ApplyMonitorSelectionsToStore();
            }
        };
        _removeViewButton.Click += (_, _) =>
        {
            if (_monitorViews.Count > 1)
            {
                _monitorViews.RemoveAt(_activeViewIndex);
                _activeViewIndex = Math.Clamp(_activeViewIndex, 0, _monitorViews.Count - 1);
                RebuildMonitorGrid();
                RebuildViewNav();
                RebuildChannelList();
                ApplyMonitorSelectionsToStore();
            }
        };
        _selectAllChannelsButton.Click += (_, _) => SetDeviceChannelsForActiveView(true);
        _clearChannelsButton.Click += (_, _) => SetDeviceChannelsForActiveView(false);
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

        _acquisition.Faulted += fault => Dispatcher.UIThread.Post(() => _status.Text = fault.Message);
        _acquisition.TelemetryUpdated += telemetry => Dispatcher.UIThread.Post(() => UpdateTelemetry(telemetry));

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _settings.Display.TargetFps))
        };
        _renderTimer.Tick += (_, _) =>
        {
            _displayFrameCounter++;
            foreach (MonitorViewState view in _monitorViews)
            {
                view.Waveform.InvalidateVisual();
            }
        };
        _renderTimer.Start();

        _runtimeStatsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _runtimeStatsTimer.Tick += (_, _) => UpdateRuntimeTitle();
        _runtimeStatsTimer.Start();
        UpdateRuntimeTitle();

        Closing += async (_, _) =>
        {
            await StopAsync();
            await _displayPipeline.DisposeAsync();
            if (_storageService is not null)
            {
                await _storageService.DisposeAsync();
            }

            _tdmsViewer.Dispose();
            _captureTimer.Stop();
            _renderTimer.Stop();
            _runtimeStatsTimer.Stop();
            _runtimeUsageSampler.Dispose();
            await _acquisition.DisposeAsync();
        };

        UpdateStoragePreview();
    }

    private void InitializeCompressionControls()
    {
        CompressionSettings compression = _settings.Storage.Compression;
        _compressionEnabledCheck.IsChecked = compression.Enabled;

        OptionItem<CompressionAlgorithm>[] algorithms =
        {
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
            new(CompressionPreprocessor.Lpc, "LPC")
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
            Margin = new Thickness(12, 10),
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*")
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
            Margin = new Thickness(10, 4, 10, 0),
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
        var root = new DockPanel();

        Control nav = BuildMonitorNav();
        DockPanel.SetDock(nav, Dock.Top);
        root.Children.Add(nav);

        var scroll = new ScrollViewer
        {
            Content = _monitorGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 8)
        };

        root.Children.Add(scroll);
        RebuildMonitorGrid();
        RebuildViewNav();
        return root;
    }

    private Control BuildMonitorNav()
    {
        _deviceCombo.MinWidth = 210;
        _deviceCombo.FontSize = 13;
        _channelPanel.Orientation = Orientation.Horizontal;
        _channelPanel.Spacing = 6;
        _activeViewText.Foreground = TextSecondary;
        _activeViewText.FontSize = 13;
        _activeViewText.VerticalAlignment = VerticalAlignment.Center;

        var viewScroll = new ScrollViewer
        {
            Content = _viewNavPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 42
        };

        var viewRow = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(0, 0, 10, 0),
                    Children = { _addViewButton, _removeViewButton, _activeViewText }
                },
                viewScroll
            }
        };

        DockPanel.SetDock(viewRow.Children[0], Dock.Right);

        var channelScroll = new ScrollViewer
        {
            Content = _channelPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = 42
        };

        var editRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        editRow.Children.Add(_deviceCombo);

        Grid.SetColumn(_selectAllChannelsButton, 1);
        editRow.Children.Add(_selectAllChannelsButton);
        Grid.SetColumn(_clearChannelsButton, 2);
        editRow.Children.Add(_clearChannelsButton);
        Grid.SetColumn(channelScroll, 3);
        editRow.Children.Add(channelScroll);

        return new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(10),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 0,
                Children = { viewRow, editRow }
            }
        };
    }

    private Control BuildSideBar()
    {
        _deviceCombo.Margin = new Thickness(12, 10, 12, 8);
        _deviceCombo.FontSize = 14;
        DockPanel.SetDock(_deviceCombo, Dock.Top);

        var header = new TextBlock
        {
            Text = "\u901a\u9053\u53e0\u52a0",
            Foreground = TextPrimary,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 2, 12, 0)
        };
        DockPanel.SetDock(header, Dock.Top);

        var scroll = new ScrollViewer
        {
            Content = _channelPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 4, 8, 8)
        };

        var panel = new DockPanel
        {
            Width = 330,
            Children =
            {
                _deviceCombo,
                header,
                scroll
            }
        };

        return new Border
        {
            Margin = new Thickness(0, 10, 0, 10),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel
        };
    }

    private Control BuildDeviceTab()
    {
        _deviceInfoPanel.Margin = new Thickness(14);
        _deviceInfoPanel.Spacing = 12;
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
            Margin = new Thickness(18),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var pathGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
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
            RowSpacing = 10,
            Children =
            {
                StorageField("\u91c7\u96c6\u65f6\u5199\u5165", _storageTabEnabledCheck),
                StorageField("\u4fdd\u5b58\u4f4d\u7f6e", pathGrid),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 12,
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

        Control storageModule = StorageModule("\u5b58\u50a8", storageGrid);
        Grid.SetColumn(storageModule, 0);
        Grid.SetRow(storageModule, 0);
        root.Children.Add(storageModule);

        Control compressionModule = StorageModule("\u65e0\u635f\u538b\u7f29", BuildCompressionSettingsPanel());
        Grid.SetColumn(compressionModule, 1);
        Grid.SetRow(compressionModule, 0);
        root.Children.Add(compressionModule);

        var previewBlock = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _storagePreview
            }
        };
        Control previewModule = StorageModule("\u9884\u89c8", previewBlock);
        Grid.SetColumn(previewModule, 0);
        Grid.SetRow(previewModule, 1);
        root.Children.Add(previewModule);

        var runtimeGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 10,
            Children =
            {
                StorageValue("\u5206\u6587\u4ef6", $"{FormatFileSplitSize()}    Flush {_settings.Storage.FlushIntervalMs} ms"),
                StorageValue("TDMS DLL", _settings.Storage.TdmRuntimeDir)
            }
        };
        Grid.SetRow(runtimeGrid.Children[1], 1);
        Control runtimeModule = StorageModule("\u8fd0\u884c\u53c2\u6570", runtimeGrid);
        Grid.SetColumn(runtimeModule, 1);
        Grid.SetRow(runtimeModule, 1);
        root.Children.Add(runtimeModule);

        return new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
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
                Spacing = 10,
                Children = { switches, options, _compressionAlgorithmParams, _compressionPreprocessorParams }
            }
        };
    }

    private static Control StorageModule(string title, Control content)
    {
        return new Border
        {
            Padding = new Thickness(12),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 10,
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
            ColumnDefinitions = new ColumnDefinitions("92,*"),
            ColumnSpacing = 10,
            MinHeight = 38,
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
        block.Margin = new Thickness(0, 0, 12, 10);
        return block;
    }

    private static Control SliderField(string label, Slider slider, TextBlock valueBlock, string hint)
    {
        valueBlock.Width = 38;
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
            Width = 220,
            Margin = new Thickness(0, 0, 12, 8),
            Spacing = 3,
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
        _metrics.Margin = new Thickness(12, 6);
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
        DeviceKey? selectedKey = _deviceCombo.SelectedItem is DeviceItem selected
            ? new DeviceKey(selected.Device.DeviceId, selected.Device.IpAddress)
            : null;

        List<DeviceItem> items = _acquisition.Devices
            .Select((device, index) => new DeviceItem(device, index + 1))
            .ToList();
        _deviceCombo.ItemsSource = items;

        if (items.Count == 0)
        {
            _deviceCombo.SelectedIndex = -1;
        }
        else if (selectedKey is { } key)
        {
            DeviceItem? next = items.FirstOrDefault(item => item.Key.Equals(key));
            _deviceCombo.SelectedItem = next ?? items[0];
        }
        else if (_deviceCombo.SelectedIndex < 0)
        {
            _deviceCombo.SelectedIndex = 0;
        }

        if (seedDefault)
        {
            SeedDefaultMonitorSelection();
        }

        ApplyMonitorSelectionsToStore();
        RebuildChannelList();
        RefreshDeviceInfoPanel();
    }

    private async Task ShowConnectionFailureAsync(Exception ex)
    {
        string message =
            "\u8bbe\u5907\u8fde\u63a5\u5931\u8d25\u3002\n\n" +
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
        bool storageEnabled = _settings.Storage.Enabled;
        _acquisition.SetStorageEnabled(storageEnabled);
        _waveformStore.Clear();

        if (_storageService is not null)
        {
            await _storageService.DisposeAsync();
            _storageService = null;
        }

        if (storageEnabled)
        {
            _storageService = new TdmsStorageService(_acquisition, _settings.Storage);
            _storageService.Faulted += fault => Dispatcher.UIThread.Post(() => _status.Text = fault.Message);
            await _storageService.StartAsync(_acquisition.Devices, CancellationToken.None);
        }

        await _displayPipeline.StartAsync(CancellationToken.None);
        await _acquisition.StartAsync(CancellationToken.None);
        StartCaptureTimer();
        SetButtons(connect: false, start: false, stop: true);
        _storageEnabledCheck.IsEnabled = false;
        _storageTabEnabledCheck.IsEnabled = false;
        _status.Text = storageEnabled
            ? (_settings.Storage.Compression.Enabled ? "\u91c7\u96c6\u4e2d\uff0c\u6b63\u5728\u5199\u5165\u539f\u751f\u538b\u7f29\u6587\u4ef6" : "\u91c7\u96c6\u4e2d\uff0c\u6b63\u5728\u4fdd\u5b58 TDMS")
            : "\u91c7\u96c6\u4e2d\uff0c\u4ec5\u663e\u793a\u4e0d\u4fdd\u5b58";
    }

    private async Task StopAsync()
    {
        _stopButton.IsEnabled = false;
        StopCaptureTimer();
        await _acquisition.StopAsync(CancellationToken.None);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while ((_acquisition.GetTelemetry().StorageQueueDepth > 0 || _acquisition.GetTelemetry().DisplayQueueDepth > 0) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await _displayPipeline.StopAsync();
        if (_storageService is not null)
        {
            await _storageService.StopAsync();
            _storageService = null;
        }
        _acquisition.ReleaseQueuedBlocks();

        SetButtons(connect: true, start: _acquisition.Devices.Count > 0, stop: false);
        _storageEnabledCheck.IsEnabled = true;
        _storageTabEnabledCheck.IsEnabled = true;
        if (_acquisition.Devices.Count > 0)
        {
            _status.Text = "\u5df2\u505c\u6b62";
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

    private void RebuildChannelList()
    {
        _channelPanel.Children.Clear();
        if (_deviceCombo.SelectedItem is not DeviceItem selected || _monitorViews.Count == 0)
        {
            return;
        }

        MonitorViewState view = _monitorViews[_activeViewIndex];
        foreach (ChannelDescriptor channel in selected.Device.Channels.Take(256))
        {
            var checkBox = new CheckBox
            {
                Content = channel.Name,
                Tag = channel,
                IsChecked = view.SelectedKeys.Contains(new ChannelKey(channel)),
                Margin = new Thickness(0, 0, 4, 0),
                Foreground = channel.Online ? TextPrimary : TextSecondary,
                FontSize = 13
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (checkBox.Tag is not ChannelDescriptor item)
                {
                    return;
                }

                ChannelKey key = new(item);
                if (checkBox.IsChecked == true)
                {
                    if (view.SelectedKeys.Count >= MaxChannelsPerMonitorView && !view.SelectedKeys.Contains(key))
                    {
                        checkBox.IsChecked = false;
                        return;
                    }

                    view.SelectedKeys.Add(key);
                }
                else
                {
                    view.SelectedKeys.Remove(key);
                }

                RefreshViewChannels(view);
                ApplyMonitorSelectionsToStore();
            };

            _channelPanel.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 5),
                Background = PanelBackground2,
                BorderBrush = BorderBrushSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = checkBox
            });
        }

        UpdateActiveViewText();
    }

    private void AddMonitorView()
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
            Margin = new Thickness(4),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
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

        var view = new MonitorViewState(waveform, host, title);
        host.PointerPressed += (_, _) => SelectMonitorView(_monitorViews.IndexOf(view));
        _monitorViews.Add(view);
        RefreshViewChannels(view);
    }

    private void SelectMonitorView(int index)
    {
        if (index < 0 || index >= _monitorViews.Count)
        {
            return;
        }

        _activeViewIndex = index;
        RebuildViewNav();
        RebuildChannelList();
        UpdateViewSelectionChrome();
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
            button.Click += (_, _) => SelectMonitorView(index);
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

        int count = Math.Max(1, _monitorViews.Count);
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

        for (int i = 0; i < _monitorViews.Count; i++)
        {
            Border host = _monitorViews[i].Host;
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
        if (_acquisition.Devices.Count == 0 || _monitorViews.Count == 0 || _monitorViews.Any(view => view.SelectedKeys.Count > 0))
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
    }

    private void SetDeviceChannelsForActiveView(bool selected)
    {
        if (_deviceCombo.SelectedItem is not DeviceItem item || _monitorViews.Count == 0)
        {
            return;
        }

        MonitorViewState view = _monitorViews[_activeViewIndex];
        foreach (ChannelDescriptor channel in item.Device.Channels)
        {
            ChannelKey key = new(channel);
            if (selected)
            {
                if (view.SelectedKeys.Count >= MaxChannelsPerMonitorView && !view.SelectedKeys.Contains(key))
                {
                    break;
                }

                view.SelectedKeys.Add(key);
            }
            else
            {
                view.SelectedKeys.Remove(key);
            }
        }

        RefreshViewChannels(view);
        RebuildChannelList();
        ApplyMonitorSelectionsToStore();
    }

    private void ApplyMonitorSelectionsToStore()
    {
        foreach (MonitorViewState view in _monitorViews)
        {
            RefreshViewChannels(view);
        }

        ChannelDescriptor[] union = _monitorViews
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

    private void RefreshViewChannels(MonitorViewState view)
    {
        Dictionary<ChannelKey, ChannelDescriptor> lookup = _acquisition.Devices
            .SelectMany(device => device.Channels)
            .GroupBy(channel => new ChannelKey(channel))
            .ToDictionary(group => group.Key, group => group.First());

        view.Channels = view.SelectedKeys
            .Where(lookup.ContainsKey)
            .Select(key => lookup[key])
            .Take(MaxChannelsPerMonitorView)
            .ToArray();
        view.SelectedKeys.Clear();
        foreach (ChannelDescriptor channel in view.Channels)
        {
            view.SelectedKeys.Add(new ChannelKey(channel));
        }

        int viewIndex = _monitorViews.IndexOf(view);
        view.Title.Text = $"View {viewIndex + 1}    {view.Channels.Count} ch";
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
        _activeViewText.Text = $"View {_activeViewIndex + 1}/{_monitorViews.Count}    {view.Channels.Count}/{MaxChannelsPerMonitorView} ch";
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
        _metrics.Text = $"Blocks {telemetry.BlocksReceived}    Data {mb:0.0} MB    StorageQ {telemetry.StorageQueueDepth}    DisplayQ {telemetry.DisplayQueueDepth}    Drops {telemetry.DisplayDrops}    {telemetry.BackpressureLevel}";
        if (!string.IsNullOrWhiteSpace(telemetry.Status))
        {
            _status.Text = TranslateStatus(telemetry.Status);
        }
    }

    private void UpdateRuntimeTitle()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        double elapsedSeconds = Math.Max(0.001, (now - _lastRuntimeStatsAt).TotalSeconds);
        double displayFps = _displayFrameCounter / elapsedSeconds;
        _displayFrameCounter = 0;
        _lastRuntimeStatsAt = now;

        RuntimeUsageSnapshot usage = _runtimeUsageSampler.Sample();
        Title = $"DASH Capture | FPS {displayFps:0.0} | CPU App {FormatPercent(usage.ProcessCpuPercent)} Sys {FormatPercent(usage.SystemCpuPercent)} | {FormatGpuUsage(usage)}";
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

    private void ApplyStorageSettingsFromUi()
    {
        _settings.Storage.RootPath = string.IsNullOrWhiteSpace(_storagePath.Text) ? _settings.Storage.RootPath : _storagePath.Text.Trim();
        _settings.Storage.Enabled = _storageEnabledCheck.IsChecked == true;
        _settings.Storage.NamingMode = _namingMode.SelectedIndex == 1 ? FileNamingMode.Custom : FileNamingMode.Time;
        _settings.Storage.CustomFileName = string.IsNullOrWhiteSpace(_customFileName.Text) ? "DashCapture" : _customFileName.Text.Trim();
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
        string extension = _compressionEnabledCheck.IsChecked == true ? ".dhcap" : ".tdms";
        string preview = custom
            ? $"{baseName}_0001{extension}\uff1b\u82e5\u91cd\u540d\u5219\u81ea\u52a8\u4f7f\u7528 {baseName}_001\\..."
            : $"DashCapture_yyyyMMdd_HHmmss_0001{extension}";
        _storagePreview.Text = $"\u4fdd\u5b58\u76ee\u5f55: {folder}\n\u6587\u4ef6\u540d: {preview}\n\u538b\u7f29: {CompressionSummaryFromUi()}";
        _storagePreview.Foreground = TextPrimary;
        _storagePreview.FontSize = 14;
    }

    private string CompressionSummaryFromUi()
    {
        if (_compressionEnabledCheck.IsChecked != true)
        {
            return "\u672a\u542f\u7528";
        }

        string preprocessor = SelectedValue(_compressionPreprocessorCombo, CompressionPreprocessor.None) == CompressionPreprocessor.None
            ? "\u65e0\u9884\u5904\u7406"
            : SelectedLabel(_compressionPreprocessorCombo);
        return $"\u539f\u751f\u538b\u7f29 .dhcap\uff0c\u53ef\u5728\u67e5\u770b\u9875\u5bfc\u51fa TDMS    {preprocessor} + {SelectedLabel(_compressionAlgorithmCombo)}";
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

        SetVisible(_compressionZstdLevelField, enabled && algorithm == CompressionAlgorithm.Zstd);
        SetVisible(_compressionZstdWindowLogField, enabled && algorithm == CompressionAlgorithm.Zstd);
        SetVisible(_compressionLz4HcField, enabled && algorithm == CompressionAlgorithm.Lz4Hc);
        SetVisible(_compressionZlibField, enabled && algorithm == CompressionAlgorithm.Zlib);
        SetVisible(_compressionBZip2Field, enabled && algorithm == CompressionAlgorithm.BZip2);
        SetVisible(_compressionLpcField, enabled && preprocessor == CompressionPreprocessor.Lpc);

        _compressionAlgorithmParams.IsVisible =
            enabled && (algorithm == CompressionAlgorithm.Zstd ||
                        algorithm == CompressionAlgorithm.Lz4Hc ||
                        algorithm == CompressionAlgorithm.Zlib ||
                        algorithm == CompressionAlgorithm.BZip2);
        _compressionPreprocessorParams.IsVisible = enabled && preprocessor == CompressionPreprocessor.Lpc;
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
            Margin = new Thickness(0, 0, 16, 0),
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
            Margin = new Thickness(0, 0, 16, 0),
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
        button.Padding = new Thickness(16, 8);
        button.MinWidth = 88;
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
    }

    private static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.Background = Brushes.White;
        comboBox.Foreground = TextPrimary;
        comboBox.BorderBrush = BorderBrushSoft;
        comboBox.FontSize = 14;
        comboBox.Padding = new Thickness(8, 5);
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
        return $"Device {displayIndex} ({device.IpAddress})";
    }

    private sealed class DeviceItem
    {
        public DeviceItem(DeviceDescriptor device, int displayIndex)
        {
            Device = device;
            Key = new DeviceKey(device.DeviceId, device.IpAddress);
            DisplayName = FormatDeviceName(device, displayIndex);
        }

        public DeviceDescriptor Device { get; }
        public DeviceKey Key { get; }
        public string DisplayName { get; }
        public override string ToString() => DisplayName;
    }

    private readonly record struct DeviceKey(int DeviceId, string IpAddress);

    private sealed record OptionItem<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class MonitorViewState
    {
        public MonitorViewState(WaveformControl waveform, Border host, TextBlock title)
        {
            Waveform = waveform;
            Host = host;
            Title = title;
        }

        public WaveformControl Waveform { get; }
        public Border Host { get; }
        public TextBlock Title { get; }
        public HashSet<ChannelKey> SelectedKeys { get; } = new();
        public IReadOnlyList<ChannelDescriptor> Channels { get; set; } = Array.Empty<ChannelDescriptor>();
    }
}
