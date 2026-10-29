using System.Text;
using Redmond.Notepad.Core;
using Redmond.Shortcuts;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var verificationDirectory = Path.Combine(
    Path.GetTempPath(),
    $"redmond-notepad-verification-{Guid.NewGuid():N}");
Directory.CreateDirectory(verificationDirectory);

try
{
    var store = new PhysicalTextFileStore();
    await VerifyRoundTripAsync(
        store,
        verificationDirectory,
        "utf8-no-bom.txt",
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        LineEnding.Lf,
        "alpha\r\nbeta\rgamma\n✓",
        "alpha\nbeta\ngamma\n✓",
        expectedPreamble: []);
    await VerifyRoundTripAsync(
        store,
        verificationDirectory,
        "utf8-bom.txt",
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        LineEnding.Crlf,
        "eins\nzwei\r三",
        "eins\r\nzwei\r\n三",
        expectedPreamble: [0xEF, 0xBB, 0xBF]);
    await VerifyRoundTripAsync(
        store,
        verificationDirectory,
        "utf16-be.txt",
        new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        LineEnding.Cr,
        "one\r\ntwo\nthree",
        "one\rtwo\rthree",
        expectedPreamble: [0xFE, 0xFF]);
    await VerifyExternalChangeProtectionAsync(store, verificationDirectory);

    VerifyUntitledPreview();
    VerifySessionChangeRestoration();
    VerifyCrashRecoveryStore(verificationDirectory);
    VerifyProcessRegistration(verificationDirectory);
    VerifyPortableEditLogic();
    VerifyPrintFieldFormatting();
    VerifyRecentFiles();
    VerifySpellCheckPrimitives();
    VerifyTabScopedSpellCheckOverride();
    VerifyShortcutConventions();
    VerifyMacOSShortcutModifierPreference();

    Console.WriteLine("Core file verification: passed");
}
finally
{
    Directory.Delete(verificationDirectory, recursive: true);
}

static void VerifyTabScopedSpellCheckOverride()
{
    var workspace = new NotepadWorkspace();
    var first = workspace.SelectedTab;
    var second = workspace.CreateTab();
    var third = workspace.CreateTab();

    Assert(first.IsSpellCheckEnabledOverride is null
           && second.IsSpellCheckEnabledOverride is null
           && third.IsSpellCheckEnabledOverride is null,
        "new tabs must inherit the global spelling policy");

    first.IsSpellCheckEnabledOverride = false;
    first.EditorState.CaretOffset = 12;
    first.EditorState.SelectionStart = 4;
    first.EditorState.SelectionLength = 8;
    first.EditorState.HorizontalOffset = 17;
    first.EditorState.VerticalOffset = 240;
    first.EditorState.IgnoredSpellingWords.Add("Redmond");
    workspace.SelectTab(second);
    Assert(second.IsSpellCheckEnabledOverride is null,
        "a spelling override must remain owned by its tab");

    workspace.MoveTab(first, 2);
    Assert(ReferenceEquals(workspace.Tabs[2], first),
        "reordering must move the original tab instance");

    var selectedAfterDetach = workspace.DetachTab(first);
    Assert(!workspace.Tabs.Contains(first) && ReferenceEquals(selectedAfterDetach, second),
        "detaching a background tab must preserve the selected tab");

    var target = new NotepadWorkspace();
    target.AttachTab(first, 0);
    Assert(ReferenceEquals(target.SelectedTab, first)
           && ReferenceEquals(target.Tabs[0], first),
        "attaching must transfer the original tab instance");

    Assert(first.EditorState.CaretOffset == 12
           && first.EditorState.SelectionStart == 4
           && first.EditorState.SelectionLength == 8
           && first.EditorState.HorizontalOffset == 17
           && first.EditorState.VerticalOffset == 240
           && first.EditorState.IgnoredSpellingWords.Contains("Redmond"),
        "transferring a tab must retain all session-owned editor state");
    Assert(first.IsSpellCheckEnabledOverride == false,
        "a spelling override must survive transfer for the current process");

    var rejectedBySource = false;
    try
    {
        workspace.SelectTab(first);
    }
    catch (ArgumentException)
    {
        rejectedBySource = true;
    }

    Assert(rejectedBySource,
        "a detached tab must no longer be selectable in its source workspace");
}

