using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DashCapture.Analysis;
using DashCapture.Storage;

namespace DashCapture.App;

public sealed class SignalProcessingControl : UserControl, IDisposable
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
    private static readonly IBrush AccentRed = new SolidColorBrush(Color.FromRgb(159, 66, 66));

    private readonly string _tdmRuntimeDir;
    private readonly string _resultRootPath;
    private readonly Button _openFileButton = new() { Content = "打开数据" };
    private readonly Button _openFolderButton = new() { Content = "打开文件夹" };
    private readonly Button _importModuleButton = new() { Content = "导入处理组" };
    private readonly Button _builtInModuleButton = new() { Content = "幅值分析" };
    private readonly Button _processAllButton = new() { Content = "处理全部通道", IsEnabled = false };
    private readonly Button _processDeviceButton = new() { Content = "处理当前设备", IsEnabled = false };
    private readonly Button _processChannelButton = new() { Content = "处理当前通道", IsEnabled = false };
    private readonly Button _exportCsvButton = new() { Content = "另存CSV", IsEnabled = false };
    private readonly ComboBox _devicePicker = new() { Width = 230, IsEnabled = false };
    private readonly ComboBox _channelPicker = new() { Width = 260, IsEnabled = false };
    private readonly TextBlock _fileText = new() { Text = "未打开历史数据。" };
    private readonly TextBlock _moduleText = new();
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _resultPathText = new();
    private readonly StackPanel _resultRows = new() { Spacing = 0 };

    private TdmsFileReader? _reader;
    private SignalProcessingModuleDefinition _module = SignalProcessingModuleDefinition.BuiltInAmplitudeAnalysis;
    private HistoricalSignalProcessingResult? _lastResult;
    private string? _lastCsvPath;
    private CancellationTokenSource? _operationCts;
    private bool _busy;

    public SignalProcessingControl(string tdmRuntimeDir, string resultRootPath)
    {
        _tdmRuntimeDir = tdmRuntimeDir;
        _resultRootPath = string.IsNullOrWhiteSpace(resultRootPath) ? @".\Analysis" : resultRootPath;
        Background = PageBackground;
        Content = BuildContent();

        _openFileButton.Click += async (_, _) => await OpenFileAsync();
        _openFolderButton.Click += async (_, _) => await OpenFolderAsync();
        _importModuleButton.Click += async (_, _) => await ImportModuleAsync();
        _builtInModuleButton.Click += (_, _) => SetModule(SignalProcessingModuleDefinition.BuiltInAmplitudeAnalysis, "已切换到内置幅值分析处理组。");
        _processAllButton.Click += async (_, _) => await ProcessAsync(ProcessingScope.AllChannels);
        _processDeviceButton.Click += async (_, _) => await ProcessAsync(ProcessingScope.CurrentDevice);
        _processChannelButton.Click += async (_, _) => await ProcessAsync(ProcessingScope.CurrentChannel);
        _exportCsvButton.Click += async (_, _) => await ExportCsvAsync();
        _devicePicker.SelectionChanged += (_, _) => RefreshChannelPicker();

        StyleButton(_openFileButton, AccentBlue);
        StyleButton(_openFolderButton, AccentBlue);
        StyleButton(_importModuleButton, AccentBlue);
        StyleButton(_builtInModuleButton, AccentBlue);
        StyleButton(_processAllButton, AccentGreen);
        StyleButton(_processDeviceButton, AccentGreen);
        StyleButton(_processChannelButton, AccentGreen);
        StyleButton(_exportCsvButton, AccentGreen);
        StyleComboBox(_devicePicker);
        StyleComboBox(_channelPicker);
        UpdateModuleText();
        SetStatus("打开历史数据后，可导入算法组并运行历史处理。");
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10
        };

        Control toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        ShowResultMessage("暂无处理结果。");

        var resultPanel = new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelRadius),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _resultRows
            }
        };
        Grid.SetRow(resultPanel, 1);
        root.Children.Add(resultPanel);

        var statusGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1.1*,1.6*"),
            ColumnSpacing = 14,
            Children =
            {
                _summaryText,
                _resultPathText,
                _statusText
            }
        };
        Grid.SetColumn(_resultPathText, 1);
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
        ConfigureText(_fileText, TextPrimary);
        ConfigureText(_moduleText, TextPrimary);
        ConfigureText(_summaryText, TextSecondary);
        ConfigureText(_statusText, TextSecondary);
        ConfigureText(_resultPathText, TextSecondary);

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _openFileButton,
                _openFolderButton,
                _importModuleButton,
                _builtInModuleButton,
                ToolbarField("设备", _devicePicker),
                ToolbarField("通道", _channelPicker),
                _processAllButton,
                _processDeviceButton,
                _processChannelButton,
                _exportCsvButton
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
                Children = { toolbar, _moduleText, _fileText }
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
            Title = "打开历史数据",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("采集数据") { Patterns = new[] { "*.tdms", "*.dhcap" } },
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
            Title = "打开采集文件夹"
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenPathAsync(path);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        CancellationToken token = ResetOperation();
        SetBusy(true, "正在打开历史数据...");
        try
        {
            TdmsFileReader reader = await Task.Run(() => TdmsFileReader.Open(path, _tdmRuntimeDir), token);
            token.ThrowIfCancellationRequested();
            _reader?.Dispose();
            _reader = reader;
            _lastResult = null;
            _lastCsvPath = null;
            _fileText.Text = reader.Path;
            PopulateDevices(reader.FileInfo);
            ShowResultMessage("历史数据已打开。请选择处理范围后点击处理按钮。");
            _resultPathText.Text = string.Empty;
            SetStatus("历史数据已打开。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("打开已取消。");
        }
        catch (Exception ex)
        {
            SetStatus("打开失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ImportModuleAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "导入信号处理组",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("处理组 JSON") { Patterns = new[] { "*.json", "*.dhproc" } },
                FilePickerFileTypes.All
            }
        });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            SignalProcessingModuleDefinition module = SignalProcessingModuleDefinition.LoadFromFile(path);
            SetModule(module, "处理组已导入：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("处理组导入失败：" + ex.Message);
        }
    }

    private async Task ProcessAsync(ProcessingScope scope)
    {
        TdmsFileReader? reader = _reader;
        if (reader is null)
        {
            SetStatus("请先打开历史数据。");
            return;
        }

        TdmsChannelInfo[] channels = ResolveChannels(scope);
        if (channels.Length == 0)
        {
            SetStatus("当前处理范围没有可用通道。");
            return;
        }

        CancellationToken token = ResetOperation();
        SetBusy(true, $"正在处理 {channels.Length:N0} 个通道...");
        var progress = new Progress<HistoricalSignalProcessingProgress>(item =>
            SetStatus($"正在处理 {item.Percent:0.0}%    {item.ChannelName}    {item.ChannelSamplesDone:N0}/{item.ChannelSamplesTotal:N0}"));

        try
        {
            HistoricalSignalProcessingResult result = await Task.Run(() =>
                HistoricalSignalProcessor.Process(
                    reader,
                    _module,
                    channels,
                    new HistoricalSignalProcessingOptions(),
                    token,
                    progress),
                token);
            token.ThrowIfCancellationRequested();

            string jsonPath = BuildResultPath(reader.Path, _module.Name, ".dhanalysis");
            string csvPath = Path.ChangeExtension(jsonPath, ".csv");
            await Task.Run(() =>
            {
                SignalProcessingResultWriter.WriteJson(result, jsonPath);
                SignalProcessingResultWriter.WriteCsv(result, csvPath);
            }, token);

            _lastResult = result;
            _lastCsvPath = csvPath;
            _resultPathText.Text = Path.GetFileName(jsonPath);
            _exportCsvButton.IsEnabled = true;
            RenderResultTable(result, maxRows: 500);
            SetStatus($"历史处理完成：{result.Channels.Count:N0} 个通道，耗时 {result.Elapsed.TotalSeconds:0.0} 秒。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("历史处理已取消。");
        }
        catch (Exception ex)
        {
            SetStatus("历史处理失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExportCsvAsync()
    {
        HistoricalSignalProcessingResult? result = _lastResult;
        if (result is null)
        {
            SetStatus("没有可导出的处理结果。");
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存信号处理结果 CSV",
            SuggestedFileName = SuggestedResultName(result.SourcePath, result.ModuleName) + ".csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                FilePickerFileTypes.All
            }
        });

        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            path += ".csv";
        }

        try
        {
            SignalProcessingResultWriter.WriteCsv(result, path);
            _lastCsvPath = path;
            SetStatus("CSV 已导出：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("CSV 导出失败：" + ex.Message);
        }
    }

    private void PopulateDevices(TdmsFileInfo fileInfo)
    {
        _devicePicker.ItemsSource = fileInfo.Groups
            .Select(group => new ComboItem<TdmsGroupInfo>($"{group.Name}（{group.Channels.Count} 通道）", group))
            .ToArray();
        _devicePicker.SelectedIndex = fileInfo.Groups.Count > 0 ? 0 : -1;
        _devicePicker.IsEnabled = fileInfo.Groups.Count > 0;
        RefreshChannelPicker();

        ulong maxSamples = fileInfo.Groups
            .SelectMany(group => group.Channels)
            .Select(channel => channel.SampleCount)
            .DefaultIfEmpty(0UL)
            .Max();
        double maxDuration = fileInfo.Groups
            .SelectMany(group => group.Channels)
            .Select(channel => channel.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
        _summaryText.Text = $"设备组 {fileInfo.Groups.Count:N0}    通道 {fileInfo.ChannelCount:N0}    最大样本 {maxSamples:N0}    最长 {maxDuration:0.###} s";
        UpdateButtons();
    }

    private void RefreshChannelPicker()
    {
        if (_devicePicker.SelectedItem is not ComboItem<TdmsGroupInfo> device)
        {
            _channelPicker.ItemsSource = Array.Empty<ComboItem<TdmsChannelInfo>>();
            _channelPicker.SelectedIndex = -1;
            _channelPicker.IsEnabled = false;
            UpdateButtons();
            return;
        }

        _channelPicker.ItemsSource = device.Value.Channels
            .Select(channel => new ComboItem<TdmsChannelInfo>($"{channel.Name}    {channel.SampleRate:0.###} Hz    {channel.SampleCount:N0}", channel))
            .ToArray();
        _channelPicker.SelectedIndex = device.Value.Channels.Count > 0 ? 0 : -1;
        _channelPicker.IsEnabled = device.Value.Channels.Count > 0;
        UpdateButtons();
    }

    private TdmsChannelInfo[] ResolveChannels(ProcessingScope scope)
    {
        TdmsFileReader? reader = _reader;
        if (reader is null)
        {
            return Array.Empty<TdmsChannelInfo>();
        }

        return scope switch
        {
            ProcessingScope.CurrentChannel => _channelPicker.SelectedItem is ComboItem<TdmsChannelInfo> channel
                ? new[] { channel.Value }
                : Array.Empty<TdmsChannelInfo>(),
            ProcessingScope.CurrentDevice => _devicePicker.SelectedItem is ComboItem<TdmsGroupInfo> device
                ? device.Value.Channels.ToArray()
                : Array.Empty<TdmsChannelInfo>(),
            _ => reader.FileInfo.Groups.SelectMany(group => group.Channels).ToArray()
        };
    }

    private void SetModule(SignalProcessingModuleDefinition module, string status)
    {
        _module = module;
        _lastResult = null;
        _lastCsvPath = null;
        _exportCsvButton.IsEnabled = false;
        UpdateModuleText();
        SetStatus(status);
    }

    private void UpdateModuleText()
    {
        string algorithms = string.Join("、", _module.Algorithms.Select(algorithm => algorithm.Name));
        _moduleText.Text = $"当前处理组：{_module.Name}    算法：{algorithms}";
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
        bool hasReader = _reader is not null;
        bool hasDevice = _devicePicker.SelectedItem is ComboItem<TdmsGroupInfo>;
        bool hasChannel = _channelPicker.SelectedItem is ComboItem<TdmsChannelInfo>;
        _openFileButton.IsEnabled = !_busy;
        _openFolderButton.IsEnabled = !_busy;
        _importModuleButton.IsEnabled = !_busy;
        _builtInModuleButton.IsEnabled = !_busy;
        _devicePicker.IsEnabled = !_busy && hasReader && hasDevice;
        _channelPicker.IsEnabled = !_busy && hasReader && hasChannel;
        _processAllButton.IsEnabled = !_busy && hasReader;
        _processDeviceButton.IsEnabled = !_busy && hasReader && hasDevice;
        _processChannelButton.IsEnabled = !_busy && hasReader && hasChannel;
        _exportCsvButton.IsEnabled = !_busy && _lastResult is not null;
    }

    private CancellationToken ResetOperation()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        return _operationCts.Token;
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

    private string BuildResultPath(string sourcePath, string moduleName, string extension)
    {
        string directory = Path.Combine(_resultRootPath, "SignalProcessing");
        string name = SuggestedResultName(sourcePath, moduleName);
        return Path.Combine(directory, name + extension);
    }

    private static string SuggestedResultName(string sourcePath, string moduleName)
    {
        string sourceName = Directory.Exists(sourcePath)
            ? new DirectoryInfo(sourcePath).Name
            : Path.GetFileNameWithoutExtension(sourcePath);
        string safeSource = SanitizeFileName(string.IsNullOrWhiteSpace(sourceName) ? "DashCapture" : sourceName);
        string safeModule = SanitizeFileName(string.IsNullOrWhiteSpace(moduleName) ? "SignalProcessing" : moduleName);
        return $"{safeSource}_{safeModule}_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private static string SanitizeFileName(string text)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private void ShowResultMessage(string message)
    {
        _resultRows.Children.Clear();
        _resultRows.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = TextSecondary,
            FontSize = 14,
            Margin = new Thickness(14),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void RenderResultTable(HistoricalSignalProcessingResult result, int maxRows)
    {
        _resultRows.Children.Clear();
        string algorithmText = string.Join("、", result.Algorithms.Select(algorithm => algorithm.Name));
        _resultRows.Children.Add(new TextBlock
        {
            Text = $"处理内容：对每个通道的原始时域数据计算 {algorithmText}。",
            Foreground = TextPrimary,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 10, 12, 4),
            TextWrapping = TextWrapping.Wrap
        });
        _resultRows.Children.Add(new TextBlock
        {
            Text = $"源数据：{result.SourcePath}    通道 {result.Channels.Count:N0} 个    耗时 {result.Elapsed.TotalSeconds:0.0} 秒    已自动保存 .dhanalysis 和 CSV。",
            Foreground = TextSecondary,
            FontSize = 12,
            Margin = new Thickness(12, 0, 12, 10),
            TextWrapping = TextWrapping.Wrap
        });

        Grid table = CreateResultGrid(result.Algorithms);
        AddResultHeader(table, result.Algorithms);
        int row = 1;
        foreach (SignalProcessingChannelResult channel in result.Channels.Take(maxRows))
        {
            AddResultRow(table, row, channel, result.Algorithms);
            row++;
        }

        _resultRows.Children.Add(table);
        if (result.Channels.Count > maxRows)
        {
            _resultRows.Children.Add(new TextBlock
            {
                Text = $"仅预览前 {maxRows:N0} 行，完整结果请查看 CSV。",
                Foreground = TextSecondary,
                FontSize = 12,
                Margin = new Thickness(12, 8, 12, 12)
            });
        }
    }

    private static Grid CreateResultGrid(IReadOnlyList<SignalProcessingAlgorithmDefinition> algorithms)
    {
        var grid = new Grid
        {
            Margin = new Thickness(12, 0, 12, 12)
        };

        double[] fixedWidths = { 58, 92, 102, 132, 92 };
        foreach (double width in fixedWidths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(width)));
        }

        foreach (SignalProcessingAlgorithmDefinition _ in algorithms)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(116)));
        }

        grid.MinWidth = fixedWidths.Sum() + algorithms.Count * 116;
        return grid;
    }

    private static void AddResultHeader(Grid table, IReadOnlyList<SignalProcessingAlgorithmDefinition> algorithms)
    {
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddResultCell(table, "设备", 0, 0, header: true);
        AddResultCell(table, "通道", 0, 1, header: true);
        AddResultCell(table, "采样率Hz", 0, 2, header: true, alignment: TextAlignment.Right);
        AddResultCell(table, "样本数", 0, 3, header: true, alignment: TextAlignment.Right);
        AddResultCell(table, "时长s", 0, 4, header: true, alignment: TextAlignment.Right);
        for (int i = 0; i < algorithms.Count; i++)
        {
            AddResultCell(table, algorithms[i].Name, 0, 5 + i, header: true, alignment: TextAlignment.Right);
        }
    }

    private static void AddResultRow(
        Grid table,
        int row,
        SignalProcessingChannelResult channel,
        IReadOnlyList<SignalProcessingAlgorithmDefinition> algorithms)
    {
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        IBrush background = row % 2 == 0 ? PanelBackground2 : PanelBackground;
        AddResultCell(table, (channel.DeviceId + 1).ToString(CultureInfo.InvariantCulture), row, 0, background: background);
        AddResultCell(table, channel.ChannelName, row, 1, background: background);
        AddResultCell(table, FormatValue(channel.SampleRate), row, 2, alignment: TextAlignment.Right, background: background);
        AddResultCell(table, channel.SampleCount.ToString("N0", CultureInfo.InvariantCulture), row, 3, alignment: TextAlignment.Right, background: background);
        AddResultCell(table, FormatValue(channel.DurationSeconds), row, 4, alignment: TextAlignment.Right, background: background);
        for (int i = 0; i < algorithms.Count; i++)
        {
            double value = channel.MetricValue(algorithms[i].Type) ?? double.NaN;
            AddResultCell(table, FormatValue(value), row, 5 + i, alignment: TextAlignment.Right, background: background);
        }
    }

    private static void AddResultCell(
        Grid table,
        string text,
        int row,
        int column,
        bool header = false,
        TextAlignment alignment = TextAlignment.Left,
        IBrush? background = null)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = header ? TextPrimary : TextSecondary,
            FontSize = 12,
            FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
            TextAlignment = alignment,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Background = header ? PanelBackground2 : background ?? PanelBackground,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 6),
            Child = block
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        table.Children.Add(border);
    }

    private static string FormatValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return string.Empty;
        }

        double abs = Math.Abs(value);
        if (abs > 0 && abs < 0.000001 || abs >= 10_000_000)
        {
            return value.ToString("0.######E+0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static void ConfigureText(TextBlock block, IBrush foreground)
    {
        block.Foreground = foreground;
        block.FontSize = 12;
        block.TextWrapping = TextWrapping.NoWrap;
        block.TextTrimming = TextTrimming.CharacterEllipsis;
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
        button.MinWidth = 70;
        button.FontSize = 12;
        button.FontWeight = FontWeight.SemiBold;
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

    public void Dispose()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = null;
        _reader?.Dispose();
        _reader = null;
    }

    private enum ProcessingScope
    {
        AllChannels,
        CurrentDevice,
        CurrentChannel
    }

    private sealed record ComboItem<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }
}
