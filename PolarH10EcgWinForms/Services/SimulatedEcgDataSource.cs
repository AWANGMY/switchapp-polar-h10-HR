using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PolarH10EcgWinForms.Models;

namespace PolarH10EcgWinForms.Services
{
    public sealed class SimulatedEcgDataSource : IEcgDataSource, IDisposable
    {
        private const int TickIntervalMs = 1000;

        private readonly object _gate = new object();
        private readonly Random _random = new Random();
        private Timer _timer;
        private double _currentBpm = 72.0;
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

        public Task StartAsync(int sampleRateHz, CancellationToken cancellationToken)
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

            _timer = new Timer(EmitSamples, null, 0, TickIntervalMs);
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
            double bpm;
            lock (_gate)
            {
                // Slow random walk to mimic realistic resting HR variation.
                double delta = (_random.NextDouble() - 0.5) * 4.0;
                _currentBpm = Math.Max(45.0, Math.Min(180.0, _currentBpm + delta));
                bpm = Math.Round(_currentBpm, 1);
            }

            SamplesReceived?.Invoke(this, new EcgSamplesEventArgs(DateTime.UtcNow, new List<double> { bpm }));
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