static void VerifyUntitledPreview()
{
    var document = new NotepadDocument(new StringTextBuffer("\r\n   \n  A title/with an invalid separator\r\nignored"));
    Assert(document.DisplayName == "A titlewith an invalid separator",
        "untitled preview must use and sanitize the first non-empty line");
    Assert(document.SuggestedFileName == "A titlewith an invalid separator.txt",
        "untitled preview must become the Save As suggestion");

    document.LoadText(string.Empty);
    Assert(document.DisplayName == NotepadDocument.UntitledName,
        "empty documents must retain the Untitled title");
    Assert(document.SuggestedFileName == "Untitled.txt",
        "empty documents must retain the Untitled Save As suggestion");
}

static void VerifySessionChangeRestoration()
{
    var buffer = new StringTextBuffer();
    var document = new NotepadDocument(buffer);
    var version = new FileContentVersion(4, DateTimeOffset.UtcNow, "session-hash");
    document.RestoreSessionChanges(
        new DocumentFileReference(Path.Combine(Path.GetTempPath(), "session.txt")),
        string.Empty,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        LineEnding.Lf,
        version);

    Assert(document.IsModified,
        "an empty restored edit must retain its unsaved state");
    Assert(document.FileVersion == version,
        "a restored edit must retain its base file hash for external-change protection");
    Assert(document.LineEnding == LineEnding.Lf,
        "a restored edit must retain its line-ending contract");
}

static void VerifyCrashRecoveryStore(string verificationDirectory)
{
    var recoveryDirectory = Path.Combine(verificationDirectory, "recovery");
    var store = new NotepadSessionRecoveryStore(recoveryDirectory);
    var firstWindowId = Guid.NewGuid();
    var secondWindowId = Guid.NewGuid();
    var documentId = Guid.NewGuid();
    var version = new FileContentVersion(17, DateTimeOffset.UtcNow, "base-content-hash");
    var editorState = new NotepadTabEditorSessionState(11, 4, 7, 12.5, 640);
    var modifiedText = "unsaved text that must survive a forced process exit";
    var modifiedBuffer = new StringTextBuffer(modifiedText);
    var documentState = new NotepadSessionDocumentState(
        Path.Combine(verificationDirectory, "original.txt"),
        null,
        Encoding.UTF8.WebName,
        false,
        LineEnding.Lf,
        version,
        documentId,
        EditorState: editorState);

    store.SaveWindow(new NotepadSessionWindowSnapshot(
        firstWindowId,
        [new NotepadSessionDocumentSnapshot(documentState, modifiedBuffer.CreateSnapshot())],
        0,
        1));
    store.SaveWindow(new NotepadSessionWindowSnapshot(
        secondWindowId,
        [new NotepadSessionDocumentSnapshot(
            new NotepadSessionDocumentState(null, null, Id: Guid.NewGuid()),
            null)],
        0,
        1));

    var manifest = File.ReadAllText(Path.Combine(recoveryDirectory, "session.json"));
    Assert(!manifest.Contains(modifiedText, StringComparison.Ordinal),
        "large unsaved buffers must be kept outside the recovery manifest");

    var restored = store.Load();
    Assert(restored.Windows.Length == 2,
        "recovery must retain every open window");
    var restoredDocument = restored.Windows
        .Single(window => window.Id == firstWindowId)
        .Documents
        .Single();
    Assert(restoredDocument.ModifiedText == modifiedText,
        "recovery must restore the exact unsaved buffer");
    Assert(restoredDocument.BaseFileVersion == version,
        "recovery must retain the base file hash used for external-change detection");
    Assert(restoredDocument.EditorState == editorState,
        "recovery must retain the tab's caret, selection, and scroll position");

    var staleBuffer = new StringTextBuffer("stale edit");
    store.SaveWindow(new NotepadSessionWindowSnapshot(
        firstWindowId,
        [new NotepadSessionDocumentSnapshot(documentState, staleBuffer.CreateSnapshot())],
        0,
        0));
    Assert(store.Load().Windows
            .Single(window => window.Id == firstWindowId)
            .Documents.Single().ModifiedText == modifiedText,
        "an older asynchronous snapshot must not overwrite newer recovery data");

    store.RemoveWindow(firstWindowId, 2);
    restored = store.Load();
    Assert(restored.Windows.Length == 1 && restored.Windows[0].Id == secondWindowId,
        "dismissing a window must remove only that window from recovery");
    Assert(!Directory.EnumerateFiles(
            Path.Combine(recoveryDirectory, "session-documents"),
            $"*-{documentId:N}-*.txt").Any(),
        "recovery payloads must be deleted when their tabs are dismissed");

    var newestText = "newest edit wins";
    store.SaveWindow(new NotepadSessionWindowSnapshot(
        firstWindowId,
        [new NotepadSessionDocumentSnapshot(
            documentState,
            new StringTextBuffer(newestText).CreateSnapshot())],
        0,
        3,
        DateTimeOffset.UtcNow.AddSeconds(1)));
    var latestWindow = store.LoadMostRecentWindow();
    Assert(latestWindow?.Id == firstWindowId
           && latestWindow.Documents.Single().ModifiedText == newestText,
        "cold start must restore the window with the newest edit or close timestamp");
    Assert(store.Load().Windows is [{ Id: var retainedId }] && retainedId == firstWindowId,
        "claiming the newest recovery candidate must discard older windows");

    var concurrentDirectory = Path.Combine(verificationDirectory, "concurrent-recovery");
    var concurrentFirst = new NotepadSessionRecoveryStore(concurrentDirectory);
    var concurrentSecond = new NotepadSessionRecoveryStore(concurrentDirectory);
    Task.WhenAll(
        Task.Run(() => concurrentFirst.SaveWindow(new NotepadSessionWindowSnapshot(
            Guid.NewGuid(),
            [new NotepadSessionDocumentSnapshot(
                new NotepadSessionDocumentState(null, null, Id: Guid.NewGuid()),
                null)],
            0,
            1,
            DateTimeOffset.UtcNow))),
        Task.Run(() => concurrentSecond.SaveWindow(new NotepadSessionWindowSnapshot(
            Guid.NewGuid(),
            [new NotepadSessionDocumentSnapshot(
                new NotepadSessionDocumentState(null, null, Id: Guid.NewGuid()),
                null)],
            0,
            1,
            DateTimeOffset.UtcNow)))).GetAwaiter().GetResult();
    Assert(concurrentFirst.Load().Windows.Length == 2,
        "independent Notepad processes must merge recovery candidates under an inter-process lock");

    store.Clear();
    Assert(store.Load().Windows.Length == 0,
        "starting with a new session must clear the recovery store");
}

