using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using PolarH10EcgWinForms.Models;
using PolarH10EcgWinForms.Services;

namespace PolarH10EcgWinForms
{
    public partial class MainForm : Form
    {
        private const int MaxVisibleSamples = 1000;
        private const double DefaultAxisYMinBpm = 40.0;
        private const double DefaultAxisYMaxBpm = 200.0;
        private const double AxisYPaddingRatio = 0.12;
        private const double AxisYMinSpanBpm = 10.0;

        private readonly object _captureGate = new object();
        private readonly List<string> _capturedRows = new List<string>();
        private readonly string _csvDelimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        private IEcgDataSource _dataSource;
        private int _sampleIndex;
        private DateTime? _streamStartUtc;

        public MainForm()
        {
            InitializeComponent();
            chkSimulation.Checked = false;
            lblSampleRate.Visible = false;
            cmbSampleRate.Visible = false;
            ConfigureChart();
            UpdateUiState();
            AppendLog("App started. Default mode is real Polar H10 heart rate connection.");
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            btnConnect.Enabled = false;
            try
            {
                await ResetDataSourceAsync().ConfigureAwait(true);

                _dataSource = CreateDataSource();
                _dataSource.SamplesReceived += OnSamplesReceived;

                SetStatus("Connecting...");
                AppendLog("Connecting to data source...");
                await _dataSource.ConnectAsync(txtDeviceFilter.Text.Trim(), CancellationToken.None).ConfigureAwait(true);

                if (chkSimulation.Checked)
                {
                    SetStatus("Connected (Simulation)");
                    AppendLog("Connected in simulation mode (no physical device).");
                }
                else
                {
                    SetStatus("Connected (Polar H10)");
                    AppendLog("Connected to Polar H10 heart rate service.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Connect failed: " + ex.Message);
                SetStatus("Disconnected");
                await ResetDataSourceAsync().ConfigureAwait(true);
            }
            finally
            {
                UpdateUiState();
            }
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (_dataSource == null)
            {
                return;
            }

            btnStart.Enabled = false;
            try
            {
                ClearChart();
                _streamStartUtc = null;

                lock (_captureGate)
                {
                    _capturedRows.Clear();
                    _capturedRows.Add(string.Format(CultureInfo.InvariantCulture, "time{0}bpm", _csvDelimiter));
                }

                await _dataSource.StartAsync(1, CancellationToken.None).ConfigureAwait(true);
                SetStatus(chkSimulation.Checked ? "Streaming Heart Rate (Simulation)" : "Streaming Heart Rate (Polar H10)");
                AppendLog("Heart rate streaming started.");
            }
            catch (Exception ex)
            {
                AppendLog("Start failed: " + ex.Message);
            }
            finally
            {
                UpdateUiState();
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            if (_dataSource == null)
            {
                return;
            }

            btnStop.Enabled = false;
            try
            {
                await _dataSource.StopAsync(CancellationToken.None).ConfigureAwait(true);
                SetStatus(chkSimulation.Checked ? "Connected (Simulation)" : "Connected (Polar H10)");
                AppendLog("Heart rate streaming stopped.");
            }
            catch (Exception ex)
            {
                AppendLog("Stop failed: " + ex.Message);
            }
            finally
            {
                UpdateUiState();
            }
        }

        private async void btnDisconnect_Click(object sender, EventArgs e)
        {
            btnDisconnect.Enabled = false;
            try
            {
                await ResetDataSourceAsync().ConfigureAwait(true);
                SetStatus("Disconnected");
                AppendLog("Disconnected.");
            }
            finally
            {
                UpdateUiState();
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            ClearChart();
            AppendLog("Chart cleared.");
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            List<string> rows;
            lock (_captureGate)
            {
                rows = new List<string>(_capturedRows);
            }

            if (rows.Count <= 1)
            {
                AppendLog("No captured samples to export.");
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                dialog.FileName = "heart-rate-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                dialog.Title = "Export Heart Rate CSV";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    File.WriteAllLines(dialog.FileName, rows);
                    AppendLog("Exported CSV: " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    AppendLog("Export failed: " + ex.Message);
                }
            }
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await ResetDataSourceAsync().ConfigureAwait(true);
        }

        private IEcgDataSource CreateDataSource()
        {
            return chkSimulation.Checked
                ? (IEcgDataSource)new SimulatedEcgDataSource()
                : new PolarH10BleDataSource();
        }

        private async Task ResetDataSourceAsync()
        {
            if (_dataSource == null)
            {
                return;
            }

            var oldDataSource = _dataSource;
            _dataSource = null;

            oldDataSource.SamplesReceived -= OnSamplesReceived;

            try
            {
                await oldDataSource.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
            }

            if (oldDataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private void OnSamplesReceived(object sender, EcgSamplesEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, EcgSamplesEventArgs>(OnSamplesReceived), sender, e);
                return;
            }

            if (!_streamStartUtc.HasValue)
            {
                _streamStartUtc = e.TimestampUtc;
            }

            Series series = chartEcg.Series["EcgSeries"];
            ChartArea area = chartEcg.ChartAreas["EcgArea"];

            lock (_captureGate)
            {
                foreach (double bpm in e.Samples)
                {
                    int index = _sampleIndex++;
                    series.Points.AddXY(index, bpm);

                    double elapsedSeconds = (e.TimestampUtc - _streamStartUtc.Value).TotalSeconds;
                    _capturedRows.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:F3}{2}{1:F1}",
                        elapsedSeconds,
                        bpm,
                        _csvDelimiter));
                }
            }

            while (series.Points.Count > MaxVisibleSamples)
            {
                series.Points.RemoveAt(0);
            }

            UpdateDynamicYAxis(area, series);
            area.AxisX.Minimum = Math.Max(0, _sampleIndex - MaxVisibleSamples);
            area.AxisX.Maximum = Math.Max(MaxVisibleSamples, _sampleIndex);
            btnExport.Enabled = true;
        }

