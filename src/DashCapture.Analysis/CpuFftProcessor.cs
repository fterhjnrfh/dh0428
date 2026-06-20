using System.Numerics;

namespace DashCapture.Analysis;

public sealed class CpuFftProcessor
{
    private readonly Dictionary<int, FftPlan> _plans = new();

    public void ComputeMagnitude(ReadOnlySpan<float> samples, Span<float> magnitudes)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        int fftSize = samples.Length;
        int binCount = fftSize / 2 + 1;
        if (magnitudes.Length < binCount)
        {
            throw new ArgumentException("Magnitude buffer is smaller than the required FFT bin count.", nameof(magnitudes));
        }

        GetPlan(fftSize).ComputeMagnitude(samples, magnitudes.Slice(0, binCount));
    }

    private FftPlan GetPlan(int fftSize)
    {
        if (_plans.TryGetValue(fftSize, out FftPlan? plan))
        {
            return plan;
        }

        plan = IsPowerOfTwo(fftSize)
            ? new Radix2FftPlan(fftSize)
            : new BluesteinFftPlan(fftSize);
        _plans[fftSize] = plan;
        return plan;
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    private abstract class FftPlan
    {
        protected FftPlan(int fftSize)
        {
            FftSize = fftSize;
            BinCount = fftSize / 2 + 1;
        }

        protected int FftSize { get; }
        protected int BinCount { get; }

        public abstract void ComputeMagnitude(ReadOnlySpan<float> samples, Span<float> magnitudes);

        protected void StoreMagnitudeBins(ReadOnlySpan<Complex> spectrum, Span<float> magnitudes)
        {
            double scale = 1.0 / FftSize;
            for (int i = 0; i < BinCount; i++)
            {
                magnitudes[i] = (float)(spectrum[i].Magnitude * scale);
            }
        }
    }

    private sealed class Radix2FftPlan : FftPlan
    {
        private readonly Complex[] _buffer;

        public Radix2FftPlan(int fftSize)
            : base(fftSize)
        {
            _buffer = new Complex[fftSize];
        }

        public override void ComputeMagnitude(ReadOnlySpan<float> samples, Span<float> magnitudes)
        {
            for (int i = 0; i < FftSize; i++)
            {
                _buffer[i] = new Complex(samples[i], 0);
            }

            Transform(_buffer, inverse: false);
            StoreMagnitudeBins(_buffer, magnitudes);
        }
    }

    private sealed class BluesteinFftPlan : FftPlan
    {
        private readonly int _convolutionSize;
        private readonly Complex[] _chirp;
        private readonly Complex[] _kernelSpectrum;
        private readonly Complex[] _buffer;

        public BluesteinFftPlan(int fftSize)
            : base(fftSize)
        {
            _convolutionSize = NextPowerOfTwo(checked(fftSize * 2 - 1));
            _chirp = new Complex[fftSize];
            _kernelSpectrum = new Complex[_convolutionSize];
            _buffer = new Complex[_convolutionSize];
            BuildPlan();
        }

        public override void ComputeMagnitude(ReadOnlySpan<float> samples, Span<float> magnitudes)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            for (int i = 0; i < FftSize; i++)
            {
                _buffer[i] = samples[i] * _chirp[i];
            }

            Transform(_buffer, inverse: false);
            for (int i = 0; i < _buffer.Length; i++)
            {
                _buffer[i] *= _kernelSpectrum[i];
            }

            Transform(_buffer, inverse: true);
            double scale = 1.0 / FftSize;
            for (int i = 0; i < BinCount; i++)
            {
                Complex value = _buffer[i] * _chirp[i];
                magnitudes[i] = (float)(value.Magnitude * scale);
            }
        }

        private void BuildPlan()
        {
            for (int i = 0; i < FftSize; i++)
            {
                double angle = -Math.PI * i * (double)i / FftSize;
                _chirp[i] = Complex.FromPolarCoordinates(1, angle);
            }

            _kernelSpectrum[0] = Complex.One;
            for (int i = 1; i < FftSize; i++)
            {
                double angle = Math.PI * i * (double)i / FftSize;
                Complex value = Complex.FromPolarCoordinates(1, angle);
                _kernelSpectrum[i] = value;
                _kernelSpectrum[_convolutionSize - i] = value;
            }

            Transform(_kernelSpectrum, inverse: false);
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        if (value <= 1)
        {
            return 1;
        }

        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }

    private static void Transform(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = 2 * Math.PI / length * (inverse ? 1 : -1);
            Complex step = Complex.FromPolarCoordinates(1, angle);
            int halfLength = length >> 1;
            for (int start = 0; start < n; start += length)
            {
                Complex factor = Complex.One;
                for (int offset = 0; offset < halfLength; offset++)
                {
                    Complex even = buffer[start + offset];
                    Complex odd = buffer[start + offset + halfLength] * factor;
                    buffer[start + offset] = even + odd;
                    buffer[start + offset + halfLength] = even - odd;
                    factor *= step;
                }
            }
        }

        if (!inverse)
        {
            return;
        }

        double scale = 1.0 / n;
        for (int i = 0; i < n; i++)
        {
            buffer[i] *= scale;
        }
    }
}