static void VerifyProcessRegistration(string verificationDirectory)
{
    var registrationDirectory = Path.Combine(verificationDirectory, "process-registration");
    using var first = NotepadProcessRegistration.Register(registrationDirectory);
    Assert(!first.HasOtherActiveProcesses,
        "the first running Notepad process must be eligible to restore recovery");
    using (var second = NotepadProcessRegistration.Register(registrationDirectory))
    {
        Assert(second.HasOtherActiveProcesses,
            "a later Notepad process must detect the existing process and start blank");
    }

    first.Dispose();
    using var afterExit = NotepadProcessRegistration.Register(registrationDirectory);
    Assert(!afterExit.HasOtherActiveProcesses,
        "after the final process exits, the next process must be eligible to restore recovery");
}

static void VerifyShortcutConventions()
{
    var definitions = NotepadShortcutCatalog.CreateDefinitions();
    var windows = new ShortcutService(ShortcutPlatform.Windows);
    var macOS = new ShortcutService(ShortcutPlatform.MacOS);
    foreach (var definition in definitions)
    {
        windows.Register(definition);
        macOS.Register(definition);
    }

    Assert(windows.GetGestureDisplayText(NotepadShortcutIds.NewTab) == "Ctrl+N",
        "Windows new-tab shortcut must preserve the reference Notepad binding");
    Assert(macOS.GetGestureDisplayText(NotepadShortcutIds.NewTab) == "⌘T",
        "macOS new-tab shortcut must follow the native tab convention");
    Assert(macOS.GetGestureDisplayText(NotepadShortcutIds.NewWindow) == "⌘N",
        "macOS new-window shortcut must follow the native document-window convention");
    Assert(macOS.GetGestureDisplayText(NotepadShortcutIds.SaveAll) == "⌥⌘S",
        "macOS Save All must avoid Windows Control/Alt semantics");
    Assert(windows.GetGestureDisplayText(NotepadShortcutIds.FindNext) == "F3",
        "Windows Find next must preserve the reference Notepad binding");
    Assert(macOS.GetGestureDisplayText(NotepadShortcutIds.FindNext) == "⌘G",
        "macOS Find next must use the native application convention");
    Assert(windows.GetGestureDisplayText(NotepadShortcutIds.Replace) == "Ctrl+H",
        "Windows Replace must preserve the reference Notepad binding");
    Assert(macOS.GetGestureDisplayText(NotepadShortcutIds.Replace) == "⌥⌘F",
        "macOS Replace must avoid the browser-history Command+Shift+H convention");
    var windowsEditGestures = new Dictionary<string, string>
    {
        [NotepadShortcutIds.Undo] = "Ctrl+Z",
        [NotepadShortcutIds.Cut] = "Ctrl+X",
        [NotepadShortcutIds.Copy] = "Ctrl+C",
        [NotepadShortcutIds.Paste] = "Ctrl+V",
        [NotepadShortcutIds.Delete] = "Delete",
        [NotepadShortcutIds.Find] = "Ctrl+F",
        [NotepadShortcutIds.FindNext] = "F3",
        [NotepadShortcutIds.FindPrevious] = "Shift+F3",
        [NotepadShortcutIds.Replace] = "Ctrl+H",
        [NotepadShortcutIds.GoTo] = "Ctrl+G",
        [NotepadShortcutIds.SelectAll] = "Ctrl+A",
        [NotepadShortcutIds.TimeDate] = "F5",
    };
    foreach (var (shortcutId, expectedGesture) in windowsEditGestures)
    {
        Assert(windows.GetGestureDisplayText(shortcutId) == expectedGesture,
            $"Windows {shortcutId} must display as {expectedGesture}");
    }

    Assert(windows.IsMatch(
            NotepadShortcutIds.ZoomIn,
            new ShortcutInput(new ShortcutGesture(
                ShortcutKey.Named("OemPlus"),
                ShortcutModifiers.Control | ShortcutModifiers.Shift))),
        "Windows Zoom in must accept the shifted plus key");
    Assert(windows.IsMatch(
            NotepadShortcutIds.ZoomOut,
            new ShortcutInput(new ShortcutGesture(
                ShortcutKey.Named("NumPadSubtract"),
                ShortcutModifiers.Control))),
        "Windows Zoom out must accept the numeric-keypad minus key");
    Assert(macOS.IsMatch(
            NotepadShortcutIds.ZoomIn,
            new ShortcutInput(new ShortcutGesture(
                ShortcutKey.Named("OemPlus"),
                ShortcutModifiers.Command | ShortcutModifiers.Shift))),
        "macOS Zoom in must use Command and accept the shifted plus key");
    Assert(macOS.IsMatch(
            NotepadShortcutIds.RestoreDefaultZoom,
            new ShortcutInput(new ShortcutGesture(
                ShortcutKey.Named("D0"),
                ShortcutModifiers.Command))),
        "macOS Restore default zoom must use Command+0");

    Assert(windows.GetConflicts().Count == 0 && macOS.GetConflicts().Count == 0,
        "Notepad shortcut catalogs must not contain duplicate active gestures");
    Assert(macOS.ValidateConventions().All(issue => issue.Severity != ShortcutConventionSeverity.Error),
        "macOS shortcut catalog must avoid reserved or conflicting system gestures");
}