        private void ConfigureChart()
        {
            ChartArea area = chartEcg.ChartAreas["EcgArea"];
            area.AxisX.Title = "Sample Index";
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.Title = "Heart Rate (bpm)";
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.Minimum = DefaultAxisYMinBpm;
            area.AxisY.Maximum = DefaultAxisYMaxBpm;

            Series series = chartEcg.Series["EcgSeries"];
            series.ChartType = SeriesChartType.FastLine;
            series.Color = Color.FromArgb(42, 98, 161);
            series.BorderWidth = 2;
        }

        private void ClearChart()
        {
            _sampleIndex = 0;
            _streamStartUtc = null;

            Series series = chartEcg.Series["EcgSeries"];
            series.Points.Clear();

            ChartArea area = chartEcg.ChartAreas["EcgArea"];
            area.AxisY.Minimum = DefaultAxisYMinBpm;
            area.AxisY.Maximum = DefaultAxisYMaxBpm;
        }

        private void UpdateDynamicYAxis(ChartArea area, Series series)
        {
            if (series.Points.Count == 0)
            {
                area.AxisY.Minimum = DefaultAxisYMinBpm;
                area.AxisY.Maximum = DefaultAxisYMaxBpm;
                return;
            }

            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;

            foreach (DataPoint point in series.Points)
            {
                if (point.YValues == null || point.YValues.Length == 0)
                {
                    continue;
                }

                double y = point.YValues[0];
                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }

            if (double.IsPositiveInfinity(minY) || double.IsNegativeInfinity(maxY))
            {
                area.AxisY.Minimum = DefaultAxisYMinBpm;
                area.AxisY.Maximum = DefaultAxisYMaxBpm;
                return;
            }

            double span = maxY - minY;
            if (span < AxisYMinSpanBpm)
            {
                double center = (maxY + minY) / 2.0;
                minY = center - (AxisYMinSpanBpm / 2.0);
                maxY = center + (AxisYMinSpanBpm / 2.0);
                span = AxisYMinSpanBpm;
            }

            double padding = Math.Max(span * AxisYPaddingRatio, 1.0);
            area.AxisY.Minimum = minY - padding;
            area.AxisY.Maximum = maxY + padding;
        }

        private void UpdateUiState()
        {
            bool connected = _dataSource != null && _dataSource.IsConnected;
            bool streaming = connected && _dataSource.IsStreaming;

            bool hasCapturedData;
            lock (_captureGate)
            {
                hasCapturedData = _capturedRows.Count > 1;
            }

            btnConnect.Enabled = !connected;
            btnDisconnect.Enabled = connected;
            btnStart.Enabled = connected && !streaming;
            btnStop.Enabled = connected && streaming;
            btnExport.Enabled = hasCapturedData;
            chkSimulation.Enabled = !connected;
            txtDeviceFilter.Enabled = !connected;
            cmbSampleRate.Enabled = false;
        }

        private void SetStatus(string message)
        {
            toolStripStatusLabelValue.Text = message;
        }

        private void AppendLog(string message)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
    }
}
