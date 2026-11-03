/*
 *  WattyPatty - Story metadata extractor for Wattpad - MarkdownExporter.cs
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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WattyPatty;

/// <summary>
///     Renders <see cref="StoryMetadata" /> as a single self-contained Markdown document:
///     YAML front matter, story header and one section per chapter.
/// </summary>
public static class MarkdownExporter {
    private static readonly Regex InvalidFileNameCharsRegex =
        new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);

    public static string Build(StoryMetadata story) {
        var builder = new StringBuilder();

        AppendFrontMatter(builder, story);

        builder.Append("# ").Append(EscapeInline(story.StoryName)).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(story.AuthorInformation.AuthorName)) {
            builder.Append("*by ");
            if (story.AuthorInformation.AuthorProfile.AbsoluteUri is { Length: > 0 } profile &&
                profile != "https://www.wattpad.com/")
                builder.Append('[').Append(EscapeInline(story.AuthorInformation.AuthorName)).Append("](")
                    .Append(profile).Append(')');
            else
                builder.Append(EscapeInline(story.AuthorInformation.AuthorName));

            builder.Append("*\n\n");
        }

        if (story.StoryImage.AbsoluteUri != "https://www.wattpad.com/")
            builder.Append("![Cover](").Append(story.StoryImage.AbsoluteUri).Append(")\n\n");

        var chapters = story.Chapters.ToList();

        for (var i = 0; i < chapters.Count; i++) {
            var chapter = chapters[i];

            builder.Append("## ").Append(chapter.ChapterNumber).Append(". ")
                .Append(EscapeInline(chapter.ChapterName.Trim())).Append("\n\n");

            builder.Append('*');
            if (chapter.ReleaseDate is { } releaseDate)
                builder.Append("Published: ")
                    .Append(releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" · ");

            builder.Append("Reads: ").Append(chapter.ReadCount)
                .Append(" · Votes: ").Append(chapter.VoteCount)
                .Append(" · Comments: ").Append(chapter.CommentCount)
                .Append(" · [Read on Wattpad](").Append(chapter.ChapterLink.AbsoluteUri).Append(')')
                .Append("*\n\n");

            var body = HtmlToMarkdown.Convert(chapter.StoryText);

            if (body.Length == 0)
                builder.Append("_This chapter could not be retrieved._\n\n");
            else
                builder.Append(body).Append('\n');

            if (i < chapters.Count - 1)
                builder.Append("---\n\n");
        }

        return builder.ToString();
    }

    /// <summary>Write the story as <c>{StoryName}.md</c> inside <paramref name="directory" />.</summary>
    public static async Task<string> WriteAsync(StoryMetadata story, string directory,
        CancellationToken cancellationToken = default) {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{GetFileName(story)}.md");

        await File.WriteAllTextAsync(path, Build(story), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    /// <summary>Build a filesystem-safe file name (without extension) from the story title.</summary>
    public static string GetFileName(StoryMetadata story) {
        var name = InvalidFileNameCharsRegex.Replace(story.StoryName, " ");
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '.');

        return name.Length == 0 ? $"story-{story.StoryIdentifier}" : name;
    }

    private static void AppendFrontMatter(StringBuilder builder, StoryMetadata story) {
        builder.Append("---\n");
        AppendYaml(builder, "title", story.StoryName);
        AppendYaml(builder, "author", story.AuthorInformation.AuthorName);
        AppendYaml(builder, "author_profile", story.AuthorInformation.AuthorProfile.AbsoluteUri);
        AppendYaml(builder, "story_id", story.StoryIdentifier.ToString(CultureInfo.InvariantCulture));
        AppendYaml(builder, "source", $"https://www.wattpad.com/story/{story.StoryIdentifier}");
        AppendYaml(builder, "cover", story.StoryImage.AbsoluteUri);
        AppendYaml(builder, "status", story.IsOnProgress ? "ongoing" : "completed");
        AppendYaml(builder, "is_paid", story.IsPaid ? "true" : "false");
        AppendYaml(builder, "chapters", story.Chapters.Count().ToString(CultureInfo.InvariantCulture));
        AppendYaml(builder, "reads", story.ViewCount.ToString(CultureInfo.InvariantCulture));
        AppendYaml(builder, "votes", story.StarCount.ToString(CultureInfo.InvariantCulture));
        AppendYaml(builder, "reading_time", story.ReadingTime);

        if (story.FirstPublishedAt != default)
            AppendYaml(builder, "first_published", story.FirstPublishedAt.ToString("O", CultureInfo.InvariantCulture));

        builder.Append("---\n\n");
    }

    private static void AppendYaml(StringBuilder builder, string key, string value) {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        builder.Append(key).Append(": \"").Append(escaped).Append("\"\n");
    }

    /// <summary>Escape the few characters that could break a heading or a paragraph in Markdown.</summary>
    private static string EscapeInline(string value) {
        return value.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`")
            .Replace("[", "\\[").Replace("]", "\\]");
    }
}