static void VerifyMacOSShortcutModifierPreference()
{
    static ShortcutService CreateService(ShortcutPlatform platform, MacOSShortcutModifier modifier)
    {
        var service = new ShortcutService(platform);
        foreach (var definition in NotepadShortcutCatalog.CreateDefinitions(modifier))
        {
            service.Register(definition);
        }

        return service;
    }

    static ShortcutInput Input(string key, ShortcutModifiers modifiers) =>
        new(new ShortcutGesture(ShortcutKey.Named(key), modifiers));

    var macDefault = new ShortcutService(ShortcutPlatform.MacOS);
    foreach (var definition in NotepadShortcutCatalog.CreateDefinitions())
    {
        macDefault.Register(definition);
    }

    var macCommand = CreateService(ShortcutPlatform.MacOS, MacOSShortcutModifier.Command);
    var macControl = CreateService(ShortcutPlatform.MacOS, MacOSShortcutModifier.Control);
    Assert(macDefault.GetGestureDisplayText(NotepadShortcutIds.Save) == "⌘S",
        "macOS shortcuts must default to Command");
    Assert(macCommand.GetGestureDisplayText(NotepadShortcutIds.NewTab) == "⌘T",
        "the Command preference must keep the native macOS bindings");

    var expectedControlGestures = new Dictionary<string, string>
    {
        [NotepadShortcutIds.NewTab] = "⌃T",
        [NotepadShortcutIds.Save] = "⌃S",
        [NotepadShortcutIds.SaveAs] = "⌃⇧S",
        [NotepadShortcutIds.SaveAll] = "⌃⌥S",
        [NotepadShortcutIds.Copy] = "⌃C",
        [NotepadShortcutIds.FindNext] = "⌃G",
        [NotepadShortcutIds.Replace] = "⌃⌥F",
        [NotepadShortcutIds.Delete] = "⌦",
        [NotepadShortcutIds.TimeDate] = "F5",
    };
    foreach (var (shortcutId, expectedGesture) in expectedControlGestures)
    {
        var actual = macControl.GetGestureDisplayText(shortcutId);
        Assert(actual == expectedGesture,
            $"the macOS Control preference must display {shortcutId} as {expectedGesture}, not {actual}");
    }

    Assert(macControl.IsMatch(NotepadShortcutIds.Open, Input("O", ShortcutModifiers.Control)),
        "the macOS Control preference must accept Control+O");
    Assert(!macControl.IsMatch(NotepadShortcutIds.Open, Input("O", ShortcutModifiers.Command)),
        "the macOS Control preference must stop Command+O from opening files");
    Assert(!macCommand.IsMatch(NotepadShortcutIds.Open, Input("O", ShortcutModifiers.Control)),
        "the macOS Command preference must not react to Control+O");
    Assert(macControl.IsMatch(
            NotepadShortcutIds.ZoomIn,
            Input("OemPlus", ShortcutModifiers.Control | ShortcutModifiers.Shift)),
        "the macOS Control preference must remap alternate gestures too");
    Assert(macControl.IsMatch(NotepadShortcutIds.ZoomOut, Input("NumPadSubtract", ShortcutModifiers.Control)),
        "the macOS Control preference must remap numeric-keypad zoom");
    Assert(macControl.IsMatch(NotepadShortcutIds.RestoreDefaultZoom, Input("D0", ShortcutModifiers.Control)),
        "the macOS Control preference must restore the default zoom with Control+0");
    Assert(macControl.GetConflicts().Count == 0,
        "the macOS Control preference must not introduce duplicate gestures");
    Assert(macControl.ValidateConventions().All(issue => issue.Severity != ShortcutConventionSeverity.Error),
        "the macOS Control preference must avoid reserved or conflicting system gestures");

    foreach (var platform in new[] { ShortcutPlatform.Windows, ShortcutPlatform.Linux })
    {
        var command = CreateService(platform, MacOSShortcutModifier.Command);
        var control = CreateService(platform, MacOSShortcutModifier.Control);
        foreach (var definition in NotepadShortcutCatalog.CreateDefinitions())
        {
            Assert(command.GetGestureDisplayTexts(definition.Id)
                    .SequenceEqual(control.GetGestureDisplayTexts(definition.Id)),
                $"the macOS modifier preference must not change {definition.Id} on {platform}");
        }
    }
}

