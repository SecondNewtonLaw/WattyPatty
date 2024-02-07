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
using PuppeteerSharp;
using Spectre.Console;

namespace WattyPatty;

public class MetadataScraper : IScraper<StoryMetadata> {
    public async Task<StoryMetadata?> ScrapeAsync(IPage page) {
        var metadata = new StoryMetadata { };

        // //div[@class="story-header"] | Xpath->Header
        // div.story-header            | QSelector->Header
        // div.story-header div.story-cover img | QSelector->Header->StoryCover->Img

        { // Story Author Name
            var storyTitle = await page.QuerySelectorAsync("div .story-header>div.story-info>div.author-info>div.author-info__username>a");
            if (storyTitle == null) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            metadata.AuthorInformation.AuthorName = await (await storyTitle.GetPropertyAsync("innerText")).JsonValueAsync<string>();
            // The href is a relative url into /user/xxxxxxxxxxxxxxxxxxx xxxxx being the name of the user.
            metadata.AuthorInformation.AuthorProfile = new Uri(await (await storyTitle.GetPropertyAsync("href")).JsonValueAsync<string>());
        }

        { // Story cover
            var storyCover = await page.QuerySelectorAsync("div.story-header>div.story-cover>img");

            if (storyCover == null) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            metadata.StoryImage = new Uri(await (await storyCover.GetPropertyAsync("src")).JsonValueAsync<string>());
        }
        // div.story-header div.story-info | QSelector->Header->StoryInfo

        {
            // div.story-header div.story-info div.paid-indicator | QSelector->Header->StoryInfo->paid-indicator
            if (await page.QuerySelectorAsync("div.story-header>div.story-info>div.paid-indicator") != null)
                metadata.IsPaid = true;
        }

        {
            var storyTitle = await page.QuerySelectorAsync("div.story-header>div.story-info>div.story-info__title");
            if (storyTitle == null) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            metadata.StoryName = await (await storyTitle.GetPropertyAsync("innerText")).JsonValueAsync<string>();
        }

        { // Story id 
            // a.card.on-navigate
            // Hacky way of getting it, we must read the href property and split the string, Nasty, ik.

            var bestRankingOfStory = await page.QuerySelectorAsync("a.card.on-navigate");

            if (bestRankingOfStory == null) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            metadata.StoryIdentifier = long.Parse((await (await bestRankingOfStory.GetPropertyAsync("href")).JsonValueAsync<string>()).Split("/")[4]);
        }

        var numberOfChapters = 0L;
        {
            // Statistic items
            // div .story-header div.story-info ul li.stats-item

            var elements = await page.QuerySelectorAllAsync("div.story-header>div.story-info>ul>li.stats-item>span.sr-only");

            if (elements == null || elements.Length == 0) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            foreach (var element in elements) {
                var innerText = await (await element.GetPropertyAsync("innerText")).JsonValueAsync<string>();

                if (innerText.Contains("Time")) {
                    var readTime = string.Join(' ', innerText.Split(' ')[1..]);
                    metadata.ReadingTime = readTime;
                    continue;
                }

                if (innerText.Contains("Parts")) {
                    innerText = innerText.Split(' ')[1].Replace(",", "").Replace(".", "").Trim();
                    var mfactor = 1;
                    if (innerText.Contains("M")) {
                        innerText = innerText.TrimEnd('M');
                        mfactor = 1_000_000;
                    }
                    else if (innerText.Contains("K")) {
                        innerText = innerText.TrimEnd('K');
                        mfactor = 1_000;
                    }

                    numberOfChapters = long.Parse(innerText) * mfactor;
                    continue;
                }

                if (innerText.Contains("Votes")) {
                    innerText = innerText.Split(' ')[1].Replace(",", "").Replace(".", "").Trim();
                    var mfactor = 1;
                    if (innerText.Contains("M")) {
                        innerText = innerText.TrimEnd('M');
                        mfactor = 1_000_000;
                    }
                    else if (innerText.Contains("K")) {
                        innerText = innerText.TrimEnd('K');
                        mfactor = 1_000;
                    }

                    metadata.StarCount = long.Parse(innerText) * mfactor;
                    continue;
                }

                if (innerText.Contains("Reads")) {
                    innerText = innerText.Split(' ')[1].Replace(",", "").Replace(".", "").Trim();
                    var mfactor = 1;
                    if (innerText.Contains("M")) {
                        innerText = innerText.TrimEnd('M');
                        mfactor = 1_000_000;
                    }
                    else if (innerText.Contains("K")) {
                        innerText = innerText.TrimEnd('K');
                        mfactor = 1_000;
                    }

                    metadata.ViewCount = long.Parse(innerText) * mfactor;
                }
            }

        }

        {
            List<StoryChapter> chapters = new((int)numberOfChapters);

            // div.story-parts>ul>li>a | Chapter a tag.

            var linkToChapter = await page.QuerySelectorAllAsync("div.story-parts>ul>li>a");

            if (linkToChapter == null || linkToChapter.Length == 0) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            var chapterNumber = 1; // User facing value; beautify. 
            foreach (var element in linkToChapter) {
                var chapterUrl = await (await element.GetPropertyAsync("href")).JsonValueAsync<string>();
                var chapterName = await (await (await element.QuerySelectorAsync("div.left-container>div.part__label>div.part-title")).GetPropertyAsync("innerText"))
                    .JsonValueAsync<string>();
                var wasChapterRecientlyUpdated = await element.QuerySelectorAsync("div.left-container>div.part__label>div.icon-container>span") != null;
                var releaseDate = await element.QuerySelectorAsync("div.right-label"); // The QuerySelector calls on an element are relative to it.

                if (releaseDate == null) {
                    AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                    chapterNumber++;
                    continue;
                }

                var date = (await (await releaseDate.GetPropertyAsync("innerText")).JsonValueAsync<string>());
                var fixedDate = ""; // Small workaround to non-standard date names.

                {
                    date = date.Replace("Locked", "");

                    if (date.Contains(" hours ago")) {
                        // Fix string.
                        var hour = date.Split(' ')[0];
                        date = DateTimeOffset.Now.AddHours(-int.Parse(hour)).ToString();
                    }

                    fixedDate = date;
                }

                var chapter = new StoryChapter {
                    ChapterLink = new Uri($"{chapterUrl}"),
                    ChapterNumber = chapterNumber,
                    ChapterName = chapterName,
                    ReleaseDateAsString = date, // This is required becuase of "Paid" stories having this bullshit, lmao.
                    ReleaseDate = null,
                    ChapterPageCount = -1, // TBI.
                    IsNewChapter = wasChapterRecientlyUpdated,
                };

                if (DateTimeOffset.TryParse(fixedDate, out var releaseDateAsDateOffset))
                    chapter.ReleaseDate = releaseDateAsDateOffset; // This is optional, and therefore not enforced.

                chapterNumber++;
                chapters.Add(chapter);
            }

            metadata.Chapters = chapters;
        }

        { // Initial release date.
            // div.story-badges>span.sr-only
            var storyProgressStatus = await page.QuerySelectorAsync("div.story-badges>div.completed>div.tag-item");

            if (storyProgressStatus == null) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Scraper error![/]");
                return null;
            }

            var storyStatus = await (await storyProgressStatus.GetPropertyAsync("innerText")).JsonValueAsync<string>();
            metadata.IsOnProgress = storyStatus == "Ongoing"; // Story is on going, aka on progress.

            var publishDateElement = (await page.QuerySelectorAsync("div.story-badges>div#publish-date>strong"));
            var publishDate = await (await publishDateElement.GetPropertyAsync("innerHTML")).JsonValueAsync<string>();
            metadata.FirstPublishedAt = DateTimeOffset.Parse(publishDate);
        }

        return metadata;
    }
}