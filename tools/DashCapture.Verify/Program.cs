using DashCapture.Storage;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

if (args.Length == 0)
{
    return Fail("Usage: DashCapture.Verify <file.tdms> [audit.raw.csv] [tdm-runtime-dir]\n       DashCapture.Verify stats <file-or-folder> [tdm-runtime-dir]");
}

if (string.Equals(args[0], "stats", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        return Fail("Usage: DashCapture.Verify stats <file-or-folder> [tdm-runtime-dir]");
    }

    string inputPath = args[1];
    string runtimeDir = args.Length >= 3
        ? args[2]
        : Path.GetFullPath(@".\TDM C DLL[官方源文件]\dev\bin\64-bit");

    try
    {
        using TdmsFileReader reader = TdmsFileReader.Open(inputPath, runtimeDir);
        Console.WriteLine($"TDMS source: {reader.FileInfo.Path}");
        Console.WriteLine($"Groups: {reader.FileInfo.Groups.Count}, Channels: {reader.FileInfo.ChannelCount}, MaxSamples: {reader.FileInfo.MaxSampleCount:N0}");
        Console.WriteLine("Device\tGroup\tChannel\tSamples\tSampleRate\tMin\tMax\tPeakToPeak\tHalfAmplitude");

        foreach (TdmsGroupInfo group in reader.FileInfo.Groups)
        {
            foreach (TdmsChannelInfo channel in group.Channels)
            {
                if (channel.SampleCount == 0)
                {
                    Console.WriteLine($"{group.DeviceId + 1}\t{group.Name}\t{channel.Name}\t0\t{channel.SampleRate:0.###}\t0\t0\t0\t0");
                    continue;
                }

                TdmsChannelEnvelope envelope = reader.ReadEnvelope(channel, 0, channel.SampleCount, 1, CancellationToken.None);
                TdmsEnvelopePoint point = envelope.Points.Count > 0
                    ? envelope.Points[0]
                    : new TdmsEnvelopePoint(0, 0, 0, 0, 0);
                double peakToPeak = point.Maximum - point.Minimum;
                double halfAmplitude = peakToPeak / 2.0;
                Console.WriteLine(
                    $"{group.DeviceId + 1}\t{group.Name}\t{channel.Name}\t{channel.SampleCount}\t{channel.SampleRate:0.###}\t{point.Minimum:0.######}\t{point.Maximum:0.######}\t{peakToPeak:0.######}\t{halfAmplitude:0.######}");
            }
        }

        return 0;
    }
    catch (Exception ex)
    {
        return Fail(ex.Message);
    }
}

string tdmsPath = args[0];
string? auditPath = args.Length >= 2 ? args[1] : null;
string tdmRuntimeDir = args.Length >= 3
    ? args[2]
    : Path.GetFullPath(@".\TDM C DLL[官方源文件]\dev\bin\64-bit");

try
{
    TdmsAuditVerificationResult result = TdmsAuditVerifier.Verify(tdmsPath, auditPath, tdmRuntimeDir);

    Console.WriteLine($"TDMS: {result.TdmsPath}");
    Console.WriteLine($"Audit: {result.AuditCsvPath}");
    Console.WriteLine($"Checked blocks: {result.CheckedBlocks}");
    Console.WriteLine($"Checked bytes: {result.CheckedBytes}");

    if (result.Success)
    {
        Console.WriteLine("Result: PASS");
        return 0;
    }

    Console.WriteLine("Result: FAIL");
    foreach (TdmsAuditMismatch mismatch in result.Mismatches.Take(20))
    {
        Console.WriteLine(
            $"Block={mismatch.BlockIndex}, Group={mismatch.GroupId}, Pos={mismatch.TotalDataCount}, Expected={mismatch.ExpectedCrc32}, Actual={mismatch.ActualCrc32}, Reason={mismatch.Reason}");
    }

    return 1;
}
catch (Exception ex)
{
    return Fail(ex.Message);
}
