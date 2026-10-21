using System.Text;

namespace WithWindows.Core.Chat;

/// <summary>把常见 HTML 折叠块转换为 Markdig 可渲染的原生容器。</summary>
public static class MarkdownPreprocessor
{
    public static string NormalizeDetails(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        bool inDetails = false;
        bool containerOpened = false;
        bool inFence = false;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            bool fenceLine = trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal);

            if (fenceLine)
            {
                inFence = !inFence;
                output.Add(line);
                continue;
            }

            if (inFence)
            {
                output.Add(line);
                continue;
            }

            if (!inDetails
                && trimmed.StartsWith("<details", StringComparison.OrdinalIgnoreCase))
            {
                inDetails = true;
                containerOpened = false;
                continue;
            }

            if (!inDetails)
            {
                output.Add(line);
                continue;
            }

            if (trimmed.StartsWith("</details", StringComparison.OrdinalIgnoreCase))
            {
                EnsureContainerOpened(output, ref containerOpened, "详情");
                output.Add(":::");
                inDetails = false;
                containerOpened = false;
                continue;
            }

            if (TryReadSummary(trimmed, out string? summary))
            {
                EnsureContainerOpened(output, ref containerOpened, summary ?? "详情");
                continue;
            }

            if (!containerOpened && trimmed.Length == 0)
                continue;

            EnsureContainerOpened(output, ref containerOpened, "详情");
            output.Add(line);
        }

        if (inDetails)
        {
            EnsureContainerOpened(output, ref containerOpened, "详情");
            output.Add(":::");
        }

        return string.Join('\n', output);
    }

    private static void EnsureContainerOpened(
        List<string> output,
        ref bool opened,
        string title)
    {
        if (opened)
            return;

        string safeTitle = string.IsNullOrWhiteSpace(title) ? "详情" : title.Trim();
        output.Add("::: details " + safeTitle);
        output.Add("");
        opened = true;
    }

    private static bool TryReadSummary(string line, out string? summary)
    {
        summary = null;
        if (!line.StartsWith("<summary", StringComparison.OrdinalIgnoreCase))
            return false;

        int start = line.IndexOf('>');
        int end = line.LastIndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end <= start)
            return false;

        summary = line[(start + 1)..end].Trim();
        return true;
    }
}
