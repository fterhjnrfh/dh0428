using DashCapture.Display;
using DashCapture.Core.Models;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

float[] spike = new float[10_000];
spike[4321] = 100;
EnvelopePoint[] envelope = EnvelopeDownsampler.Downsample(spike, 100);
Assert(envelope.Any(p => p.Maximum == 100), "Envelope downsampling must preserve spikes.");

float[] square = Enumerable.Range(0, 1000).Select(i => i < 500 ? -1f : 1f).ToArray();
EnvelopePoint[] squareEnvelope = EnvelopeDownsampler.Downsample(square, 50);
Assert(squareEnvelope.Any(p => p.Minimum < 0) && squareEnvelope.Any(p => p.Maximum > 0), "Envelope downsampling must preserve square wave levels.");

float[] noisy = { float.NaN, -2, float.PositiveInfinity, 3, float.NegativeInfinity };
EnvelopePoint[] noisyEnvelope = EnvelopeDownsampler.Downsample(noisy, 1);
Assert(noisyEnvelope[0].Minimum == -2 && noisyEnvelope[0].Maximum == 3, "Envelope downsampling must ignore non-finite samples.");

EnvelopePoint[] mergedEnvelope = EnvelopeDownsampler.Downsample(new[]
{
    new EnvelopePoint(0, 1, 2, -10, 4),
    new EnvelopePoint(1, 2, 3, -1, 99),
    new EnvelopePoint(2, 3, 4, -3, 5)
}, 1);
Assert(mergedEnvelope[0].Minimum == -10 && mergedEnvelope[0].Maximum == 99, "Envelope merging must preserve extrema.");

var ring = new WaveformRingBuffer(5);
ring.Append(new float[] { 1, 2, 3, 4, 5 });
WaveformRingBuffer resized = ring.Resize(3);
Assert(resized.Snapshot().SequenceEqual(new float[] { 3, 4, 5 }), "Ring buffer resize must preserve newest samples.");

var store = new WaveformStore(8);
var channel = new ChannelDescriptor(0, "127.0.0.1", 0, 0, 0, true, "AI1", SampleRate: 1_000_000);
store.SetVisibleChannels(new[] { channel });
store.AppendEnvelope(channel, new[] { new EnvelopePoint(0, 1, 2, -5, 7) }, 4000);
var snapshot = store.SnapshotSeries(new[] { channel }).Single();
Assert(snapshot.Channel.SampleRate == 1_000_000, "Waveform store must keep the raw hardware sample rate on channel metadata.");
Assert(snapshot.DisplaySampleRate == 4000, "Waveform store must expose a separate display sample rate.");
Assert(snapshot.Points[0].Minimum == -5 && snapshot.Points[0].Maximum == 7, "Waveform store must keep envelope extrema.");
Assert(store.SnapshotSeries(Array.Empty<ChannelDescriptor>()).Count == 0, "Empty monitor views must not render all channels.");

Console.WriteLine("Display downsampling checks passed.");
