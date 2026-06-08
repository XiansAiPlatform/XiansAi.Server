using System.Text;
using Markdig;
using Markdig.Syntax;        // Provides 'Block', 'ParagraphBlock', etc.
using Markdig.Syntax.Inlines;

namespace Features.AppsApi.Converters;

public static class MarkdigSlackConverter
{
    public static string ToSlack(string? markdown)
    {
        if(string.IsNullOrEmpty(markdown)) return string.Empty;
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = Markdown.Parse(markdown, pipeline);
        var sb = new StringBuilder();

        foreach (var block in document)
        {
            ProcessBlock(block, sb);
        }

        return sb.ToString().Trim();
    }

    private static void ProcessBlock(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                ProcessInlines(paragraph.Inline, sb);
                sb.AppendLine("\n"); // Slack needs double newlines for paragraphs
                break;
            case HeadingBlock heading:
                sb.Append("*"); // Convert headers to Bold
                ProcessInlines(heading.Inline, sb);
                sb.AppendLine("*\n");
                break;
            case ListBlock listBlock:
                foreach (var item in listBlock)
                {
                    sb.Append("• "); // Simple bullet
                    ProcessBlock((ListItemBlock)item, sb);
                }
                break;
            case ListItemBlock listItem:
                foreach (var subBlock in listItem) ProcessBlock(subBlock, sb);
                break;
            // Add more blocks (CodeBlock, QuoteBlock) as needed
        }
    }

    private static void ProcessInlines(ContainerInline? inlines, StringBuilder sb)
    {
        if (inlines == null) return;

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content);
                    break;
                case EmphasisInline emphasis:
                    var symbol = emphasis.DelimiterCount == 2 ? "*" : "_"; // ** -> * (bold), * -> _ (italic)
                    sb.Append(symbol);
                    ProcessInlines(emphasis, sb);
                    sb.Append(symbol);
                    break;
                case LinkInline link:
                    sb.Append($"<{link.Url}|");
                    ProcessInlines(link, sb);
                    sb.Append(">");
                    break;
                case LineBreakInline:
                    sb.AppendLine();
                    break;
            }
        }
    }
}