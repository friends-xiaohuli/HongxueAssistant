using System.Drawing;
using HongxueAssistant.Core.Models;

namespace HongxueAssistant.Core.Services;

/// <summary>Confirms a purchase from a stable named card, two empty frames and a funds decrease.</summary>
public sealed class ShopPurchaseDetector
{
    private List<ShopItem> _cards = [];
    private ShopEconomy? _before;
    private int _width, _height, _stable, _emptyFrames;
    private string _pending = "";
    private int? _pendingMoney;
    private bool _purchasePending;
    private int? _fundsBeforeCardChange;
    private int _fundsChangeGrace;
    private string? _lastCandidateDiagnostic;
    public bool NeedsConfirmation => _emptyFrames == 1;

    public bool NeedsFundsRead(IReadOnlyList<ShopItem> cards, bool shopOpen)
        => shopOpen && !_purchasePending && (_before is null || cards.Count != _cards.Count
            || cards.Any(c => !_cards.Any(p => SameSlot(c, p) && c.Name == p.Name)));

    public bool CanReuseHudLayout(IReadOnlyList<ShopItem> cards, int width, int height)
        => _before is not null && width == _width && height == _height && cards.Count < _cards.Count
            && cards.All(c => _cards.Any(p => SameSlot(c, p) && c.Name == p.Name));

    public void Reset(string reason)
    {
        if (_before is not null)
            AppLogger.Info("ShopPurchase", $"Baseline cleared: reason={reason}, stable={_stable}, money={_before.Money}, pending={_purchasePending}, cards=[{string.Join(", ", _cards.Select(c => c.Name))}]");
        _cards = [];
        _before = null;
        _stable = _emptyFrames = 0;
        _pending = "";
        _pendingMoney = null;
        _purchasePending = false;
        _fundsBeforeCardChange = null;
        _fundsChangeGrace = 0;
        _lastCandidateDiagnostic = null;
    }

