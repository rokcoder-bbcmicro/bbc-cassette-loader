using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bbc_cassette_loader
{
    class HighPassFilter : ISampleProvider
    {
        private readonly ISampleProvider sourceProvider;
        private readonly BiQuadFilter filter;

        public HighPassFilter(ISampleProvider sourceProvider, int cutOffFrequency)
        {
            this.sourceProvider = sourceProvider;
            filter = BiQuadFilter.HighPassFilter(sourceProvider.WaveFormat.SampleRate, cutOffFrequency, 1);
        }

        public WaveFormat WaveFormat => sourceProvider.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = sourceProvider.Read(buffer, offset, count);
            for (int n = 0; n < samplesRead; n++)
                buffer[offset + n] = filter.Transform(buffer[offset + n]);
            return samplesRead;
        }
    }
}
