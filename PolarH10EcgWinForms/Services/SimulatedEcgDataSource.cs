using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolarH10EcgWinForms.Models;

namespace PolarH10EcgWinForms.Services
{
    public sealed class SimulatedEcgDataSource : IEcgDataSource, IDisposable
    {
        private readonly object _gate = new object();
        private readonly Random _random = new Random();
        private Timer _timer;
        private double _timeSeconds;
        private bool _disposed;

        public event EventHandler<EcgSamplesEventArgs> SamplesReceived;

        public bool IsConnected { get; private set; }

        public bool IsStreaming { get; private set; }

        public Task ConnectAsync(string deviceNameFilter, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!IsConnected)
            {
                throw new InvalidOperationException("Data source is not connected.");
            }

            if (IsStreaming)
            {
                return Task.CompletedTask;
            }

            _timer = new Timer(EmitSamples, null, 0, 40);
            IsStreaming = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _timer?.Dispose();
            _timer = null;
            IsStreaming = false;
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await StopAsync(cancellationToken).ConfigureAwait(false);
            IsConnected = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        private void EmitSamples(object state)
        {
            const double sampleRate = 130.0;
            const int sampleCountPerTick = 5;
            var samples = new List<double>(sampleCountPerTick);

            lock (_gate)
            {
                for (int i = 0; i < sampleCountPerTick; i++)
                {
                    double heartCycle = _timeSeconds % 1.0;
                    double baselineMv = 0.10 * Math.Sin(2.0 * Math.PI * 1.2 * _timeSeconds);
                    double qrsSpikeMv = heartCycle < 0.03 ? 1.2 * Math.Exp(-220.0 * heartCycle) : 0.0;
                    double noiseMv = (_random.NextDouble() - 0.5) * 0.04;
                    double sampleUv = (baselineMv + qrsSpikeMv + noiseMv) * 1000.0;

                    samples.Add(sampleUv);
                    _timeSeconds += 1.0 / sampleRate;
                }
            }

            SamplesReceived?.Invoke(this, new EcgSamplesEventArgs(DateTime.UtcNow, samples));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SimulatedEcgDataSource));
            }
        }
    }
}
