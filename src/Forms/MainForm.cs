using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace bbc_cassette_loader
{
	public partial class MainForm : Form
	{
		BBCFileHandler fileHandler;
		CancellationTokenSource fileImportCancellation;
		CancellationTokenSource updateCheckCancellation;
		bool closeWhenImportStops;
		readonly List<StripLine> diagnosticStripLines = new List<StripLine>();
		Panel blockMapPanel;
		Label selectedFileLabel;
		Label selectedFileStatusLabel;
		Label diagnosticTitleLabel;
		ToolStripStatusLabel recoverySummaryLabel;
		ToolStripStatusLabel activityStatusLabel;
		ToolStripProgressBar importProgressBar;
		string importingFile;
		ContextMenuStrip importOptionsMenu;
		ContextMenuStrip exportMenu;
		Button importOptionsButton;
		ToolStripMenuItem exportCswToolStripMenuItem;
		ToolStripMenuItem listenLineInMenuItem;
		ToolStripMenuItem recordLineInMenuItem;
		ContextMenuStrip lineInOptionsMenu;
		ToolStripMenuItem lineInOptionsListenMenuItem;
		ToolStripMenuItem lineInOptionsRecordMenuItem;
		ToolStripMenuItem importWavMenuItem;
		ToolStripMenuItem fileImportWavMenuItem;
		ToolStripMenuItem topAudioChannelMenuItem;
		ToolStripMenuItem topRecoveryPassesMenuItem;
		ToolStripMenuItem topCrcRepairMenuItem;
		ToolStripMenuItem topCaptureDeviceMenuItem;
		ToolStripMenuItem exportTopMenu;
		ToolStripMenuItem restoreRecoveryMenuItem;
		ToolStripMenuItem checkForUpdatesMenuItem;
		ToolStripMenuItem[] importChannelItems;
		ToolStripMenuItem[] topAudioChannelItems;
		ToolStripMenuItem captureDeviceMenu;
		ToolStripMenuItem crcRepairMenuItem;
		int selectedCaptureDevice;
		int crcRepairHeaders;
		int crcRepairData;
		int crcRepairBits;
		FileUIData selectedFileData;
		SplitContainer detailSplitContainer;
		double filePaneRatio = 0.36;
		double blockPaneRatio = 0.42;
		bool adjustingSplitters;
		bool lineInActive;
		internal const string KoFiSupportUrl = "https://ko-fi.com/rokcoder";

		// The test suite supplies this so close-policy tests never need to drive a
		// native message box. Normal application use leaves it null.
		internal Func<DialogResult> CloseRecoveryPromptOverride { get; set; }
		readonly string recoveryStateDirectoryOverride;

		public MainForm()
			: this(null)
		{
		}

		internal MainForm(string recoveryStateDirectoryOverride)
		{
			this.recoveryStateDirectoryOverride = recoveryStateDirectoryOverride;
			InitializeComponent();
			ConfigureModernLayout();
		}

		void ConfigureModernLayout()
		{
			if (components == null) components = new Container();
			Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
			BackColor = Color.FromArgb(247, 248, 250);
			MinimumSize = new Size(640, 480);

			newToolStripMenuItem.Text = "New cassette...";
			loadToolStripMenuItem.Visible = false;
			saveToolStripMenuItem.Text = "Save recovery state";
			saveToolStripMenuItem.Click += save_Click;
			exportRawFilesToolStripMenuItem.Text = "Recovered files and .inf...";
			exportCswToolStripMenuItem = new ToolStripMenuItem("CSW cassette image...");
			exportCswToolStripMenuItem.Click += export_Click;
			fileToolStripMenuItem.DropDownItems.Insert(
				fileToolStripMenuItem.DropDownItems.IndexOf(exportRawFilesToolStripMenuItem) + 1,
				exportCswToolStripMenuItem);
			recoveryPassesMenuItem.Text = "Use recovery passes (slower)";

			buttonTest.Text = "Import WAV...";
			buttonListen.Text = "Line-in options  ▾";
			buttonCancelImport.Text = "Cancel";
			buttonCancelImport.Visible = false;
			channelModeComboBox.Visible = false;
			save.Visible = false;
			export.Text = "Export  ▾";
			export.Click -= export_Click;

			StyleCommandButton(buttonTest, true);
			StyleCommandButton(buttonListen, false);
			StyleCommandButton(buttonCancelImport, false);
			StyleCommandButton(export, false);

			importOptionsButton = new Button { Name = "importOptionsButton", Text = "Import options  ▾", AutoSize = true };
			StyleCommandButton(importOptionsButton, false);
			BuildCommandMenus();
			buttonListen.Click += (sender, args) =>
			{
				if (lineInActive)
					buttonListen_Click(sender, args);
				else
					lineInOptionsMenu.Show(buttonListen, 0, buttonListen.Height);
			};
			importOptionsButton.Click += (sender, args) => importOptionsMenu.Show(importOptionsButton, 0, importOptionsButton.Height);
			export.Click += (sender, args) => exportMenu.Show(export, export.Width - exportMenu.PreferredSize.Width, export.Height);

			var commandBar = new Panel
			{
				Name = "commandBar",
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				Padding = new Padding(8, 5, 8, 5),
				Margin = Padding.Empty
			};
			var commandLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};
			commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			var leftCommands = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = false,
				WrapContents = false,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};
			leftCommands.Controls.Add(buttonTest);
			leftCommands.Controls.Add(buttonListen);
			leftCommands.Controls.Add(importOptionsButton);
			leftCommands.Controls.Add(buttonCancelImport);
			var rightCommands = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				WrapContents = false,
				FlowDirection = FlowDirection.RightToLeft,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};
			rightCommands.Controls.Add(export);
			commandLayout.Controls.Add(leftCommands, 0, 0);
			commandLayout.Controls.Add(rightCommands, 1, 0);
			commandBar.Controls.Add(commandLayout);

			fileListBox.DrawMode = DrawMode.OwnerDrawFixed;
			fileListBox.ItemHeight = 54;
			fileListBox.BorderStyle = BorderStyle.None;
			fileListBox.BackColor = Color.White;
			fileListBox.IntegralHeight = false;
			fileListBox.SelectedIndexChanged += FileListBox_SelectedIndexChanged;

			selectedFileLabel = new Label
			{
				AutoSize = true,
				Font = new Font(Font, FontStyle.Bold),
				Text = "Select a recovered file",
				Location = new Point(14, 11)
			};
			selectedFileStatusLabel = new Label
			{
				AutoSize = true,
				ForeColor = Color.DimGray,
				Text = "Its block recovery map will appear here.",
				Location = new Point(14, 31)
			};
			var detailHeader = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = Color.White };
			detailHeader.Controls.Add(selectedFileLabel);
			detailHeader.Controls.Add(selectedFileStatusLabel);

			blockMapPanel = new Panel
			{
				Name = "blockMapPanel",
				Dock = DockStyle.Fill,
				AutoScroll = true,
				BackColor = Color.White,
				Padding = new Padding(12)
			};
			blockMapPanel.Paint += BlockMapPanel_Paint;
			blockMapPanel.MouseClick += BlockMapPanel_MouseClick;

			var blockDetailPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
			blockDetailPanel.Controls.Add(blockMapPanel);
			blockDetailPanel.Controls.Add(detailHeader);

			diagnosticTitleLabel = new Label
			{
				Dock = DockStyle.Top,
				Height = 36,
				Padding = new Padding(14, 10, 0, 0),
				Text = "Diagnostic waveform",
				Font = new Font(Font, FontStyle.Bold),
				BackColor = Color.FromArgb(247, 248, 250)
			};
			waveChart.BackColor = Color.FromArgb(247, 248, 250);
			waveChart.ChartAreas["ChartArea1"].BackColor = Color.White;
			waveChart.Legends["Legend1"].Enabled = false;
			var diagnosticPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 248, 250) };
			diagnosticPanel.Controls.Add(waveChart);
			diagnosticPanel.Controls.Add(diagnosticTitleLabel);

			detailSplitContainer = new SplitContainer
			{
				Dock = DockStyle.Fill,
				Size = new Size(480, 600),
				Orientation = Orientation.Horizontal,
				SplitterWidth = 6,
				Panel1MinSize = 135,
				Panel2MinSize = 150,
				SplitterDistance = 235,
				BackColor = Color.FromArgb(220, 224, 231)
			};
			detailSplitContainer.Panel1.Controls.Add(blockDetailPanel);
			detailSplitContainer.Panel2.Controls.Add(diagnosticPanel);

			contentSplitContainer.Orientation = Orientation.Vertical;
			contentSplitContainer.Dock = DockStyle.Fill;
			contentSplitContainer.Margin = Padding.Empty;
			contentSplitContainer.SplitterWidth = 6;
			contentSplitContainer.Panel1MinSize = 230;
			contentSplitContainer.Panel2MinSize = 360;
			contentSplitContainer.SplitterDistance = 275;
			contentSplitContainer.BackColor = Color.FromArgb(220, 224, 231);
			contentSplitContainer.Panel2.Controls.Clear();
			contentSplitContainer.Panel2.Controls.Add(detailSplitContainer);
			contentSplitContainer.SplitterMoved += (sender, args) => RememberSplitterRatios();
			detailSplitContainer.SplitterMoved += (sender, args) => RememberSplitterRatios();
			SizeChanged += (sender, args) => ApplySplitterRatios();

			var statusStrip = new StatusStrip
			{
				Name = "statusStrip",
				Dock = DockStyle.Bottom,
				SizingGrip = false,
				BackColor = Color.White
			};
			recoverySummaryLabel = new ToolStripStatusLabel("No recovered files");
			var statusSpacer = new ToolStripStatusLabel { Name = "statusSpacer", Spring = true };
			importProgressBar = new ToolStripProgressBar
			{
				Name = "importProgressBar",
				Minimum = 0,
				Maximum = 100,
				Size = new Size(120, 16),
				Visible = false
			};
			activityStatusLabel = new ToolStripStatusLabel("Ready")
			{
				Name = "activityStatusLabel",
				TextAlign = ContentAlignment.MiddleRight
			};
			statusStrip.Items.Add(recoverySummaryLabel);
			statusStrip.Items.Add(statusSpacer);
			statusStrip.Items.Add(importProgressBar);
			statusStrip.Items.Add(activityStatusLabel);
			statusStrip.Margin = Padding.Empty;

			var rootLayout = new TableLayoutPanel
			{
				Name = "rootLayout",
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};
			rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
			rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
			rootLayout.Controls.Add(commandBar, 0, 0);
			rootLayout.Controls.Add(contentSplitContainer, 0, 1);
			rootLayout.Controls.Add(statusStrip, 0, 2);

			buttonPanel.Controls.Clear();
			buttonPanel.AutoSize = false;
			buttonPanel.Padding = Padding.Empty;
			buttonPanel.Controls.Add(rootLayout);
			ApplySplitterRatios();
		}

		void RememberSplitterRatios()
		{
			if (adjustingSplitters) return;
			var horizontalSpace = contentSplitContainer.ClientSize.Width - contentSplitContainer.SplitterWidth;
			if (horizontalSpace > 0)
				filePaneRatio = (double)contentSplitContainer.SplitterDistance / horizontalSpace;
			var verticalSpace = detailSplitContainer.ClientSize.Height - detailSplitContainer.SplitterWidth;
			if (verticalSpace > 0)
				blockPaneRatio = (double)detailSplitContainer.SplitterDistance / verticalSpace;
		}

		void ApplySplitterRatios()
		{
			if (adjustingSplitters || detailSplitContainer == null) return;
			adjustingSplitters = true;
			try
			{
				SetSplitterRatio(contentSplitContainer, filePaneRatio);
				SetSplitterRatio(detailSplitContainer, blockPaneRatio);
			}
			finally { adjustingSplitters = false; }
		}

		static void SetSplitterRatio(SplitContainer split, double ratio)
		{
			var extent = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
			var available = extent - split.SplitterWidth;
			var maximum = extent - split.Panel2MinSize - split.SplitterWidth;
			if (available <= 0 || maximum < split.Panel1MinSize) return;
			var desired = (int)Math.Round(available * ratio);
			split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(maximum, desired));
		}

		void StyleCommandButton(Button button, bool primary)
		{
			button.AutoSize = true;
			button.Height = 32;
			button.MinimumSize = new Size(0, 32);
			button.Margin = new Padding(0, 0, 8, 0);
			button.Padding = new Padding(10, 0, 10, 0);
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 1;
			button.FlatAppearance.BorderColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(210, 215, 224);
			button.BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White;
			button.ForeColor = primary ? Color.White : Color.FromArgb(28, 36, 48);
			button.UseVisualStyleBackColor = false;
		}

		void BuildCommandMenus()
		{
			importOptionsMenu = new ContextMenuStrip();
			components.Add(importOptionsMenu);
			captureDeviceMenu = new ToolStripMenuItem("Capture device");
			captureDeviceMenu.DropDownOpening += (sender, args) => PopulateCaptureDevices();
			importOptionsMenu.Items.Add(captureDeviceMenu);
			var channels = new ToolStripMenuItem("Audio channel");
			importChannelItems = new[]
			{
				new ToolStripMenuItem("Mix stereo") { Checked = true },
				new ToolStripMenuItem("Left channel"),
				new ToolStripMenuItem("Right channel")
			};
			for (var index = 0; index < importChannelItems.Length; index++)
			{
				var channelIndex = index;
				importChannelItems[index].Click += (sender, args) => channelModeComboBox.SelectedIndex = channelIndex;
			}
			channels.DropDownItems.AddRange(importChannelItems);
			channels.DropDownOpening += (sender, args) => UpdateChannelMenuChecks();
			importOptionsMenu.Items.Add(channels);
			importOptionsMenu.Items.Add(recoveryPassesMenuItem);
			crcRepairMenuItem = new ToolStripMenuItem("Attempt conservative CRC repair")
			{
				Name = "crcRepairMenuItem",
				CheckOnClick = true,
				ToolTipText = "After the selected WAV recovery pass or passes, try only unique low-confidence bit repairs validated by cassette CRC."
			};
			importOptionsMenu.Items.Add(crcRepairMenuItem);

			exportMenu = new ContextMenuStrip();
			components.Add(exportMenu);
			var rawExport = new ToolStripMenuItem("Recovered files and .inf...");
			rawExport.Click += exportRawFilesToolStripMenuItem_Click;
			var cswExport = new ToolStripMenuItem("CSW cassette image...");
			cswExport.Click += export_Click;
			var diskExport = new ToolStripMenuItem("BBC Micro disk image...");
			diskExport.Click += exportDiskImage_Click;
			exportMenu.Items.Add(rawExport);
			exportMenu.Items.Add(cswExport);
			exportMenu.Items.Add(diskExport);

			fileToolStripMenuItem.DropDownItems.Clear();
			fileToolStripMenuItem.DropDownItems.Add(newToolStripMenuItem);
			restoreRecoveryMenuItem = new ToolStripMenuItem("Restore recovery backup...")
			{
				Name = "restoreRecoveryMenuItem"
			};
			restoreRecoveryMenuItem.Click += restoreRecoveryMenuItem_Click;
			fileToolStripMenuItem.DropDownItems.Add(restoreRecoveryMenuItem);
			fileImportWavMenuItem = new ToolStripMenuItem("Import WAV...");
			fileImportWavMenuItem.Click += buttonTest_Click;
			fileToolStripMenuItem.DropDownItems.Add(fileImportWavMenuItem);
			fileToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
			fileToolStripMenuItem.DropDownItems.Add(saveToolStripMenuItem);
			fileToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
			fileToolStripMenuItem.DropDownItems.Add("Exit", null, (sender, args) => Close());
			fileToolStripMenuItem.DropDownOpening += (sender, args) => UpdateRestoreMenuEnabled();

			var inputMenu = new ToolStripMenuItem("Input") { Name = "inputMenu" };
			importWavMenuItem = new ToolStripMenuItem("Import WAV...");
			importWavMenuItem.Click += buttonTest_Click;
			inputMenu.DropDownItems.Add(importWavMenuItem);
			inputMenu.DropDownItems.Add(new ToolStripSeparator());
			listenLineInMenuItem = new ToolStripMenuItem("Listen to line-in") { Name = "listenLineInMenuItem" };
			listenLineInMenuItem.Click += buttonListen_Click;
			inputMenu.DropDownItems.Add(listenLineInMenuItem);
			recordLineInMenuItem = new ToolStripMenuItem("Record line-in to WAV...") { Name = "recordLineInMenuItem" };
			recordLineInMenuItem.Click += recordLineInMenuItem_Click;
			inputMenu.DropDownItems.Add(recordLineInMenuItem);

			lineInOptionsMenu = new ContextMenuStrip();
			components.Add(lineInOptionsMenu);
			lineInOptionsListenMenuItem = new ToolStripMenuItem("Listen to line-in", null, buttonListen_Click);
			lineInOptionsRecordMenuItem = new ToolStripMenuItem("Record line-in to WAV...", null, recordLineInMenuItem_Click);
			lineInOptionsMenu.Items.Add(lineInOptionsListenMenuItem);
			lineInOptionsMenu.Items.Add(lineInOptionsRecordMenuItem);
			buttonListen.ContextMenuStrip = lineInOptionsMenu;
			inputMenu.DropDownItems.Add(new ToolStripSeparator());
			var audioChannelMenu = new ToolStripMenuItem("Audio channel");
			var audioChannels = new[] { "Mix stereo", "Left channel", "Right channel" };
			topAudioChannelItems = new ToolStripMenuItem[audioChannels.Length];
			for (var index = 0; index < audioChannels.Length; index++)
			{
				var channelIndex = index;
				var channelItem = new ToolStripMenuItem(audioChannels[index]);
				channelItem.Click += (sender, args) => channelModeComboBox.SelectedIndex = channelIndex;
				topAudioChannelItems[index] = channelItem;
				audioChannelMenu.DropDownItems.Add(channelItem);
			}
			audioChannelMenu.DropDownOpening += (sender, args) => UpdateChannelMenuChecks();
			inputMenu.DropDownItems.Add(audioChannelMenu);
			topAudioChannelMenuItem = audioChannelMenu;
			topRecoveryPassesMenuItem = new ToolStripMenuItem("Use recovery passes") { CheckOnClick = true };
			topRecoveryPassesMenuItem.Click += (sender, args) => recoveryPassesMenuItem.Checked = topRecoveryPassesMenuItem.Checked;
			topRecoveryPassesMenuItem.DropDownOpening += (sender, args) => topRecoveryPassesMenuItem.Checked = recoveryPassesMenuItem.Checked;
			inputMenu.DropDownItems.Add(topRecoveryPassesMenuItem);
			topCrcRepairMenuItem = new ToolStripMenuItem("Attempt conservative CRC repair") { CheckOnClick = true };
			topCrcRepairMenuItem.Click += (sender, args) => crcRepairMenuItem.Checked = topCrcRepairMenuItem.Checked;
			topCrcRepairMenuItem.DropDownOpening += (sender, args) => topCrcRepairMenuItem.Checked = crcRepairMenuItem.Checked;
			inputMenu.DropDownItems.Add(topCrcRepairMenuItem);
			topCaptureDeviceMenuItem = new ToolStripMenuItem("Capture device");
			topCaptureDeviceMenuItem.DropDownOpening += (sender, args) => PopulateCaptureDevices(topCaptureDeviceMenuItem);
			inputMenu.DropDownItems.Add(topCaptureDeviceMenuItem);
			inputMenu.DropDownOpening += (sender, args) =>
			{
				topRecoveryPassesMenuItem.Checked = recoveryPassesMenuItem.Checked;
				topCrcRepairMenuItem.Checked = crcRepairMenuItem.Checked;
			};
			menuStrip.Items.Add(inputMenu);

			exportTopMenu = new ToolStripMenuItem("Export") { Name = "exportTopMenu" };
			exportTopMenu.DropDownItems.Add("Recovered files and .inf...", null, exportRawFilesToolStripMenuItem_Click);
			exportTopMenu.DropDownItems.Add("CSW cassette image...", null, export_Click);
			exportTopMenu.DropDownItems.Add("BBC Micro disk image...", null, exportDiskImage_Click);
			menuStrip.Items.Add(exportTopMenu);
			channelModeComboBox.SelectedIndexChanged += (sender, args) => UpdateChannelMenuChecks();

			var helpMenu = new ToolStripMenuItem("Help") { Name = "helpMenu" };
			helpMenu.DropDownItems.Add("Local help", null, (sender, args) => OpenBundledDocument("help.html", "Local help"));
			helpMenu.DropDownItems.Add("Project/GitHub", null, (sender, args) => OpenExternalUrl("https://github.com/rokcoder-bbcmicro/bbc-cassette-loader"));
			var koFiSupportMenuItem = new ToolStripMenuItem("Support RokCoder on Ko-fi...")
			{
				Name = "koFiSupportMenuItem",
				Tag = KoFiSupportUrl
			};
			koFiSupportMenuItem.Click += (sender, args) => OpenExternalUrl(KoFiSupportUrl, "the Ko-fi support page", "Ko-fi");
			helpMenu.DropDownItems.Add(koFiSupportMenuItem);
			helpMenu.DropDownItems.Add(new ToolStripSeparator());
			checkForUpdatesMenuItem = new ToolStripMenuItem("Check for updates") { Name = "checkForUpdatesMenuItem" };
			checkForUpdatesMenuItem.Click += checkForUpdatesMenuItem_Click;
			helpMenu.DropDownItems.Add(checkForUpdatesMenuItem);
			helpMenu.DropDownItems.Add(new ToolStripSeparator());
			helpMenu.DropDownItems.Add("About", null, (sender, args) => ShowAboutDialog());
			menuStrip.Items.Add(helpMenu);
		}

		void OpenBundledDocument(string fileName, string description)
		{
			var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
			if (!File.Exists(path))
			{
				MessageBox.Show(this, description + " is not available in this installation:\n" + path,
					description, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, "Unable to open " + description.ToLowerInvariant() + ":\n" + exception.Message,
					description, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		void OpenExternalUrl(string url, string description = "the project page", string title = "Project/GitHub")
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, "Unable to open " + description + ":\n" + exception.Message,
					title, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private async void checkForUpdatesMenuItem_Click(object sender, EventArgs e)
		{
			if (updateCheckCancellation != null) return;
			var cancellation = new CancellationTokenSource();
			updateCheckCancellation = cancellation;
			checkForUpdatesMenuItem.Enabled = false;
			try
			{
				Version currentVersion;
				if (!Version.TryParse(Application.ProductVersion, out currentVersion))
				{
					ShowUpdateCheckMessage(new UpdateCheckResult(UpdateCheckStatus.InvalidResponse));
					return;
				}

				using (var client = UpdateChecker.CreateClient("BBC-Micro-Cassette-Loader/" + Application.ProductVersion))
				{
					var result = await UpdateChecker.CheckAsync(currentVersion, client, cancellation.Token);
					if (!IsDisposed && !Disposing)
						ShowUpdateCheckMessage(result);
				}
			}
			catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
			{
				// Closing the form or a user cancellation is intentionally silent.
			}
			catch (Exception exception)
			{
				if (!IsDisposed && !Disposing)
					MessageBox.Show(this, "Unable to check for updates:\n" + exception.Message,
						"Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally
			{
				if (ReferenceEquals(updateCheckCancellation, cancellation))
					updateCheckCancellation = null;
				if (!IsDisposed && !Disposing && checkForUpdatesMenuItem != null)
					checkForUpdatesMenuItem.Enabled = true;
				cancellation.Dispose();
			}
		}

		void ShowUpdateCheckMessage(UpdateCheckResult result)
		{
			switch (result.Status)
			{
				case UpdateCheckStatus.NewerAvailable:
					if (MessageBox.Show(this,
						"Version " + result.LatestVersionText + " is available. Open the release page?",
						"Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
						OpenExternalUrl(result.ReleaseUrl, "the release page", "Update available");
					break;
				case UpdateCheckStatus.UpToDate:
					MessageBox.Show(this, "No newer stable release is available.",
						"Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
					break;
				case UpdateCheckStatus.NoRelease:
					MessageBox.Show(this, "No published stable release is available yet.",
						"Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
					break;
				case UpdateCheckStatus.RateLimited:
					MessageBox.Show(this, "GitHub has temporarily rate-limited update checks. Try again later.",
						"Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					break;
				case UpdateCheckStatus.InvalidResponse:
					MessageBox.Show(this, "GitHub returned update information in an unexpected format.",
						"Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					break;
				default:
					MessageBox.Show(this, "Unable to reach GitHub to check for updates. Check your connection and try again later.",
						"Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					break;
			}
		}

		void ShowAboutDialog()
		{
			using (var dialog = new AboutForm())
				dialog.ShowDialog(this);
		}

		void UpdateChannelMenuChecks()
		{
			var selectedIndex = channelModeComboBox.SelectedIndex;
			if (importChannelItems != null)
				for (var index = 0; index < importChannelItems.Length; index++)
					importChannelItems[index].Checked = index == selectedIndex;
			if (topAudioChannelItems != null)
				for (var index = 0; index < topAudioChannelItems.Length; index++)
					topAudioChannelItems[index].Checked = index == selectedIndex;
		}

		void PopulateCaptureDevices()
		{
			PopulateCaptureDevices(captureDeviceMenu);
		}

		void PopulateCaptureDevices(ToolStripMenuItem target)
		{
			target.DropDownItems.Clear();
			var devices = ToneHandler.GetCaptureDeviceNames();
			target.Enabled = true;
			if (devices.Count == 0)
			{
				target.DropDownItems.Add("No capture devices found").Enabled = false;
				selectedCaptureDevice = 0;
				return;
			}

			if (selectedCaptureDevice >= devices.Count)
			{
				selectedCaptureDevice = 0;
				fileHandler?.SetCaptureDeviceNumber(selectedCaptureDevice);
			}
			for (var index = 0; index < devices.Count; index++)
			{
				var deviceIndex = index;
				var item = new ToolStripMenuItem(devices[index])
				{
					Checked = index == selectedCaptureDevice
				};
				item.Click += (sender, args) =>
				{
					selectedCaptureDevice = deviceIndex;
					fileHandler?.SetCaptureDeviceNumber(deviceIndex);
					foreach (ToolStripMenuItem sibling in target.DropDownItems)
						sibling.Checked = ReferenceEquals(sibling, item);
				};
				target.DropDownItems.Add(item);
			}
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			channelModeComboBox.SelectedIndex = 0;
			fileHandler = new BBCFileHandler(
				recoveryStateDirectory: recoveryStateDirectoryOverride,
				captureDeviceNumber: selectedCaptureDevice);

			FormClosing += MainForm_FormClosing;
			fileHandler.UpdateFile += UpdateFile;
			fileHandler.InvalidBlockReceived += InvalidBlockReceived;
			fileHandler.FileImportProgress += FileImportProgressReceived;
			fileHandler.CrcRepairAccepted += CrcRepairAccepted;
			fileHandler.CrcRepairAcceptedDetailed += CrcRepairAcceptedDetailed;

			fileHandler.NewData += UpdateWaveGraph;
			fileHandler.LineInStoppedEvent += LineInStopped;
			fileHandler.LineInRecordingStoppedEvent += LineInRecordingStopped;

			allFileData = new Dictionary<string, FileUIData>();
			try
			{
				var recoveryLoad = fileHandler.Deserialise();
				if (recoveryLoad != null && recoveryLoad.Warnings.Count > 0)
					MessageBox.Show(this, string.Join(Environment.NewLine, recoveryLoad.Warnings),
						"Recovery state warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, "Recovery state could not be loaded. The application will continue with an empty session.\n\n" + exception.Message,
					"Recovery state warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

		}

		private void newToolStripMenuItem_Click(object sender, EventArgs e)
		{
			StartNewCassette(true);
		}

		bool StartNewCassette(bool confirmIntent)
		{
			if (fileHandler.HasUnsavedRecoveryState)
			{
				var saveChoice = MessageBox.Show(this,
					"Start a new cassette?\n\nUnsaved changes will be lost unless you choose Yes. Saved data will be backed up.",
					"Start a new cassette", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
				if (saveChoice == DialogResult.Cancel) return false;
				if (saveChoice == DialogResult.Yes)
				{
					try { fileHandler.Serialise(); }
					catch (Exception exception)
					{
						MessageBox.Show(this, exception.Message, "Recovery state could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
						return false;
					}
				}
				confirmIntent = false;
			}
			if (confirmIntent && MessageBox.Show(this,
				"Start a new cassette?\n\nCurrent recovered data will be cleared. Saved data will be backed up.",
				"Start a new cassette", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
				return false;
			try
			{
				fileHandler.ResetRecoveredFiles();
				ClearRecoveredFileUi();
				activityStatusLabel.Text = "Ready for a new cassette";
				return true;
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, exception.Message, "New cassette could not be started", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		bool ConfirmCassetteBoundary()
		{
			if (!fileHandler.HasRecoveredFiles) return true;
			var choice = MessageBox.Show(this,
				"This session already contains recovered data. Add the selected recording to the current cassette?\n\nChoose No to start a new cassette, or Cancel to stop the import.",
				"Cassette boundary", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
			if (choice == DialogResult.Yes) return true;
			if (choice == DialogResult.No) return StartNewCassette(false);
			return false;
		}

		private void restoreRecoveryMenuItem_Click(object sender, EventArgs e)
		{
			if (fileHandler == null) return;
			var backups = fileHandler.GetResetBackups();
			if (backups.Count == 0)
			{
				MessageBox.Show(this, "No reset recovery backups are available.",
					"Restore recovery backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			RecoveryBackupInfo selectedBackup;
			using (var dialog = new RecoveryBackupDialog(backups))
			{
				if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedBackup == null)
					return;
				selectedBackup = dialog.SelectedBackup;
			}

			if (fileHandler.HasUnsavedRecoveryState)
			{
				var choice = CloseRecoveryPromptOverride != null
					? CloseRecoveryPromptOverride()
					: MessageBox.Show(this,
						"Restore this recovery backup?\n\nChoose Yes to save current changes first, No to discard them, or Cancel to keep the current session.",
						"Restore recovery backup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
				if (choice == DialogResult.Cancel) return;
				if (choice == DialogResult.Yes)
				{
					try
					{
						fileHandler.StopListening();
						fileHandler.Serialise();
					}
					catch (Exception exception)
					{
						MessageBox.Show(this, exception.Message, "Recovery state could not be saved",
							MessageBoxButtons.OK, MessageBoxIcon.Error);
						return;
					}
				}
			}

			try
			{
				fileHandler.RestoreRecoveryBackup(selectedBackup.DirectoryPath);
				ClearRecoveredFileUi();
				foreach (var fileUID in fileHandler.files.Keys.ToArray())
					UpdateFile(this, fileUID);
				activityStatusLabel.Text = "Recovery backup restored";
				UpdateRestoreMenuEnabled();
				MessageBox.Show(this,
					"The recovery backup was restored as the current session. Diagnostic waveforms are not included in backups.",
					"Recovery backup restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, exception.Message, "Recovery backup could not be restored",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		void ClearRecoveredFileUi()
		{
			allFileData.Clear();
			fileListBox.Items.Clear();
			selectedFileData = null;
			UpdateSelectedFileDisplay();
			ClearDiagnosticDisplay();
			UpdateRecoverySummary();
		}

		void UpdateRestoreMenuEnabled()
		{
			if (restoreRecoveryMenuItem != null)
				restoreRecoveryMenuItem.Enabled = fileImportCancellation == null &&
					fileHandler != null && fileHandler.GetResetBackups().Count > 0;
		}

		void ClearDiagnosticDisplay()
		{
			waveChart.Series["waveSeries"].Points.Clear();
			var axis = waveChart.ChartAreas["ChartArea1"].AxisX;
			foreach (var line in diagnosticStripLines) axis.StripLines.Remove(line);
			diagnosticStripLines.Clear();
			diagnosticTitleLabel.Text = "Diagnostic waveform";
		}

		private void buttonListen_Click(object sender, EventArgs e)
		{
			if (lineInActive)
			{
				fileHandler.StopListening();
				return;
			}
			if (ToneHandler.GetCaptureDeviceNames().Count == 0)
			{
				MessageBox.Show(this, "No audio capture device is available.", "Line-in unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			try
			{
				fileHandler.StartListeningToLineIn();
				SetLineInUiActive(true);
				activityStatusLabel.Text = "Listening to line-in";
			}
			catch (Exception exception)
			{
				activityStatusLabel.Text = "Line-in unavailable";
				MessageBox.Show(this, exception.Message, "Line-in unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void recordLineInMenuItem_Click(object sender, EventArgs e)
		{
			if (lineInActive)
				return;
			if (ToneHandler.GetCaptureDeviceNames().Count == 0)
			{
				MessageBox.Show(this, "No audio capture device is available.", "Line-in unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			using (var dialog = new SaveFileDialog
			{
				AddExtension = true,
				DefaultExt = "wav",
				Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*",
				OverwritePrompt = true,
				Title = "Record line-in to WAV"
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return;
				try
				{
					fileHandler.StartListeningToLineIn(dialog.FileName);
					SetLineInUiActive(true);
					activityStatusLabel.Text = "Recording line-in to " + Path.GetFileName(dialog.FileName);
				}
				catch (Exception exception)
				{
					activityStatusLabel.Text = "Line-in unavailable";
					MessageBox.Show(this, exception.Message, "WAV recording unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		void LineInStopped(object sender, EventArgs e)
		{
			if (IsDisposed || Disposing) return;
			if (InvokeRequired)
			{
				if (IsHandleCreated) BeginInvoke((MethodInvoker)(() => LineInStopped(sender, e)));
				return;
			}
			if (fileHandler != null && fileHandler.IsListeningToLineIn)
				return;
			SetLineInUiActive(false);
			if (activityStatusLabel != null && activityStatusLabel.Text == "Listening to line-in")
				activityStatusLabel.Text = "Line-in stopped";
		}

		void LineInRecordingStopped(object sender, LineInRecordingResult result)
		{
			if (IsDisposed || Disposing) return;
			if (InvokeRequired)
			{
				if (IsHandleCreated) BeginInvoke((MethodInvoker)(() => LineInRecordingStopped(sender, result)));
				return;
			}
			if (activityStatusLabel == null)
				return;
			activityStatusLabel.Text = result.Error == null
				? "WAV saved: " + Path.GetFileName(result.FilePath)
				: "WAV recording ended with an error: " + result.Error.Message;
		}

		void SetLineInUiActive(bool active)
		{
			lineInActive = active;
			buttonListen.ContextMenuStrip = active ? null : lineInOptionsMenu;
			buttonListen.Text = active ? "Stop line-in" : "Line-in options  ▾";
			if (listenLineInMenuItem != null)
				listenLineInMenuItem.Text = active ? "Stop line-in" : "Listen to line-in";
			if (lineInOptionsListenMenuItem != null)
				lineInOptionsListenMenuItem.Text = "Listen to line-in";
			if (recordLineInMenuItem != null)
			{
				recordLineInMenuItem.Enabled = !active;
				recordLineInMenuItem.Text = active && fileHandler != null && fileHandler.IsRecordingToFile
					? "Recording line-in to WAV"
					: "Record line-in to WAV...";
			}
			if (lineInOptionsRecordMenuItem != null)
			{
				lineInOptionsRecordMenuItem.Enabled = true;
				lineInOptionsRecordMenuItem.Text = "Record line-in to WAV...";
			}
			var enabled = !active && fileImportCancellation == null;
			newToolStripMenuItem.Enabled = enabled;
			if (restoreRecoveryMenuItem != null) restoreRecoveryMenuItem.Enabled = enabled;
			saveToolStripMenuItem.Enabled = enabled;
			exportRawFilesToolStripMenuItem.Enabled = enabled;
			if (exportCswToolStripMenuItem != null) exportCswToolStripMenuItem.Enabled = enabled;
			if (exportTopMenu != null) exportTopMenu.Enabled = enabled;
			save.Enabled = enabled;
			export.Enabled = enabled;
		}

		private void buttonTest_Click(object sender, EventArgs e)
		{
			if (fileImportCancellation != null)
			{
				buttonTest.Enabled = false;
				fileImportCancellation.Cancel();
				fileHandler?.StopListening();
				return;
			}
			openRawFileDialog.ShowDialog();
		}

		private async void openRawFileDialog_FileOk(object sender, CancelEventArgs e)
		{
			if (!ConfirmCassetteBoundary())
			{
				e.Cancel = true;
				return;
			}
			var channelMode = (AudioChannelMode)channelModeComboBox.SelectedIndex;
			var recoveryPasses = recoveryPassesMenuItem.Checked;
			fileHandler.EnableCrcRepair = crcRepairMenuItem.Checked;
			crcRepairHeaders = 0;
			crcRepairData = 0;
			crcRepairBits = 0;
			var cancellation = new CancellationTokenSource();
			fileImportCancellation = cancellation;
			SetFileImportActive(true);

			try
			{
				foreach (var file in openRawFileDialog.FileNames)
				{
					BeginFileImportProgress(file, recoveryPasses);
					await fileHandler.StartListeningToFileAsync(file, channelMode, cancellation.Token, recoveryPasses);
				}
				activityStatusLabel.Text = FormatRepairSummary("Import finished");
			}
			catch (OperationCanceledException)
			{
				activityStatusLabel.Text = "Import cancelled";
			}
			catch (Exception exception)
			{
				activityStatusLabel.Text = "Import failed";
				MessageBox.Show(this, exception.Message, "File import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				if (ReferenceEquals(fileImportCancellation, cancellation))
					fileImportCancellation = null;
				cancellation.Dispose();
				if (!IsDisposed)
					SetFileImportActive(false);
				if (closeWhenImportStops && !IsDisposed)
					BeginInvoke((MethodInvoker)Close);
			}
		}

		private void buttonCancelImport_Click(object sender, EventArgs e)
		{
			buttonCancelImport.Enabled = false;
			fileImportCancellation?.Cancel();
			fileHandler.StopListening();
		}

		void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			updateCheckCancellation?.Cancel();
			if (fileImportCancellation != null)
			{
				e.Cancel = true;
				closeWhenImportStops = true;
				fileImportCancellation.Cancel();
				return;
			}

			if (!ConfirmCloseWithUnsavedRecoveryState())
			{
				closeWhenImportStops = false;
				e.Cancel = true;
				return;
			}

			fileHandler?.FormClosing();
		}

		bool ConfirmCloseWithUnsavedRecoveryState()
		{
			if (fileHandler == null || !fileHandler.HasUnsavedRecoveryState)
				return true;

			var choice = CloseRecoveryPromptOverride != null
				? CloseRecoveryPromptOverride()
				: MessageBox.Show(this,
					"Save the recovered cassette state before closing?\n\nChoose No to discard unsaved changes, or Cancel to keep the application open.",
					"Save recovery state before closing", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
			if (choice == DialogResult.Cancel)
				return false;
			if (choice != DialogResult.Yes)
				return true;

			try
			{
				// Freeze live input before taking the snapshot to ensure buffers arriving
				// during serialization are included in the saved state.
				fileHandler.StopListening();
				fileHandler.Serialise();
				return true;
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, exception.Message, "Recovery state could not be saved before closing", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		void SetFileImportActive(bool active)
		{
			buttonListen.Enabled = !active;
			buttonTest.Enabled = true;
			buttonTest.Text = active ? "Cancel import" : "Import WAV...";
			importOptionsButton.Enabled = !active;
			if (importWavMenuItem != null) importWavMenuItem.Enabled = !active;
			if (fileImportWavMenuItem != null) fileImportWavMenuItem.Enabled = !active;
			if (listenLineInMenuItem != null) listenLineInMenuItem.Enabled = !active;
			if (recordLineInMenuItem != null) recordLineInMenuItem.Enabled = !active;
			if (topAudioChannelMenuItem != null) topAudioChannelMenuItem.Enabled = !active;
			if (topRecoveryPassesMenuItem != null) topRecoveryPassesMenuItem.Enabled = !active;
			if (topCrcRepairMenuItem != null) topCrcRepairMenuItem.Enabled = !active;
			if (topCaptureDeviceMenuItem != null) topCaptureDeviceMenuItem.Enabled = !active;
			newToolStripMenuItem.Enabled = !active;
			if (restoreRecoveryMenuItem != null) restoreRecoveryMenuItem.Enabled = !active;
			saveToolStripMenuItem.Enabled = !active;
			channelModeComboBox.Enabled = !active;
			recoveryPassesMenuItem.Enabled = !active;
			crcRepairMenuItem.Enabled = !active;
			exportRawFilesToolStripMenuItem.Enabled = !active;
			exportCswToolStripMenuItem.Enabled = !active;
			if (exportTopMenu != null) exportTopMenu.Enabled = !active;
			save.Enabled = !active;
			export.Enabled = !active;
			buttonCancelImport.Enabled = false;
			buttonCancelImport.Visible = false;
			if (!active)
			{
				UpdateRestoreMenuEnabled();
				SetLineInUiActive(false);
				importingFile = null;
				importProgressBar.Visible = false;
				importProgressBar.Value = importProgressBar.Minimum;
			}
		}

		void CrcRepairAccepted(object sender, string kind)
		{
			if (string.Equals(kind, "header", StringComparison.OrdinalIgnoreCase))
				Interlocked.Increment(ref crcRepairHeaders);
			else if (string.Equals(kind, "data", StringComparison.OrdinalIgnoreCase))
				Interlocked.Increment(ref crcRepairData);
		}

		void CrcRepairAcceptedDetailed(object sender, BlockHandler.CrcRepairInfo repair)
		{
			Interlocked.Add(ref crcRepairBits, repair.correctedBits);
		}

		string FormatRepairSummary(string prefix)
		{
			var headers = Volatile.Read(ref crcRepairHeaders);
			var data = Volatile.Read(ref crcRepairData);
			var bits = Volatile.Read(ref crcRepairBits);
			return headers + data == 0 ? prefix :
				prefix + " (" + (headers + data) + " CRC repairs: " + headers + " header, " + data + " data; " + bits + " corrected bits)";
		}

		void BeginFileImportProgress(string file, bool recoveryPasses)
		{
			importingFile = file;
			importProgressBar.Value = importProgressBar.Minimum;
			importProgressBar.Visible = true;
			var passCount = recoveryPasses ? BBCFileHandler.RecoveryProfileCount : 1;
			activityStatusLabel.Text = "Importing " + Path.GetFileName(file) + " — pass 1 of " + passCount + " — 0%";
		}

		void FileImportProgressReceived(object sender, ToneHandler.FileImportProgressData progress)
		{
			if (IsDisposed || Disposing) return;
			if (InvokeRequired)
			{
				if (IsHandleCreated)
					BeginInvoke((MethodInvoker)(() => FileImportProgressReceived(sender, progress)));
				return;
			}
			if (!string.Equals(importingFile, progress.fileName, StringComparison.OrdinalIgnoreCase)) return;

			var percent = progress.totalSamples <= 0 ? 0 :
				(int)Math.Min(100, progress.samplesRead * 100 / progress.totalSamples);
			importProgressBar.Value = Math.Max(importProgressBar.Minimum, Math.Min(importProgressBar.Maximum, percent));
			importProgressBar.Visible = true;
			activityStatusLabel.Text = "Importing " + Path.GetFileName(progress.fileName) +
				" — pass " + progress.passNumber + " of " + progress.passCount +
				" (" + progress.profileName + ") — " + percent + "%";
		}

		internal class FileUIData
		{
			public struct InvalidBlockUIData
			{
				public ushort blockNum;
				public bool isData;
				public long diagnosticId;
				public string description;
			}

			public string name;
			public bool lengthKnown;
			public List<bool> hasHeader;
			public List<bool> hasData;
			public List<InvalidBlockUIData> invalidBlocks;
			public bool isComplete;
			public int displayBlockCount;
			public int CompleteBlockCount
			{
				get
				{
					var count = 0;
					for (var index = 0; index < Math.Min(hasHeader.Count, hasData.Count); index++)
						if (hasHeader[index] && hasData[index]) count++;
					return count;
				}
			}

			public FileUIData(BBCFile file)
				: this(file.filename)
			{
				Update(file);
			}

			public FileUIData(string name)
			{
				this.name = name;
				hasHeader = new List<bool>();
				hasData = new List<bool>();
				invalidBlocks = new List<InvalidBlockUIData>();
			}

			public void Update(BBCFile file)
			{
				lengthKnown = file.TotalBlocksKnown;
				for (var i = 0; i < hasData.Count; i++)
				{
					hasHeader[i] = file.HasHeader(i);
					hasData[i] = file.HasData(i);
				}
				for (var i = hasData.Count; i < file.NumBlocks; i++)
				{
					hasHeader.Add(file.HasHeader(i));
					hasData.Add(file.HasData(i));
				}
				displayBlockCount = Math.Max(displayBlockCount, file.NumBlocks);
				isComplete = file.IsComplete();
				invalidBlocks.RemoveAll(invalidBlock =>
					invalidBlock.blockNum < hasHeader.Count &&
					(invalidBlock.isData ? hasData[invalidBlock.blockNum] : hasHeader[invalidBlock.blockNum]));
			}

			public void AddInvalidBlock(BBCFileHandler.InvalidBlockData invalidBlock)
			{
				displayBlockCount = Math.Max(displayBlockCount, invalidBlock.blockNum + 1);
				if (invalidBlock.blockNum < hasHeader.Count &&
					(invalidBlock.isData ? hasData[invalidBlock.blockNum] : hasHeader[invalidBlock.blockNum]))
					return;
				invalidBlocks.Add(new InvalidBlockUIData
				{
					blockNum = invalidBlock.blockNum,
					isData = invalidBlock.isData,
					diagnosticId = invalidBlock.diagnosticId,
					description = invalidBlock.description
				});
			}

			public bool HasInvalidBlock(int blockNum, bool isData)
			{
				for (var index = invalidBlocks.Count - 1; index >= 0; index--)
					if (invalidBlocks[index].blockNum == blockNum && invalidBlocks[index].isData == isData)
						return true;
				return false;
			}

			public bool TryGetLatestInvalidBlock(int blockNum, bool isData, out InvalidBlockUIData invalidBlock)
			{
				for (var index = invalidBlocks.Count - 1; index >= 0; index--)
					if (invalidBlocks[index].blockNum == blockNum && invalidBlocks[index].isData == isData)
					{
						invalidBlock = invalidBlocks[index];
						return true;
					}
				invalidBlock = default(InvalidBlockUIData);
				return false;
			}
		}

		Dictionary<string, FileUIData> allFileData;

		void UpdateFile(object s, string fileUID)
		{
			if (IsDisposed || Disposing)
				return;
			if (InvokeRequired)
			{
				if (IsHandleCreated)
					BeginInvoke((MethodInvoker)(() => UpdateFile(s, fileUID)));
				return;
			}

			if (!allFileData.ContainsKey(fileUID))
			{
				allFileData.Add(fileUID, new FileUIData(fileHandler.files[fileUID]));
				fileListBox.Items.Add(allFileData[fileUID]);
			}

			var fileData = allFileData[fileUID];
			fileData.Update(fileHandler.files[fileUID]);
			var itemIndex = fileListBox.Items.IndexOf(fileData);
			if (itemIndex >= 0)
				fileListBox.Invalidate(fileListBox.GetItemRectangle(itemIndex));
			if (fileListBox.SelectedIndex < 0 && fileListBox.Items.Count > 0)
				fileListBox.SelectedIndex = 0;
			if (ReferenceEquals(selectedFileData, fileData))
				UpdateSelectedFileDisplay();
			UpdateRecoverySummary();
		}

		void InvalidBlockReceived(object sender, BBCFileHandler.InvalidBlockData invalidBlock)
		{
			if (IsDisposed || Disposing)
				return;
			if (InvokeRequired)
			{
				if (IsHandleCreated)
					BeginInvoke((MethodInvoker)(() => InvalidBlockReceived(sender, invalidBlock)));
				return;
			}

			FileUIData fileData;
			if (!allFileData.TryGetValue(invalidBlock.fileUID, out fileData))
			{
				fileData = new FileUIData(invalidBlock.fileUID);
				allFileData.Add(invalidBlock.fileUID, fileData);
				fileListBox.Items.Add(fileData);
			}
			fileData.AddInvalidBlock(invalidBlock);
			var itemIndex = fileListBox.Items.IndexOf(fileData);
			if (itemIndex >= 0)
				fileListBox.Invalidate(fileListBox.GetItemRectangle(itemIndex));
			if (fileListBox.SelectedIndex < 0 && fileListBox.Items.Count > 0)
				fileListBox.SelectedIndex = 0;
			if (ReferenceEquals(selectedFileData, fileData))
				UpdateSelectedFileDisplay();
			UpdateRecoverySummary();
		}

		private void fileListBox_DrawItem(object sender, DrawItemEventArgs e)
		{
			//TODO Add a horizontal scrollbar (which may be difficult as it's automatic depending on the width of the text in a text item)
			var listBox = sender as ListBox;

			if (e.Index >= listBox.Items.Count || e.Index < 0)
				return;

			var fileData = (FileUIData)listBox.Items[e.Index];
			if (fileData == null)
				return;

			var selected = (e.State & DrawItemState.Selected) != 0;
			var background = selected ? Color.FromArgb(234, 241, 255) : Color.White;
			using (var backgroundBrush = new SolidBrush(background))
				e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
			if (selected)
				using (var accentBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
					e.Graphics.FillRectangle(accentBrush, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);

			var completeBlocks = fileData.CompleteBlockCount;
			var totalText = fileData.lengthKnown ? fileData.displayBlockCount.ToString() : "?";
			var statusText = fileData.isComplete ? "Complete" : "Partial";
			var statusColor = fileData.isComplete ? Color.FromArgb(8, 122, 85) : Color.FromArgb(164, 91, 7);
			var nameBounds = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 7, e.Bounds.Width - 100, 20);
			var detailsBounds = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 29, e.Bounds.Width - 110, 18);
			var statusBounds = new Rectangle(e.Bounds.Right - 88, e.Bounds.Top + 8, 74, 18);
			TextRenderer.DrawText(e.Graphics, fileData.name, Font, nameBounds, Color.FromArgb(28, 36, 48), TextFormatFlags.EndEllipsis);
			TextRenderer.DrawText(e.Graphics, completeBlocks + " of " + totalText + " blocks", Font, detailsBounds, Color.DimGray, TextFormatFlags.EndEllipsis);
			TextRenderer.DrawText(e.Graphics, statusText, Font, statusBounds, statusColor, TextFormatFlags.Right);
			using (var separatorPen = new Pen(Color.FromArgb(235, 238, 243)))
				e.Graphics.DrawLine(separatorPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
			if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();

		}

		void FileListBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			selectedFileData = fileListBox.SelectedItem as FileUIData;
			UpdateSelectedFileDisplay();
		}

		void UpdateSelectedFileDisplay()
		{
			if (selectedFileData == null)
			{
				selectedFileLabel.Text = "Select a recovered file";
				selectedFileStatusLabel.Text = "Its block recovery map will appear here.";
			}
			else
			{
				selectedFileLabel.Text = selectedFileData.name;
				selectedFileStatusLabel.Text = (selectedFileData.isComplete ? "Complete" : "Partial") + "  ·  " +
					selectedFileData.CompleteBlockCount + " of " +
					(selectedFileData.lengthKnown ? selectedFileData.displayBlockCount.ToString() : "?") + " blocks";
			}
			blockMapPanel.Invalidate();
		}

		void UpdateRecoverySummary()
		{
			var files = allFileData == null ? new List<FileUIData>() : allFileData.Values.ToList();
			var complete = files.Count(file => file.isComplete);
			var blocks = files.Sum(file => file.CompleteBlockCount);
			recoverySummaryLabel.Text = complete + " complete  ·  " + (files.Count - complete) + " partial  ·  " + blocks + " recovered blocks";
		}

		void BlockMapPanel_Paint(object sender, PaintEventArgs e)
		{
			if (selectedFileData == null) return;
			const int cellWidth = 44;
			const int cellHeight = 34;
			const int gap = 6;
			const int margin = 14;
			const int legendHeight = 25;
			var columns = Math.Max(1, (blockMapPanel.ClientSize.Width - margin * 2) / (cellWidth + gap));
			var rows = (selectedFileData.displayBlockCount + columns - 1) / columns;
			blockMapPanel.AutoScrollMinSize = new Size(0, margin * 2 + legendHeight + rows * (cellHeight + gap));
			var offset = blockMapPanel.AutoScrollPosition;
			TextRenderer.DrawText(e.Graphics, "Top: header    Bottom: data    Red ×: invalid", Font,
				new Point(margin + offset.X, 8 + offset.Y), Color.DimGray);
			using (var outlinePen = new Pen(Color.FromArgb(205, 211, 220)))
			using (var invalidPen = new Pen(Color.FromArgb(194, 53, 53), 2))
			using (var headerBrush = new SolidBrush(Color.FromArgb(42, 157, 110)))
			using (var dataBrush = new SolidBrush(Color.FromArgb(218, 155, 63)))
			using (var emptyBrush = new SolidBrush(Color.FromArgb(238, 240, 244)))
			{
				for (var block = 0; block < selectedFileData.displayBlockCount; block++)
				{
					var column = block % columns;
					var row = block / columns;
					var bounds = new Rectangle(margin + column * (cellWidth + gap) + offset.X, margin + legendHeight + row * (cellHeight + gap) + offset.Y, cellWidth, cellHeight);
					if (!e.ClipRectangle.IntersectsWith(bounds)) continue;
					var headerBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height / 2);
					var dataBounds = new Rectangle(bounds.X, bounds.Y + bounds.Height / 2, bounds.Width, bounds.Height - bounds.Height / 2);
					e.Graphics.FillRectangle(block < selectedFileData.hasHeader.Count && selectedFileData.hasHeader[block] ? headerBrush : emptyBrush, headerBounds);
					e.Graphics.FillRectangle(block < selectedFileData.hasData.Count && selectedFileData.hasData[block] ? dataBrush : emptyBrush, dataBounds);
					e.Graphics.DrawRectangle(outlinePen, bounds);
					TextRenderer.DrawText(e.Graphics, block.ToString("X2"), Font, bounds, Color.FromArgb(28, 36, 48), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
					if (selectedFileData.HasInvalidBlock(block, false)) DrawCross(e.Graphics, invalidPen, headerBounds);
					if (selectedFileData.HasInvalidBlock(block, true)) DrawCross(e.Graphics, invalidPen, dataBounds);
				}
			}
		}

		static void DrawCross(Graphics graphics, Pen pen, Rectangle bounds)
		{
			graphics.DrawLine(pen, bounds.Left + 3, bounds.Top + 3, bounds.Right - 3, bounds.Bottom - 3);
			graphics.DrawLine(pen, bounds.Right - 3, bounds.Top + 3, bounds.Left + 3, bounds.Bottom - 3);
		}

		void BlockMapPanel_MouseClick(object sender, MouseEventArgs e)
		{
			if (selectedFileData == null) return;
			const int cellWidth = 44;
			const int cellHeight = 34;
			const int gap = 6;
			const int margin = 14;
			const int legendHeight = 25;
			var columns = Math.Max(1, (blockMapPanel.ClientSize.Width - margin * 2) / (cellWidth + gap));
			var x = e.X - blockMapPanel.AutoScrollPosition.X - margin;
			var y = e.Y - blockMapPanel.AutoScrollPosition.Y - margin - legendHeight;
			if (x < 0 || y < 0 || x % (cellWidth + gap) >= cellWidth || y % (cellHeight + gap) >= cellHeight) return;
			var blockNum = y / (cellHeight + gap) * columns + x / (cellWidth + gap);
			if (blockNum < 0 || blockNum >= selectedFileData.displayBlockCount) return;
			var isData = y % (cellHeight + gap) >= cellHeight / 2;
			OpenInvalidBlockDiagnostic(selectedFileData, blockNum, isData);
		}

		void OpenInvalidBlockDiagnostic(FileUIData fileData, int blockNum, bool isData)
		{
			FileUIData.InvalidBlockUIData invalidBlock;
			if (fileData == null || !fileData.TryGetLatestInvalidBlock(blockNum, isData, out invalidBlock))
				return;

			ToneHandler.ErrorDataForGraph diagnostic;
			if (fileHandler.TryGetDiagnostic(invalidBlock.diagnosticId, out diagnostic))
			{
				diagnosticTitleLabel.Text = fileData.name + "  ·  block " + blockNum.ToString("X2") + "  ·  " + invalidBlock.description;
				UpdateWaveGraph(this, diagnostic);
				return;
			}

			var availability = fileHandler.IsDiagnosticPending(invalidBlock.diagnosticId)
				? "Waveform context is still being collected."
				: "Waveform context is no longer retained.";
			MessageBox.Show(
				this,
				invalidBlock.description + Environment.NewLine + availability,
				"Block diagnostic",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
		}

		public void UpdateWaveGraph(object s, ToneHandler.ErrorDataForGraph errorData)
		{
			if (IsDisposed || Disposing)
				return;
			if (InvokeRequired)
			{
				if (IsHandleCreated)
					BeginInvoke((MethodInvoker)(() => UpdateWaveGraph(s, errorData)));
				return;
			}

			var p = waveChart.Series["waveSeries"].Points;
			p.Clear();
			for (var i = 0; i < errorData.data.Count; i++)
				p.Add(errorData.data[i]);

			var c = waveChart.ChartAreas["ChartArea1"].AxisX;
			foreach (var line in diagnosticStripLines)
				c.StripLines.Remove(line);
			diagnosticStripLines.Clear();

			var bitInterval = GetTypicalMarkerInterval(
				errorData.bitBoundaryMarker,
				Math.Max(1, errorData.data.Count / 80));
			// Mark byte boundaries without showing inferred diagnostic byte values.
			AddDiagnosticMarkers(
				c,
				errorData.byteBoundaryMarker,
				Color.FromArgb(255, 0, 0, 255),
				2,
				bitInterval * 10,
				errorData.data.Count,
				null,
				true);
			// Mark bit boundaries and centre each label over its bit waveform.
			AddDiagnosticMarkers(
				c,
				errorData.bitBoundaryMarker,
				Color.FromArgb(100, 0, 128, 255),
				1,
				bitInterval,
				errorData.data.Count,
				errorData.byteBoundaryMarker,
				false);
			// Mark point at which error detected
			AddDiagnosticLine(c, new StripLine
			{
				BorderColor = Color.Red,
				BorderWidth = 2,
				IntervalOffset = errorData.errorData.markerPosition
			});
			AddDiagnosticLabel(
				c,
				errorData.errorData,
				bitInterval,
				errorData.data.Count,
				errorData.bitBoundaryMarker,
				errorData.byteBoundaryMarker);

			var viewSize = Math.Min(1000, errorData.data.Count);
			c.ScaleView.Size = Math.Max(1, viewSize);
			c.ScaleView.Position = Math.Max(
				0,
				Math.Min(errorData.errorData.markerPosition - viewSize / 2, errorData.data.Count - viewSize));
		}

		void AddDiagnosticLine(Axis axis, StripLine line)
		{
			axis.StripLines.Add(line);
			diagnosticStripLines.Add(line);
		}

		void AddDiagnosticMarkers(
			Axis axis,
			List<ToneHandler.MarkerData> markers,
			Color lineColor,
			int lineWidth,
			int fallbackLabelWidth,
			int sampleCount,
			List<ToneHandler.MarkerData> additionalBoundaries,
			bool hideDiagnosticByteMarkers)
		{
			for (var index = 0; index < markers.Count; index++)
			{
				var marker = markers[index];
				if (hideDiagnosticByteMarkers && marker.markerDescription.StartsWith(
					"diagnostic byte ",
					StringComparison.Ordinal))
					continue;

				AddDiagnosticLine(axis, new StripLine
				{
					BorderColor = lineColor,
					BorderWidth = lineWidth,
					IntervalOffset = marker.markerPosition
				});

				if (string.IsNullOrEmpty(marker.markerDescription))
					continue;

				AddDiagnosticLabel(
					axis,
					marker,
					fallbackLabelWidth,
					sampleCount,
					markers,
					additionalBoundaries);
			}
		}

		void AddDiagnosticLabel(
			Axis axis,
			ToneHandler.MarkerData marker,
			int fallbackLabelWidth,
			int sampleCount,
			params List<ToneHandler.MarkerData>[] boundaryGroups)
		{
			if (string.IsNullOrEmpty(marker.markerDescription))
				return;

			var labelEnd = marker.markerPosition + fallbackLabelWidth;
			foreach (var boundaries in boundaryGroups)
			{
				if (boundaries == null)
					continue;
				foreach (var boundary in boundaries)
					if (boundary.markerPosition > marker.markerPosition &&
						boundary.markerPosition < labelEnd)
						labelEnd = boundary.markerPosition;
			}
			labelEnd = Math.Min(sampleCount, labelEnd);
			if (labelEnd <= marker.markerPosition)
				return;

			AddDiagnosticLine(axis, new StripLine
			{
				BackColor = Color.Transparent,
				BorderWidth = 0,
				IntervalOffset = marker.markerPosition,
				StripWidth = labelEnd - marker.markerPosition,
				Text = marker.markerDescription,
				TextAlignment = StringAlignment.Center
			});
		}

		static int GetTypicalMarkerInterval(
			List<ToneHandler.MarkerData> markers,
			int fallback)
		{
			var intervals = new List<int>();
			for (var index = 1; index < markers.Count; index++)
			{
				var interval = markers[index].markerPosition - markers[index - 1].markerPosition;
				if (interval > 0)
					intervals.Add(interval);
			}
			if (intervals.Count == 0)
				return fallback;
			intervals.Sort();
			return intervals[intervals.Count / 2];
		}

		private void save_Click(object sender, EventArgs e)
		{
			if (fileHandler.IsListeningToLineIn) return;
			try
			{
				fileHandler.StopListening();
				fileHandler.Serialise();
				activityStatusLabel.Text = "Recovery state saved";
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, exception.Message, "Recovery state could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void export_Click(object sender, EventArgs e)
		{
			if (fileHandler.IsListeningToLineIn) return;
			try
			{
				fileHandler.StopListening();
				fileHandler.Save();
			}
			catch (Exception exception)
			{
				MessageBox.Show(this, exception.Message, "CSW export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void exportRawFilesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using (var dialog = new FolderBrowserDialog
			{
				Description = "Choose a folder for complete recovered BBC files and their .inf metadata"
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				try
				{
					var exported = fileHandler.ExportRawFiles(dialog.SelectedPath);
					activityStatusLabel.Text = exported == 1 ? "Exported 1 recovered file" : "Exported " + exported + " recovered files";
					MessageBox.Show(
						this,
						exported == 1 ? "Exported 1 complete recovered file." : "Exported " + exported + " complete recovered files.",
						"Raw export",
						MessageBoxButtons.OK,
						MessageBoxIcon.Information);
				}
				catch (Exception exception)
				{
					MessageBox.Show(this, exception.Message, "Raw export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void exportDiskImage_Click(object sender, EventArgs e)
		{
			using (var plan = new DiskImageExportForm(fileHandler.files))
			{
				if (plan.ShowDialog(this) != DialogResult.OK) return;
				var format = plan.Format;
				var extension = DiskImageExporter.GetDefaultExtension(format);
				using (var dialog = new SaveFileDialog
				{
					Title = "Export BBC Micro disk image",
					Filter = DiskImageExporter.GetDescription(format) + " (*." + extension + ")|*." + extension + "|All files (*.*)|*.*",
					FilterIndex = 1,
					DefaultExt = extension,
					AddExtension = true,
					CheckPathExists = true,
					OverwritePrompt = true
				})
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return;
					var outputPath = Path.ChangeExtension(dialog.FileName, extension);
					if (!string.Equals(outputPath, dialog.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(outputPath) &&
						MessageBox.Show(this, "The selected format uses " + outputPath + ". Overwrite it?", "Disk image export",
							MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
						return;
					try
					{
						var result = fileHandler.ExportDiskImage(outputPath, format, plan.Names, plan.Excluded);
						activityStatusLabel.Text = "Exported " + DiskImageExporter.GetDescription(format);
						MessageBox.Show(this,
							result.files + " complete recovered file(s) exported to " + outputPath +
							". Used " + result.sectorsUsed + " of " + result.sectorsAvailable + " data sectors.",
							"Disk image export", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					catch (Exception exception)
					{
						MessageBox.Show(this, exception.Message, "Disk image export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			}
		}
	}
}
