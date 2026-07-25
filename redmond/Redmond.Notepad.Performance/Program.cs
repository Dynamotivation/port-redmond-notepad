using System.Diagnostics;
using System.Text;
using Redmond.Notepad.Core;
using Redmond.Notepad.Editor.AvaloniaEdit;

const int targetCharacters = 25_000_000;
const int incrementalEditCount = 10_000;
const int tabCount = 64;
const int charactersPerTab = 200_000;
const string line = "The quick brown fox jumps over the lazy dog. 0123456789\n";

var sourceBuilder = new StringBuilder(targetCharacters + line.Length);
while (sourceBuilder.Length < targetCharacters)
{
    sourceBuilder.Append(line);
}

var source = sourceBuilder.ToString(0, targetCharacters);
var expectedLineCount = source.Count(character => character == '\n') + 1;
var factory = new AvaloniaEditTextBufferFactory();

var undoBuffer = (AvaloniaEditTextBuffer)factory.Create("saved");
undoBuffer.MarkAsOriginal();
undoBuffer.Replace(undoBuffer.Length, 0, " change");
if (!undoBuffer.IsModified)
{
    throw new InvalidOperationException("Editing an original document did not mark it as modified.");
}

undoBuffer.Document.UndoStack.Undo();
if (undoBuffer.IsModified || undoBuffer.Text != "saved")
{
    throw new InvalidOperationException("Undoing back to the save point did not restore the original state.");
}

var commandBuffer = (AvaloniaEditTextBuffer)factory.Create("typed text");
commandBuffer.MarkAsOriginal();
commandBuffer.Document.UndoStack.StartUndoGroup();
try
{
    commandBuffer.Replace(commandBuffer.Length, 0, " timestamp");
}
finally
{
    commandBuffer.Document.UndoStack.EndUndoGroup();
}

commandBuffer.Document.UndoStack.Undo();
if (commandBuffer.IsModified || commandBuffer.Text != "typed text")
{
    throw new InvalidOperationException("A grouped Edit command did not undo independently to the save point.");
}

var construction = Stopwatch.StartNew();
var buffer = factory.Create(source);
construction.Stop();
var snapshot = buffer.CreateSnapshot();

var editOffset = buffer.Length / 2;
var edits = Stopwatch.StartNew();
for (var index = 0; index < incrementalEditCount; index++)
{
    buffer.Replace(editOffset, 0, "x");
    buffer.Replace(editOffset, 1, string.Empty);
}
edits.Stop();

var locations = Stopwatch.StartNew();
var checksum = 0;
for (var index = 0; index < incrementalEditCount; index++)
{
    var position = buffer.GetPosition((int)((long)buffer.Length * index / incrementalEditCount));
    checksum = unchecked(checksum + position.Line + position.Column);
}
locations.Stop();

if (buffer.Length != source.Length || buffer.LineCount != expectedLineCount || buffer.Text != source)
{
    throw new InvalidOperationException("The incremental editor benchmark changed document contents or metadata.");
}
if (snapshot.Length != source.Length || snapshot.Text != source)
{
    throw new InvalidOperationException("The immutable editor snapshot changed with the live document.");
}

var tabPayload = source[..charactersPerTab];
var tabStress = Stopwatch.StartNew();
var workspace = new NotepadWorkspace(factory);
var tabs = new List<NotepadTab>(tabCount);
for (var index = 0; index < tabCount; index++)
{
    var tab = index == 0 ? workspace.SelectedTab : workspace.CreateTab();
    tab.Document.LoadText(tabPayload);
    tab.EditorState.CaretOffset = index * 97 % tab.Document.CharacterCount;
    tab.EditorState.VerticalOffset = index * 31.5;
    tab.EditorState.IgnoredSpellingWords.Add($"projectword{index}");
    tabs.Add(tab);
}

var transferProbe = tabs[0];
var transferProbeBuffer = (AvaloniaEditTextBuffer)transferProbe.Document.Buffer;
var transferProbeDocument = transferProbeBuffer.Document;
transferProbeBuffer.Replace(transferProbeBuffer.Length, 0, "x");

for (var index = 0; index < tabCount; index++)
{
    workspace.MoveTab(tabs[index], tabCount - 1 - index);
}

var transferTarget = new NotepadWorkspace(factory);
foreach (var tab in tabs.Where((_, index) => index % 2 == 0))
{
    workspace.DetachTab(tab);
    transferTarget.AttachTab(tab, transferTarget.Tabs.Count);
}

tabStress.Stop();
if (workspace.Tabs.Count != tabCount / 2
    || transferTarget.Tabs.Count != tabCount / 2 + 1)
{
    throw new InvalidOperationException("Multi-tab transfer lost or duplicated a tab.");
}

if (!ReferenceEquals(
        ((AvaloniaEditTextBuffer)transferProbe.Document.Buffer).Document,
        transferProbeDocument))
{
    throw new InvalidOperationException("Transferring a tab recreated its AvaloniaEdit document.");
}

transferProbeDocument.UndoStack.Undo();
if (transferProbe.Document.IsModified || transferProbe.Document.CharacterCount != charactersPerTab)
{
    throw new InvalidOperationException("Transferring a tab lost its undo history or save point.");
}

foreach (var tab in tabs)
{
    var expectedIndex = tabs.IndexOf(tab);
    if (tab.Document.CharacterCount != charactersPerTab
        || tab.EditorState.CaretOffset != expectedIndex * 97 % charactersPerTab
        || !tab.EditorState.IgnoredSpellingWords.Contains($"projectword{expectedIndex}"))
    {
        throw new InvalidOperationException("A reordered or transferred tab lost document or editor state.");
    }
}

Console.WriteLine($"Buffer: {buffer.GetType().Name}");
Console.WriteLine($"Document: {buffer.Length:N0} chars, {buffer.LineCount:N0} lines");
Console.WriteLine($"Construct: {construction.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Edit pairs: {incrementalEditCount:N0} in {edits.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Locations: {incrementalEditCount:N0} in {locations.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Tabs: {tabCount:N0} × {charactersPerTab:N0} chars, reorder/transfer in {tabStress.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Managed memory: {GC.GetTotalMemory(forceFullCollection: true) / 1024d / 1024d:N1} MiB");
Console.WriteLine($"Checksum: {checksum}");
Console.WriteLine("Verification: passed");
