namespace Redmond.Notepad.Core;

public sealed class NotepadTab
{
    internal NotepadTab(ITextBuffer buffer) => Document = new NotepadDocument(buffer);

    public Guid Id { get; } = Guid.NewGuid();

    public NotepadDocument Document { get; }

    /// <summary>
    /// Overrides the global spelling policy for this tab until the process exits.
    /// A null value inherits the current global and file-type settings.
    /// This deliberately is not part of session persistence.
    /// </summary>
    public bool? IsSpellCheckEnabledOverride { get; set; }

    public NotepadTabEditorState EditorState { get; } = new();

    public string Title => Document.DisplayName;
}

public sealed class NotepadTabEditorState
{
    public int CaretOffset { get; set; }

    public int SelectionStart { get; set; }

    public int SelectionLength { get; set; }

    public double HorizontalOffset { get; set; }

    public double VerticalOffset { get; set; }

    public ISet<string> IgnoredSpellingWords { get; } =
        new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
}

public sealed class NotepadWorkspace
{
    private readonly List<NotepadTab> _tabs = [];
    private readonly ITextBufferFactory _textBufferFactory;

    public NotepadWorkspace(ITextBufferFactory? textBufferFactory = null)
    {
        _textBufferFactory = textBufferFactory ?? new StringTextBufferFactory();
        CreateTab();
    }

    public IReadOnlyList<NotepadTab> Tabs => _tabs;

    public NotepadTab SelectedTab { get; private set; } = null!;

    public NotepadTab CreateTab()
    {
        var tab = new NotepadTab(_textBufferFactory.Create());
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

    public void MoveTab(NotepadTab tab, int destinationIndex)
    {
        var sourceIndex = _tabs.IndexOf(tab);
        if (sourceIndex < 0)
        {
            throw new ArgumentException("The tab does not belong to this workspace.", nameof(tab));
        }

        destinationIndex = Math.Clamp(destinationIndex, 0, _tabs.Count - 1);
        if (sourceIndex == destinationIndex)
        {
            return;
        }

        _tabs.RemoveAt(sourceIndex);
        _tabs.Insert(destinationIndex, tab);
    }

    public NotepadTab? DetachTab(NotepadTab tab)
    {
        var detachedIndex = _tabs.IndexOf(tab);
        if (detachedIndex < 0)
        {
            throw new ArgumentException("The tab does not belong to this workspace.", nameof(tab));
        }

        _tabs.RemoveAt(detachedIndex);
        if (_tabs.Count == 0)
        {
            return null;
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = _tabs[Math.Min(detachedIndex, _tabs.Count - 1)];
        }

        return SelectedTab;
    }

    public void AttachTab(NotepadTab tab, int destinationIndex)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_tabs.Contains(tab))
        {
            throw new ArgumentException("The tab already belongs to this workspace.", nameof(tab));
        }

        destinationIndex = Math.Clamp(destinationIndex, 0, _tabs.Count);
        _tabs.Insert(destinationIndex, tab);
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
