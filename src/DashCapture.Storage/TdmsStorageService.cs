using DashCapture.Core.Acquisition;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;

namespace DashCapture.Storage;

public sealed class TdmsStorageService : IAsyncDisposable
{
    private readonly AcquisitionService _acquisition;
    private readonly StorageSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private ICaptureStorageWriter? _writer;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    public TdmsStorageService(AcquisitionService acquisition, StorageSettings settings)
    {
        _acquisition = acquisition;
        _settings = settings;
    }

    public event Action<AcquisitionFault>? Faulted;
    public string CurrentPath => _writer?.CurrentPath ?? string.Empty;
    public string CurrentFolder => _writer?.CurrentFolder ?? string.Empty;
    public string CurrentAuditPath => _writer?.CurrentAuditPath ?? string.Empty;
    public bool IsRunning => _worker is not null && !_worker.IsCompleted;

    public Task StartAsync(IReadOnlyList<DeviceDescriptor> devices, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _writer = _settings.Compression.Enabled
            ? new CompressedCaptureWriter(_settings, devices)
            : new TdmsCaptureWriter(_settings, devices);
        _writer.Faulted += OnWriterFaulted;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await DrainStorageQueueAsync().ConfigureAwait(false);
        _cts?.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_writer is not null)
        {
            _writer.Faulted -= OnWriterFaulted;
            _writer.Dispose();
            _writer = null;
        }
        _cts?.Dispose();
        _cts = null;
        _worker = null;
    }

    private async Task DrainStorageQueueAsync()
    {
        if (_worker is null)
        {
            return;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(1000, _settings.DrainTimeoutMs));
        while (!_worker.IsCompleted && _acquisition.GetTelemetry().StorageQueueDepth > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        int remaining = _acquisition.GetTelemetry().StorageQueueDepth;
        if (remaining > 0)
        {
            Faulted?.Invoke(new AcquisitionFault(
                DateTimeOffset.UtcNow,
                "TDMS_DRAIN_TIMEOUT",
                $"Storage queue still has {remaining} block(s) after waiting for drain timeout."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        if (_writer is null)
        {
            return;
        }

        await foreach (AcquisitionBlock block in _acquisition.StorageReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                _writer.UpdateDeviceRates(_acquisition.Devices);
                _writer.AppendBlock(block);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if ((now - _lastSave).TotalMilliseconds >= _settings.FlushIntervalMs)
                {
                    _writer.Save();
                    _lastSave = now;
                }
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(new AcquisitionFault(DateTimeOffset.UtcNow, "STORAGE_WRITE_FAILED", ex.Message, block.Header.MachineId));
            }
            finally
            {
                block.Release();
                _acquisition.MarkStorageBlockConsumed();
            }
        }
    }

    private void OnWriterFaulted(Exception exception, string path)
    {
        Faulted?.Invoke(new AcquisitionFault(
            DateTimeOffset.UtcNow,
            "STORAGE_WRITE_FAILED",
            $"Storage writer failed for {path}: {exception.Message}"));
    }
}
