/*
 *  WattyPatty - Story metadata extractor for Wattpad - WattpadApi.cs
 *  Copyright (C) 2026 Dottik
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU Affero General Public License as published
 *  by the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Affero General Public License for more details.
 *
 *  You should have received a copy of the GNU Affero General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace WattyPatty;

/// <summary>
///     Minimal, unauthenticated client for Wattpad's public (undocumented) HTTP API.
///     The website itself is a Remix application with hashed CSS-module class names, which makes
///     DOM scraping extremely brittle. The endpoints below are the same ones the site uses.
/// </summary>
public sealed class WattpadApi : IDisposable {
    private const string SiteRoot = "https://www.wattpad.com";
    private const int MaxChapterPages = 100;

    /// <summary>
    ///     Field selection for <c>/api/v3/stories/{id}</c>.
    ///     IMPORTANT: <c>isPaywalled</c>, <c>paidModel</c>, <c>readCount</c> and <c>mature</c> are only
    ///     returned when explicitly requested. <c>parts</c> is returned in reading order.
    /// </summary>
    private const string StoryFields =
        "id,title,length,createDate,modifyDate,voteCount,commentCount,readCount,completed,mature," +
        "isPaywalled,paidModel,cover,url,user(name,username),firstPublishedPart(id,createDate)," +
        "parts(id,title,url,createDate,length,commentCount,voteCount,readCount,isNew,draft,deleted)";

    private static readonly Regex StoryIdRegex = new(@"/story/(\d+)", RegexOptions.Compiled);
    private static readonly Regex GroupIdRegex = new(@"""group""\s*:\s*\{\s*""id""\s*:\s*""?(\d+)", RegexOptions.Compiled);

    private readonly HttpClient m_httpClient;
    private readonly bool m_ownsHttpClient;
    private readonly GlobalRequestThrottle m_throttle = new();

    /// <summary>Optional sink for user-facing notices (rate limiting, retries).</summary>
    public Action<string>? DiagnosticMessage { get; set; }

    /// <summary>True when the server has rate limited this client and retries were given up.</summary>
    public bool IsRateLimited => m_throttle.IsAbandoned;

    public WattpadApi(HttpClient? httpClient = null) {
        m_ownsHttpClient = httpClient is null;
        m_httpClient = httpClient ?? new HttpClient(new HttpClientHandler {
            AutomaticDecompression = DecompressionMethods.All,
        });

        if (m_httpClient.DefaultRequestHeaders.UserAgent.Count == 0) {
            m_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/131.0.0.0 Safari/537.36");
        }

        // NOTE: do not ask for text/plain here. When "text/plain" outranks "application/json" the legacy
        // PHP backend answers with a var_export() dump ("array ( ... )") instead of JSON.
        m_httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        m_httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public void Dispose() {
        if (m_ownsHttpClient)
            m_httpClient.Dispose();
    }

    /// <summary>
    ///     Resolve a wattpad URL (story or part/chapter URL) to a story identifier.
    ///     Returns <see langword="null" /> when the URL does not point to a story.
    /// </summary>
    public async Task<long?> ResolveStoryIdentifierAsync(Uri uri, CancellationToken cancellationToken = default) {
        var storyMatch = StoryIdRegex.Match(uri.AbsoluteUri);
        if (storyMatch.Success)
            return long.Parse(storyMatch.Groups[1].Value);

        // Part URLs (/123456789-title) have no story id in them, so ask the page itself.
        // The reader still ships a prefetched payload that contains the owning story ("group").
        var html = await ResolveBodyAsync(uri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
        if (html is null)
            return null;

        var groupMatch = GroupIdRegex.Match(html);
        return groupMatch.Success ? long.Parse(groupMatch.Groups[1].Value) : null;
    }

    /// <summary>Fetch a story (metadata + all parts) from the public v3 API.</summary>
    public async Task<JObject?> GetStoryAsync(long storyIdentifier, CancellationToken cancellationToken = default) {
        var url = $"{SiteRoot}/api/v3/stories/{storyIdentifier}?fields={StoryFields}";
        var payload = await ResolveBodyAsync(url, cancellationToken).ConfigureAwait(false);

        // Guard against the legacy PHP backend's var_export() dump.
        if (payload is null || !payload.TrimStart().StartsWith('{'))
            return null;

        return JObject.Parse(payload);
    }

    /// <summary>
    ///     Download the full HTML body of a part (chapter) by walking the <c>apiv2 storytext</c> pages.
    ///     The API returns an empty body once there are no further pages.
    /// </summary>
    public async Task<(string Text, long PageCount)> GetPartTextAsync(long partIdentifier,
        CancellationToken cancellationToken = default) {
        var builder = new StringBuilder();
        string? previousPage = null;
        long page = 0;

        while (page < MaxChapterPages) {
            var url = $"{SiteRoot}/apiv2/?m=storytext&id={partIdentifier}&page={page}";
            var (statusCode, chunk) = await SendAsync(url, cancellationToken).ConfigureAwait(false);

            // Locked, deleted or paywalled parts cannot be read. Not fatal, but it must be visible.
            if (statusCode is 403 or 404 or 410)
                return (builder.ToString(), page);

            // A failed request must not be mistaken for the end of the chapter, otherwise the archive
            // would silently be truncated.
            if (statusCode != 200 || chunk is null)
                throw new InvalidOperationException(
                    $"Could not download page {page} of chapter {partIdentifier} (HTTP {statusCode}) after several attempts.");

            // End of chapter (empty response), or a server that repeats the last page forever.
            if (chunk.Length == 0 || chunk == previousPage)
                break;

            builder.Append(chunk);
            previousPage = chunk;
            page++;
        }

        return (builder.ToString(), page);
    }

    /// <summary>
    ///     Query the story safety classification. Returns <see langword="null" /> if the endpoint fails, so the
    ///     caller leaves the default classification untouched instead of guessing.
    /// </summary>
    public async Task<bool?> IsBrandSafeAsync(long storyIdentifier, CancellationToken cancellationToken = default) {
        var url = $"{SiteRoot}/v5/stories/{storyIdentifier}/classification/safety";
        var payload = await ResolveBodyAsync(url, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        try {
            var safeFor = JObject.Parse(payload)["safe_for"];
            // safe_for: 1 == brand safe, 0 == NOT brand safe.
            return safeFor?.Value<int>() == 1;
        }
        catch {
            return null;
        }
    }

    private async Task<string?> ResolveBodyAsync(string url, CancellationToken cancellationToken) {
        var (statusCode, body) = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        return statusCode == 200 ? body : null;
    }

    private void ReportDiagnostic(string message) {
        try {
            DiagnosticMessage?.Invoke(message);
        }
        catch {
            // Diagnostics must never break the scrape.
        }
    }

    /// <summary>Whether the response body is Wattpad's rate-limit error.</summary>
    private static bool IsRateLimitedBody(string body) {
        return body.Contains("RateLimitExceeded", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     GET the url, retrying transient failures a few times. Never throws for HTTP/network errors;
    ///     the status code (0 for a network failure) lets callers decide what is fatal.
    /// </summary>
    private async Task<(int StatusCode, string? Body)> SendAsync(string url, CancellationToken cancellationToken) {
        const int maxTransientAttempts = 4;
        const int maxThrottleAttempts = 5;

        var transientAttempts = 0;
        var throttleAttempts = 0;

        while (true) {
            // Once the limiter has been given up on, fail fast instead of burning more cooldowns.
            if (m_throttle.IsAbandoned)
                return ((int)HttpStatusCode.TooManyRequests, null);

            await m_throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

            int statusCode;
            string body;

            try {
                using var response = await m_httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                statusCode = (int)response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) {
                statusCode = 0;
                body = string.Empty;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
                statusCode = 0; // Request timeout.
                body = string.Empty;
            }

            if (statusCode is >= 200 and < 300) {
                m_throttle.ReportSuccess();
                return (statusCode, body);
            }

            if (statusCode == (int)HttpStatusCode.TooManyRequests || IsRateLimitedBody(body)) {
                if (++throttleAttempts > maxThrottleAttempts) {
                    m_throttle.ReportForfeited();
                    ReportDiagnostic(
                        "Giving up: Wattpad keeps rate limiting this client. Chapters that were not downloaded are marked as not retrieved; try again in a few minutes.");
                    return (statusCode, body);
                }

                m_throttle.ReportThrottled();
                ReportDiagnostic(
                    $"Wattpad is rate limiting this client (HTTP 429); pausing {(int)m_throttle.CurrentCooldown.TotalSeconds}s before retrying.");
                continue;
            }

            if (statusCode == 0 || statusCode >= 500) {
                if (++transientAttempts >= maxTransientAttempts)
                    return (statusCode, null);

                await Task.Delay(TimeSpan.FromMilliseconds(300 * transientAttempts), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            // Permanent 4XX client error.
            return (statusCode, body);
        }
    }
}
