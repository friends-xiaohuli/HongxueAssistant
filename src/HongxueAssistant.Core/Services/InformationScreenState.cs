namespace HongxueAssistant.Core.Services;

/// <summary>One attempt per opening; confirmed gameplay or the run lobby rearms it, missing OCR does not.</summary>
public sealed class InformationScreenState
{
    private readonly object _sync = new();
    private int _openingFrames;
    private int _outsideOpeningFrames;
    private bool _consumed;
    private bool _runStarted;
    private bool _recorded;
    private DateTime _retryAfter;

    public bool IsConsumed { get { lock (_sync) return _consumed; } }

    public bool Observe(bool opening, bool canRearm)
    {
        lock (_sync)
        {
            if (canRearm)
            {
                _openingFrames = 0;
                if (++_outsideOpeningFrames >= 2) _consumed = _runStarted = _recorded = false;
                return false;
            }
            _outsideOpeningFrames = 0;
            if (!opening)
            {
                _openingFrames = 0;
                return false;
            }
            if (_consumed || DateTime.UtcNow < _retryAfter || ++_openingFrames < 2) return false;
            _consumed = true;
            return true;
        }
    }

    public void MarkRecorded()
    {
        lock (_sync)
        {
            _consumed = _recorded = true;
            _openingFrames = _outsideOpeningFrames = 0;
        }
    }

    public bool TryStartNewRun(bool bansChanged)
    {
        lock (_sync)
        {
            if (_runStarted && !bansChanged) return false;
            _runStarted = _consumed = true;
            _openingFrames = _outsideOpeningFrames = 0;
            return true;
        }
    }

    public void RetryFailedAttempt()
    {
        lock (_sync)
        {
            if (_recorded) return;
            _consumed = false; _openingFrames = 0;
            _retryAfter = DateTime.UtcNow.AddSeconds(3);
        }
    }

    public void Suspend()
    {
        lock (_sync) _openingFrames = _outsideOpeningFrames = 0;
    }
}
