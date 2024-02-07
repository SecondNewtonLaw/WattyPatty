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
using System.Text;
using PuppeteerSharp;
using Spectre.Console;

namespace WattyPatty;

public class ChapterScraper : IScraper<StoryMetadata> {
    private StoryMetadata m_storyMetadata;
    private HttpClient m_httpClient;
    /// <summary>
    ///     Construct a Chapter scraper for the given story metadata. Will mutate the given story metadata.
    /// </summary>
    /// <param name="metadata"></param>
    public ChapterScraper(in StoryMetadata metadata, in HttpClient client) {
        m_storyMetadata = metadata;
        m_httpClient = client;
    }
    public async Task<StoryMetadata?> ScrapeAsync(IPage page) {
        var storyMetadata = m_storyMetadata;
        var oldLink = page.Url;
        var chapterCount = storyMetadata.Chapters.Count();
        for (var i = 0; i < chapterCount; i++) {
            var j = i;
            var redirectTo = storyMetadata.Chapters.ElementAt(j).ChapterLink.AbsoluteUri;
            AnsiConsole.MarkupLine($" [green][[-]][/] Extended Chapter Data Scrape [[[yellow]{i + 1}[/] of [green]{chapterCount + 1}[/]]]");
            AnsiConsole.MarkupLine($" [green][[-]][/] Changing Location from {page.Url} to {redirectTo}");
            await page.GoToAsync(redirectTo);
            await CompleteChapterInformation(page, storyMetadata, storyMetadata.Chapters.ElementAt(j)); // Classes are always ref types.
        }

        var navPromise = page.WaitForNavigationAsync();
        await page.GoToAsync(oldLink);
        await navPromise;

        return storyMetadata;
    }

    public async Task CompleteChapterInformation(IPage page, StoryMetadata storyMetadata, StoryChapter chapter) {
        // Process the views, votes and comments on the start (for ordering)

        // Reads QSelector div.story-stats>span.reads
        // Votes QSelector div.story-stats>span.votes
        // Comments QSelector div.story-stats>span.comments>a

        // Get chapter identifier 
        {
            // Cleaner version, couldn't get it working as attempting to access the data-part-id was a failure on many attempts.
            //var storyPartsContainer = await page.QuerySelectorAsync("article[data-part-id]");
            //var s = await (await storyPartsContainer.GetPropertyAsync("data-part-id")).JsonValueAsync();

            chapter.ChapterIdentifier = long.Parse(page.Url.Split('/')[3].Split('-')[0]);
        }

        #region Story Stats Scrape

        {
            var readsElement = await page.QuerySelectorAsync("div.story-stats>span.reads");
            if (readsElement == null)
                throw new Exception("Invalid reads scrape");

            var txt = await (await readsElement.GetPropertyAsync("innerText")).JsonValueAsync<string>();
            txt = txt.Replace(",", "").Replace(".", "").Trim();
            var mfactor = 1;
            if (txt.Contains("M")) {
                txt = txt.TrimEnd('M');
                mfactor = 1_000_000;
            }
            else if (txt.Contains("K")) {
                txt = txt.TrimEnd('K');
                mfactor = 1_000;
            }

            chapter.ReadCount = long.Parse(txt) * mfactor;

        }

        {
            var votesElements = await page.QuerySelectorAsync("div.story-stats>span.votes");
            if (votesElements == null)
                throw new Exception("Invalid votes scrape");


            var txt = await (await votesElements.GetPropertyAsync("innerText")).JsonValueAsync<string>();
            txt = txt.Replace(",", "").Replace(".", "").Trim();
            var mfactor = 1;
            if (txt.Contains("M")) {
                txt = txt.TrimEnd('M');
                mfactor = 1_000_000;
            }
            else if (txt.Contains("K")) {
                txt = txt.TrimEnd('K');
                mfactor = 1_000;
            }

            chapter.VoteCount = long.Parse(txt) * mfactor;
        }

        {
            var commentsElement = await page.QuerySelectorAsync("div.story-stats>span.comments>a");
            if (commentsElement == null)
                throw new Exception("Invalid reads comments");

            var txt = await (await commentsElement.GetPropertyAsync("innerText")).JsonValueAsync<string>();
            txt = txt.Replace(",", "").Replace(".", "").Trim();
            var mfactor = 1;
            if (txt.Contains("M")) {
                txt = txt.TrimEnd('M');
                mfactor = 1_000_000;
            }
            else if (txt.Contains("K")) {
                txt = txt.TrimEnd('K');
                mfactor = 1_000;
            }

            chapter.CommentCount = long.Parse(txt) * mfactor;
        }

        #endregion Story Stats Scrape

        // pre>p | Story text chunks


        // Whether or not the story is nsfw.

        // https://www.wattpad.com/v5/stories/{STORY_ID}/classification/safety
        chapter.IsNsfw = !(await (await m_httpClient.GetAsync($"https://www.wattpad.com/v5/stories/{storyMetadata.StoryIdentifier}/classification/safety")).Content
            .ReadAsStringAsync()).Contains("1", StringComparison.InvariantCultureIgnoreCase); // Safe for 1 == brand safe | Safe for 0 == NOT brand safe.

        // We can use the undocumented api endpoint -> https://www.wattpad.com/apiv2/?m=storytext&id={CHAPTER_ID}&page={PAGE_NUM}

        // Story text, the JUICY part.
        StringBuilder sb = new();

        var previousLength = -1;
        var currentPage = 0L;

        while (sb.Length != previousLength) {
            var response = await m_httpClient.GetAsync($"https://www.wattpad.com/apiv2/?m=storytext&id={chapter.ChapterIdentifier}&page={currentPage}");

            var s = await response.Content.ReadAsStringAsync();
            previousLength = sb.Length;
            sb.Append(s);
            currentPage++;
        }

        chapter.ChapterPageCount = currentPage;
        chapter.StoryText = sb.ToString();
    }
}