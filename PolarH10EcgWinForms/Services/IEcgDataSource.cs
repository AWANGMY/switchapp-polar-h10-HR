using System;
using System.Threading;
using System.Threading.Tasks;
using PolarH10EcgWinForms.Models;

namespace PolarH10EcgWinForms.Services
{
    public interface IEcgDataSource
    {
        event EventHandler<EcgSamplesEventArgs> SamplesReceived;

        bool IsConnected { get; }

        bool IsStreaming { get; }

        Task ConnectAsync(string deviceNameFilter, CancellationToken cancellationToken);

        Task StartAsync(CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);

        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
