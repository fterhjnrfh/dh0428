# DashCapture 文件格式说明

本文说明 DashCapture 当前使用的两类自研文件格式：

- `.dhcap`：采集原始数据存储格式。
- `.dhfft`：FFT 分析结果存储格式。
- `.dhanalysis`：通用信号处理结果格式。

这两类格式都是 DashCapture 为高吞吐采集、压缩、快速回放和分析而设计的内部格式。它们不是 TDMS、HDF5 或通用 `.bin` 文件。对外交换时，建议通过软件导出 TDMS、CSV，后续也可以补充 HDF5 导出。

## 格式定位

| 格式 | 定位 | 是否自研 | 主要用途 |
| --- | --- | --- | --- |
| `.dhcap` | 采集原始数据容器 | 是 | 高速写入、无损压缩、按通道快速读取、全局预览 |
| `.dhfft` | FFT 结果容器 | 是 | 保存频谱幅值、峰值趋势、FFT 结果查看和导出 |
| `.dhanalysis` | 通用信号处理结果容器 | 是 | 保存历史数据处理后的幅值、统计等算法结果 |
| `.tdms` | 标准导出格式 | 否 | 面向 NI/第三方工具的数据交换 |
| `.csv` | 文本导出格式 | 否 | 单帧频谱、趋势、峰值结果交换 |

简单理解：

- `.dhcap` 是“原始采集数据仓库”，后续可以重新查看波形、导出 TDMS，也可以重新计算 FFT。
- `.dhfft` 是“FFT 分析结果缓存”，用于快速查看频谱和趋势，但不能替代原始波形数据。
- `.dhanalysis` 是“算法处理结果表”，用于保存对历史采集数据执行信号处理组后的结果。

## `.dhcap` 采集数据格式

### 基本信息

| 项 | 当前实现 |
| --- | --- |
| 扩展名 | `.dhcap` |
| 当前格式版本 | `3` |
| 文件魔数 | `DHCAP01\0` |
| 文件尾索引魔数 | `DHCIDX3\0` |
| 原始样本类型 | `float32` |
| 字节序 | Manifest 中记录，当前 Windows 环境为 LittleEndian |
| 默认压缩 | `Lz4 + ByteShuffle` |
| 默认聚合块大小 | `4 MB` |
| 默认分片上限 | `65536 MB`，即 64GB |

`.dhcap` 当前是按采集任务创建文件夹，然后在文件夹内按分片写入：

```text
Data/
  DashCapture_yyyyMMdd_HHmmss/
    DashCapture_yyyyMMdd_HHmmss_0000.dhcap
    DashCapture_yyyyMMdd_HHmmss_0001.dhcap
    ...
```

如果采集数据没有超过分片上限，通常只会有一个 `_0000.dhcap`。

### 文件整体结构

```text
+------------------------------+
| 文件头                       |
| Magic: DHCAP01\0             |
| Version: int32               |
| ManifestLength: int32        |
| ManifestJson: UTF-8 JSON     |
+------------------------------+
| 数据块 1                     |
| 数据块 2                     |
| ...                          |
+------------------------------+
| 文件尾索引                   |
| IndexJson: UTF-8 JSON        |
| IndexLength: int32           |
| FooterMagic: DHCIDX3\0       |
+------------------------------+
```

文件头中的 Manifest 负责描述整次采集和所有通道；文件尾索引用于快速定位每个通道的每个数据块。

### Manifest 内容

Manifest 是 UTF-8 JSON，主要包含：

| 字段类别 | 内容 |
| --- | --- |
| 采集任务 | `RunStem`、`RunFolder`、`SegmentIndex`、开始时间、创建时间 |
| 压缩配置 | 是否启用压缩、实际压缩算法、预处理算法、块大小、压缩参数 |
| 原始数据描述 | `RawType=float32`、`ByteOrder` |
| 回调块描述 | SDK 回调块信息结构说明 |
| 通道列表 | 设备 ID、通道 ID、通道名、采样率、单位、数据索引等 |

通道元数据包括：

| 字段 | 含义 |
| --- | --- |
| `Ordinal` | 文件内通道序号 |
| `DeviceId` | 设备编号 |
| `GroupName` | 设备组名 |
| `SampleRate` | 该通道采样率 |
| `RawType` | 原始数据类型，当前为 `float32` |
| `ByteOrder` | 字节序 |
| `ChannelName` | 通道名，如 `AI001` |
| `Unit` | 单位 |
| `ChannelId` | 设备内通道编号 |
| `DataIndex` | 数据源中的全局数据索引 |
| `LocalDataIndex` | 设备内数据索引 |

