using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace bbc_cassette_loader
{
	sealed class DiskImageExportForm : Form
	{
		readonly Dictionary<string, BBCFile> files;
		readonly ComboBox formatBox;
		readonly DataGridView fileGrid;
		readonly Label summary;
		readonly Label selectedIssue;
		public DiskImageFormat Format { get; private set; }
		public Dictionary<string, string> Names { get; private set; }
		public HashSet<string> Excluded { get; private set; }

		public DiskImageExportForm(IEnumerable<KeyValuePair<string, BBCFile>> source)
		{
			files = source.Where(pair => pair.Value.IsComplete())
				.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(pair => pair.Key, pair => pair.Value);
			Text = "Prepare BBC Micro disk image";
			MinimumSize = new Size(720, 440);
			Size = new Size(820, 600);
			StartPosition = FormStartPosition.CenterParent;
			MinimizeBox = false;
			MaximizeBox = false;

			var formatLabel = new Label { Text = "Image format:", AutoSize = true, Left = 12, Top = 16 };
			formatBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 105, Top = 12, Width = 300 };
			foreach (DiskImageFormat format in Enum.GetValues(typeof(DiskImageFormat))) formatBox.Items.Add(DiskImageExporter.GetDescription(format));
			formatBox.SelectedIndex = 0;
			formatBox.SelectedIndexChanged += delegate { UpdateSummary(); };

			fileGrid = new DataGridView {
				Left = 12, Top = 48, Width = 780, Height = 390, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
				AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
				AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
				EditMode = DataGridViewEditMode.EditOnEnter
			};
			fileGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Include", Width = 55, TrueValue = true, FalseValue = false });
			fileGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Recovered filename", ReadOnly = true, Width = 300 });
			fileGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name on disk image", Width = 300 });
			foreach (var pair in files)
				fileGrid.Rows.Add(true, pair.Key, pair.Value.filename);
			fileGrid.ClearSelection();
			fileGrid.CurrentCell = null;
			fileGrid.CellValueChanged += delegate { UpdateSummary(); };
			fileGrid.CurrentCellDirtyStateChanged += delegate { if (fileGrid.IsCurrentCellDirty) fileGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
			fileGrid.CurrentCellChanged += delegate { UpdateSelectedIssue(); };

			summary = new Label { Left = 12, Top = 448, Width = 780, Height = 32, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true };
			selectedIssue = new Label { Left = 12, Top = 480, Width = 780, Height = 28, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true, ForeColor = Color.DarkRed };
			var ok = new Button { Text = "Continue", DialogResult = DialogResult.None, Width = 90, Left = 600, Top = 520, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
			var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Left = 700, Top = 520, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
			ok.Click += Confirm;
			Controls.AddRange(new Control[] { formatLabel, formatBox, fileGrid, summary, selectedIssue, ok, cancel });
			AcceptButton = ok;
			CancelButton = cancel;
			UpdateSummary();
		}

		void UpdateSummary()
		{
			HighlightDuplicateNames();
			UpdateSelectedIssue();
			try
			{
				var result = DiskImageExporter.Preview((DiskImageFormat)formatBox.SelectedIndex, BuildFiles(), BuildNames(), BuildExcluded());
				summary.Text = result.files + " file(s); " + result.sectorsUsed + " of " + result.sectorsAvailable + " data sectors used. Names and capacity are valid.";
				summary.ForeColor = Color.DarkGreen;
			}
			catch (Exception)
			{
				summary.Text = "Fix the highlighted issues before continuing.";
				summary.ForeColor = Color.DarkRed;
			}
		}

		void HighlightDuplicateNames()
		{
			var includedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (DataGridViewRow row in fileGrid.Rows)
			{
				if (!Convert.ToBoolean(row.Cells[0].Value)) continue;
				var name = row.Cells[2].Value as string;
				if (name == null) continue;
				int count;
				includedNames.TryGetValue(name, out count);
				includedNames[name] = count + 1;
			}
			var format = (DiskImageFormat)formatBox.SelectedIndex;
			var isAdfs = format >= DiskImageFormat.AdfsS;
			var includedFiles = 0;
			var adfsUsedSectors = 7;
			var adfsCapacity = isAdfs ? DiskImageExporter.GetDataSectorCapacity(format) + 7 : 0;
			var dfsTracks = format == DiskImageFormat.Dfs40Ssd || format == DiskImageFormat.Dfs40Dsd ? 40 : 80;
			var dfsSides = format == DiskImageFormat.Dfs40Dsd || format == DiskImageFormat.Dfs80Dsd ? 2 : 1;
			var dfsSectorsPerSide = dfsTracks * 10;
			var dfsUsedSectors = new int[dfsSides];
			var dfsFileCounts = new int[dfsSides];
			foreach (DataGridViewRow row in fileGrid.Rows)
			{
				var included = Convert.ToBoolean(row.Cells[0].Value);
				var name = row.Cells[2].Value as string;
				var duplicate = included && name != null && includedNames[name] > 1;
				var nameError = included ? DiskImageExporter.GetNameValidationError(format, name) : null;
				var capacityError = false;
				if (included)
				{
					var sectors = DiskImageExporter.GetFileSectorCount(files[(string)row.Cells[1].Value]);
					if (isAdfs)
					{
						capacityError = includedFiles >= 47 || adfsUsedSectors + sectors > adfsCapacity;
						if (!capacityError) adfsUsedSectors += sectors;
					}
					else
					{
						var side = dfsFileCounts[0] == 31 || dfsUsedSectors[0] + 2 + sectors > dfsSectorsPerSide ? 1 : 0;
						capacityError = side >= dfsSides || dfsFileCounts[side] >= 31 || dfsUsedSectors[side] + 2 + sectors > dfsSectorsPerSide;
						if (!capacityError)
						{
							dfsUsedSectors[side] += 2 + sectors;
							dfsFileCounts[side]++;
						}
					}
					includedFiles++;
				}
				var messages = new List<string>();
				if (duplicate) messages.Add("Duplicate disk name.");
				if (nameError != null) messages.Add(nameError);
				if (capacityError) messages.Add("This file exceeds the selected image's remaining capacity or file-count limit.");
				row.DefaultCellStyle.BackColor = capacityError ? Color.LemonChiffon : Color.Empty;
				row.Cells[2].Style.BackColor = messages.Count == 0 ? Color.Empty : Color.MistyRose;
				row.Cells[2].ToolTipText = messages.Count == 0 ? "" : string.Join(" ", messages) + " Select this cell for details.";
				row.Cells[1].ToolTipText = capacityError ? messages[messages.Count - 1] + " Select this cell for details." : "";
			}
		}

		void UpdateSelectedIssue()
		{
			if (selectedIssue == null || fileGrid.CurrentCell == null)
			{
				if (selectedIssue != null) selectedIssue.Text = "Select a highlighted cell to see its specific validation error.";
				return;
			}
			var message = fileGrid.CurrentCell.ToolTipText;
			selectedIssue.Text = string.IsNullOrEmpty(message)
				? "Select a highlighted cell to see its specific validation error."
				: message.Replace(" Select this cell for details.", "");
		}

		Dictionary<string, BBCFile> BuildFiles() { return files; }
		Dictionary<string, string> BuildNames()
		{
			var result = new Dictionary<string, string>();
			foreach (DataGridViewRow row in fileGrid.Rows) result[(string)row.Cells[1].Value] = (string)row.Cells[2].Value;
			return result;
		}
		HashSet<string> BuildExcluded()
		{
			var result = new HashSet<string>();
			foreach (DataGridViewRow row in fileGrid.Rows) if (!Convert.ToBoolean(row.Cells[0].Value)) result.Add((string)row.Cells[1].Value);
			return result;
		}

		void Confirm(object sender, EventArgs e)
		{
			fileGrid.EndEdit();
			try
			{
				Format = (DiskImageFormat)formatBox.SelectedIndex;
				Names = BuildNames();
				Excluded = BuildExcluded();
				DiskImageExporter.Preview(Format, files, Names, Excluded);
				DialogResult = DialogResult.OK;
				Close();
			}
			catch (Exception exception)
			{
				DialogResult = DialogResult.None;
				MessageBox.Show(this, exception.Message, "Disk image export needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}
	}
}
