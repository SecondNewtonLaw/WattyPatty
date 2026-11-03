/*
 *  WattyPatty - Story metadata extractor for Wattpad - HtmlToMarkdown.cs
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

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WattyPatty;

/// <summary>
///     Converts the HTML fragment returned by Wattpad's <c>storytext</c> endpoint into Markdown.
///     Wattpad mostly emits <c>&lt;p&gt;</c> and <c>&lt;br&gt;</c>, but the older editors also allow inline
///     emphasis, links and images, so those are handled too. Unknown tags are dropped and their text kept.
/// </summary>
public static class HtmlToMarkdown {
    /// <summary>Non-printing marker for an explicit line break (&lt;br&gt;) inside a paragraph.</summary>
    private const char HardBreakMarker = '\u0001';

    private static readonly Regex TagRegex = new(
        @"<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>",
        RegexOptions.Compiled);

    private static readonly Regex AttributeRegex = new(
        @"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))",
        RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex BreakPaddingRegex = new(@"[ \t]*" + HardBreakMarker + @"[ \t]*", RegexOptions.Compiled);
    private static readonly Regex RepeatedSpaceRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public static string Convert(string? html) {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var current = new StringBuilder(html.Length);
        var blocks = new List<(string Text, int QuoteDepth)>();
        var openLinks = new Stack<string?>();
        var quoteDepth = 0;
        var currentQuoteDepth = 0;
        var position = 0;

        // Finish the block being built, remembering the quote depth it belongs to.
        void BlockBreak() {
            if (current.Length > 0) {
                var text = current.ToString().Trim();
                if (text.Length > 0)
                    blocks.Add((text, currentQuoteDepth));

                current.Clear();
            }

            currentQuoteDepth = quoteDepth;
        }

        foreach (Match tag in TagRegex.Matches(html)) {
            AppendText(current, html[position..tag.Index]);
            position = tag.Index + tag.Length;

            var isClosing = tag.Groups[1].Value.Length > 0;
            var name = tag.Groups[2].Value.ToLowerInvariant();
            var attributes = tag.Groups[3].Value;

            switch (name) {
                // Block level elements.
                case "p" or "div" or "section" or "article" or "ul" or "ol" or "table" or "tr":
                    BlockBreak();
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    BlockBreak();
                    if (!isClosing)
                        current.Append('#', name[1] - '0').Append(' ');
                    break;
                case "li":
                    BlockBreak();
                    if (!isClosing)
                        current.Append("- ");
                    break;
                case "br":
                    current.Append(HardBreakMarker);
                    break;
                case "hr":
                    BlockBreak();
                    current.Append("---");
                    BlockBreak();
                    break;
                case "blockquote":
                    BlockBreak();
                    if (isClosing)
                        quoteDepth = Math.Max(0, quoteDepth - 1);
                    else
                        quoteDepth++;

                    currentQuoteDepth = quoteDepth;
                    break;

                // Inline elements.
                case "b" or "strong":
                    current.Append("**");
                    break;
                case "i" or "em":
                    current.Append('*');
                    break;
                case "s" or "strike" or "del":
                    current.Append("~~");
                    break;
                case "code":
                    current.Append('`');
                    break;
                case "u":
                    // Markdown has no underline; keeping the tag works in every CommonMark renderer.
                    current.Append(isClosing ? "</u>" : "<u>");
                    break;
                case "a":
                    if (isClosing) {
                        if (openLinks.Count == 0)
                            break;

                        var href = openLinks.Pop();
                        if (href is not null)
                            current.Append("](").Append(FormatUrl(href)).Append(')');
                        break;
                    }

                    var linkTarget = ExtractAttribute(attributes, "href");
                    openLinks.Push(linkTarget);
                    if (linkTarget is not null)
                        current.Append('[');
                    break;
                case "img":
                    if (isClosing)
                        break;

                    var source = ExtractAttribute(attributes, "src");
                    if (source is not null) {
                        var alt = ExtractAttribute(attributes, "alt") ?? string.Empty;
                        current.Append("![").Append(EscapeText(alt)).Append("](").Append(FormatUrl(source)).Append(')');
                    }

                    break;
            }
        }

        AppendText(current, html[position..]);
        BlockBreak();
        return Finalize(blocks);
    }

    private static void AppendText(StringBuilder builder, string rawText) {
        if (rawText.Length == 0)
            return;

        var decoded = WebUtility.HtmlDecode(rawText);

        // HTML collapses whitespace; our own structural newlines are emitted outside of text nodes.
        decoded = WhitespaceRegex.Replace(decoded, " ");
        builder.Append(EscapeText(decoded));
    }

    /// <summary>Escape characters that would otherwise be interpreted as Markdown syntax.</summary>
    private static string EscapeText(string text) {
        var builder = new StringBuilder(text.Length + 8);

        foreach (var character in text) {
            switch (character) {
                case '\\' or '`' or '*' or '_' or '[' or ']' or '<':
                    builder.Append('\\').Append(character);
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string FormatUrl(string url) {
        url = url.Trim();
        return url.Any(character => character is ' ' or '(' or ')') ? $"<{url}>" : url;
    }

    private static string? ExtractAttribute(string attributes, string attributeName) {
        foreach (Match attribute in AttributeRegex.Matches(attributes)) {
            if (!attribute.Groups[1].Value.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                continue;

            return WebUtility.HtmlDecode(attribute.Groups["value"].Value);
        }

        return null;
    }

    private static string Finalize(List<(string Text, int QuoteDepth)> blocks) {
        var rendered = new List<string>(blocks.Count);

        foreach (var (text, quoteDepth) in blocks) {
            var block = BreakPaddingRegex.Replace(text, HardBreakMarker.ToString());
            block = RepeatedSpaceRegex.Replace(block, " ");
            block = block.Replace(HardBreakMarker.ToString(), "  \n");

            // A <br> at the end of a paragraph carries no meaning in Markdown and would add an extra
            // blank line once blocks are joined.
            block = block.TrimEnd();
            if (block.Length == 0)
                continue;

            if (quoteDepth > 0) {
                var prefix = string.Concat(Enumerable.Repeat("> ", quoteDepth));
                block = prefix + block.Replace("\n", "\n" + prefix);
            }

            rendered.Add(block);
        }

        return rendered.Count == 0 ? string.Empty : string.Join("\n\n", rendered) + "\n";
    }
}
