using System;
using System.Collections.Generic;

namespace PolarH10EcgWinForms.Services
{
    internal static class PmdProtocol
    {
        public static readonly Guid PmdServiceUuid = Guid.Parse("FB005C80-02E7-F387-1CAD-8ACD2D8DF0C8");
        public static readonly Guid PmdControlPointCharacteristicUuid = Guid.Parse("FB005C81-02E7-F387-1CAD-8ACD2D8DF0C8");
        public static readonly Guid PmdDataCharacteristicUuid = Guid.Parse("FB005C82-02E7-F387-1CAD-8ACD2D8DF0C8");

        public const byte RequestGetMeasurementSettings = 0x01;
        public const byte RequestStartMeasurement = 0x02;
        public const byte RequestStopMeasurement = 0x03;

        public const byte MeasurementTypeEcg = 0x00;
        public const byte ControlPointResponseCode = 0xF0;
        public const byte FeatureReadResponseCode = 0x0F;

        private const byte SettingTypeSampleRate = 0x00;
        private const byte SettingTypeResolution = 0x01;
        private const byte SettingTypeRange = 0x02;
        private const byte SettingTypeRangeMilliunit = 0x03;
        private const byte SettingTypeChannels = 0x04;
        private const byte SettingTypeFactor = 0x05;
        private const byte SettingTypeSecurity = 0x06;

        public static byte[] BuildGetEcgSettingsCommand()
        {
            return new byte[]
            {
                RequestGetMeasurementSettings,
                MeasurementTypeEcg
            };
        }

