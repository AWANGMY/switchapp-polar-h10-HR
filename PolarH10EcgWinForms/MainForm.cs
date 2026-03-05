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

        private readonly object _captureGate = new object();
        private readonly List<string> _capturedRows = new List<string>();
        private IEcgDataSource _dataSource;
        private int _sampleIndex;

        public MainForm()
        {
            InitializeComponent();
            chkSimulation.Checked = false;
            ConfigureChart();
            UpdateUiState();
            AppendLog("App started. Default mode is real Polar H10 connection.");
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

                SetStatus("Connected");
                AppendLog("Connected.");
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
                lock (_captureGate)
                {
                    _capturedRows.Clear();
                    _capturedRows.Add("timestamp_utc,sample_index,ecg_uV");
                }

                await _dataSource.StartAsync(CancellationToken.None).ConfigureAwait(true);
                SetStatus("Streaming");
                AppendLog("ECG streaming started.");
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
                SetStatus("Connected");
                AppendLog("ECG streaming stopped.");
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
                dialog.FileName = "ecg-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                dialog.Title = "Export ECG CSV";

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

            Series series = chartEcg.Series["EcgSeries"];
            ChartArea area = chartEcg.ChartAreas["EcgArea"];

            lock (_captureGate)
            {
                foreach (double sampleUv in e.Samples)
                {
                    int index = _sampleIndex++;
                    double sampleMv = sampleUv / 1000.0;
                    series.Points.AddXY(index, sampleMv);

                    _capturedRows.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:o},{1},{2:F3}",
                        e.TimestampUtc,
                        index,
                        sampleUv));
                }
            }

            while (series.Points.Count > MaxVisibleSamples)
            {
                series.Points.RemoveAt(0);
            }

            area.AxisX.Minimum = Math.Max(0, _sampleIndex - MaxVisibleSamples);
            area.AxisX.Maximum = Math.Max(MaxVisibleSamples, _sampleIndex);
            btnExport.Enabled = true;
        }

        private void ConfigureChart()
        {
            ChartArea area = chartEcg.ChartAreas["EcgArea"];
            area.AxisX.Title = "Sample Index";
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.Title = "ECG (mV)";
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.Minimum = -2.0;
            area.AxisY.Maximum = 2.0;

            Series series = chartEcg.Series["EcgSeries"];
            series.ChartType = SeriesChartType.FastLine;
            series.Color = Color.FromArgb(42, 98, 161);
            series.BorderWidth = 2;
        }

        private void ClearChart()
        {
            _sampleIndex = 0;
            Series series = chartEcg.Series["EcgSeries"];
            series.Points.Clear();
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

