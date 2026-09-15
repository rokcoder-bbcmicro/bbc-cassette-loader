using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace bbc_cassette_loader
{
	sealed class AboutForm : Form
	{
		const string ProjectUrl = "https://github.com/rokcoder-bbcmicro/bbc-cassette-loader";

		public AboutForm()
		{
			Text = "About BBC Micro Cassette Loader";
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var layout = new TableLayoutPanel
			{
				AutoSize = true,
				ColumnCount = 1,
				Padding = new Padding(18),
				Dock = DockStyle.Fill
			};
			layout.Controls.Add(new Label
			{
				Text = "BBC Micro Cassette Loader",
				Font = new Font(Font, FontStyle.Bold),
				AutoSize = true,
				Margin = new Padding(0, 0, 0, 8)
			});
			layout.Controls.Add(new Label
			{
				Text = "Recover BBC Micro cassette files from WAV recordings or line-in audio.",
				AutoSize = true,
				Margin = new Padding(0, 0, 0, 4)
			});
			layout.Controls.Add(new Label
			{
				Text = "Version " + Application.ProductVersion + "\nCopyright © 2026 Cliff Davies\nLicensed under GPLv3-only.",
				AutoSize = true,
				Margin = new Padding(0, 0, 0, 12)
			});

			var links = new FlowLayoutPanel
			{
				AutoSize = true,
				WrapContents = false,
				Margin = Padding.Empty
			};
			AddLink(links, "Project/GitHub", () => OpenUrl(ProjectUrl));
			AddLink(links, "LICENSE", () => OpenFile("LICENSE", "Licence"));
			AddLink(links, "Third-party notices", () => OpenFile("THIRD-PARTY-NOTICES.txt", "Third-party notices"));
			layout.Controls.Add(links);

			var close = new Button
			{
				Text = "OK",
				AutoSize = true,
				DialogResult = DialogResult.OK,
				Anchor = AnchorStyles.Right,
				Margin = new Padding(0, 14, 0, 0)
			};
			layout.Controls.Add(close);
			AcceptButton = close;
			Controls.Add(layout);
		}

		static void AddLink(Control parent, string text, Action action)
		{
			var link = new LinkLabel { Text = text, AutoSize = true, TabStop = true, Margin = new Padding(0, 0, 14, 0) };
			link.LinkClicked += (sender, args) => action();
			parent.Controls.Add(link);
		}

		void OpenFile(string fileName, string description)
		{
			var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
			if (!File.Exists(path))
			{
				MessageBox.Show(this, description + " is not available in this installation:\n" + path,
					description, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
			catch (Exception exception)
			{
				MessageBox.Show(this, "Unable to open " + description.ToLowerInvariant() + ":\n" + exception.Message,
					description, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		void OpenUrl(string url)
		{
			try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
			catch (Exception exception)
			{
				MessageBox.Show(this, "Unable to open the project page:\n" + exception.Message,
					"Project/GitHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
	}
}
