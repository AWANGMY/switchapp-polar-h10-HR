using System;
using System.Collections.Generic;

namespace PolarH10EcgWinForms.Models
{
    public sealed class EcgSamplesEventArgs : EventArgs
    {
        public EcgSamplesEventArgs(DateTime timestampUtc, IReadOnlyList<double> samples)
        {
            TimestampUtc = timestampUtc;
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        }

        public DateTime TimestampUtc { get; }

        public IReadOnlyList<double> Samples { get; }
    }
}
