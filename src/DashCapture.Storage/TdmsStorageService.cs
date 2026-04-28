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
    private TdmsCaptureWriter? _writer;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    public TdmsStorageService(AcquisitionService acquisition, StorageSettings settings)
    {
        _acquisition = acquisition;
        _settings = settings;
    }

    public event Action<AcquisitionFault>? Faulted;
    public string CurrentPath => _writer?.CurrentPath ?? string.Empty;
    public bool IsRunning => _worker is not null && !_worker.IsCompleted;

    public Task StartAsync(IReadOnlyList<DeviceDescriptor> devices, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _writer = new TdmsCaptureWriter(_settings, devices);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

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

        _writer?.Dispose();
        _writer = null;
        _cts?.Dispose();
        _cts = null;
        _worker = null;
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
                Faulted?.Invoke(new AcquisitionFault(DateTimeOffset.UtcNow, "TDMS_WRITE_FAILED", ex.Message, block.Header.MachineId));
            }
            finally
            {
                block.Release();
                _acquisition.MarkStorageBlockConsumed();
            }
        }
    }
}
