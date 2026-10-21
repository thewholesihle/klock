using System.Text.Json;

namespace Klock.Core.Updates;

public enum UpdateState { UpToDate, Available, Failed }

public sealed record UpdateInfo(Version Latest, string Url, string? Notes);

public sealed record UpdateResult(UpdateState State, UpdateInfo? Info = null);

/// <summary>
/// Privacy-respecting update check: one GET of a static JSON manifest. Nothing about the user or machine is sent.
/// Manifest shape: <c>{ "version": "1.2.0", "url": "https://.../Setup.exe", "notes": "..." }</c>.
/// </summary>
public static class UpdateChecker
{
    public static UpdateInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var v) || !Version.TryParse(v.GetString(), out var version)) return null;
            if (!root.TryGetProperty("url", out var u) || u.GetString() is not { Length: > 0 } url) return null;
            // Only ever offer an https download.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            string? notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
            return new UpdateInfo(version, url, notes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsNewer(Version latest, Version current) => latest > current;

    public static async Task<UpdateResult> CheckAsync(Version current, string? manifestUrl = null, HttpClient? client = null,
        CancellationToken ct = default)
    {
        bool owns = client == null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, manifestUrl ?? AppInfo.UpdateManifestUrl);
            req.Headers.UserAgent.ParseAdd($"{AppInfo.Name}/{current}");
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return new UpdateResult(UpdateState.Failed);
            var info = Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (info == null) return new UpdateResult(UpdateState.Failed);
            return IsNewer(info.Latest, current)
                ? new UpdateResult(UpdateState.Available, info)
                : new UpdateResult(UpdateState.UpToDate, info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or UriFormatException)
        {
            return new UpdateResult(UpdateState.Failed);
        }
        finally
        {
            if (owns) client.Dispose();
        }
    }
}
