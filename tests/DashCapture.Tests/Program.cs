using DashCapture.Display;

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

Console.WriteLine("Display downsampling checks passed.");
