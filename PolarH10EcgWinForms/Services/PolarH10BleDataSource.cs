using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PolarH10EcgWinForms.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace PolarH10EcgWinForms.Services
{
    public sealed class PolarH10BleDataSource : IEcgDataSource, IDisposable
    {
        private static readonly Guid HeartRateServiceUuid = Guid.Parse("0000180D-0000-1000-8000-00805F9B34FB");
        private static readonly Guid HeartRateMeasurementCharacteristicUuid = Guid.Parse("00002A37-0000-1000-8000-00805F9B34FB");

        private const int DiscoveryTimeoutSeconds = 10;
        private const int StopTimeoutSeconds = 2;

        private BluetoothLEDevice _device;
        private GattDeviceService _heartRateService;
        private GattCharacteristic _heartRateCharacteristic;
        private TypedEventHandler<BluetoothLEDevice, object> _connectionStatusChangedHandler;
        private bool _disposed;

        public event EventHandler<EcgSamplesEventArgs> SamplesReceived;

        public bool IsConnected { get; private set; }

        public bool IsStreaming { get; private set; }

        public async Task ConnectAsync(string deviceNameFilter, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (IsConnected)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(deviceNameFilter))
            {
                deviceNameFilter = "Polar H10";
            }

            ulong address = await DiscoverDeviceAddressAsync(deviceNameFilter, cancellationToken);
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_device == null)
            {
                throw new InvalidOperationException("Failed to create BluetoothLEDevice instance.");
            }

            _connectionStatusChangedHandler = (sender, _) =>
            {
                if (sender?.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                {
                    IsStreaming = false;
                    IsConnected = false;
                }
            };
            _device.ConnectionStatusChanged += _connectionStatusChangedHandler;

            try
            {
                await InitializeHeartRateAsync();
                IsConnected = _heartRateCharacteristic != null;
                if (!IsConnected)
                {
                    throw new InvalidOperationException("Polar H10 heart rate characteristic is unavailable.");
                }
            }
            catch
            {
                await DisconnectAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task StartAsync(int sampleRateHz, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!IsConnected)
            {
                throw new InvalidOperationException("Device is not connected.");
            }

            if (IsStreaming)
            {
                return;
            }

            if (_heartRateCharacteristic == null)
            {
                throw new InvalidOperationException("Heart rate characteristic is unavailable.");
            }

            GattClientCharacteristicConfigurationDescriptorValue cccdValue =
                ResolveHeartRateCccdValue(_heartRateCharacteristic.CharacteristicProperties);

            _heartRateCharacteristic.ValueChanged -= HeartRateCharacteristicOnValueChanged;
            _heartRateCharacteristic.ValueChanged += HeartRateCharacteristicOnValueChanged;

            GattCommunicationStatus notifyStatus =
                await _heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

            if (notifyStatus != GattCommunicationStatus.Success)
            {
                _heartRateCharacteristic.ValueChanged -= HeartRateCharacteristicOnValueChanged;
                throw new InvalidOperationException(
                    "Failed to subscribe to heart rate notifications. GATT status: " + notifyStatus);
            }

            IsStreaming = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!IsConnected || !IsStreaming)
            {
                return;
            }

            try
            {
                if (_heartRateCharacteristic != null)
                {
                    _heartRateCharacteristic.ValueChanged -= HeartRateCharacteristicOnValueChanged;
                    await _heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
            }
            catch
            {
            }
            finally
            {
                IsStreaming = false;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    stopCts.CancelAfter(TimeSpan.FromSeconds(StopTimeoutSeconds));
                    await StopAsync(stopCts.Token);
                }
            }
            catch
            {
            }

            if (_heartRateService != null)
            {
                _heartRateService.Dispose();
                _heartRateService = null;
            }

            _heartRateCharacteristic = null;

            if (_device != null && _connectionStatusChangedHandler != null)
            {
                _device.ConnectionStatusChanged -= _connectionStatusChangedHandler;
                _connectionStatusChangedHandler = null;
            }

            _device?.Dispose();
            _device = null;

            IsConnected = false;
            IsStreaming = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
            }
            finally
            {
                _disposed = true;
            }
        }

        private async Task<ulong> DiscoverDeviceAddressAsync(string deviceNameFilter, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> onReceived =
                (sender, args) =>
                {
                    string localName = args.Advertisement.LocalName;
                    if (string.IsNullOrWhiteSpace(localName))
                    {
                        return;
                    }

                    if (localName.IndexOf(deviceNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return;
                    }

                    if (tcs.TrySetResult(args.BluetoothAddress))
                    {
                        sender.Stop();
                    }
                };

            watcher.Received += onReceived;
            watcher.Start();

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(DiscoveryTimeoutSeconds));
                using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    try
                    {
                        return await tcs.Task;
                    }
                    catch (TaskCanceledException)
                    {
                        throw new TimeoutException(
                            $"Unable to find Polar H10 advertisement within timeout window ({DiscoveryTimeoutSeconds}s).");
                    }
                    finally
                    {
                        watcher.Received -= onReceived;
                        watcher.Stop();
                    }
                }
            }
        }

        private async Task InitializeHeartRateAsync()
        {
            if (_device == null)
            {
                throw new InvalidOperationException("BluetoothLEDevice instance is unavailable for GATT initialization.");
            }

            var serviceResult = await _device.GetGattServicesForUuidAsync(
                HeartRateServiceUuid,
                BluetoothCacheMode.Uncached);

            if (serviceResult.Status != GattCommunicationStatus.Success || !serviceResult.Services.Any())
            {
                throw new InvalidOperationException(
                    "Heart rate service query failed. GATT status: " + serviceResult.Status
                    + ", connection status: " + _device.ConnectionStatus + ".");
            }

            _heartRateService = serviceResult.Services[0];

            var characteristicResult = await _heartRateService.GetCharacteristicsForUuidAsync(
                HeartRateMeasurementCharacteristicUuid,
                BluetoothCacheMode.Uncached);

            if (characteristicResult.Status != GattCommunicationStatus.Success
                || !characteristicResult.Characteristics.Any())
            {
                throw new InvalidOperationException(
                    "Heart rate characteristic query failed. GATT status: " + characteristicResult.Status);
            }

            _heartRateCharacteristic = characteristicResult.Characteristics[0];
        }

        private static GattClientCharacteristicConfigurationDescriptorValue ResolveHeartRateCccdValue(
            GattCharacteristicProperties properties)
        {
            if ((properties & GattCharacteristicProperties.Notify) != 0)
            {
                return GattClientCharacteristicConfigurationDescriptorValue.Notify;
            }

            if ((properties & GattCharacteristicProperties.Indicate) != 0)
            {
                return GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            }

            throw new InvalidOperationException("Heart rate characteristic does not support notify/indicate.");
        }

        private void HeartRateCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (args?.CharacteristicValue == null)
            {
                return;
            }

            byte[] payload = BufferToByteArray(args.CharacteristicValue);
            if (!TryParseHeartRate(payload, out int bpm))
            {
                return;
            }

            SamplesReceived?.Invoke(this, new EcgSamplesEventArgs(DateTime.UtcNow, new[] { (double)bpm }));
        }

        private static bool TryParseHeartRate(byte[] payload, out int bpm)
        {
            bpm = 0;
            if (payload == null || payload.Length < 2)
            {
                return false;
            }

            byte flags = payload[0];
            bool isUInt16Format = (flags & 0x01) != 0;
            if (isUInt16Format)
            {
                if (payload.Length < 3)
                {
                    return false;
                }

                bpm = payload[1] | (payload[2] << 8);
            }
            else
            {
                bpm = payload[1];
            }

            return bpm > 0 && bpm < 255;
        }

        private static byte[] BufferToByteArray(IBuffer buffer)
        {
            if (buffer == null)
            {
                return Array.Empty<byte>();
            }

            byte[] bytes = new byte[buffer.Length];
            using (DataReader reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            return bytes;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PolarH10BleDataSource));
            }
        }
    }
}
