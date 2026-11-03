# WattyPatty

A Proof of Concept program to make an archival of Wattpad stories. It downloads a story and writes both a lossless metadata dump (`meta.json`) and a ready-to-read Markdown document (`.md`).

## How it works

Wattpad's website is a Remix application whose class names are generated hashes (`uG9U1`, `we["story-parts"]`, ...), so DOM scraping broke as soon as the redesign shipped. This tool now talks to the same undocumented HTTP endpoints the site itself uses:

| Purpose | Endpoint |
| --- | --- |
| Story metadata + every part/chapter (reads, votes, comments, dates, paywall flags) | `https://www.wattpad.com/api/v3/stories/{STORY_ID}?fields=...` |
| Chapter body (`storytext`) | `https://www.wattpad.com/apiv2/?m=storytext&id={PART_ID}&page={PAGE}` |
| Content safety classification | `https://www.wattpad.com/v5/stories/{STORY_ID}/classification/safety` |

Because the v3 story payload already contains the full chapter list, no headless browser is required — no Chromium download, no Puppeteer, no stealth plugins.

Known quirks of the endpoints, kept in the code:

- `isPaywalled`, `paidModel`, `readCount` and `mature` are only returned when they are listed in the `fields` query parameter.
- If the `Accept` header prefers `text/plain` over `application/json`, the legacy PHP backend answers with a `var_export()` dump (`array ( ... )`) instead of JSON, so only `application/json` is sent.
- `storytext` pages are 0-indexed and the API returns an empty body after the last page.
- Locked, deleted or paywalled parts answer with an HTTP client error; those chapters are kept but marked as not retrieved.

By default chapters are fetched up to 4 at a time. API rate limits are handled gracefully, but very large stories may still trigger them.

## Usage

```shell
dotnet run --project WattyPatty/WattyPatty.csproj -- "https://www.wattpad.com/story/408339322-misterio-en-el-hotel-garza"
```

The URL may also be a chapter/part URL; the owning story is resolved automatically. Without an argument the URL is prompted for.

Everything is written to `output/`:

- `meta.json` — the raw archive: story metadata plus every chapter, with `StoryText` still holding the chapter HTML verbatim.
- `{Story Name}.md` — YAML front matter (title, author, cover, source, statistics), a story header and one section per chapter.

## HTML → Markdown

`HtmlToMarkdown` converts the chapter fragments. Wattpad mostly emits `<p>` and `<br>`, but the older editors also allow inline emphasis, lists, quotes, links and images, so those are handled as well; unknown tags are dropped while their text is kept.

- `<p>`/`<div>`/heading/list/`<hr>`/`<blockquote>` become proper blocks; `<br>` becomes a Markdown hard break.
- `<b>/<strong>`, `<i>/<em>`, `<s>/<strike>/<del>`, `<code>`, `<a>` and `<img>` become their Markdown equivalents. `<u>` is kept as inline HTML because Markdown has no underline.
- Entities are decoded, whitespace collapsed, and literal Markdown characters (`*`, `_`, `` ` ``, `[`, `]`, `<`, `\`) are escaped so prose is never reinterpreted as formatting.
- Empty paragraphs and paragraph-only `<br>` spacers are dropped instead of producing stray blank lines.

Licensing:
- This project has been licensed under the GNU Affero General Public License 3.0 license. You should have received a copy of the GNU Affero General Public License
  along with this program.