### 数据块结构

每个数据块对应一个通道的一段连续样本。当前写入顺序为：

| 顺序 | 字段 | 类型 | 含义 |
| --- | --- | --- | --- |
| 1 | `ChannelOrdinal` | int32 | 文件内通道序号 |
| 2 | `SampleStart` | uint64 | 该块起始样本序号 |
| 3 | `SampleCount` | int32 | 样本数量 |
| 4 | `OriginalLength` | int32 | 原始字节长度 |
| 5 | `Preprocessor` | byte | 预处理算法 |
| 6 | `Codec` | byte | 压缩算法 |
| 7 | `TransformedLength` | int32 | 预处理后字节长度 |
| 8 | `PayloadLength` | int32 | 实际载荷字节长度 |
| 9 | `Flags` | byte | 存储标志 |
| 10 | `RawCrc32` | uint32 | 原始数据 CRC |
| 11 | `PayloadCrc32` | uint32 | 载荷 CRC |
| 12 | `First` | float32 | 该块首样本 |
| 13 | `Last` | float32 | 该块末样本 |
| 14 | `Minimum` | float32 | 该块最小值 |
| 15 | `Maximum` | float32 | 该块最大值 |
| 16 | `Payload` | byte[] | 原始或压缩后的数据 |

其中 `First`、`Last`、`Minimum`、`Maximum` 是为了快速预览和缩放浏览准备的摘要信息。数据查看页的总览条、金字塔预览和降采样读取都依赖这些信息来避免全量读入。

### Flags 含义

| 标志 | 值 | 含义 |
| --- | --- | --- |
| `StoredChunkFlag` | `0x01` | 载荷是直接存储的字节，不需要解压 |
| `PreprocessedChunkFlag` | `0x02` | 载荷写入前做过预处理，读取时需要反向恢复 |
| `RawStoredChunkFlag` | `0x04` | 载荷是未经压缩、未经预处理的原始 float32 字节 |

当压缩关闭时，数据以原始 `float32` 字节写入，并带 `StoredChunkFlag | RawStoredChunkFlag`。

当压缩开启时，流程是：

```text
float32 原始字节
  -> 可选预处理，例如 ByteShuffle
  -> 压缩，例如 LZ4
  -> 如果压缩后不变小，则直接存储预处理后的字节
  -> 写入 .dhcap
```

读取时会根据 `Flags`、`Codec`、`Preprocessor` 反向恢复出原始 `float32` 样本。

### 文件尾索引

文件尾索引是 UTF-8 JSON，包含：

| 字段 | 含义 |
| --- | --- |
| `Version` | 索引格式版本 |
| `RecordCount` | 数据块数量 |
| `RawBytes` | 原始数据总字节数 |
| `StoredBytes` | 实际载荷总字节数 |
| `Records` | 每个数据块的索引记录 |

索引记录保存每个块的通道、样本范围、载荷位置、载荷长度、压缩参数、CRC 和摘要信息。正常停止采样后，软件会写入文件尾索引。强制关闭软件时，文件尾可能不完整，后续读取和修复能力取决于当时已经落盘的内容。

### `.dhcap` 与 TDMS 的关系

`.dhcap` 不是 TDMS。当前设计是：

- 采集时优先写 `.dhcap`，保证吞吐、压缩和快速读取。
- 数据查看页可以打开 `.dhcap`。
- 需要对外交付时，可以导出标准 `.tdms`。

这样做的原因是 TDMS 更适合交换和工具兼容，而 `.dhcap` 更适合本项目的实时高性能链路。

## `.dhfft` FFT 结果格式

### 基本信息

| 项 | 当前实现 |
| --- | --- |
| 扩展名 | `.dhfft` |
| 当前格式版本 | `3` |
| 文件魔数 | `DFFT` |
| 帧魔数 | `FFTF` |
| 结果载荷 | `magnitude_float32` |
| 默认保存目录 | `.\Analysis` |
| 文件名 | `DashCaptureFft_yyyyMMdd_HHmmss.dhfft` |

`.dhfft` 保存的是 FFT 计算后的频谱幅值，不保存原始时域波形，也不保存复数相位。

### 文件整体结构

```text
+------------------------------+
| 文件头                       |
| FileMagic: DFFT              |
| FormatVersion: int32         |
| CreatedAtUtcMs: int64        |
| FFT 配置                     |
+------------------------------+
| FFT 帧 1                     |
| FFT 帧 2                     |
| ...                          |
+------------------------------+
```

