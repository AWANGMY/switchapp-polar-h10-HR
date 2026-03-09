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
        private const int DiscoveryTimeoutSeconds = 30;
        private const int ControlPointResponseTimeoutSeconds = 10;
        private const int StopTimeoutSeconds = 2;
        private const int DefaultEcgResolutionBits = 14;
        private const byte EcgFeatureBit = 0x01;

        private readonly object _controlPointResponseGate = new object();
        private readonly SemaphoreSlim _controlPointCommandLock = new SemaphoreSlim(1, 1);

        private BluetoothLEDevice _device;
        private GattDeviceService _pmdService;
        private GattCharacteristic _controlPointCharacteristic;
        private GattCharacteristic _dataCharacteristic;
        private TypedEventHandler<BluetoothLEDevice, object> _connectionStatusChangedHandler;
        private TaskCompletionSource<byte[]> _pendingControlPointResponse;
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
                await InitializePmdAsync().ConfigureAwait(false);
                IsConnected = _device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (!IsConnected)
                {
                    throw new InvalidOperationException("Polar H10 disconnected during PMD initialization.");
                }
            }
            catch
            {
                await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
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

            int requestedSampleRate = Math.Max(1, Math.Min(sampleRateHz, 1000));
            int resolvedSampleRate = requestedSampleRate;
            int resolvedResolution = DefaultEcgResolutionBits;

            byte[] settingsResponse = await WriteControlPointAndWaitResponseAsync(
                PmdProtocol.BuildGetEcgSettingsCommand(),
                cancellationToken).ConfigureAwait(false);

            if (!PmdProtocol.TryParseEcgSettingsResponse(
                    settingsResponse,
                    out var supportedSampleRates,
                    out var supportedResolutions,
                    out byte settingsErrorCode,
                    out string settingsParseError))
            {
                throw new InvalidOperationException("Failed to parse PMD ECG settings response: " + settingsParseError);
            }

            if (settingsErrorCode != 0)
            {
                throw new InvalidOperationException(
                    "PMD rejected ECG settings request: " + PmdProtocol.DescribeControlPointError(settingsErrorCode));
            }

            resolvedSampleRate = PmdProtocol.ResolveNearestSupportedValue(requestedSampleRate, supportedSampleRates);
            resolvedResolution = PmdProtocol.ResolveNearestSupportedValue(DefaultEcgResolutionBits, supportedResolutions);

            byte[] startResponse = await WriteControlPointAndWaitResponseAsync(
                PmdProtocol.BuildStartEcgCommand(resolvedSampleRate, resolvedResolution),
                cancellationToken).ConfigureAwait(false);

            if (!PmdProtocol.TryParseControlPointResponse(
                    startResponse,
                    PmdProtocol.RequestStartMeasurement,
                    PmdProtocol.MeasurementTypeEcg,
                    out byte startErrorCode,
                    out string startParseError))
            {
                throw new InvalidOperationException("Failed to parse PMD ECG start response: " + startParseError);
            }

            if (startErrorCode != 0)
            {
                throw new InvalidOperationException(
                    "PMD rejected ECG start command: " + PmdProtocol.DescribeControlPointError(startErrorCode));
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
                byte[] stopResponse = await WriteControlPointAndWaitResponseAsync(
                    PmdProtocol.BuildStopEcgCommand(),
                    cancellationToken).ConfigureAwait(false);

                if (PmdProtocol.TryParseControlPointResponse(
                    stopResponse,
                    PmdProtocol.RequestStopMeasurement,
                    PmdProtocol.MeasurementTypeEcg,
                    out byte stopErrorCode,
                    out string stopParseError))
                {
                    if (stopErrorCode != 0 && stopErrorCode != 6)
                    {
                        throw new InvalidOperationException(
                            "PMD rejected ECG stop command: " + PmdProtocol.DescribeControlPointError(stopErrorCode));
                    }
                }
                else
                {
                    throw new InvalidOperationException("Failed to parse PMD ECG stop response: " + stopParseError);
                }
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
                    await StopAsync(stopCts.Token).ConfigureAwait(false);
                }
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
                        GattClientCharacteristicConfigurationDescriptorValue.None).ConfigureAwait(false);
                }
                catch
                {
                }

                _dataCharacteristic = null;
            }

            if (_controlPointCharacteristic != null)
            {
                _controlPointCharacteristic.ValueChanged -= ControlPointCharacteristicOnValueChanged;
                try
                {
                    await _controlPointCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None).ConfigureAwait(false);
                }
                catch
                {
                }

                _controlPointCharacteristic = null;
            }

            lock (_controlPointResponseGate)
            {
                _pendingControlPointResponse?.TrySetCanceled();
                _pendingControlPointResponse = null;
            }

            _pmdService?.Dispose();
            _pmdService = null;

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
                _controlPointCommandLock.Dispose();
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
                        throw new TimeoutException("Unable to find Polar H10 advertisement within timeout window (30s).");
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
            if (_device?.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                throw new InvalidOperationException("Polar H10 is not connected at GATT initialization stage.");
            }

            var serviceResult = await _device.GetGattServicesForUuidAsync(
                PmdProtocol.PmdServiceUuid,
                BluetoothCacheMode.Uncached).ConfigureAwait(false);

            if (serviceResult.Status != GattCommunicationStatus.Success || !serviceResult.Services.Any())
            {
                throw new InvalidOperationException(
                    "PMD service query failed. GATT status: " + serviceResult.Status);
            }

            _pmdService = serviceResult.Services[0];

            var controlResult = await _pmdService.GetCharacteristicsForUuidAsync(
                PmdProtocol.PmdControlPointCharacteristicUuid,
                BluetoothCacheMode.Uncached).ConfigureAwait(false);

            if (controlResult.Status != GattCommunicationStatus.Success || !controlResult.Characteristics.Any())
            {
                throw new InvalidOperationException(
                    "PMD control point characteristic query failed. GATT status: " + controlResult.Status);
            }

            _controlPointCharacteristic = controlResult.Characteristics[0];
            _controlPointCharacteristic.ValueChanged += ControlPointCharacteristicOnValueChanged;

            GattCommunicationStatus controlCccdStatus = await EnableControlPointIndicationOrNotificationAsync().ConfigureAwait(false);
            if (controlCccdStatus != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException(
                    "Failed to subscribe to PMD control point notifications/indications. GATT status: " + controlCccdStatus);
            }

            bool ecgFeatureSupported = await IsEcgFeatureSupportedAsync().ConfigureAwait(false);
            if (!ecgFeatureSupported)
            {
                throw new InvalidOperationException("Connected Polar device does not report PMD ECG feature support.");
            }

            var dataResult = await _pmdService.GetCharacteristicsForUuidAsync(
                PmdProtocol.PmdDataCharacteristicUuid,
                BluetoothCacheMode.Uncached).ConfigureAwait(false);

            if (dataResult.Status != GattCommunicationStatus.Success || !dataResult.Characteristics.Any())
            {
                throw new InvalidOperationException(
                    "PMD data characteristic query failed. GATT status: " + dataResult.Status);
            }

            _dataCharacteristic = dataResult.Characteristics[0];
            _dataCharacteristic.ValueChanged += DataCharacteristicOnValueChanged;

            GattCommunicationStatus dataNotifyStatus = await _dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).ConfigureAwait(false);

            if (dataNotifyStatus != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException(
                    "Failed to subscribe to PMD data notifications. GATT status: " + dataNotifyStatus);
            }
        }

        private async Task<GattCommunicationStatus> EnableControlPointIndicationOrNotificationAsync()
        {
            if (_controlPointCharacteristic == null)
            {
                throw new InvalidOperationException("Control point characteristic is unavailable.");
            }

            GattClientCharacteristicConfigurationDescriptorValue cccdValue;
            GattCharacteristicProperties properties = _controlPointCharacteristic.CharacteristicProperties;

            if ((properties & GattCharacteristicProperties.Indicate) != 0)
            {
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            }
            else if ((properties & GattCharacteristicProperties.Notify) != 0)
            {
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
            }
            else
            {
                throw new InvalidOperationException("PMD control point characteristic does not support indications/notifications.");
            }

            return await _controlPointCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue)
                .ConfigureAwait(false);
        }

        private async Task<bool> IsEcgFeatureSupportedAsync()
        {
            if (_controlPointCharacteristic == null)
            {
                throw new InvalidOperationException("Control point characteristic is unavailable.");
            }

            GattReadResult readResult = await _controlPointCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached)
                .ConfigureAwait(false);

            if (readResult.Status != GattCommunicationStatus.Success || readResult.Value == null)
            {
                throw new InvalidOperationException(
                    "Failed to read PMD control point features. GATT status: " + readResult.Status);
            }

            byte[] payload = BufferToByteArray(readResult.Value);
            if (!PmdProtocol.TryParseAvailableFeatures(payload, out byte featureMask))
            {
                throw new InvalidOperationException("PMD control point feature response format is invalid.");
            }

            return (featureMask & EcgFeatureBit) != 0;
        }

        private async Task<byte[]> WriteControlPointAndWaitResponseAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (_controlPointCharacteristic == null)
            {
                throw new InvalidOperationException("Control point characteristic is unavailable.");
            }

            bool lockTaken = false;
            TaskCompletionSource<byte[]> responseAwaiter = null;

            try
            {
                await _controlPointCommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                lockTaken = true;

                responseAwaiter = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_controlPointResponseGate)
                {
                    _pendingControlPointResponse = responseAwaiter;
                }

                var writer = new DataWriter();
                writer.WriteBytes(payload);
                GattCommunicationStatus writeStatus = await _controlPointCharacteristic.WriteValueAsync(
                    writer.DetachBuffer(),
                    GattWriteOption.WriteWithResponse).ConfigureAwait(false);

                if (writeStatus != GattCommunicationStatus.Success)
                {
                    throw new InvalidOperationException(
                        "Failed to write PMD control command. GATT status: " + writeStatus);
                }

                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(ControlPointResponseTimeoutSeconds));
                    using (timeoutCts.Token.Register(() => responseAwaiter.TrySetCanceled()))
                    {
                        try
                        {
                            return await responseAwaiter.Task.ConfigureAwait(false);
                        }
                        catch (TaskCanceledException)
                        {
                            throw new TimeoutException("Timed out waiting for PMD control point response.");
                        }
                    }
                }
            }
            finally
            {
                lock (_controlPointResponseGate)
                {
                    if (responseAwaiter != null && ReferenceEquals(_pendingControlPointResponse, responseAwaiter))
                    {
                        _pendingControlPointResponse = null;
                    }
                }

                if (lockTaken)
                {
                    _controlPointCommandLock.Release();
                }
            }
        }

        private void ControlPointCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (args?.CharacteristicValue == null)
            {
                return;
            }

            byte[] payload = BufferToByteArray(args.CharacteristicValue);
            TaskCompletionSource<byte[]> pendingResponse;
            lock (_controlPointResponseGate)
            {
                pendingResponse = _pendingControlPointResponse;
            }

            pendingResponse?.TrySetResult(payload);
        }

        private void DataCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (args?.CharacteristicValue == null)
            {
                return;
            }

            byte[] packet = BufferToByteArray(args.CharacteristicValue);
            var samples = PmdProtocol.TryParseEcgSamples(packet);
            if (samples.Count == 0)
            {
                return;
            }

            SamplesReceived?.Invoke(this, new EcgSamplesEventArgs(DateTime.UtcNow, samples));
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

