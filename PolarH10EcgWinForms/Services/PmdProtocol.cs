using System;
using System.Collections.Generic;

namespace PolarH10EcgWinForms.Services
{
    internal static class PmdProtocol
    {
        public static readonly Guid PmdServiceUuid = Guid.Parse("FB005C80-02E7-F387-1CAD-8ACD2D8DF0C8");
        public static readonly Guid PmdControlPointCharacteristicUuid = Guid.Parse("FB005C81-02E7-F387-1CAD-8ACD2D8DF0C8");
        public static readonly Guid PmdDataCharacteristicUuid = Guid.Parse("FB005C82-02E7-F387-1CAD-8ACD2D8DF0C8");

        private const byte RequestStartMeasurement = 0x02;
        private const byte RequestStopMeasurement = 0x03;
        private const byte MeasurementTypeEcg = 0x00;

        public static byte[] BuildStartEcgCommand(int sampleRateHz)
        {
            ushort rate = (ushort)Math.Max(1, Math.Min(sampleRateHz, 1000));
            const ushort resolutionBits = 14;

            return new byte[]
            {
                RequestStartMeasurement,
                MeasurementTypeEcg,
                0x00, 0x01, (byte)(rate & 0xFF), (byte)(rate >> 8),
                0x01, 0x01, (byte)(resolutionBits & 0xFF), (byte)(resolutionBits >> 8)
            };
        }

        public static byte[] BuildStopEcgCommand()
        {
            return new byte[]
            {
                RequestStopMeasurement,
                MeasurementTypeEcg
            };
        }

        public static IReadOnlyList<double> TryParseEcgSamples(byte[] packet)
        {
            if (packet == null || packet.Length < 10)
            {
                return Array.Empty<double>();
            }

            if (packet[0] != MeasurementTypeEcg)
            {
                return Array.Empty<double>();
            }

            const int headerLength = 10;
            int sampleBytes = packet.Length - headerLength;
            if (sampleBytes <= 0)
            {
                return Array.Empty<double>();
            }

            int sampleCount = sampleBytes / 3;
            if (sampleCount == 0)
            {
                return Array.Empty<double>();
            }

            var samples = new List<double>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = headerLength + (i * 3);
                int raw = packet[offset] | (packet[offset + 1] << 8) | (packet[offset + 2] << 16);
                if ((raw & 0x00800000) != 0)
                {
                    raw |= unchecked((int)0xFF000000);
                }

                samples.Add(raw);
            }

            return samples;
        }
    }
}
