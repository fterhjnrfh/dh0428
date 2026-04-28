using Avalonia;
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
    private readonly StackPanel _deviceInfoPanel = new();
    private readonly TextBox _storagePath = new();
    private readonly TextBox _customFileName = new();
    private readonly ComboBox _namingMode = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _metrics = new();
    private readonly TextBlock _storagePreview = new();
    private readonly Button _connectButton = new() { Content = "连接设备" };
    private readonly Button _startButton = new() { Content = "开始采集", IsEnabled = false };
    private readonly Button _stopButton = new() { Content = "停止采集", IsEnabled = false };
    private readonly Button _browseButton = new() { Content = "浏览" };
    private readonly WaveformControl _waveform = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly List<ChannelDescriptor> _selectedChannels = new();

    public MainWindow()
    {
        _settings = AppSettingsLoader.Load();
        _acquisition = new AcquisitionService(_settings);
        _waveformStore = new WaveformStore(Math.Max(1000, _settings.Display.WindowSeconds * 10000));
        _displayPipeline = new DisplayPipeline(_acquisition, _waveformStore, () => _acquisition.Devices);
        _waveform.Store = _waveformStore;
        _waveform.WindowSeconds = _settings.Display.WindowSeconds;

        _storagePath.Text = _settings.Storage.RootPath;
        _customFileName.Text = _settings.Storage.CustomFileName;
        _namingMode.ItemsSource = new[] { "按时间命名", "自定义命名" };
        _namingMode.SelectedIndex = _settings.Storage.NamingMode == FileNamingMode.Time ? 0 : 1;
        StyleInput(_storagePath);
        StyleInput(_customFileName);
        StyleComboBox(_deviceCombo);
        StyleComboBox(_namingMode);

        Title = "DASH Capture";
        Background = PageBackground;
        MinWidth = 1120;
        MinHeight = 720;
        Width = 1360;
        Height = 820;
        Content = BuildContent();

        _connectButton.Click += async (_, _) => await ConnectAsync();
        _startButton.Click += async (_, _) => await StartAsync();
        _stopButton.Click += async (_, _) => await StopAsync();
        _browseButton.Click += async (_, _) => await BrowseStorageFolderAsync();
        _deviceCombo.SelectionChanged += (_, _) => RebuildChannelList();
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

        _acquisition.Faulted += fault => Dispatcher.UIThread.Post(() => _status.Text = fault.Message);
        _acquisition.TelemetryUpdated += telemetry => Dispatcher.UIThread.Post(() => UpdateTelemetry(telemetry));

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _settings.Display.TargetFps))
        };
        _renderTimer.Tick += (_, _) => _waveform.InvalidateVisual();
        _renderTimer.Start();

        Closing += async (_, _) =>
        {
            await StopAsync();
            await _displayPipeline.DisposeAsync();
            if (_storageService is not null)
            {
                await _storageService.DisposeAsync();
            }

            await _acquisition.DisposeAsync();
        };

        UpdateStoragePreview();
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

        _status.Text = "未连接";
        _status.Foreground = TextPrimary;
        _status.VerticalAlignment = VerticalAlignment.Center;

        var bar = new Grid
        {
            Margin = new Thickness(12, 10),
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*")
        };

        Control deviceGroup = AddControlGroup("设备", _connectButton);
        Grid.SetColumn(deviceGroup, 0);
        bar.Children.Add(deviceGroup);

        var sampleGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _startButton, _stopButton }
        };
        Control acquisitionGroup = AddControlGroup("采集", sampleGroup);
        Grid.SetColumn(acquisitionGroup, 1);
        bar.Children.Add(acquisitionGroup);

        Control statusPill = AddStatusPill();
        Grid.SetColumn(statusPill, 2);
        bar.Children.Add(statusPill);

        var title = new TextBlock
        {
            Text = "高吞吐回调采集 / TDMS 原始存储",
            Foreground = TextSecondary,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(title, 3);
        bar.Children.Add(title);

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
                new TabItem { Header = "主监控", Content = BuildMonitorTab() },
                new TabItem { Header = "设备通道", Content = BuildDeviceTab() },
                new TabItem { Header = "存储", Content = BuildStorageTab() }
            }
        };
    }

    private Control BuildMonitorTab()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("330,*")
        };

        Control sideBar = BuildSideBar();
        Grid.SetColumn(sideBar, 0);
        grid.Children.Add(sideBar);

        var plotHost = new Border
        {
            Margin = new Thickness(10, 10, 0, 10),
            Padding = new Thickness(0),
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = _waveform
        };
        Grid.SetColumn(plotHost, 1);
        grid.Children.Add(plotHost);

        return grid;
    }

    private Control BuildSideBar()
    {
        _deviceCombo.Margin = new Thickness(12, 10, 12, 8);
        _deviceCombo.FontSize = 14;
        DockPanel.SetDock(_deviceCombo, Dock.Top);

        var header = new TextBlock
        {
            Text = "通道叠加",
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
        _storagePath.Width = 580;
        _storagePath.Watermark = "选择 TDMS 文件保存目录";
        _customFileName.Width = 360;
        _customFileName.Watermark = "例如 TestRun_A";
        _namingMode.Width = 180;
        StyleControlButton(_browseButton, AccentBlue);

        var root = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            MaxWidth = 860,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        root.Children.Add(SectionTitle("TDMS 存储设置"));

        var pathRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                _storagePath,
                _browseButton
            }
        };
        root.Children.Add(FieldBlock("保存位置", pathRow));

        root.Children.Add(FieldBlock("文件命名方式", _namingMode));
        root.Children.Add(FieldBlock("自定义文件名", _customFileName));

        root.Children.Add(new Border
        {
            Padding = new Thickness(12),
            Background = PanelBackground2,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = _storagePreview
        });

        root.Children.Add(new TextBlock
        {
            Text = $"分文件大小: {_settings.Storage.FileSplitGb} GB    Flush 间隔: {_settings.Storage.FlushIntervalMs} ms",
            Foreground = TextSecondary,
            FontSize = 14
        });
        root.Children.Add(new TextBlock
        {
            Text = $"NI TDMS DLL: {_settings.Storage.TdmRuntimeDir}",
            Foreground = TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });

        return root;
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
            Title = "选择 TDMS 保存目录"
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
        _status.Text = "正在连接";
        try
        {
            await _acquisition.ConnectAsync(CancellationToken.None);
            _deviceCombo.ItemsSource = _acquisition.Devices.Select(d => new DeviceItem(d)).ToList();
            _deviceCombo.SelectedIndex = _acquisition.Devices.Count > 0 ? 0 : -1;
            RefreshDeviceInfoPanel();
            _startButton.IsEnabled = _acquisition.Devices.Count > 0;
            _connectButton.IsEnabled = true;
            _status.Text = _acquisition.Devices.Count > 0 ? "已连接" : "未发现设备";
        }
        catch (Exception ex)
        {
            _status.Text = "连接失败";
            await ShowConnectionFailureAsync(ex);
            SetButtons(connect: true, start: false, stop: false);
        }
    }

    private async Task ShowConnectionFailureAsync(Exception ex)
    {
        string message =
            "设备连接失败。\n\n" +
            $"错误: {ex.Message}\n\n" +
            $"DashRoot: {_settings.Sdk.DashRoot}\n" +
            $"ConfigDir: {_settings.Sdk.ConfigDir}\n\n" +
            "请确认 DASH 目录、Config/Serial、网络配置和模拟仪器/真实仪器已启动。";

        await new Window
        {
            Title = "连接失败",
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

        UpdateSelectedChannels();
        ApplyStorageSettingsFromUi();
        _waveformStore.Clear();

        if (_storageService is not null)
        {
            await _storageService.DisposeAsync();
        }

        _storageService = new TdmsStorageService(_acquisition, _settings.Storage);
        _storageService.Faulted += fault => Dispatcher.UIThread.Post(() => _status.Text = fault.Message);

        await _storageService.StartAsync(_acquisition.Devices, CancellationToken.None);
        await _displayPipeline.StartAsync(CancellationToken.None);
        await _acquisition.StartAsync(CancellationToken.None);
        SetButtons(connect: false, start: false, stop: true);
        _status.Text = "采集中";
    }

    private async Task StopAsync()
    {
        _stopButton.IsEnabled = false;
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
        }

        SetButtons(connect: true, start: _acquisition.Devices.Count > 0, stop: false);
        if (_acquisition.Devices.Count > 0)
        {
            _status.Text = "已停止";
        }
    }

    private void RebuildChannelList()
    {
        _channelPanel.Children.Clear();
        if (_deviceCombo.SelectedItem is not DeviceItem selected)
        {
            return;
        }

        int added = 0;
        foreach (ChannelDescriptor channel in selected.Device.Channels)
        {
            var label = new TextBlock
            {
                Text = channel.Name,
                FontSize = 15,
                Foreground = channel.Online ? TextPrimary : TextSecondary,
                FontWeight = FontWeight.SemiBold
            };

            var checkBox = new CheckBox
            {
                Content = label,
                Tag = channel,
                IsChecked = added < Math.Min(4, _settings.Display.MaxVisibleChannels),
                Margin = new Thickness(0)
            };
            checkBox.IsCheckedChanged += (_, _) => UpdateSelectedChannels();

            _channelPanel.Children.Add(new Border
            {
                Margin = new Thickness(0, 4),
                Padding = new Thickness(10, 8),
                Background = added % 2 == 0 ? PanelBackground2 : new SolidColorBrush(Color.FromRgb(246, 249, 253)),
                BorderBrush = BorderBrushSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = checkBox
            });
            added++;
        }

        UpdateSelectedChannels();
    }

    private void RefreshDeviceInfoPanel()
    {
        _deviceInfoPanel.Children.Clear();
        if (_acquisition.Devices.Count == 0)
        {
            _deviceInfoPanel.Children.Add(new TextBlock
            {
                Text = "未连接设备",
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
                Text = $"{device.DisplayName}",
                Foreground = TextPrimary,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold
            });
            card.Children.Add(new TextBlock
            {
                Text = $"采样率 {device.SampleRate:0.##} Hz    通道 {device.Channels.Count}    状态 {(device.Online ? "在线" : "离线")}",
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
        _selectedChannels.Clear();
        if (_deviceCombo.SelectedItem is not DeviceItem selected)
        {
            _waveformStore.SetVisibleChannels(Array.Empty<ChannelDescriptor>());
            return;
        }

        foreach (CheckBox checkBox in _channelPanel.Children
            .OfType<Border>()
            .Select(border => border.Child)
            .OfType<CheckBox>())
        {
            if (checkBox.IsChecked == true && checkBox.Tag is ChannelDescriptor channel && channel.DeviceId == selected.Device.DeviceId)
            {
                _selectedChannels.Add(channel);
            }
        }

        if (_selectedChannels.Count > _settings.Display.MaxVisibleChannels)
        {
            _selectedChannels.RemoveRange(_settings.Display.MaxVisibleChannels, _selectedChannels.Count - _settings.Display.MaxVisibleChannels);
        }

        int sampleRate = Math.Max(1, (int)selected.Device.SampleRate);
        _waveformStore.SetCapacity(Math.Max(1000, sampleRate * Math.Max(1, _settings.Display.WindowSeconds)));
        _waveformStore.SetVisibleChannels(_selectedChannels);
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

    private void ApplyStorageSettingsFromUi()
    {
        _settings.Storage.RootPath = string.IsNullOrWhiteSpace(_storagePath.Text) ? _settings.Storage.RootPath : _storagePath.Text.Trim();
        _settings.Storage.NamingMode = _namingMode.SelectedIndex == 1 ? FileNamingMode.Custom : FileNamingMode.Time;
        _settings.Storage.CustomFileName = string.IsNullOrWhiteSpace(_customFileName.Text) ? "DashCapture" : _customFileName.Text.Trim();
        UpdateStoragePreview();
    }

    private void UpdateStoragePreview()
    {
        bool custom = _namingMode.SelectedIndex == 1;
        _customFileName.IsEnabled = custom;
        string folder = string.IsNullOrWhiteSpace(_storagePath.Text) ? _settings.Storage.RootPath : _storagePath.Text.Trim();
        string baseName = string.IsNullOrWhiteSpace(_customFileName.Text) ? "DashCapture" : _customFileName.Text.Trim();
        string preview = custom
            ? $"{baseName}.tdms；若重名则自动使用 {baseName}_001.tdms、{baseName}_002.tdms"
            : $"DashCapture_yyyyMMdd_HHmmss_001.tdms";
        _storagePreview.Text = $"保存目录: {folder}\n文件名: {preview}";
        _storagePreview.Foreground = TextPrimary;
        _storagePreview.FontSize = 14;
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
                        Text = "状态",
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
            "Idle" => "空闲",
            "Sampling" => "采集中",
            "Stopped" => "已停止",
            var text when text.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) => "已连接",
            _ => status
        };
    }

    private sealed class DeviceItem
    {
        public DeviceItem(DeviceDescriptor device)
        {
            Device = device;
        }

        public DeviceDescriptor Device { get; }
        public override string ToString() => Device.DisplayName;
    }
}
