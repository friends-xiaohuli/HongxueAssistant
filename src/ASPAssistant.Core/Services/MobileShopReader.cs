// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Reads the shop's bottom name strip, independently of card colour and slot count.
/// Uses the selected game's name band and colour preprocessing.
/// Gold light on an operator is not evidence of an elite upgrade.
/// </summary>
internal sealed class MobileShopReader(IOcrEngine engine, IReadOnlyList<string> catalogNames)
{
    // Source-image coordinates use a 1080px reference height and scale uniformly.
    // Horizontal card/control offsets are measured from the image centre.
    private const double ReferenceHeight = 1080;
    private const int MaximumShopSlots = 6;
    private const int CloseControlOffsetFromCenter = 770;
    private const int CloseControlWidth = 120;

    // Vertical positions in the prepared 2x OCR canvas, at the reference height.
    private const int EnhancedEvenRowTop = 60;
    private const int EnhancedOddRowTop = 200;
    private const int EnhancedRowSplit = 170;
    private const int OriginalTextRowThreshold = 300;

    private sealed record ShopLayout(
        int CardPitch,
        int FirstSlotOffsetFromCenter,
        int NameStripTop,
        int NameStripHeight,
        int NameLineTop,
        int NameLineHeight,
        int FallbackTextInset,
        int CloseControlTop,
        int CloseControlHeight);

    // Preserve the upstream PC measurements, including its close-control crop.
    private static readonly ShopLayout PcTerminalLayout = new(
        CardPitch: 220,
        FirstSlotOffsetFromCenter: -600,
        NameStripTop: 1014,
        NameStripHeight: 34,
        NameLineTop: 1018,
        NameLineHeight: 26,
        FallbackTextInset: 32,
        CloseControlTop: 1006,
        CloseControlHeight: 42);

    private static readonly ShopLayout PcOtherThemeLayout = PcTerminalLayout with
    {
        NameStripTop = 990,
        NameStripHeight = 50,
        NameLineTop = 1006,
        NameLineHeight = 30,
        CloseControlTop = 980,
        CloseControlHeight = 35,
    };

    // Calibrated from this MuMu 12 terminal-theme sample at 1920x1080, 16:9,
    // with the game's "异形屏UI适配" set to 0. Different UI margins need calibration.
    private static readonly ShopLayout MuMuTerminalLayout = PcTerminalLayout with
    {
        CardPitch = 242,
        FirstSlotOffsetFromCenter = -772,
        NameLineHeight = 30,
        FallbackTextInset = 48,
        CloseControlTop = 985,
        CloseControlHeight = 65,
    };

    // Retains the existing other-theme values. MuMu validation covers Terminal.
    private static readonly ShopLayout MuMuOtherThemeLayout = PcOtherThemeLayout with
    {
        CardPitch = MuMuTerminalLayout.CardPitch,
        FirstSlotOffsetFromCenter = MuMuTerminalLayout.FirstSlotOffsetFromCenter,
        FallbackTextInset = MuMuTerminalLayout.FallbackTextInset,
    };

    private static ShopLayout GetLayout(GameShopTheme theme, bool useMobileLayout)
    {
        if (theme == GameShopTheme.Terminal)
            return useMobileLayout ? MuMuTerminalLayout : PcTerminalLayout;
        return useMobileLayout ? MuMuOtherThemeLayout : PcOtherThemeLayout;
    }

    private readonly string[] _names = catalogNames.OrderByDescending(n => n.Length).ToArray();
    private volatile GameShopTheme _theme = GameShopTheme.Terminal;
    public GameShopTheme Theme { get => _theme; set => _theme = value; }
    /// <summary>Selects the sample-calibrated MuMu layout used by ADB mode.</summary>
    public bool UseMobileLayout { get; set; }

