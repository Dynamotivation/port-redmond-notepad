using System.Diagnostics;
using System.Text;
using Redmond.Notepad.Editor.AvaloniaEdit;

const int targetCharacters = 10_000_000;
const int incrementalEditCount = 10_000;
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

Console.WriteLine($"Buffer: {buffer.GetType().Name}");
Console.WriteLine($"Document: {buffer.Length:N0} chars, {buffer.LineCount:N0} lines");
Console.WriteLine($"Construct: {construction.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Edit pairs: {incrementalEditCount:N0} in {edits.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Locations: {incrementalEditCount:N0} in {locations.Elapsed.TotalMilliseconds:N1} ms");
Console.WriteLine($"Checksum: {checksum}");
Console.WriteLine("Verification: passed");