        public static byte[] BuildStartEcgCommand(int sampleRateHz, int resolutionBits)
        {
            ushort rate = (ushort)Math.Max(1, Math.Min(sampleRateHz, 1000));
            ushort resolution = (ushort)Math.Max(1, Math.Min(resolutionBits, 32));

            return new byte[]
            {
                RequestStartMeasurement,
                MeasurementTypeEcg,
                SettingTypeSampleRate, 0x01, (byte)(rate & 0xFF), (byte)(rate >> 8),
                SettingTypeResolution, 0x01, (byte)(resolution & 0xFF), (byte)(resolution >> 8)
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

        public static bool TryParseAvailableFeatures(byte[] payload, out byte features)
        {
            features = 0;
            if (payload == null || payload.Length < 2)
            {
                return false;
            }

            if (payload[0] != FeatureReadResponseCode)
            {
                return false;
            }

            features = payload[1];
            return true;
        }

        public static bool TryParseControlPointResponse(
            byte[] payload,
            byte expectedOperation,
            byte expectedMeasurementType,
            out byte errorCode,
            out string errorMessage)
        {
            errorCode = 0;
            errorMessage = null;

            if (payload == null || payload.Length < 5)
            {
                errorMessage = "PMD control point response is empty or too short.";
                return false;
            }

            if (payload[0] != ControlPointResponseCode)
            {
                errorMessage = $"Unexpected PMD response header: 0x{payload[0]:X2}.";
                return false;
            }

            if (payload[1] != expectedOperation)
            {
                errorMessage = $"Unexpected PMD operation in response: 0x{payload[1]:X2}.";
                return false;
            }

            if (payload[2] != expectedMeasurementType)
            {
                errorMessage = $"Unexpected PMD measurement type in response: 0x{payload[2]:X2}.";
                return false;
            }

            errorCode = payload[3];
            return true;
        }

        public static bool TryParseEcgSettingsResponse(
            byte[] payload,
            out IReadOnlyList<int> sampleRates,
            out IReadOnlyList<int> resolutions,
            out byte errorCode,
            out string errorMessage)
        {
            sampleRates = Array.Empty<int>();
            resolutions = Array.Empty<int>();
            errorCode = 0;
            errorMessage = null;

            if (!TryParseControlPointResponse(
                    payload,
                    RequestGetMeasurementSettings,
                    MeasurementTypeEcg,
                    out errorCode,
                    out errorMessage))
            {
                return false;
            }

            if (errorCode != 0)
            {
                return true;
            }

            if (payload.Length == 5)
            {
                return true;
            }

            var parsedSampleRates = new List<int>();
            var parsedResolutions = new List<int>();

            int index = 5;
            while (index + 2 <= payload.Length)
            {
                byte settingType = payload[index];
                int valueCount = payload[index + 1];
                int fieldSize = GetFieldSize(settingType);
                index += 2;

                int bytesRequired = valueCount * fieldSize;
                if (index + bytesRequired > payload.Length)
                {
                    errorMessage = "PMD settings payload is truncated.";
                    return false;
                }

                for (int i = 0; i < valueCount; i++)
                {
                    int valueOffset = index + (i * fieldSize);
                    int parsedValue;
                    switch (fieldSize)
                    {
                        case 1:
                            parsedValue = payload[valueOffset];
                            break;
                        case 2:
                            parsedValue = payload[valueOffset] | (payload[valueOffset + 1] << 8);
                            break;
                        case 4:
                            parsedValue = payload[valueOffset]
                                | (payload[valueOffset + 1] << 8)
                                | (payload[valueOffset + 2] << 16)
                                | (payload[valueOffset + 3] << 24);
                            break;
                        default:
                            parsedValue = 0;
                            break;
                    }

                    if (settingType == SettingTypeSampleRate)
                    {
                        parsedSampleRates.Add(parsedValue);
                    }
                    else if (settingType == SettingTypeResolution)
                    {
                        parsedResolutions.Add(parsedValue);
                    }
                }

                index += bytesRequired;
            }

            sampleRates = parsedSampleRates;
            resolutions = parsedResolutions;
            return true;
        }

        public static string DescribeControlPointError(byte errorCode)
        {
            switch (errorCode)
            {
                case 0: return "SUCCESS";
                case 1: return "ERROR_INVALID_OP_CODE";
                case 2: return "ERROR_INVALID_MEASUREMENT_TYPE";
                case 3: return "ERROR_NOT_SUPPORTED";
                case 4: return "ERROR_INVALID_LENGTH";
                case 5: return "ERROR_INVALID_PARAMETER";
                case 6: return "ERROR_ALREADY_IN_STATE";
                case 7: return "ERROR_INVALID_RESOLUTION";
                case 8: return "ERROR_INVALID_SAMPLE_RATE";
                case 9: return "ERROR_INVALID_RANGE";
                case 10: return "ERROR_INVALID_MTU";
                case 11: return "ERROR_INVALID_NUMBER_OF_CHANNELS";
                case 12: return "ERROR_INVALID_STATE";
                case 13: return "ERROR_DEVICE_IN_CHARGER";
                default: return $"UNKNOWN_ERROR_{errorCode}";
            }
        }

        public static int ResolveNearestSupportedValue(int requestedValue, IReadOnlyList<int> supportedValues)
        {
            if (supportedValues == null || supportedValues.Count == 0)
            {
                return requestedValue;
            }

            int bestValue = supportedValues[0];
            int bestDistance = Math.Abs(bestValue - requestedValue);

            for (int i = 1; i < supportedValues.Count; i++)
            {
                int candidate = supportedValues[i];
                int distance = Math.Abs(candidate - requestedValue);
                if (distance < bestDistance)
                {
                    bestValue = candidate;
                    bestDistance = distance;
                }
            }

            return bestValue;
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

        private static int GetFieldSize(byte settingType)
        {
            switch (settingType)
            {
                case SettingTypeSampleRate:
                case SettingTypeResolution:
                case SettingTypeRange:
                    return 2;
                case SettingTypeRangeMilliunit:
                case SettingTypeFactor:
                    return 4;
                case SettingTypeChannels:
                    return 1;
                case SettingTypeSecurity:
                    return 16;
                default:
                    return 2;
            }
        }
    }
}