    public IReadOnlyList<string> Observe(byte[] image, IReadOnlyList<ShopItem> cards, bool shopOpen,
        ShopEconomy? economy, IReadOnlySet<string> targets)
    {
        if (!shopOpen || economy is not { IsPreparation: true, Round: not null })
        {
            Reset(!shopOpen ? "shop-closed" : economy is null ? "hud-unavailable"
                : !economy.IsPreparation ? "not-preparation" : "round-unavailable");
            return [];
        }
        var (width, height) = ImageBytes.GetDimensions(image);
        bool sameScene = _before is not null && _before.Round == economy.Round && _before.Mode == economy.Mode
            && _width == width && _height == height;
        // A replacement at any position is a refresh, even when it costs the same one coin as an operator.
        bool sameCards = sameScene && cards.All(c => _cards.Any(p => SameSlot(c, p) && c.Name == p.Name));
        var missing = sameCards ? _cards.Where(p => !cards.Any(c => SameSlot(c, p))).ToList() : [];
        var relevantMissing = missing.Any(c => targets.Contains(c.Name));
        if (!sameCards || missing.Count > 0 && !relevantMissing)
        {
            _fundsBeforeCardChange = null;
            _fundsChangeGrace = 0;
        }
        else if (missing.Count == 0)
        {
            if (_stable >= 2 && _before!.Money > economy.Money)
            {
                // Payment can precede the name disappearing. Keep its baseline for two scans.
                if (_fundsBeforeCardChange is null)
                    AppLogger.Info("ShopPurchase", $"Payment baseline retained: money={_before.Money}->{economy.Money}, stable={_stable}");
                _fundsBeforeCardChange ??= _before.Money;
                _fundsChangeGrace = 2;
            }
            else if (_fundsChangeGrace > 0 && --_fundsChangeGrace == 0)
            {
                AppLogger.Info("ShopPurchase", $"Payment baseline expired: money={_fundsBeforeCardChange}->{economy.Money}");
                _fundsBeforeCardChange = null;
            }
        }
        var beforeMoney = _fundsBeforeCardChange ?? _before?.Money;
        var empty = false;
        if (sameCards && _stable >= 2 && relevantMissing && beforeMoney > economy.Money)
        {
            using var stream = new MemoryStream(image);
            using var bitmap = new Bitmap(stream);
            empty = missing.All(c => IsEmpty(bitmap, c));
        }
        if (missing.Count > 0)
        {
            var reason = !relevantMissing ? "not-tracked" : _stable < 2 ? "baseline-not-stable"
                : !(beforeMoney > economy.Money) ? "funds-not-decreased" : !empty ? "slot-not-empty" : "awaiting-second-empty-frame";
            var diagnostic = $"Candidate: reason={reason}, stable={_stable}, money={beforeMoney}->{economy.Money}, round={economy.Round}, missing=[{string.Join(", ", missing.Select(c => $"{c.Name}@{c.OcrRegion.X:F3}"))}]";
            if (diagnostic != _lastCandidateDiagnostic)
            {
                AppLogger.Info("ShopPurchase", diagnostic);
                _lastCandidateDiagnostic = diagnostic;
            }
        }
        else _lastCandidateDiagnostic = null;
        if (empty)
        {
            _purchasePending = true;
            var pending = string.Join('|', missing.Select(c => $"{c.Name}@{c.OcrRegion.X:F3}"));
            _emptyFrames = pending == _pending && _pendingMoney == economy.Money ? _emptyFrames + 1 : 1;
            _pending = pending;
            _pendingMoney = economy.Money;
            if (_emptyFrames < 2) return [];
            var purchased = missing.Where(c => targets.Contains(c.Name)).Select(c => c.Name).Distinct().ToList();
            AppLogger.Info("ShopPurchase", $"Confirmed [{string.Join(", ", purchased)}], money={beforeMoney}->{economy.Money}");
            _cards = cards.ToList();
            _before = economy;
            _emptyFrames = 0;
            _pending = "";
            _purchasePending = false;
            _fundsBeforeCardChange = null;
            _fundsChangeGrace = 0;
            _lastCandidateDiagnostic = null;
            return purchased;
        }
        if (sameCards && _stable >= 2 && relevantMissing)
        {
            // Keep the named card until empty pixels and funds agree; an animation or one bad funds
            // reading must not erase the only record of what occupied the now-empty position.
            _emptyFrames = 0;
            _pending = "";
            _purchasePending = true;
            return [];
        }
        if (!sameCards)
            AppLogger.Info("ShopPurchase", $"Baseline established: sameScene={sameScene}, round={economy.Round}, mode={economy.Mode}, size={width}x{height}, money={economy.Money}, previous=[{string.Join(", ", _cards.Select(c => c.Name))}], cards=[{string.Join(", ", cards.Select(c => $"{c.Name}@{c.OcrRegion.X:F3}"))}]");
        else if (_stable == 1 && missing.Count == 0)
            AppLogger.Info("ShopPurchase", $"Baseline stable: money={economy.Money}, cards={cards.Count}");
        _stable = sameCards && missing.Count == 0 ? _stable + 1 : 1;
        _cards = cards.ToList();
        _before = economy;
        _width = width; _height = height;
        _emptyFrames = 0; _pending = "";
        _purchasePending = false;
        return [];
    }

    private static bool SameSlot(ShopItem a, ShopItem b)
        => Math.Abs(a.OcrRegion.X - b.OcrRegion.X) < .018 && Math.Abs(a.OcrRegion.Y - b.OcrRegion.Y) < .015;

    // Empty merchandise slots reveal the dark scene behind the shop. Check the card body and its bottom
    // edge as well as the name: losing OCR text on a frozen/dark card must never count as a purchase.
    public static bool IsEmpty(Bitmap image, ShopItem card)
    {
        var left = (int)(card.OcrRegion.X * image.Width);
        var right = Math.Min(image.Width - 1, left + (int)(image.Height * .075));
        var top = (int)(image.Height * .84);
        int dark = 0, samples = 0;
        double strongestEdge = 0;
        for (int y = top; y < image.Height - 1; y += Math.Max(1, image.Height / 540))
        {
            int edge = 0, row = 0;
            for (int x = left; x < right; x += Math.Max(1, image.Height / 540))
            {
                var color = image.GetPixel(x, y);
                var below = image.GetPixel(x, Math.Min(image.Height - 1, y + Math.Max(1, image.Height / 360)));
                if (color.R < 85 && color.G < 100 && color.B < 145) dark++;
                if (Math.Abs(color.R - below.R) + Math.Abs(color.G - below.G) + Math.Abs(color.B - below.B) > 90) edge++;
                samples++; row++;
            }
            if (row > 0) strongestEdge = Math.Max(strongestEdge, edge / (double)row);
        }
        // Empty-slot emblems create short edges; a card border crosses most of the sampled width.
        return samples > 0 && dark / (double)samples > .92 && strongestEdge < .5;
    }
}
