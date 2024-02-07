/*
 *  WattyPatty - Story metadata extractor for Wattpad - StoryChapter.cs
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

namespace WattyPatty;

public class StoryChapter {
    public StoryChapter(bool isNewChapter, Uri chapterLink, long chapterNumber, long chapterPageCount, string chapterName, DateTimeOffset? releaseDate,
        string releaseDateAsString, string storyText) {
        IsNewChapter = isNewChapter;
        ChapterLink = chapterLink;
        ChapterNumber = chapterNumber;
        ChapterPageCount = chapterPageCount;
        ChapterName = chapterName;
        ReleaseDate = releaseDate;
        ReleaseDateAsString = releaseDateAsString;
        StoryText = storyText;
    }

    public StoryChapter() {
        IsNewChapter = false;
        ChapterLink = new Uri("https://www.wattpad.com/");
        ChapterNumber = -1;
        ChapterPageCount = -1;
        ChapterName = "Unknown";
        ReleaseDate = new DateTimeOffset();
        ReleaseDateAsString = "Unknown";
        StoryText = "...";
    }

    public bool IsNsfw { get; set; }
    public bool IsNewChapter { get; set; }

    public Uri ChapterLink { get; set; }

    public long ChapterNumber { get; set; }

    public long ChapterPageCount { get; set; }

    public long ReadCount { get; set; }

    public long VoteCount { get; set; }

    public long CommentCount { get; set; }
    public long ChapterIdentifier { get; set; }

    public string ChapterName { get; set; }

    public DateTimeOffset? ReleaseDate { get; set; }

    public string ReleaseDateAsString { get; set; }

    public string? StoryText { get; set; }
}