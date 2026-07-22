namespace Redmond.Notepad.Core;

public sealed class RecentFileList
{
    public const int MaximumCount = 10;

    private readonly List<string> _paths = [];
    private readonly StringComparer _pathComparer;

    public RecentFileList(IEnumerable<string>? paths = null)
    {
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        if (paths is null)
        {
            return;
        }

        foreach (var path in paths.Reverse())
        {
            Add(path);
        }
    }

    public IReadOnlyList<string> Paths => _paths;

    public void Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        _paths.RemoveAll(candidate => _pathComparer.Equals(candidate, fullPath));
        _paths.Insert(0, fullPath);
        if (_paths.Count > MaximumCount)
        {
            _paths.RemoveRange(MaximumCount, _paths.Count - MaximumCount);
        }
    }

    public bool Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        return _paths.RemoveAll(candidate => _pathComparer.Equals(candidate, fullPath)) > 0;
    }

    public void Clear() => _paths.Clear();
}