static void VerifyPortableEditLogic()
{
    var buffer = new StringTextBuffer("alpha beta\r\ngamma");
    Assert(NotepadEditLogic.GetSearchText(buffer, 7, 0) == "beta",
        "Find must seed from the word under the caret, matching Windows Notepad behavior");
    Assert(NotepadEditLogic.GetSearchText(buffer, 0, 5) == "alpha",
        "Find must seed from the current single-line selection");
    Assert(NotepadEditLogic.GetSearchText(buffer, 0, 13) == string.Empty,
        "Find must not seed from a multiline selection");
    Assert(NotepadEditLogic.GetSearchText(buffer, 5, 0) == string.Empty,
        "Find must not infer a word when the caret is on punctuation or whitespace");

    var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
    var instant = new DateTime(2026, 7, 22, 16, 42, 3);
    Assert(
        NotepadEditLogic.GetDateTimeText(instant, culture)
            == $"{instant.ToString("t", culture)} {instant.ToString("d", culture)}",
        "Time/Date must compose localized short time followed by localized short date");
    Assert(
        NotepadEditLogic.GetDateTimeText(
            instant,
            System.Globalization.CultureInfo.GetCultureInfo("de-DE")) == "16:42 22.07.2026",
        "Time/Date must match the verified German Windows Notepad format");
}

