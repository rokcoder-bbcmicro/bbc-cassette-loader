using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace bbc_cassette_loader
{
	internal enum UpdateCheckStatus
	{
		NewerAvailable,
		UpToDate,
		NoRelease,
		RateLimited,
		Unavailable,
		InvalidResponse
	}

	internal sealed class UpdateCheckResult
	{
		public readonly UpdateCheckStatus Status;
		public readonly Version LatestVersion;
		public readonly string LatestVersionText;
		public readonly string ReleaseUrl;

		public UpdateCheckResult(
			UpdateCheckStatus status,
			Version latestVersion = null,
			string latestVersionText = null,
			string releaseUrl = null)
		{
			Status = status;
			LatestVersion = latestVersion;
			LatestVersionText = latestVersionText;
			ReleaseUrl = releaseUrl;
		}
	}

	internal static class UpdateChecker
	{
		internal const string LatestReleaseUrl =
			"https://api.github.com/repos/rokcoder-bbcmicro/bbc-cassette-loader/releases/latest";
		internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

		internal static HttpClient CreateClient(string userAgent, HttpMessageHandler handler = null)
		{
			var client = new HttpClient(handler ?? new HttpClientHandler())
			{
				Timeout = RequestTimeout
			};
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
			client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
			return client;
		}

		internal static async Task<UpdateCheckResult> CheckAsync(
			Version currentVersion,
			HttpClient client,
			CancellationToken cancellationToken)
		{
			if (currentVersion == null) throw new ArgumentNullException(nameof(currentVersion));
			if (client == null) throw new ArgumentNullException(nameof(client));

			HttpResponseMessage response;
			try
			{
				response = await client.GetAsync(
					LatestReleaseUrl,
					HttpCompletionOption.ResponseContentRead,
					cancellationToken).ConfigureAwait(false);
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return new UpdateCheckResult(UpdateCheckStatus.Unavailable);
			}
			catch (HttpRequestException)
			{
				return new UpdateCheckResult(UpdateCheckStatus.Unavailable);
			}

			using (response)
			{
				if (response.StatusCode == HttpStatusCode.NotFound)
					return new UpdateCheckResult(UpdateCheckStatus.NoRelease);
				if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
					return new UpdateCheckResult(UpdateCheckStatus.RateLimited);
				if (!response.IsSuccessStatusCode)
					return new UpdateCheckResult(UpdateCheckStatus.Unavailable);

				string json;
				try { json = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
				catch (HttpRequestException) { return new UpdateCheckResult(UpdateCheckStatus.Unavailable); }

				JObject release;
				try { release = JObject.Parse(json); }
				catch (JsonException)
				{
					return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);
				}

				var tag = (string)release["tag_name"];
				var releaseUrl = (string)release["html_url"];
				Version latestVersion;
				if (!TryParseStableVersion(tag, out latestVersion) || !IsCanonicalReleaseUrl(releaseUrl))
					return new UpdateCheckResult(UpdateCheckStatus.InvalidResponse);

				var current = new Version(
					currentVersion.Major,
					Math.Max(0, currentVersion.Minor),
					Math.Max(0, currentVersion.Build));
				return latestVersion.CompareTo(current) > 0
					? new UpdateCheckResult(UpdateCheckStatus.NewerAvailable, latestVersion, tag.Trim(), releaseUrl)
					: new UpdateCheckResult(UpdateCheckStatus.UpToDate, latestVersion, tag.Trim(), releaseUrl);
			}
		}

		internal static bool TryParseStableVersion(string text, out Version version)
		{
			version = null;
			if (string.IsNullOrWhiteSpace(text)) return false;
			var match = Regex.Match(
				text.Trim(),
				@"^[vV]?([0-9]+)\.([0-9]+)\.([0-9]+)(?:\+[0-9A-Za-z.-]+)?$",
				RegexOptions.CultureInvariant);
			if (!match.Success) return false;

			int major;
			int minor;
			int patch;
			if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
				!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor) ||
				!int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch))
				return false;
			version = new Version(major, minor, patch);
			return true;
		}

		static bool IsCanonicalReleaseUrl(string text)
		{
			Uri uri;
			return Uri.TryCreate(text, UriKind.Absolute, out uri) &&
				uri.Scheme == Uri.UriSchemeHttps &&
				string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
				uri.AbsolutePath.StartsWith(
					"/rokcoder-bbcmicro/bbc-cassette-loader/releases/",
					StringComparison.OrdinalIgnoreCase);
		}
	}
}
