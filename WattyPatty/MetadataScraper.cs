/*
 *  WattyPatty - Story metadata extractor for Wattpad - MetadataScraper.cs
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

using System.Globalization;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace WattyPatty;

/// <summary>
///     Builds <see cref="StoryMetadata" /> from Wattpad's public v3 API.
///     A story payload already contains every part (chapter), so no browser navigation is required.
/// </summary>
public class MetadataScraper : IScraper<StoryMetadata> {
    private readonly long m_storyIdentifier;

    public MetadataScraper(long storyIdentifier) {
        m_storyIdentifier = storyIdentifier;
    }

    public async Task<StoryMetadata?> ScrapeAsync(WattpadApi api, CancellationToken cancellationToken = default) {
        var story = await api.GetStoryAsync(m_storyIdentifier, cancellationToken).ConfigureAwait(false);

        if (story is null) {
            AnsiConsole.MarkupLine($" [orange3][[*]][/] [red]Scraper error![/] Could not fetch story {m_storyIdentifier}.");
            return null;
        }

        var parts = story["parts"] as JArray;

        if (parts is null || parts.Count == 0) {
            AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/] The story has no readable parts.");
            return null;
        }

        var author = story["user"];
        var metadata = new StoryMetadata {
            StoryIdentifier = story.Value<long>("id"),
            StoryName = story.Value<string>("title") ?? "Unknown",
            StoryImage = TryCreateUri(story.Value<string>("cover")),
            IsPaid = story.Value<bool?>("isPaywalled") ?? false,
            IsOnProgress = !(story.Value<bool?>("completed") ?? false),
            ViewCount = story.Value<long?>("readCount") ?? 0,
            StarCount = story.Value<long?>("voteCount") ?? 0,
            ReadingTime = FormatReadingTime(story.Value<long?>("length") ?? 0),
            FirstPublishedAt = ParseDate(story["firstPublishedPart"]?.Value<string>("createDate"))
                              ?? ParseDate(story.Value<string>("createDate"))
                              ?? default,
            AuthorInformation = new Author {
                AuthorName = author?.Value<string>("name") ?? "Unknown",
                AuthorProfile = TryCreateUri(author?.Value<string>("username") is { } username
                    ? $"https://www.wattpad.com/user/{username}"
                    : null),
            },
            Chapters = BuildChapters(parts),
        };

        return metadata;
    }

    private static List<StoryChapter> BuildChapters(JArray parts) {
        var chapters = new List<StoryChapter>(parts.Count);
        long chapterNumber = 1;

        foreach (var part in parts) {
            var releaseDateAsString = part.Value<string>("createDate") ?? "Unknown";

            chapters.Add(new StoryChapter {
                ChapterIdentifier = part.Value<long>("id"),
                ChapterLink = TryCreateUri(part.Value<string>("url")),
                ChapterNumber = chapterNumber++,
                ChapterName = part.Value<string>("title") ?? "Untitled",
                ChapterPageCount = 0, // Filled in by ChapterScraper once the text is downloaded.
                ReleaseDateAsString = releaseDateAsString,
                ReleaseDate = ParseDate(releaseDateAsString),
                IsNewChapter = part.Value<bool?>("isNew") ?? false,
                ReadCount = part.Value<long?>("readCount") ?? 0,
                VoteCount = part.Value<long?>("voteCount") ?? 0,
                CommentCount = part.Value<long?>("commentCount") ?? 0,
                StoryText = null,
            });
        }

        return chapters;
    }

    /// <summary>
    ///     Wattpad reports the story length in characters. Their reader assumes roughly 5 characters per word and
    ///     200 words per minute, so mirror that to obtain a comparable "reading time".
    /// </summary>
    private static string FormatReadingTime(long characterCount) {
        if (characterCount <= 0)
            return "Unknown";

        var minutes = (long)Math.Ceiling(characterCount / 5.0 / 200.0);
        if (minutes < 60)
            return $"{minutes} mins";

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        var hourLabel = hours == 1 ? "hour" : "hours";
        return remainingMinutes == 0
            ? $"{hours} {hourLabel}"
            : $"{hours} {hourLabel} {remainingMinutes} mins";
    }

    private static DateTimeOffset? ParseDate(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static Uri TryCreateUri(string? value) {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : new Uri("https://www.wattpad.com/");
    }
}
