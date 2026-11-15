using System.Text;
using System.Text.RegularExpressions;

namespace HongxueAssistant.Core.Services;

/// <summary>Reads the single-player difficulty label shared by the opening and shop screens.</summary>
public sealed class RegulationLevelReader(IOcrEngine ocr)
{
    public async Task<int?> ReadAsync(byte[] screenshot, int width, int height)
    {
        var texts = await ocr.RecognizeRegionAsync(screenshot,
            (int)(width * .07), 0, (int)(width * .25), (int)(height * .12));
        var rows = new List<List<OcrTextResult>>();
        foreach (var text in texts.OrderBy(t => t.Box.Y + t.Box.H / 2.0))
        {
            var row = rows.LastOrDefault();
            if (row is not null && Math.Abs(text.Box.Y + text.Box.H / 2.0
                    - row[0].Box.Y - row[0].Box.H / 2.0) < Math.Max(text.Box.H, row[0].Box.H) * .6)
                row.Add(text);
            else
                rows.Add([text]);
        }
        foreach (var row in rows)
        {
            var line = string.Concat(row.OrderBy(t => t.Box.X).Select(t => t.Text)).Normalize(NormalizationForm.FormKC);
            var match = Regex.Match(Regex.Replace(line, @"\s+", ""), @"规程等级([0-9]{1,2})(?![0-9])");
            if (!match.Success) continue;
            var level = int.Parse(match.Groups[1].Value);
            if (level is >= 1 and <= 20) return level;
        }
        return null;
    }
}