static void VerifyPrintFieldFormatting()
{
    var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
    var fields = NotepadPrintFieldFormatter.Format(
        "&l&f&cPage &p of &P&r&d && &t",
        new PrintFieldContext(
            "The Raven.txt",
            PageNumber: 2,
            PageCount: 7,
            new DateTime(2026, 7, 22, 16, 42, 0)),
        culture);

    Assert(fields.Left == "The Raven.txt", "print fields must expand the document name");
    Assert(fields.Centre == "Page 2 of 7", "print fields must expand current and total pages");
    Assert(
        fields.Right.StartsWith("7/22/2026 & 4:42", StringComparison.Ordinal),
        "print fields must expand date, time, and escaped ampersands");
}

static void VerifyRecentFiles()
{
    var paths = Enumerable.Range(0, 12)
        .Select(index => Path.Combine(Path.GetTempPath(), $"recent-{index}.txt"))
        .ToArray();
    var recentFiles = new RecentFileList();
    foreach (var path in paths)
    {
        recentFiles.Add(path);
    }

    Assert(recentFiles.Paths.Count == RecentFileList.MaximumCount,
        "recent files must be capped at ten entries");
    Assert(recentFiles.Paths[0] == Path.GetFullPath(paths[^1]),
        "the last opened file must be the first recent entry");
    Assert(!recentFiles.Paths.Contains(Path.GetFullPath(paths[0])),
        "the oldest file must fall out of a full recent list");

    recentFiles.Add(paths[5]);
    Assert(recentFiles.Paths[0] == Path.GetFullPath(paths[5])
        && recentFiles.Paths.Count(path => path == Path.GetFullPath(paths[5])) == 1,
        "reopening a file must promote it without creating duplicates");
    Assert(recentFiles.Remove(paths[5]) && !recentFiles.Paths.Contains(Path.GetFullPath(paths[5])),
        "removed recent files must disappear from the list");
    recentFiles.Clear();
    Assert(recentFiles.Paths.Count == 0, "clearing recent files must empty the list");
}

static void VerifySpellCheckPrimitives()
{
    var words = SpellCheckWordTokenizer.EnumerateWords(
            "plain l’esprit don't Ελληνικά 中文 123 file.txt")
        .Select(segment => "plain l’esprit don't Ελληνικά 中文 123 file.txt"
            .Substring(segment.Start, segment.Length))
        .ToArray();
    Assert(words.SequenceEqual(["plain", "l’esprit", "don't", "Ελληνικά", "中文", "file", "txt"]),
        "spell-check tokenization must preserve Unicode words and inner apostrophes");

    Assert(
        SpellCheckLanguageSelection.SelectBest(
            ["de_DE", "en_GB", "en_US"],
            System.Globalization.CultureInfo.GetCultureInfo("en-US")) == "en_US",
        "spell-check language selection must match equivalent culture separators");
    Assert(
        SpellCheckLanguageSelection.SelectBest(
            ["de_DE", "en_GB"],
            System.Globalization.CultureInfo.GetCultureInfo("en-AU")) == "en_GB",
        "spell-check language selection must fall back to a compatible language");

    Assert(AutocorrectLogic.IsSingleAdjacentTransposition("teh", "the"),
        "common-typo correction must accept one adjacent letter swap");
    Assert(AutocorrectLogic.IsSingleAdjacentTransposition("recieve", "receive"),
        "common-typo correction must accept an adjacent vowel swap");
    Assert(!AutocorrectLogic.IsSingleAdjacentTransposition("mispelled", "misspelled"),
        "common-typo correction must not silently add missing letters");
    Assert(!AutocorrectLogic.IsSingleAdjacentTransposition("form", "fromm"),
        "common-typo correction must reject replacements with a different length");
}

