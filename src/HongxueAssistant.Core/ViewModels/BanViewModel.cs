namespace HongxueAssistant.Core.ViewModels;

public class BanViewModel
{
    private readonly HashSet<string> _bannedNames = [];
    private readonly HashSet<string> _automaticNames = [];
    private readonly HashSet<string> _manualBanned = [];
    private readonly HashSet<string> _manualAllowed = [];
    private readonly HashSet<string> _bannedCovenants = [];

    public IReadOnlySet<string> BannedNames => _bannedNames;
    public IReadOnlySet<string> BannedCovenants => _bannedCovenants;
    public bool IsCovenantBanned(string name) => _bannedCovenants.Contains(name);

    public void SetCovenantBans(IEnumerable<string> names)
    {
        _bannedCovenants.Clear();
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name)) _bannedCovenants.Add(name);
        BansChanged?.Invoke();
    }

    public event Action? BansChanged;

    public bool IsBanned(string name) => _bannedNames.Contains(name);

    /// <summary>
    /// Replaces the current ban list with the provided names.
    /// Used both on initial load and when a new match ban screen is detected
    /// (auto-clears the previous run's bans before applying the new set).
    /// </summary>
    public void SetBans(IEnumerable<string> names)
    {
        _bannedNames.Clear();
        _automaticNames.Clear();
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name))
            {
                _bannedNames.Add(name);
                _automaticNames.Add(name);
            }
        _bannedNames.UnionWith(_manualBanned);
        _bannedNames.ExceptWith(_manualAllowed);
        BansChanged?.Invoke();
    }

    public void ToggleBan(string name) => SetManualBan(name, !_bannedNames.Contains(name));

    public void SetManualBan(string name, bool banned)
    {
        _automaticNames.Remove(name);
        if (banned) { _manualBanned.Add(name); _manualAllowed.Remove(name); _bannedNames.Add(name); }
        else { _manualAllowed.Add(name); _manualBanned.Remove(name); _bannedNames.Remove(name); }
        BansChanged?.Invoke();
    }

    public void ClearBans()
    {
        _manualBanned.Clear(); _manualAllowed.Clear();
        _bannedNames.Clear();
        _automaticNames.Clear();
        _bannedCovenants.Clear();
        BansChanged?.Invoke();
    }

    public void ClearRunBans()
    {
        _bannedNames.ExceptWith(_automaticNames);
        _automaticNames.Clear();
        _bannedCovenants.Clear();
        BansChanged?.Invoke();
    }
}