`.dhfft` 当前没有单独的文件尾索引。读取时会顺序扫描帧头，并按设备、通道、帧号聚合出概览。

### 文件头字段

| 顺序 | 字段 | 类型 | 含义 |
| --- | --- | --- | --- |
| 1 | `FileMagic` | int32 | 固定为 `DFFT` |
| 2 | `FormatVersion` | int32 | 当前为 `3` |
| 3 | `CreatedAtUtcMs` | int64 | 文件创建 UTC 毫秒时间戳 |
| 4 | `WindowSampleCount` | int32 | 固定窗口模式下的窗口点数；采样率分辨率模式下为 0 |
| 5 | `HopSampleCount` | int32 | 固定窗口模式下的步进点数；采样率分辨率模式下为 0 |
| 6 | `MaxChannels` | int32 | 分析链路最大通道数 |
| 7 | `MaxFftChannels` | int32 | FFT 最大通道数 |
| 8 | `PayloadKind` | string | 当前为 `magnitude_float32` |
| 9 | `ConfiguredBackend` | string | 配置的计算后端，如 Auto/Gpu/Cpu |
| 10 | `InitialBackend` | string | 文件创建时实际计算后端 |
| 11 | `InitialDevice` | string | 文件创建时计算设备 |
| 12 | `WindowMode` | string | `sample_rate_resolution` 或 `fixed_samples` |
| 13 | `TargetResolutionHz` | double | 目标频率分辨率 |
| 14 | `OverlapRatio` | double | 重叠率 |

当前项目默认使用 `sample_rate_resolution`：例如目标分辨率为 1Hz 时，某通道采样率为 10000Hz，则 FFT 窗口通常为 10000 点；采样率为 1000000Hz，则 FFT 窗口通常为 1000000 点。

### FFT 帧结构

每一帧对应一个通道的一个 FFT 窗口。当前写入顺序为：

| 顺序 | 字段 | 类型 | 含义 |
| --- | --- | --- | --- |
| 1 | `FrameMagic` | int32 | 固定为 `FFTF` |
| 2 | `FrameTimestampUtcMs` | int64 | 该帧写入时间 |
| 3 | `SourceSampleTime` | int64 | 数据源样本时间 |
| 4 | `WindowIndex` | int64 | 该通道内 FFT 窗口编号 |
| 5 | `WindowStartSample` | int64 | 窗口起始样本 |
| 6 | `WindowEndSample` | int64 | 窗口结束样本 |
| 7 | `DeviceId` | int32 | 设备编号 |
| 8 | `ChannelId` | int32 | 通道编号 |
| 9 | `DeviceIp` | string | 设备 IP 或设备标识 |
| 10 | `ChannelName` | string | 通道名 |
| 11 | `Unit` | string | 单位 |
| 12 | `ComputeBackend` | string | 实际计算后端，如 `CUDA/cuFFT batch` |
| 13 | `ComputeDevice` | string | 实际计算设备 |
| 14 | `SampleRate` | float32 | 采样率 |
| 15 | `FftSize` | int32 | FFT 点数 |
| 16 | `BinCount` | int32 | 频点数量 |
| 17 | `FrequencyResolution` | float32 | 频率分辨率，通常为 `SampleRate / FftSize` |
| 18 | `PayloadBytes` | int32 | 幅值数组字节数 |
| 19 | `Magnitudes` | float32[] | 频谱幅值数组 |

频率轴的计算方式：

```text
frequency_hz = bin_index * FrequencyResolution
```

当前写入的是单边幅值谱，`BinCount` 通常等于 `FftSize / 2 + 1`。

### FFT 趋势与峰值

`.dhfft` 文件内没有单独存一份趋势表。FFT 结果查看页打开文件后，会按需扫描帧并计算：

- 单帧频谱：读取某个通道的某个窗口。
- 峰值趋势：对每一帧查找最大幅值频点。
- 全部峰值导出：扫描所有帧，输出每帧主峰。

当前 CSV 导出字段如下：

单帧频谱：

```text
frequency_hz,magnitude
```

趋势：

```text
time_seconds,window_index,frame_timestamp_utc,peak_bin,peak_frequency_hz,peak_magnitude
```

全部峰值：

```text
device_id,channel_id,device_ip,channel_name,unit,sample_rate,fft_size,frequency_resolution_hz,time_seconds,window_index,frame_timestamp_utc,backend,compute_device,peak_bin,peak_frequency_hz,peak_magnitude
```