    public async Task<IReadOnlyList<ShopItem>> ReadAsync(
        byte[] screenshot, int width, int height, IReadOnlyList<string> trackedNames)
    {
        if (trackedNames.Count == 0)
            return [];
        var theme = Theme;
        var layout = GetLayout(theme, UseMobileLayout);

        using var stream = new MemoryStream(screenshot);
        using var source = new Bitmap(stream);
        // Small captures lose thin strokes in the frozen white labels. Normalize
        // their working height before cropping; output coordinates remain ratios.
        using var enlarged = height < ReferenceHeight
            ? new Bitmap((int)Math.Round(width * ReferenceHeight / height), (int)ReferenceHeight) : null;
        if (enlarged != null)
        {
            using var graphics = Graphics.FromImage(enlarged);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, enlarged.Width, enlarged.Height);
            width = enlarged.Width;
            height = enlarged.Height;
        }
        var workingImage = enlarged ?? source;
        // Isolate and enlarge small, dim text. Including the snowflakes below this
        // strip makes the detector merge or lose frozen card names.
        int top = (int)Math.Round(height * layout.NameStripTop / ReferenceHeight);
        int stripHeight = (int)Math.Round(height * layout.NameStripHeight / ReferenceHeight);
        int canvasTop = height * 3 / 4;
        int nameTop = (int)Math.Round(height * layout.NameLineTop / ReferenceHeight);
        int nameHeight = (int)Math.Round(height * layout.NameLineHeight / ReferenceHeight);
        // Keep vertical padding: a very wide, 50px-high input makes PaddleOCR's
        // detector resize away the frozen text strokes.
        using var strip = new Bitmap(width * 2, (height - canvasTop) * 2, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Black);
            graphics.DrawImage(workingImage, new Rectangle(0, (top - canvasTop) * 2, strip.Width, stripHeight * 2),
                0, top, width, stripHeight, GraphicsUnit.Pixel);
        }

        if (theme != GameShopTheme.Closure)
        using (var originalStrip = (Bitmap)strip.Clone())
        using (var graphics = Graphics.FromImage(strip))
        {
            // Terminal/Ursus white names become dim under the blue freezing overlay.
            // Red-channel contrast suppresses that blue background. Keep the original
            // band as well: the same enhancement can erase Closure's dark letters.
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix([
                [5, 5, 5, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 1, 0],
                [theme == GameShopTheme.Terminal ? -1.3f : -1.72f,
                 theme == GameShopTheme.Terminal ? -1.3f : -1.72f,
                 theme == GameShopTheme.Terminal ? -1.3f : -1.72f, 0, 1]]));
            // The selected profile defines the pitch and first slot of the shop grid.
            // Alternate rows prevent the detector joining neighbouring frozen labels.
            // Empty/sold slots are harmless; the original band remains layout-independent.
            for (int slot = 0; slot < MaximumShopSlots; slot++)
            {
                int left = (int)Math.Round(width / 2.0
                    + (slot * layout.CardPitch + layout.FirstSlotOffsetFromCenter) * height / ReferenceHeight);
                int bandTop = (int)Math.Round(height
                    * (slot % 2 == 0 ? EnhancedEvenRowTop : EnhancedOddRowTop) / ReferenceHeight);
                int slotWidth = (int)Math.Round(height * layout.CardPitch / ReferenceHeight);
                graphics.DrawImage(originalStrip, new Rectangle(left * 2, bandTop, slotWidth * 2, nameHeight * 2),
                    left * 2, (nameTop - canvasTop) * 2, slotWidth * 2, nameHeight * 2, GraphicsUnit.Pixel, attributes);
            }
            // Isolate the shop control too: on wide captures it can merge with
            // the adjacent card and lose "收起", incorrectly hiding all markers.
            int controlLeft = (int)Math.Round(width / 2.0 + height * CloseControlOffsetFromCenter / ReferenceHeight);
            int controlWidth = (int)Math.Round(height * CloseControlWidth / ReferenceHeight);
            int controlTop = (int)Math.Round(height * layout.CloseControlTop / ReferenceHeight);
            int controlHeight = (int)Math.Round(height * layout.CloseControlHeight / ReferenceHeight);
            graphics.DrawImage(workingImage,
                new Rectangle(controlLeft * 2, (int)Math.Round(height * EnhancedEvenRowTop / ReferenceHeight), controlWidth * 2, controlHeight * 2),
                controlLeft, controlTop,
                controlWidth, controlHeight, GraphicsUnit.Pixel);
        }
        using var encoded = new MemoryStream();
        strip.Save(encoded, ImageFormat.Png);
        var ocrInput = encoded.ToArray();
        var texts = await engine.RecognizeRegionAsync(ocrInput, 0, 0, strip.Width, strip.Height);
        if (!texts.Any(t => t.Text.Contains("收起", StringComparison.Ordinal) && t.Box.X + t.Box.W > width * 1.6))
            return [];
        var originalTexts = texts.Where(t => t.Box.Y + t.Box.H / 2.0 >= height * OriginalTextRowThreshold / ReferenceHeight
            || t.Text.Contains("收起", StringComparison.Ordinal)).ToList();
        var items = MatchNames(originalTexts, width, height, canvasTop, _names).ToList();
        var enhancedTexts = texts.Where(t => t.Box.Y + t.Box.H / 2.0 < height * OriginalTextRowThreshold / ReferenceHeight)
            .Select(t => new OcrTextResult(t.Text, (t.Box.X,
                t.Box.Y + (nameTop - canvasTop) * 2
                    - (int)Math.Round(height * (t.Box.Y + t.Box.H / 2.0 < height * EnhancedRowSplit / ReferenceHeight
                        ? EnhancedEvenRowTop : EnhancedOddRowTop) / ReferenceHeight),
                t.Box.W, t.Box.H))).ToList();
        foreach (var item in MatchNames(enhancedTexts, width, height, canvasTop, _names))
        {
            if (items.Any(i => i.Name == item.Name && Math.Abs(
                i.OcrRegion.X + i.OcrRegion.Width / 2 - item.OcrRegion.X - item.OcrRegion.Width / 2)
                < height * 110.0 / (ReferenceHeight * width)))
                continue;
            items.Add(item);
        }
        if (theme == GameShopTheme.Terminal)
        {
            // Freezing can hide the entire word from the detector. Precisely read
            // the missing card's name line instead of guessing from a partial name.
            for (int slot = 0; slot < MaximumShopSlots; slot++)
            {
                int left = (int)Math.Round(width / 2.0
                    + (slot * layout.CardPitch + layout.FirstSlotOffsetFromCenter) * height / ReferenceHeight);
                int slotWidth = (int)Math.Round(height * layout.CardPitch / ReferenceHeight);
                if (items.Any(i => (i.OcrRegion.X + i.OcrRegion.Width / 2) * width >= left
                    && (i.OcrRegion.X + i.OcrRegion.Width / 2) * width < left + slotWidth))
                    continue;
                int inset = (int)Math.Round(height * layout.FallbackTextInset / ReferenceHeight);
                // The wide crop retains long names; the narrow crop removes
                // snowflake texture to the right of short, dim names.
                foreach (int lineWidthPx in new[] { 180, 110 })
                {
                    int lineWidth = (int)Math.Round(height * lineWidthPx / ReferenceHeight);
                    var results = await engine.RecognizeLineAsync(ocrInput, (left + inset) * 2,
                        (nameTop - canvasTop) * 2, lineWidth * 2, nameHeight * 2);
                    // Observed on the 720p frozen sample; correct whole names,
                    // not individual glyphs or approximate catalogue neighbours.
                    var lines = results.Select(t => new OcrTextResult(
                        t.Text.Replace("斯卡菜", "斯卡蒂").Replace("复泰", "复奏"),
                        ((left + inset) * 2, (nameTop - canvasTop) * 2, lineWidth * 2, nameHeight * 2))).ToList();
                    var matches = MatchNames(lines, width, height, canvasTop, _names);
                    if (matches.Count == 0) continue;
                    items.AddRange(matches);
                    break;
                }
            }
        }
        var occurrences = new Dictionary<string, int>();
        items.Sort((a, b) => a.OcrRegion.X.CompareTo(b.OcrRegion.X));
        foreach (var item in items)
        {
            occurrences.TryGetValue(item.Name, out int occurrence);
            item.Id = $"{item.Name}@{occurrence}";
            occurrences[item.Name] = occurrence + 1;
        }
        return items.Where(i => trackedNames.Contains(i.Name)).ToList();
    }

    public IReadOnlyList<ShopItem> Match(IReadOnlyList<OcrTextResult> texts,
        int width, int height, int stripTop, IReadOnlyList<string> trackedNames)
    {
        // Do not leave markers on menus/closed shops with unrelated bottom text.
        if (!texts.Any(t => t.Text.Contains("收起", StringComparison.Ordinal) && t.Box.X + t.Box.W > width * 1.6))
            return [];
        return MatchNames(texts, width, height, stripTop, trackedNames);
    }

    private IReadOnlyList<ShopItem> MatchNames(IReadOnlyList<OcrTextResult> texts,
        int width, int height, int stripTop, IReadOnlyList<string> trackedNames)
    {
        var items = new List<ShopItem>();
        var occurrences = new Dictionary<string, int>();
        foreach (var text in texts.OrderBy(t => t.Box.X))
        {
            var value = text.Text.Replace(" ", "").Replace("傑", "杰").Replace("奧", "奥")
                .Replace("卫成推荐信", "卫戍推荐信").Replace("野繫", "野鬃")
                .Replace("野紫", "野鬃").Replace("云边", "云迹");
            // Resolve against the whole catalogue before filtering tracking. Otherwise
            // tracking 维式重锤 would also match an untracked 终极维式重锤.
            var matches = new List<(string Name, int Start, int Length)>();
            foreach (var name in _names)
            {
                var candidate = name.Replace(" ", "");
                int start = value.IndexOf(candidate, StringComparison.Ordinal);
                while (start >= 0)
                {
                    if ((candidate.Length > 1 || value.Trim('M', 'X', '入', 'λ', '×') == candidate)
                        && !matches.Any(m => start < m.Start + m.Length && start + candidate.Length > m.Start))
                        matches.Add((name, start, candidate.Length));
                    start = value.IndexOf(candidate, start + candidate.Length, StringComparison.Ordinal);
                }
            }

            // Wide images can merge neighbouring labels (or 鸿雪 + 收起). Split
            // their OCR bounds by character span instead of discarding a slot.
            foreach (var match in matches.OrderBy(m => m.Start))
            {
                if (!trackedNames.Contains(match.Name))
                    continue;
                // The shopkeeper panel can resemble a one-character operator
                // (e.g. "山"); only accept names within the merchandise area.
                if (text.Box.X + text.Box.W * (match.Start + match.Length / 2.0) / value.Length
                    > width + height * 1440 / ReferenceHeight)
                    continue;
                occurrences.TryGetValue(match.Name, out int occurrence);
                occurrences[match.Name] = occurrence + 1;
                items.Add(new ShopItem
                {
                    Id = $"{match.Name}@{occurrence}",
                    Name = match.Name,
                    OcrRegion = ((text.Box.X + text.Box.W * match.Start / (double)value.Length) / (2 * width),
                        (stripTop + text.Box.Y / 2.0) / height,
                        text.Box.W * match.Length / (double)value.Length / (2 * width), text.Box.H / (2.0 * height)),
                });
            }
        }
        return items;
    }
}
