
namespace bbc_cassette_loader
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea1 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
			System.Windows.Forms.DataVisualization.Charting.StripLine stripLine1 = new System.Windows.Forms.DataVisualization.Charting.StripLine();
			System.Windows.Forms.DataVisualization.Charting.Legend legend1 = new System.Windows.Forms.DataVisualization.Charting.Legend();
			System.Windows.Forms.DataVisualization.Charting.Series series1 = new System.Windows.Forms.DataVisualization.Charting.Series();
			this.menuStrip = new System.Windows.Forms.MenuStrip();
			this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.newToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.loadToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.exportRawFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.recoveryPassesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.buttonPanel = new System.Windows.Forms.Panel();
			this.contentSplitContainer = new System.Windows.Forms.SplitContainer();
			this.fileListBox = new System.Windows.Forms.ListBox();
			this.waveChart = new System.Windows.Forms.DataVisualization.Charting.Chart();
			this.save = new System.Windows.Forms.Button();
			this.export = new System.Windows.Forms.Button();
			this.buttonTest = new System.Windows.Forms.Button();
			this.channelModeComboBox = new System.Windows.Forms.ComboBox();
			this.buttonCancelImport = new System.Windows.Forms.Button();
			this.buttonListen = new System.Windows.Forms.Button();
			this.openRawFileDialog = new System.Windows.Forms.OpenFileDialog();
			this.menuStrip.SuspendLayout();
			this.buttonPanel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.contentSplitContainer)).BeginInit();
			this.contentSplitContainer.Panel1.SuspendLayout();
			this.contentSplitContainer.Panel2.SuspendLayout();
			this.contentSplitContainer.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.waveChart)).BeginInit();
			this.SuspendLayout();
			// 
			// menuStrip
			// 
			this.menuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
			this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem});
			this.menuStrip.Location = new System.Drawing.Point(0, 0);
			this.menuStrip.Name = "menuStrip";
			this.menuStrip.Padding = new System.Windows.Forms.Padding(5, 1, 0, 1);
			this.menuStrip.Size = new System.Drawing.Size(771, 26);
			this.menuStrip.TabIndex = 0;
			this.menuStrip.Text = "menuStrip";
			// 
			// fileToolStripMenuItem
			// 
			this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.newToolStripMenuItem,
            this.loadToolStripMenuItem,
            this.saveToolStripMenuItem,
            this.exportRawFilesToolStripMenuItem,
            this.recoveryPassesMenuItem});
			this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
			this.fileToolStripMenuItem.Size = new System.Drawing.Size(46, 24);
			this.fileToolStripMenuItem.Text = "File";
			// 
			// newToolStripMenuItem
			// 
			this.newToolStripMenuItem.Name = "newToolStripMenuItem";
			this.newToolStripMenuItem.Size = new System.Drawing.Size(125, 26);
			this.newToolStripMenuItem.Text = "Import WAV...";
			this.newToolStripMenuItem.Click += new System.EventHandler(this.newToolStripMenuItem_Click);
			// 
			// loadToolStripMenuItem
			// 
			this.loadToolStripMenuItem.Name = "loadToolStripMenuItem";
			this.loadToolStripMenuItem.Size = new System.Drawing.Size(125, 26);
			this.loadToolStripMenuItem.Text = "Load";
			// 
			// saveToolStripMenuItem
			// 
			this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
			this.saveToolStripMenuItem.Size = new System.Drawing.Size(125, 26);
			this.saveToolStripMenuItem.Text = "Save recovery state";
			this.exportRawFilesToolStripMenuItem.Name = "exportRawFilesToolStripMenuItem";
			this.exportRawFilesToolStripMenuItem.Text = "Export raw files...";
			this.exportRawFilesToolStripMenuItem.Click += new System.EventHandler(this.exportRawFilesToolStripMenuItem_Click);
			this.recoveryPassesMenuItem.Name = "recoveryPassesMenuItem";
            this.recoveryPassesMenuItem.Text = "Use recovery passes (slower)";
			this.recoveryPassesMenuItem.CheckOnClick = true;
            this.recoveryPassesMenuItem.ToolTipText = "Try eight complementary passes and combine CRC-valid blocks. Existing recovered data is preserved.";
			// 
			// buttonPanel
			// 
			this.buttonPanel.AutoSize = true;
			this.buttonPanel.Controls.Add(this.contentSplitContainer);
			this.buttonPanel.Controls.Add(this.save);
			this.buttonPanel.Controls.Add(this.export);
			this.buttonPanel.Controls.Add(this.buttonTest);
			this.buttonPanel.Controls.Add(this.channelModeComboBox);
			this.buttonPanel.Controls.Add(this.buttonCancelImport);
			this.buttonPanel.Controls.Add(this.buttonListen);
			this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.buttonPanel.Location = new System.Drawing.Point(0, 26);
			this.buttonPanel.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.buttonPanel.Name = "buttonPanel";
			this.buttonPanel.Size = new System.Drawing.Size(771, 740);
			this.buttonPanel.TabIndex = 1;
			// 
			// contentSplitContainer
			// 
			this.contentSplitContainer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.contentSplitContainer.Location = new System.Drawing.Point(0, 40);
			this.contentSplitContainer.Name = "contentSplitContainer";
			this.contentSplitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
			//
			// contentSplitContainer.Panel1
			//
			this.contentSplitContainer.Panel1.Controls.Add(this.fileListBox);
			this.contentSplitContainer.Panel1MinSize = 80;
			//
			// contentSplitContainer.Panel2
			//
			this.contentSplitContainer.Panel2.Controls.Add(this.waveChart);
			this.contentSplitContainer.Panel2MinSize = 120;
			this.contentSplitContainer.Size = new System.Drawing.Size(771, 700);
			this.contentSplitContainer.SplitterDistance = 330;
			this.contentSplitContainer.SplitterWidth = 6;
			this.contentSplitContainer.TabIndex = 4;
			//
			// fileListBox
			//
			this.fileListBox.Dock = System.Windows.Forms.DockStyle.Fill;
			this.fileListBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
			this.fileListBox.FormattingEnabled = true;
			this.fileListBox.HorizontalScrollbar = true;
			this.fileListBox.ItemHeight = 20;
			this.fileListBox.Location = new System.Drawing.Point(0, 0);
			this.fileListBox.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
			this.fileListBox.Name = "fileListBox";
			this.fileListBox.Size = new System.Drawing.Size(771, 330);
			this.fileListBox.TabIndex = 4;
			this.fileListBox.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.fileListBox_DrawItem);
			//
			// waveChart
			//
			this.waveChart.Dock = System.Windows.Forms.DockStyle.Fill;
			chartArea1.AxisX.LabelStyle.Enabled = false;
			chartArea1.AxisX.LineColor = System.Drawing.Color.Transparent;
			chartArea1.AxisX.MajorGrid.Enabled = false;
			chartArea1.AxisX.MajorTickMark.Enabled = false;
			chartArea1.AxisX.ScaleView.Size = 1000D;
			chartArea1.AxisX.ScaleView.SmallScrollSize = 50D;
			chartArea1.AxisX.ScrollBar.ButtonStyle = System.Windows.Forms.DataVisualization.Charting.ScrollBarButtonStyles.SmallScroll;
			stripLine1.Interval = 2.5D;
			chartArea1.AxisX.StripLines.Add(stripLine1);
			chartArea1.AxisY.Interval = 0.5D;
			chartArea1.AxisY.Maximum = 1D;
			chartArea1.AxisY.Minimum = -1D;
			chartArea1.Name = "ChartArea1";
			this.waveChart.ChartAreas.Add(chartArea1);
			legend1.Name = "Legend1";
			this.waveChart.Legends.Add(legend1);
			this.waveChart.Location = new System.Drawing.Point(0, 0);
			this.waveChart.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
			this.waveChart.Name = "waveChart";
			series1.ChartArea = "ChartArea1";
			series1.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastPoint;
			series1.IsXValueIndexed = true;
			series1.Legend = "Legend1";
			series1.MarkerSize = 2;
			series1.Name = "waveSeries";
			series1.YValuesPerPoint = 10;
			this.waveChart.Series.Add(series1);
			this.waveChart.Size = new System.Drawing.Size(771, 364);
			this.waveChart.TabIndex = 6;
			this.waveChart.Text = "chart1";
			// 
			// save
			// 
			this.save.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.save.Location = new System.Drawing.Point(563, 4);
			this.save.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.save.Name = "save";
			this.save.Size = new System.Drawing.Size(100, 28);
			this.save.TabIndex = 8;
			this.save.Text = "Save recovery state";
			this.save.UseVisualStyleBackColor = true;
			this.save.Click += new System.EventHandler(this.save_Click);
			// 
			// export
			// 
			this.export.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.export.Location = new System.Drawing.Point(670, 4);
			this.export.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.export.Name = "export";
			this.export.Size = new System.Drawing.Size(100, 28);
			this.export.TabIndex = 7;
			this.export.Text = "Export";
			this.export.UseVisualStyleBackColor = true;
			this.export.Click += new System.EventHandler(this.export_Click);
			// 
			// buttonTest
			// 
			this.buttonTest.Location = new System.Drawing.Point(112, 4);
			this.buttonTest.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.buttonTest.Name = "buttonTest";
			this.buttonTest.Size = new System.Drawing.Size(100, 28);
			this.buttonTest.TabIndex = 1;
			this.buttonTest.Text = "Import WAV...";
			this.buttonTest.UseVisualStyleBackColor = true;
			this.buttonTest.Click += new System.EventHandler(this.buttonTest_Click);
			//
			// channelModeComboBox
			//
			this.channelModeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.channelModeComboBox.FormattingEnabled = true;
			this.channelModeComboBox.Items.AddRange(new object[] {
			"Mix stereo",
			"Left channel",
			"Right channel"});
			this.channelModeComboBox.Location = new System.Drawing.Point(220, 5);
			this.channelModeComboBox.Name = "channelModeComboBox";
			this.channelModeComboBox.Size = new System.Drawing.Size(130, 24);
			this.channelModeComboBox.TabIndex = 2;
			//
			// buttonCancelImport
			// 
			this.buttonCancelImport.Enabled = false;
			this.buttonCancelImport.Location = new System.Drawing.Point(358, 4);
			this.buttonCancelImport.Name = "buttonCancelImport";
			this.buttonCancelImport.Size = new System.Drawing.Size(120, 28);
			this.buttonCancelImport.TabIndex = 3;
			this.buttonCancelImport.Text = "Cancel Import";
			this.buttonCancelImport.UseVisualStyleBackColor = true;
			this.buttonCancelImport.Click += new System.EventHandler(this.buttonCancelImport_Click);
			//
			// buttonListen
			// 
			this.buttonListen.Location = new System.Drawing.Point(4, 4);
			this.buttonListen.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.buttonListen.Name = "buttonListen";
			this.buttonListen.Size = new System.Drawing.Size(100, 28);
			this.buttonListen.TabIndex = 0;
			this.buttonListen.Text = "Line-in options  ▾";
			this.buttonListen.UseVisualStyleBackColor = true;
			// 
			// openRawFileDialog
			// 
			this.openRawFileDialog.DefaultExt = "wav";
			this.openRawFileDialog.FileName = "openRawFileDialog";
			this.openRawFileDialog.Multiselect = true;
			this.openRawFileDialog.FileOk += new System.ComponentModel.CancelEventHandler(this.openRawFileDialog_FileOk);
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(771, 766);
			this.Controls.Add(this.buttonPanel);
			this.Controls.Add(this.menuStrip);
			this.MainMenuStrip = this.menuStrip;
			this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
			this.MinimumSize = new System.Drawing.Size(640, 360);
			this.Name = "MainForm";
			this.Text = "Beeb Loader";
			this.Load += new System.EventHandler(this.MainForm_Load);
			this.menuStrip.ResumeLayout(false);
			this.menuStrip.PerformLayout();
			this.buttonPanel.ResumeLayout(false);
			this.contentSplitContainer.Panel1.ResumeLayout(false);
			this.contentSplitContainer.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.contentSplitContainer)).EndInit();
			this.contentSplitContainer.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.waveChart)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem newToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem loadToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportRawFilesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem recoveryPassesMenuItem;
        private System.Windows.Forms.Panel buttonPanel;
        private System.Windows.Forms.SplitContainer contentSplitContainer;
        private System.Windows.Forms.Button buttonListen;
        private System.Windows.Forms.Button buttonTest;
        private System.Windows.Forms.ComboBox channelModeComboBox;
        private System.Windows.Forms.Button buttonCancelImport;
        private System.Windows.Forms.OpenFileDialog openRawFileDialog;
        private System.Windows.Forms.ListBox fileListBox;
        private System.Windows.Forms.DataVisualization.Charting.Chart waveChart;
        private System.Windows.Forms.Button save;
        private System.Windows.Forms.Button export;
    }
}
