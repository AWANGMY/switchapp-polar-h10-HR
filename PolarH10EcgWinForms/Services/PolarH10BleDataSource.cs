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
        private const int DiscoveryTimeoutSeconds = 15;

        private BluetoothLEDevice _device;
        private GattDeviceService _pmdService;
        private GattCharacteristic _controlPointCharacteristic;
        private GattCharacteristic _dataCharacteristic;
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

            ulong address = await DiscoverDeviceAddressAsync(deviceNameFilter, cancellationToken).ConfigureAwait(false);
            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_device == null)
            {
                throw new InvalidOperationException("Failed to create BluetoothLEDevice instance.");
            }

            await InitializePmdAsync().ConfigureAwait(false);
            IsConnected = true;
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

            await WriteControlPointAsync(PmdProtocol.BuildStartEcgCommand(sampleRateHz)).ConfigureAwait(false);
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
                await WriteControlPointAsync(PmdProtocol.BuildStopEcgCommand()).ConfigureAwait(false);
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
                await StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }

            if (_dataCharacteristic != null)
            {
                _dataCharacteristic.ValueChanged -= DataCharacteristicOnValueChanged;
                try
                {
                    await _dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch
                {
                }

                _dataCharacteristic = null;
            }

            _controlPointCharacteristic = null;
            _pmdService?.Dispose();
            _pmdService = null;
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
                        return await tcs.Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        throw new TimeoutException("Unable to find Polar H10 advertisement within timeout window.");
                    }
                    finally
                    {
                        watcher.Received -= onReceived;
                        watcher.Stop();
                    }
                }
            }
        }

        private async Task InitializePmdAsync()
        {
            var serviceResult = await _device.GetGattServicesForUuidAsync(
                PmdProtocol.PmdServiceUuid,
                BluetoothCacheMode.Uncached);

            if (serviceResult.Status != GattCommunicationStatus.Success || !serviceResult.Services.Any())
            {
                throw new InvalidOperationException("PMD service not found on the connected device.");
            }

            _pmdService = serviceResult.Services[0];

            var controlResult = await _pmdService.GetCharacteristicsForUuidAsync(
                PmdProtocol.PmdControlPointCharacteristicUuid,
                BluetoothCacheMode.Uncached);

            if (controlResult.Status != GattCommunicationStatus.Success || !controlResult.Characteristics.Any())
            {
                throw new InvalidOperationException("PMD control point characteristic not found.");
            }

            _controlPointCharacteristic = controlResult.Characteristics[0];

            var dataResult = await _pmdService.GetCharacteristicsForUuidAsync(
                PmdProtocol.PmdDataCharacteristicUuid,
                BluetoothCacheMode.Uncached);

            if (dataResult.Status != GattCommunicationStatus.Success || !dataResult.Characteristics.Any())
            {
                throw new InvalidOperationException("PMD data characteristic not found.");
            }

            _dataCharacteristic = dataResult.Characteristics[0];
            _dataCharacteristic.ValueChanged += DataCharacteristicOnValueChanged;

            GattCommunicationStatus notifyStatus =
                await _dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyStatus != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException("Failed to subscribe to PMD data notifications.");
            }
        }

        private async Task WriteControlPointAsync(byte[] payload)
        {
            if (_controlPointCharacteristic == null)
            {
                throw new InvalidOperationException("Control point characteristic is unavailable.");
            }

            var writer = new DataWriter();
            writer.WriteBytes(payload);
            GattCommunicationStatus status = await _controlPointCharacteristic.WriteValueAsync(
                writer.DetachBuffer(),
                GattWriteOption.WriteWithResponse);

            if (status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException("Failed to write PMD control command.");
            }
        }

        private void DataCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (args?.CharacteristicValue == null)
            {
                return;
            }

            byte[] packet = new byte[args.CharacteristicValue.Length];
            using (DataReader reader = DataReader.FromBuffer(args.CharacteristicValue))
            {
                reader.ReadBytes(packet);
            }

            var samples = PmdProtocol.TryParseEcgSamples(packet);
            if (samples.Count == 0)
            {
                return;
            }

            SamplesReceived?.Invoke(this, new EcgSamplesEventArgs(DateTime.UtcNow, samples));
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
