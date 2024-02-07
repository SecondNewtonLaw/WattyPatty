/*
 *  WattyPatty - Story metadata extractor for Wattpad - Program.cs
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
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Authentication;
using Newtonsoft.Json;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.AnonymizeUa;
using PuppeteerExtraSharp.Plugins.BlockResources;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using Spectre.Console;

namespace WattyPatty;

public class MainActivity {
    private static async Task ObtainPrerequisites() {
        Console.WriteLine("Obtaining Browser...");
        var fetcher = new BrowserFetcher();
        var browser = await fetcher.DownloadAsync();
        Console.WriteLine($"Obtained Browser: {browser.Browser} for {browser.Platform} | BID: {browser.BuildId}");
    }

    private static async Task Main(string[] args) {
        AnsiConsole.MarkupLine("\t-----------------------------------------");
        AnsiConsole.MarkupLine("\t| [white]WattyPatty[/] - [orangered1]Wattpad[/] Story [green]Downloader[/] |");
        AnsiConsole.MarkupLine("\t-----------------------------------------");

        AnsiConsole.MarkupLine(" [green][[-]][/] Obtaining Pre-Requisites...");
        await ObtainPrerequisites();

        AnsiConsole.MarkupLine(" [green][[-]][/] Obtained Pre-Requisites.");

        AnsiConsole.Markup(" [green][[+]][/] Please input the story you want to download:");

        var storyUrl = AnsiConsole.Ask<string>(" ");

        if (!Uri.TryCreate(storyUrl, UriKind.RelativeOrAbsolute, out var parsedUri) || !storyUrl.Contains("https://www.wattpad.com/")) {
            AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Invalid url![/]");
            return;
        }

        AnsiConsole.MarkupLine(" [green][[-]][/] Launching Browser...");

        var extraPuppeteer = new PuppeteerExtra();
        extraPuppeteer.Use(new StealthPlugin());
        extraPuppeteer.Use(new AnonymizeUaPlugin());
        // extraPuppeteer.Use(new BlockResourcesPlugin());
        var browser = await extraPuppeteer.LaunchAsync(new LaunchOptions {
            Headless = true, Args = [
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-infobars",
                "--no-zygote",
                "--no-first-run",
                "--ignore-certificate-errors",
                "--ignore-certificate-errors-skip-list",
                "--disable-dev-shm-usage",
                "--disable-accelerated-2d-canvas",
                "--disable-gpu",
                "--hide-scrollbars",
                "--disable-notifications",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-breakpad",
                "--disable-component-extensions-with-background-pages",
                "--disable-extensions",
                "--disable-features=TranslateUI,BlinkGenPropertyTrees",
                "--disable-ipc-flooding-protection",
                "--disable-renderer-backgrounding",
                "--enable-features=NetworkService,NetworkServiceInProcess",
                "--force-color-profile=srgb",
                "--metrics-recording-only",
                "--mute-audio",
            ]
        });

        AnsiConsole.MarkupLine(" [green][[-]][/] Launched browser on headless mode!");

        AnsiConsole.MarkupLine(" [green][[-]][/] Navigating to site and scraping Metadata...");

        await Task.Delay(500);

        var pages = await browser.PagesAsync();
        var selectedPage = (IPage)default;

        if (pages != null && pages.Length != 0)
            selectedPage = pages[0];
        else
            selectedPage = await browser.NewPageAsync();

        await selectedPage.SetRequestInterceptionAsync(true);
        selectedPage.Request += async (sender, eventArgs) => {
            if (eventArgs.Request.ResourceType is ResourceType.Font or ResourceType.StyleSheet or ResourceType.Image or ResourceType.ImageSet
                    or ResourceType.Img || eventArgs.Request.ResourceType == ResourceType.Script && !eventArgs.Request.Url.Contains("wattpad.com")) {
                await eventArgs.Request.AbortAsync();
            }
            else
                await eventArgs.Request.ContinueAsync();
        };
        AnsiConsole.MarkupLine($" [green][[-]][/] Changing Location from {selectedPage.Url} to {parsedUri.AbsoluteUri}");
        var navigationTask = selectedPage.WaitForNavigationAsync();
        await selectedPage.GoToAsync(parsedUri.AbsoluteUri);
        await navigationTask;

        AnsiConsole.MarkupLine(" [green][[-]][/] Scraping metadata...");

        var httpClient = new HttpClient(new HttpClientHandler() { SslProtocols = SslProtocols.Tls12, AutomaticDecompression = DecompressionMethods.All });

        var metadataScraper = new MetadataScraper();

        var storyMetadata = await metadataScraper.ScrapeAsync(selectedPage);

        if (storyMetadata == null) {
            AnsiConsole.MarkupLine(
                " [orange3][[*]][/] [red]Error! The scraper has failed, if Wattpad has updated their front-end, then this will no longer work...[/]");
            return;
        }

        AnsiConsole.MarkupLine(" [green][[-]][/] Metadata scrape completed.");

        AnsiConsole.MarkupLine(" [green][[-]][/] Completing all metadata and downloading chapters, this will take a while...");
        var watch = Stopwatch.StartNew();

        var chapterProcessor = new ChapterScraper(in storyMetadata, in httpClient);
        await chapterProcessor.ScrapeAsync(selectedPage);

        AnsiConsole.MarkupLine(" [green][[-]][/] Writing metadata...");

        await File.WriteAllTextAsync("meta.json", JsonConvert.SerializeObject(storyMetadata, Formatting.Indented));

        AnsiConsole.MarkupLine(" [green][[-]][/] The metadata has been written to disk as meta.json. You may reconstruct the story from this metadata.");

        await browser.CloseAsync();
        httpClient.Dispose();

        AnsiConsole.MarkupLine($" [yellow][[-]][/] Extended story scrape completed in {watch.ElapsedMilliseconds}ms!");
    }
}