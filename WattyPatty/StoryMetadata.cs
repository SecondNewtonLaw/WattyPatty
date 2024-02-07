/*
 *  WattyPatty - Story metadata extractor for Wattpad - StoryMetadata.cs
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

public class StoryMetadata {
    public StoryMetadata(bool isPaid, Uri storyImage, Author authorInformation, string storyName, string readingTime, IEnumerable<StoryChapter> chapters,
        long viewCount,
        long starCount) {
        IsPaid = isPaid;
        StoryImage = storyImage;
        AuthorInformation = authorInformation;
        StoryName = storyName;
        ReadingTime = readingTime;
        Chapters = chapters;
        ViewCount = viewCount;
        StarCount = starCount;
    }
    public StoryMetadata() {
        IsPaid = false;
        StoryImage = new Uri("https://www.wattpad.com/");
        AuthorInformation = new Author();
        StoryName = "???";
        ReadingTime = "???";
        Chapters = Enumerable.Empty<StoryChapter>();
        ViewCount = 0x0;
        StarCount = 0x0;
    }

    public bool IsOnProgress { get; set; }

    public bool IsPaid { get; set; }

    public Uri StoryImage { get; set; }

    public Author AuthorInformation { get; set; }

    public string StoryName { get; set; }

    public string ReadingTime { get; set; }

    public IEnumerable<StoryChapter> Chapters { get; set; }

    public long ViewCount { get; set; }

    public long StarCount { get; set; }

    public long StoryIdentifier { get; set; }
    
    public DateTimeOffset FirstPublishedAt { get; set; }
}