## `.dhanalysis` 信号处理结果格式

### 基本信息

| 项 | 当前实现 |
| --- | --- |
| 扩展名 | `.dhanalysis` |
| 文件内容 | UTF-8 JSON |
| 默认保存目录 | `.\Analysis\SignalProcessing` |
| 同步导出 | 同名 `.csv` |
| 当前内置处理组 | `幅值分析` |

`.dhanalysis` 保存的是对历史 `.dhcap` 或 TDMS 数据执行信号处理组后的结果。它不保存原始波形，只保存每个通道的算法结果和元数据。

当前内置的“幅值分析”处理组包含：

| 算法 | 含义 |
| --- | --- |
| 最大值 | 当前通道历史数据中的最大样本值 |
| 最小值 | 当前通道历史数据中的最小样本值 |
| 峰峰值 | 最大值减最小值 |
| RMS | 均方根有效值 |
| 均值 | 样本平均值 |
| 标准差 | 按全量样本计算的总体标准差 |

### 文件内容

`.dhanalysis` 是 JSON，主要字段包括：

| 字段 | 含义 |
| --- | --- |
| `ModuleName` | 处理组名称，例如 `幅值分析` |
| `Algorithms` | 处理组内算法列表 |
| `SourcePath` | 被处理的历史数据文件或文件夹 |
| `StartedAtUtc` | 处理开始时间 |
| `CompletedAtUtc` | 处理完成时间 |
| `Channels` | 每个通道的处理结果 |

每个通道结果包括设备编号、组名、通道名、单位、采样率、样本数、时长，以及算法结果列表。

### CSV 导出字段

历史处理完成后，软件会自动写出同名 CSV，字段如下：

```text
source_path,module,device_id,group_name,channel_id,channel_name,unit,sample_rate_hz,sample_count,duration_seconds,最大值,最小值,峰峰值,RMS,均值,标准差
```

如果用户导入自定义处理组，CSV 后半部分会按处理组内算法名称动态生成列。

### 处理组导入格式

信号处理页支持导入 JSON 处理组文件，扩展名可以是 `.json` 或 `.dhproc`。示例：

```json
{
  "name": "幅值分析",
  "algorithms": [
    { "name": "最大值", "type": "Maximum" },
    { "name": "最小值", "type": "Minimum" },
    { "name": "峰峰值", "type": "PeakToPeak" },
    { "name": "RMS", "type": "Rms" },
    { "name": "均值", "type": "Mean" },
    { "name": "标准差", "type": "StandardDeviation" }
  ]
}
```

当前支持的算法类型包括：

```text
Maximum, Minimum, PeakToPeak, Mean, Rms, StandardDeviation
```

也支持对应的中文名称，例如 `最大值`、`最小值`、`峰峰值`、`均值`、`标准差`。

## 使用建议

### 内部采集与回放

优先使用 `.dhcap`。它针对当前项目做了分块、压缩、索引和摘要优化，更适合高采样率、多通道实时写入。

### 对外交付

优先导出标准格式：

- 原始采集数据：导出 TDMS。
- FFT 频谱、趋势、峰值：导出 CSV。
- 历史信号处理结果：保存 `.dhanalysis`，并导出 CSV。
- 如果甲方明确要求 HDF5，可以在现有 `.dhcap` 读取能力基础上增加 HDF5 导出。

### 数据完整性

采集正常停止后，`.dhcap` 会写入文件尾索引；`.dhfft` 会 flush 并关闭文件。为了保证文件完整性，测试和交付时应尽量通过软件的“停止采集”结束任务，避免直接强制关闭进程。

### 可恢复性

`.dhcap` 的数据块带 CRC 和摘要，理论上具备更好的定位、校验和部分恢复基础。`.dhfft` 当前是顺序帧格式，如果中途异常结束，已完整写入的帧通常可以被顺序扫描，最后一个不完整帧会被读取器忽略或报错。

## 后续可改进项

1. 为 `.dhcap` 和 `.dhfft` 增加独立的格式规范版本文档，并在每次升级时记录兼容策略。
2. 为 `.dhfft` 增加文件尾索引，提升大文件随机定位某通道、某帧的速度。
3. 为 `.dhfft` 增加可选相位或复数结果保存，支持更完整的频域分析。
4. 增加 HDF5 导出，方便科研和第三方工具读取。
5. 增加独立命令行验证工具，输出文件版本、通道数、帧数、采样率范围、CRC 检查结果等。
