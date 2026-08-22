using System.Text;

namespace NPVideoStudio.App.Services;

/// <summary>Reads plain-text lyrics and the simple ANSI RTF files produced by Windows WordPad/Word.</summary>
public static class LyricsDocumentReader
{
    public static async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!Path.GetExtension(path).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(bytes).Trim();
        }
        return ExtractRtf(Encoding.Latin1.GetString(bytes)).Trim();
    }

    public static string ExtractRtf(string rtf)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(1250);
        var output = new StringBuilder();
        var ignored = new Stack<bool>();
        var ignore = false;

        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];
            if (ch == '{')
            {
                ignored.Push(ignore);
                var rest = rtf.AsSpan(i + 1);
                ignore |= rest.StartsWith(@"\fonttbl") || rest.StartsWith(@"\colortbl") ||
                          rest.StartsWith(@"\stylesheet") || rest.StartsWith(@"\info") ||
                          rest.StartsWith(@"\*\generator") || rest.StartsWith(@"\pict");
                continue;
            }
            if (ch == '}')
            {
                ignore = ignored.Count > 0 && ignored.Pop();
                continue;
            }
            if (ch != '\\')
            {
                if (!ignore && ch is not '\r' and not '\n' and not '\0') output.Append(ch);
                continue;
            }

            if (i + 3 < rtf.Length && rtf[i + 1] == '\'' &&
                byte.TryParse(rtf.AsSpan(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                if (!ignore) output.Append(ansi.GetString(new[] { value }));
                i += 3;
                continue;
            }
            if (i + 1 < rtf.Length && rtf[i + 1] is '\\' or '{' or '}')
            {
                if (!ignore) output.Append(rtf[i + 1]);
                i++;
                continue;
            }

            var start = ++i;
            while (i < rtf.Length && char.IsLetter(rtf[i])) i++;
            var word = rtf[start..i];
            if (i < rtf.Length && (rtf[i] == '-' || char.IsDigit(rtf[i])))
            {
                var numberStart = i++;
                while (i < rtf.Length && char.IsDigit(rtf[i])) i++;
                if (word == "u" && int.TryParse(rtf[numberStart..i], out var code) && !ignore)
                {
                    output.Append((char)(code < 0 ? code + 65536 : code));
                    if (i < rtf.Length && rtf[i] != ' ') i++;
                }
            }
            if (!ignore && word is "par" or "line") output.AppendLine();
            else if (!ignore && word == "tab") output.Append('\t');
            if (i < rtf.Length && rtf[i] != ' ') i--;
        }

        return string.Join(Environment.NewLine,
            output.ToString().Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => line.Trim()).Where(line => line.Length > 0));
    }
}
