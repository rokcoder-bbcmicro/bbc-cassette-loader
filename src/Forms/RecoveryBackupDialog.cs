using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace bbc_cassette_loader
{
	internal sealed class RecoveryBackupDialog : Form
	{
		readonly ListBox backupList;

		internal RecoveryBackupInfo SelectedBackup
		{
			get { return backupList.SelectedItem as RecoveryBackupInfo; }
		}

		internal RecoveryBackupDialog(IList<RecoveryBackupInfo> backups)
		{
			Text = "Restore recovery backup";
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			ClientSize = new Size(430, 290);

			var description = new Label
			{
				Dock = DockStyle.Fill,
				Text = "Choose a reset backup to replace the current recovery session.\r\n" +
					"Diagnostic waveforms are not included in archived backups.",
				Padding = new Padding(10, 10, 10, 4),
				AutoSize = false
			};

			backupList = new ListBox
			{
				Dock = DockStyle.Fill,
				IntegralHeight = false
			};
			backupList.Items.AddRange(new List<RecoveryBackupInfo>(backups).ToArray());
			if (backupList.Items.Count > 0) backupList.SelectedIndex = 0;
			backupList.DoubleClick += (sender, args) =>
			{
				if (SelectedBackup != null) DialogResult = DialogResult.OK;
			};

			var restore = new Button { Text = "Restore", DialogResult = DialogResult.OK, AutoSize = true };
			var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
			restore.Click += (sender, args) =>
			{
				if (SelectedBackup == null)
				{
					DialogResult = DialogResult.None;
					MessageBox.Show(this, "Select a recovery backup first.", Text,
						MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			};
			AcceptButton = restore;
			CancelButton = cancel;

			var buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.RightToLeft,
				WrapContents = false,
				Padding = new Padding(0, 4, 8, 8)
			};
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(restore);

			var layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				Padding = new Padding(8)
			};
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
			layout.Controls.Add(description, 0, 0);
			layout.Controls.Add(backupList, 0, 1);
			layout.Controls.Add(buttons, 0, 2);
			Controls.Add(layout);
		}
	}
}
