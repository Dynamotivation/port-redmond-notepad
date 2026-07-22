using System.Text;
using Notepads.Utilities;
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
    VerifyPortableEditLogic();
    VerifyShortcutConventions();

    Console.WriteLine("Core file verification: passed");
}
finally
{
    Directory.Delete(verificationDirectory, recursive: true);
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
    Assert(windows.GetConflicts().Count == 0 && macOS.GetConflicts().Count == 0,
        "Notepad shortcut catalogs must not contain duplicate active gestures");
    Assert(macOS.ValidateConventions().All(issue => issue.Severity != ShortcutConventionSeverity.Error),
        "macOS shortcut catalog must avoid reserved or conflicting system gestures");
}

static void VerifyPortableEditLogic()
{
    var buffer = new StringTextBuffer("alpha beta\r\ngamma");
    Assert(NotepadEditLogic.GetSearchText(buffer, 7, 0) == "beta",
        "Find must seed from the word under the caret, matching upstream Notepads");
    Assert(NotepadEditLogic.GetSearchText(buffer, 0, 5) == "alpha",
        "Find must seed from the current single-line selection");
    Assert(NotepadEditLogic.GetSearchText(buffer, 0, 13) == string.Empty,
        "Find must not seed from a multiline selection");
    Assert(NotepadEditLogic.GetSearchText(buffer, 5, 0) == string.Empty,
        "Find must not infer a word when the caret is on punctuation or whitespace");

    var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
    var instant = new DateTime(2026, 7, 22, 16, 42, 3);
    Assert(
        NotepadEditLogic.GetDateTimeText(instant, culture) == instant.ToString(culture),
        "Time/Date must use the current-culture DateTime format from upstream Notepads");
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
