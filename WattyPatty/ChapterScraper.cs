/*
 *  WattyPatty - Story metadata extractor for Wattpad - ChapterScraper.cs
 *  Copyright (C) 2024 Dottik
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

using System.Collections.Concurrent;
using Spectre.Console;

namespace WattyPatty;

/// <summary>
///     Downloads every chapter body through the public <c>apiv2 storytext</c> endpoint.
///     Chapter statistics (reads, votes, comments) are already provided by the story payload.
/// </summary>
public class ChapterScraper : IScraper<StoryMetadata> {
    /// <summary>
    ///     Chapters are fetched in parallel. Wattpad's rate limiter is handled globally by <see cref="WattpadApi" />.
    /// </summary>
    public const int DefaultMaxConcurrency = 4;

    private readonly StoryMetadata m_storyMetadata;
    private readonly int m_maxConcurrency;

    /// <summary>
    ///     Construct a Chapter scraper for the given story metadata. Will mutate the given story metadata.
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="maxConcurrency">How many chapters to download at the same time.</param>
    public ChapterScraper(in StoryMetadata metadata, int maxConcurrency = DefaultMaxConcurrency) {
        m_storyMetadata = metadata;
        m_maxConcurrency = Math.Max(1, maxConcurrency);
    }

    public async Task<StoryMetadata?> ScrapeAsync(WattpadApi api, CancellationToken cancellationToken = default) {
        var chapters = m_storyMetadata.Chapters.ToList();

        // "safe_for": 1 == brand safe, 0 == NOT brand safe. Fall back to the story payload when unavailable.
        var isBrandSafe = await api.IsBrandSafeAsync(m_storyMetadata.StoryIdentifier, cancellationToken)
            .ConfigureAwait(false);

        var chapterCount = chapters.Count;
        AnsiConsole.MarkupLine(
            $" [green][[-]][/] Downloading [green]{chapterCount}[/] chapter(s) for [yellow]{Markup.Escape(m_storyMetadata.StoryName)}[/] using [green]{m_maxConcurrency}[/] parallel request(s)...");

        using var throttle = new SemaphoreSlim(m_maxConcurrency, m_maxConcurrency);
        var failures = new ConcurrentBag<StoryChapter>();
        var completed = 0;

        var downloads = chapters.Select(async chapter => {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                if (api.IsRateLimited) {
                    failures.Add(chapter);
                    return;
                }

                var (text, pageCount) = await api.GetPartTextAsync(chapter.ChapterIdentifier, cancellationToken)
                    .ConfigureAwait(false);

                chapter.StoryText = text;
                chapter.ChapterPageCount = pageCount;

                if (isBrandSafe.HasValue)
                    chapter.IsNsfw = !isBrandSafe.Value;

                var index = Interlocked.Increment(ref completed);

                lock (ConsoleSynchronization.Gate) {
                    if (string.IsNullOrEmpty(text)) {
                        failures.Add(chapter);
                        AnsiConsole.MarkupLine(
                            $" [orange3][[*]][/] [red]Chapter {chapter.ChapterNumber} returned no text.[/] [grey]{Markup.Escape(chapter.ChapterName)}[/]");
                    }
                    else {
                        AnsiConsole.MarkupLine(
                            $" [green][[-]][/] Downloaded [[[yellow]{index}[/] of [green]{chapterCount}[/]]] [grey]{Markup.Escape(chapter.ChapterName)}[/] [grey]({pageCount} page(s), {text.Length} chars)[/]");
                    }
                }
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception exception) {
                failures.Add(chapter);

                lock (ConsoleSynchronization.Gate) {
                    AnsiConsole.MarkupLine(
                        $" [orange3][[*]][/] [red]Could not download chapter {chapter.ChapterNumber}[/] [grey]{Markup.Escape(chapter.ChapterName)}[/]: {Markup.Escape(exception.Message)}");
                }
            }
            finally {
                throttle.Release();
            }
        });

        await Task.WhenAll(downloads).ConfigureAwait(false);

        m_storyMetadata.Chapters = chapters;

        if (!failures.IsEmpty) {
            var failedNumbers = string.Join(", ", failures.OrderBy(chapter => chapter.ChapterNumber)
                .Select(chapter => chapter.ChapterNumber));

            if (api.IsRateLimited)
                AnsiConsole.MarkupLine(
                    $" [orange3][[*]][/] [red]Wattpad rate limited this client, so {failures.Count} chapter(s) were not downloaded.[/] Try again in a few minutes; the rest of the story was archived normally.");

            AnsiConsole.MarkupLine(
                $" [orange3][[*]][/] [red]{failures.Count} chapter(s) could not be downloaded[/] (chapter(s) {Markup.Escape(failedNumbers)}). They are marked as not retrieved in the output.");
        }

        return m_storyMetadata;
    }
}
