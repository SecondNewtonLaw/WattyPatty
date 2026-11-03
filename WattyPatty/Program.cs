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
using Newtonsoft.Json;
using Spectre.Console;

namespace WattyPatty;

public class MainActivity {
    private const string MetadataDirectory = "output";
    private const string MetadataFileName = "meta.json";

    private static async Task Main(string[] args) {
        AnsiConsole.MarkupLine("\t-----------------------------------------");
        AnsiConsole.MarkupLine("\t| [white]WattyPatty[/] - [orangered1]Wattpad[/] Story [green]Downloader[/] |");
        AnsiConsole.MarkupLine("\t-----------------------------------------");

        var storyUrl = args.Length > 0
            ? args[0]
            : AnsiConsole.Ask<string>(" [green][[+]][/] Please input the story you want to download:");

        if (!Uri.TryCreate(storyUrl, UriKind.Absolute, out var parsedUri) ||
            !parsedUri.Host.EndsWith("wattpad.com", StringComparison.OrdinalIgnoreCase)) {
            AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Invalid url![/]");
            return;
        }

        using var api = new WattpadApi {
            DiagnosticMessage = message => {
                lock (ConsoleSynchronization.Gate)
                    AnsiConsole.MarkupLine($" [orange3][[*]][/] [yellow]{Markup.Escape(message)}[/]");
            },
        };
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        var cancellationToken = cancellationTokenSource.Token;

        try {
            AnsiConsole.MarkupLine(" [green][[-]][/] Resolving story identifier...");

            var storyIdentifier = await api.ResolveStoryIdentifierAsync(parsedUri, cancellationToken);
            if (storyIdentifier is null) {
                AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Error! The given url does not belong to a Wattpad story.[/]");
                return;
            }

            AnsiConsole.MarkupLine($" [green][[-]][/] Scraping metadata for story [yellow]{storyIdentifier}[/]...");

            var metadataScraper = new MetadataScraper(storyIdentifier.Value);
            var storyMetadata = await metadataScraper.ScrapeAsync(api, cancellationToken);

            if (storyMetadata is null) {
                AnsiConsole.MarkupLine(
                    " [orange3][[*]][/] [red]Error! The scraper has failed, if Wattpad has changed their API, then this will no longer work...[/]");
                return;
            }

            PrintSummary(storyMetadata);

            AnsiConsole.MarkupLine(" [green][[-]][/] Downloading all chapters, this will take a while...");
            var watch = Stopwatch.StartNew();

            var chapterProcessor = new ChapterScraper(in storyMetadata);
            await chapterProcessor.ScrapeAsync(api, cancellationToken);
            watch.Stop();

            var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), MetadataDirectory);
            Directory.CreateDirectory(outputDirectory);

            AnsiConsole.MarkupLine(" [green][[-]][/] Writing metadata and Markdown...");

            var metadataPath = Path.Combine(outputDirectory, MetadataFileName);
            await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(storyMetadata, Formatting.Indented),
                cancellationToken);
            var markdownPath = await MarkdownExporter.WriteAsync(storyMetadata, outputDirectory, cancellationToken);

            AnsiConsole.MarkupLine(
                $" [green][[-]][/] The metadata has been written to disk as [yellow]{Markup.Escape(metadataPath)}[/].");
            AnsiConsole.MarkupLine(
                $" [green][[-]][/] The story has been written as Markdown to [yellow]{Markup.Escape(markdownPath)}[/].");
            AnsiConsole.MarkupLine($" [yellow][[-]][/] Extended story scrape completed in {watch.ElapsedMilliseconds}ms!");
        }
        catch (OperationCanceledException) {
            AnsiConsole.MarkupLine(" [orange3][[*]][/] [red]Cancelled by the user.[/]");
        }
        catch (Exception exception) {
            AnsiConsole.MarkupLine($" [orange3][[*]][/] [red]Unexpected error:[/] {Markup.Escape(exception.Message)}");
        }
    }

    private static void PrintSummary(StoryMetadata storyMetadata) {
        var table = new Table().Border(TableBorder.Rounded).HideHeaders();
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Title", Markup.Escape(storyMetadata.StoryName));
        table.AddRow("Author", Markup.Escape(storyMetadata.AuthorInformation.AuthorName));
        table.AddRow("Chapters", storyMetadata.Chapters.Count().ToString());
        table.AddRow("Reads", storyMetadata.ViewCount.ToString());
        table.AddRow("Votes", storyMetadata.StarCount.ToString());
        table.AddRow("Reading time", Markup.Escape(storyMetadata.ReadingTime));
        table.AddRow("Status", storyMetadata.IsOnProgress ? "Ongoing" : "Completed");
        table.AddRow("Paid", storyMetadata.IsPaid ? "Yes" : "No");
        AnsiConsole.Write(table);
    }
}