static async Task VerifyExternalChangeProtectionAsync(
    ITextFileStore store,
    string directory)
{
    var path = Path.Combine(directory, "external-change.txt");
    var originalBytes = Encoding.UTF8.GetBytes("original-A");
    var externalBytes = Encoding.UTF8.GetBytes("external-A");
    Assert(originalBytes.Length == externalBytes.Length,
        "external-change fixture must preserve length");
    await File.WriteAllBytesAsync(path, originalBytes);

    var file = new DocumentFileReference(path);
    var document = new NotepadDocument(new StringTextBuffer());
    await document.LoadAsync(store, file);
    var originalTimestamp = File.GetLastWriteTimeUtc(path);
    document.Buffer.Text = "local-version";

    await File.WriteAllBytesAsync(path, externalBytes);
    File.SetLastWriteTimeUtc(path, originalTimestamp);
    Assert(
        await document.CheckForExternalChangesAsync(store) == ExternalFileChange.Modified,
        "SHA-256 comparison must detect same-length changes even when the timestamp is restored");

    var conflictThrown = false;
    try
    {
        await document.SaveAsync(store);
    }
    catch (FileChangedExternallyException exception)
    {
        conflictThrown = exception.Change == ExternalFileChange.Modified;
    }

    Assert(conflictThrown, "normal save must reject an externally modified file");
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(externalBytes),
        "rejected save must leave the external version untouched");

    await document.SaveAsync(store, overwriteExternalChanges: true);
    Assert(!document.IsModified, "explicit overwrite must save and mark the buffer original");
    Assert(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)) == "local-version",
        "explicit overwrite must preserve the editor version");

    document.Buffer.Text = "recreated-version";
    File.Delete(path);
    Assert(
        await document.CheckForExternalChangesAsync(store) == ExternalFileChange.Deleted,
        "deleted files must be distinguished from modified files");

    conflictThrown = false;
    try
    {
        await document.SaveAsync(store);
    }
    catch (FileChangedExternallyException exception)
    {
        conflictThrown = exception.Change == ExternalFileChange.Deleted;
    }

    Assert(conflictThrown, "normal save must not silently recreate a deleted file");
    Assert(!File.Exists(path), "rejected save must leave the deleted path absent");
    await document.SaveAsync(store, overwriteExternalChanges: true);
    Assert(File.Exists(path), "explicit recreate must restore a deleted file");

    var timestampBeforeTouch = File.GetLastWriteTimeUtc(path);
    File.SetLastWriteTimeUtc(path, timestampBeforeTouch.AddSeconds(2));
    Assert(
        await document.CheckForExternalChangesAsync(store) == ExternalFileChange.None,
        "timestamp-only touches must not produce false content conflicts");
}

static async Task VerifyRoundTripAsync(
    ITextFileStore store,
    string directory,
    string fileName,
    Encoding encoding,
    LineEnding lineEnding,
    string sourceText,
    string expectedText,
    byte[] expectedPreamble)
{
    var path = Path.Combine(directory, fileName);
    await File.WriteAllTextAsync(path, "stale content");
    var file = new DocumentFileReference(path);
    var source = new StringTextBuffer(sourceText);
    var snapshot = source.CreateSnapshot();
    source.Replace(0, 0, "changed-");
    Assert(source.IsModified, $"{fileName}: edits must mark the buffer modified");
    Assert(snapshot.Text == sourceText, $"{fileName}: immutable snapshot changed with the live buffer");
    source.Replace(0, "changed-".Length, string.Empty);

    await store.SaveAsync(file, source, new TextFileMetadata(encoding, lineEnding));
    Assert(!source.IsModified, $"{fileName}: saving must mark the buffer original");

    var bytes = await File.ReadAllBytesAsync(path);
    Assert(
        bytes.AsSpan(0, expectedPreamble.Length).SequenceEqual(expectedPreamble),
        $"{fileName}: preamble mismatch");
    Assert(
        !Directory.EnumerateFiles(directory, $".{fileName}.*.tmp").Any(),
        $"{fileName}: atomic-save temporary file was not cleaned up");

    var destination = new StringTextBuffer();
    var loaded = await store.LoadAsync(file, destination);
    Assert(destination.Text == expectedText, $"{fileName}: text or line endings did not round-trip");
    Assert(destination.LineCount == CountLines(expectedText),
        $"{fileName}: line count mismatch");
    Assert(!destination.IsModified, $"{fileName}: loading must mark the buffer original");
    Assert(loaded.LineEnding == lineEnding, $"{fileName}: line-ending metadata mismatch");
    Assert(
        TextEncoding.GetDisplayName(loaded.Encoding) == TextEncoding.GetDisplayName(encoding),
        $"{fileName}: encoding metadata mismatch");
}

static int CountLines(string text)
{
    var count = 1;
    for (var index = 0; index < text.Length; index++)
    {
        if (text[index] == '\r')
        {
            count++;
            if (index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }
        }
        else if (text[index] == '\n')
        {
            count++;
        }
    }

    return count;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
