namespace Redmond.Notepad.Core;

public sealed class NotepadTab
{
    internal NotepadTab() => Document = new NotepadDocument();

    public Guid Id { get; } = Guid.NewGuid();

    public NotepadDocument Document { get; }

    public string Title => Document.DisplayName;
}

public sealed class NotepadWorkspace
{
    private readonly List<NotepadTab> _tabs = [];

    public NotepadWorkspace() => CreateTab();

    public IReadOnlyList<NotepadTab> Tabs => _tabs;

    public NotepadTab SelectedTab { get; private set; } = null!;

    public NotepadTab CreateTab()
    {
        var tab = new NotepadTab();
        _tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    public void SelectTab(NotepadTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            throw new ArgumentException("The tab does not belong to this workspace.", nameof(tab));
        }

        SelectedTab = tab;
    }

    public NotepadTab CloseTab(NotepadTab tab)
    {
        var closedIndex = _tabs.IndexOf(tab);
        if (closedIndex < 0)
        {
            throw new ArgumentException("The tab does not belong to this workspace.", nameof(tab));
        }

        _tabs.RemoveAt(closedIndex);
        if (_tabs.Count == 0)
        {
            return CreateTab();
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = _tabs[Math.Min(closedIndex, _tabs.Count - 1)];
        }

        return SelectedTab;
    }
}
