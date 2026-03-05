using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace PolarH10EcgWinForms
{
    partial class MainForm
    {
        private IContainer components = null;
        private Panel topPanel;
        private Label lblDeviceFilter;
        private TextBox txtDeviceFilter;
        private CheckBox chkSimulation;
        private Button btnConnect;
        private Button btnStart;
        private Button btnStop;
        private Button btnDisconnect;
        private Button btnClear;
        private Button btnExport;
        private Chart chartEcg;
        private TextBox txtLog;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel toolStripStatusLabelCaption;
        private ToolStripStatusLabel toolStripStatusLabelValue;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new Container();
            ChartArea chartArea1 = new ChartArea();
            Series series1 = new Series();
            this.topPanel = new Panel();
            this.btnExport = new Button();
            this.btnClear = new Button();
            this.btnDisconnect = new Button();
            this.btnStop = new Button();
            this.btnStart = new Button();
            this.btnConnect = new Button();
            this.chkSimulation = new CheckBox();
            this.txtDeviceFilter = new TextBox();
            this.lblDeviceFilter = new Label();
            this.chartEcg = new Chart();
            this.txtLog = new TextBox();
            this.statusStrip1 = new StatusStrip();
            this.toolStripStatusLabelCaption = new ToolStripStatusLabel();
            this.toolStripStatusLabelValue = new ToolStripStatusLabel();
            this.topPanel.SuspendLayout();
            ((ISupportInitialize)(this.chartEcg)).BeginInit();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // topPanel
            // 
            this.topPanel.Controls.Add(this.btnExport);
            this.topPanel.Controls.Add(this.btnClear);
            this.topPanel.Controls.Add(this.btnDisconnect);
            this.topPanel.Controls.Add(this.btnStop);
            this.topPanel.Controls.Add(this.btnStart);
            this.topPanel.Controls.Add(this.btnConnect);
            this.topPanel.Controls.Add(this.chkSimulation);
            this.topPanel.Controls.Add(this.txtDeviceFilter);
            this.topPanel.Controls.Add(this.lblDeviceFilter);
            this.topPanel.Dock = DockStyle.Top;
            this.topPanel.Location = new System.Drawing.Point(0, 0);
            this.topPanel.Name = "topPanel";
            this.topPanel.Size = new System.Drawing.Size(1000, 74);
            this.topPanel.TabIndex = 0;
            // 
            // btnExport
            // 
            this.btnExport.Location = new System.Drawing.Point(450, 40);
            this.btnExport.Name = "btnExport";
            this.btnExport.Size = new System.Drawing.Size(84, 24);
            this.btnExport.TabIndex = 8;
            this.btnExport.Text = "Export CSV";
            this.btnExport.UseVisualStyleBackColor = true;
            this.btnExport.Click += new EventHandler(this.btnExport_Click);
            // 
            // btnClear
            // 
            this.btnClear.Location = new System.Drawing.Point(360, 40);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(84, 24);
            this.btnClear.TabIndex = 7;
            this.btnClear.Text = "Clear";
            this.btnClear.UseVisualStyleBackColor = true;
            this.btnClear.Click += new EventHandler(this.btnClear_Click);
            // 
            // btnDisconnect
            // 
            this.btnDisconnect.Location = new System.Drawing.Point(270, 40);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(84, 24);
            this.btnDisconnect.TabIndex = 6;
            this.btnDisconnect.Text = "Disconnect";
            this.btnDisconnect.UseVisualStyleBackColor = true;
            this.btnDisconnect.Click += new EventHandler(this.btnDisconnect_Click);
            // 
            // btnStop
            // 
            this.btnStop.Location = new System.Drawing.Point(180, 40);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(84, 24);
            this.btnStop.TabIndex = 5;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new EventHandler(this.btnStop_Click);
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(90, 40);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(84, 24);
            this.btnStart.TabIndex = 4;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new EventHandler(this.btnStart_Click);
            // 
            // btnConnect
            // 
            this.btnConnect.Location = new System.Drawing.Point(12, 40);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(72, 24);
            this.btnConnect.TabIndex = 3;
            this.btnConnect.Text = "Connect";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new EventHandler(this.btnConnect_Click);
            // 
            // chkSimulation
            // 
            this.chkSimulation.AutoSize = true;
            this.chkSimulation.Location = new System.Drawing.Point(282, 13);
            this.chkSimulation.Name = "chkSimulation";
            this.chkSimulation.Size = new System.Drawing.Size(104, 19);
            this.chkSimulation.TabIndex = 2;
            this.chkSimulation.Text = "Simulation mode (no device)";
            this.chkSimulation.UseVisualStyleBackColor = true;
            // 
            // txtDeviceFilter
            // 
            this.txtDeviceFilter.Location = new System.Drawing.Point(103, 10);
            this.txtDeviceFilter.Name = "txtDeviceFilter";
            this.txtDeviceFilter.Size = new System.Drawing.Size(160, 23);
            this.txtDeviceFilter.TabIndex = 1;
            this.txtDeviceFilter.Text = "Polar H10";
            // 
            // lblDeviceFilter
            // 
            this.lblDeviceFilter.AutoSize = true;
            this.lblDeviceFilter.Location = new System.Drawing.Point(12, 13);
            this.lblDeviceFilter.Name = "lblDeviceFilter";
            this.lblDeviceFilter.Size = new System.Drawing.Size(85, 15);
            this.lblDeviceFilter.TabIndex = 0;
            this.lblDeviceFilter.Text = "Device filter:";
            // 
            // chartEcg
            // 
            chartArea1.Name = "EcgArea";
            this.chartEcg.ChartAreas.Add(chartArea1);
            this.chartEcg.Dock = DockStyle.Fill;
            this.chartEcg.Location = new System.Drawing.Point(0, 74);
            this.chartEcg.Name = "chartEcg";
            series1.ChartArea = "EcgArea";
            series1.Name = "EcgSeries";
            this.chartEcg.Series.Add(series1);
            this.chartEcg.Size = new System.Drawing.Size(1000, 475);
            this.chartEcg.TabIndex = 1;
            this.chartEcg.Text = "chart1";
            // 
            // txtLog
            // 
            this.txtLog.Dock = DockStyle.Bottom;
            this.txtLog.Location = new System.Drawing.Point(0, 549);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(1000, 109);
            this.txtLog.TabIndex = 2;
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new ToolStripItem[] {
            this.toolStripStatusLabelCaption,
            this.toolStripStatusLabelValue});
            this.statusStrip1.Location = new System.Drawing.Point(0, 658);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(1000, 22);
            this.statusStrip1.TabIndex = 3;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabelCaption
            // 
            this.toolStripStatusLabelCaption.Name = "toolStripStatusLabelCaption";
            this.toolStripStatusLabelCaption.Size = new System.Drawing.Size(45, 17);
            this.toolStripStatusLabelCaption.Text = "Status:";
            // 
            // toolStripStatusLabelValue
            // 
            this.toolStripStatusLabelValue.Name = "toolStripStatusLabelValue";
            this.toolStripStatusLabelValue.Size = new System.Drawing.Size(79, 17);
            this.toolStripStatusLabelValue.Text = "Disconnected";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1000, 680);
            this.Controls.Add(this.chartEcg);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.topPanel);
            this.Name = "MainForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Polar H10 ECG Monitor";
            this.FormClosing += new FormClosingEventHandler(this.MainForm_FormClosing);
            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            ((ISupportInitialize)(this.chartEcg)).EndInit();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}

