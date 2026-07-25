using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Editor.AvaloniaEdit;

/// <summary>
/// Checks only the visible document region and retains results as the user
/// scrolls. This keeps continuous spelling independent of total file size.
/// </summary>
public sealed class SpellCheckEditorController : IDisposable
{
    private static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(280);

    private readonly TextEditor _editor;
    private readonly ISpellCheckService _service;
    private readonly SpellCheckBackgroundRenderer _renderer;
    private readonly Dictionary<TextDocument, HashSet<string>> _ignoredWords =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TextDocument, AutocorrectionChange> _autocorrections =
        new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? _checkCancellation;
    private CancellationTokenSource? _autocorrectCancellation;
    private TextDocument? _document;
    private TextSegmentCollection<SpellCheckTextSegment>? _issues;
    private bool _enabled;
    private bool _isApplyingAutocorrect;
    private AutocorrectMode _autocorrectMode;
    private int _generation;

    public SpellCheckEditorController(TextEditor editor, ISpellCheckService service)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _renderer = new SpellCheckBackgroundRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        _editor.TextArea.TextView.VisualLinesChanged += OnVisibleRegionChanged;
        _editor.TextArea.TextView.ScrollOffsetChanged += OnVisibleRegionChanged;
        _editor.DocumentChanged += OnDocumentChanged;
        AttachDocument(_editor.Document);
    }

    public bool IsAvailable => _service.IsAvailable;

    public string? ActiveLanguage => _service.ActiveLanguage;

    public IReadOnlyList<SpellCheckLanguage> AvailableLanguages => _service.AvailableLanguages;

    public event EventHandler<AutocorrectionChangedEventArgs>? AutocorrectionChanged;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled && _service.IsAvailable;
        if (!ReferenceEquals(_document, _editor.Document))
        {
            AttachDocument(_editor.Document);
        }

        if (!_enabled)
        {
            ClearAutocorrection(_document);
            ClearIssues();
            return;
        }

        ScheduleVisibleCheck(immediate: true);
    }

    public void SetAutocorrectMode(AutocorrectMode mode) =>
        _autocorrectMode = _enabled ? mode : AutocorrectMode.Off;

    public AutocorrectionChange? CurrentAutocorrection =>
        _document is not null && _autocorrections.TryGetValue(_document, out var correction)
            ? correction
            : null;

    public bool ChangeBack(AutocorrectionChange correction)
    {
        if (_document is null
            || !_autocorrections.TryGetValue(_document, out var current)
            || current != correction
            || correction.Start < 0
            || correction.Start + correction.Replacement.Length > _document.TextLength
            || !string.Equals(
                _document.GetText(correction.Start, correction.Replacement.Length),
                correction.Replacement,
                StringComparison.CurrentCulture))
        {
            return false;
        }

        var previousCaretOffset = _editor.CaretOffset;
        _isApplyingAutocorrect = true;
        try
        {
            using (_document.RunUpdate())
            {
                _document.Replace(
                    correction.Start,
                    correction.Replacement.Length,
                    correction.Original);
            }
        }
        finally
        {
            _isApplyingAutocorrect = false;
        }

        _editor.CaretOffset = Math.Clamp(
            previousCaretOffset >= correction.Start + correction.Replacement.Length
                ? previousCaretOffset + correction.Original.Length - correction.Replacement.Length
                : correction.Start + correction.Original.Length,
            0,
            _document.TextLength);
        ClearAutocorrection(_document);
        ScheduleVisibleCheck(immediate: true);
        return true;
    }

    public SpellingIssue? GetIssueAt(int offset)
    {
        if (!_enabled || _issues is null)
        {
            return null;
        }

        var segment = _issues.FindSegmentsContaining(Math.Clamp(offset, 0, _document?.TextLength ?? 0))
            .FirstOrDefault();
        return segment is null
            ? null
            : new SpellingIssue(segment.StartOffset, segment.Length, segment.Action, segment.Replacement);
    }

    public IReadOnlyList<string> Suggest(SpellingIssue issue)
    {
        if (!_enabled || _document is null || issue.Start < 0 || issue.End > _document.TextLength)
        {
            return [];
        }

        return _service.Suggest(_document.GetText(issue.Start, issue.Length));
    }

    public void Ignore(SpellingIssue issue)
    {
        if (_document is null || issue.Start < 0 || issue.End > _document.TextLength)
        {
            return;
        }

        if (!_ignoredWords.TryGetValue(_document, out var words))
        {
            words = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            _ignoredWords.Add(_document, words);
        }

        words.Add(_document.GetText(issue.Start, issue.Length));
        ScheduleVisibleCheck(immediate: true);
    }

    public void AddToDictionary(SpellingIssue issue)
    {
        if (_document is null || issue.Start < 0 || issue.End > _document.TextLength)
        {
            return;
        }

        _service.AddToDictionary(_document.GetText(issue.Start, issue.Length));
        ScheduleVisibleCheck(immediate: true);
    }

    public void Refresh()
    {
        if (!ReferenceEquals(_document, _editor.Document))
        {
            _generation++;
            CancelPendingCheck();
            AttachDocument(_editor.Document);
        }

        ScheduleVisibleCheck(immediate: true);
    }

    public void Dispose()
    {
        CancelPendingCheck();
        CancelPendingAutocorrect();
        _editor.DocumentChanged -= OnDocumentChanged;
        _editor.TextArea.TextView.VisualLinesChanged -= OnVisibleRegionChanged;
        _editor.TextArea.TextView.ScrollOffsetChanged -= OnVisibleRegionChanged;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_renderer);
        if (_document is not null)
        {
            _document.Changed -= OnTextDocumentChanged;
        }
        _issues?.Disconnect(_document!);
        _service.Dispose();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        _generation++;
        CancelPendingCheck();
        AttachDocument(e.NewDocument);
        ScheduleVisibleCheck(immediate: true);
    }

    private void AttachDocument(TextDocument? document)
    {
        if (_issues is not null && _document is not null)
        {
            _issues.Disconnect(_document);
        }
        if (_document is not null)
        {
            _document.Changed -= OnTextDocumentChanged;
        }

        _document = document;
        _issues = _document is null ? null : new TextSegmentCollection<SpellCheckTextSegment>(_document);
        if (_document is not null)
        {
            _document.Changed += OnTextDocumentChanged;
        }
        _renderer.SetIssues(_issues);
        RaiseAutocorrectionChanged();
    }

    private void OnTextDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (!_isApplyingAutocorrect)
        {
            ClearAutocorrection(_document);
            ScheduleAutocorrect(e);
        }

        ScheduleVisibleCheck(immediate: false);
    }

    private void ScheduleAutocorrect(DocumentChangeEventArgs change)
    {
        CancelPendingAutocorrect();
        if (!_enabled
            || _autocorrectMode == AutocorrectMode.Off
            || _document is null
            || change.RemovalLength != 0
            || change.InsertionLength != 1
            || change.Offset <= 0)
        {
            return;
        }

        var insertedCharacter = _document.GetCharAt(change.Offset);
        if (char.IsLetterOrDigit(insertedCharacter)
            || insertedCharacter is '\'' or '\u2019' or '_')
        {
            return;
        }

        var word = GetWordEndingAt(_document, change.Offset);
        if (word is null || word.Value.Length < 2)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _autocorrectCancellation = cancellation;
        var document = _document;
        var generation = _generation;
        _ = ApplyAutocorrectAsync(
            document,
            word.Value.Start,
            word.Value.Length,
            word.Value.Text,
            generation,
            _autocorrectMode,
            cancellation.Token);
    }

    private async Task ApplyAutocorrectAsync(
        TextDocument document,
        int start,
        int length,
        string original,
        int generation,
        AutocorrectMode mode,
        CancellationToken cancellationToken)
    {
        try
        {
            var replacement = await Task.Run(
                () => _service.GetAutomaticCorrection(original, mode),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(replacement)
                || string.Equals(replacement, original, StringComparison.CurrentCulture))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested
                    || generation != _generation
                    || !ReferenceEquals(document, _document)
                    || start < 0
                    || start + length > document.TextLength
                    || !string.Equals(
                        document.GetText(start, length),
                        original,
                        StringComparison.CurrentCulture))
                {
                    return;
                }

                _isApplyingAutocorrect = true;
                try
                {
                    using (document.RunUpdate())
                    {
                        document.Replace(start, length, replacement);
                    }
                }
                finally
                {
                    _isApplyingAutocorrect = false;
                }

                var correction = new AutocorrectionChange(start, original, replacement);
                _autocorrections[document] = correction;
                RaiseAutocorrectionChanged();
                ScheduleVisibleCheck(immediate: true);
            });
        }
        catch (OperationCanceledException)
        {
            // A subsequent edit supersedes a pending correction.
        }
    }

    private static (int Start, int Length, string Text)? GetWordEndingAt(
        TextDocument document,
        int wordEnd)
    {
        const int maximumWordWindow = 256;
        var windowStart = Math.Max(0, wordEnd - maximumWordWindow);
        var text = document.GetText(windowStart, wordEnd - windowStart);
        foreach (var (start, length) in SpellCheckWordTokenizer.EnumerateWords(text).Reverse())
        {
            if (start + length == text.Length)
            {
                return (windowStart + start, length, text.Substring(start, length));
            }
        }

        return null;
    }

    private void OnVisibleRegionChanged(object? sender, EventArgs e) =>
        ScheduleVisibleCheck(immediate: false);

    private void ScheduleVisibleCheck(bool immediate)
    {
        CancelPendingCheck();
        if (!_enabled || _document is null || !_service.IsAvailable)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _checkCancellation = cancellation;
        var generation = _generation;
        _ = CheckVisibleRegionAsync(generation, immediate, cancellation.Token);
    }

    private async Task CheckVisibleRegionAsync(
        int generation,
        bool immediate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(CheckDelay, cancellationToken);
            }

            var region = await Dispatcher.UIThread.InvokeAsync(CaptureVisibleRegion);
            if (region is null || region.Value.Text.Length == 0)
            {
                return;
            }

            var issues = await Task.Run(
                () => _service.Check(region.Value.Text),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested
                    || generation != _generation
                    || _issues is null
                    || _document is null)
                {
                    return;
                }

                ReplaceIssues(region.Value.Start, region.Value.Length, issues);
            });
        }
        catch (OperationCanceledException)
        {
            // Rapid typing and scrolling deliberately replace stale checks.
        }
    }

    private VisibleRegion? CaptureVisibleRegion()
    {
        var document = _editor.Document;
        if (document is null || document.TextLength == 0)
        {
            return null;
        }

        var textView = _editor.TextArea.TextView;
        if (!ReferenceEquals(_document, document))
        {
            AttachDocument(document);
        }

        var visualLines = textView.VisualLines;
        if (!textView.VisualLinesValid || visualLines.Count == 0)
        {
            const int fallbackLength = 64 * 1024;
            var length = Math.Min(document.TextLength, fallbackLength);
            return new VisibleRegion(0, length, document.GetText(0, length));
        }

        var firstDocumentLine = visualLines[0].FirstDocumentLine.PreviousLine
            ?? visualLines[0].FirstDocumentLine;
        var lastDocumentLine = visualLines[^1].LastDocumentLine.NextLine
            ?? visualLines[^1].LastDocumentLine;
        var start = firstDocumentLine.Offset;
        var end = lastDocumentLine.Offset + lastDocumentLine.TotalLength;
        return new VisibleRegion(start, end - start, document.GetText(start, end - start));
    }

    private void ReplaceIssues(
        int regionStart,
        int regionLength,
        IReadOnlyList<SpellingIssue> newIssues)
    {
        if (_issues is null)
        {
            return;
        }

        foreach (var existing in _issues.FindOverlappingSegments(regionStart, regionLength).ToArray())
        {
            _issues.Remove(existing);
        }

        foreach (var issue in newIssues)
        {
            if (issue.Length <= 0 || issue.Start < 0 || issue.End > regionLength)
            {
                continue;
            }

            var absoluteStart = regionStart + issue.Start;
            if (_document is not null
                && _ignoredWords.TryGetValue(_document, out var ignored)
                && ignored.Contains(_document.GetText(absoluteStart, issue.Length)))
            {
                continue;
            }

            _issues.Add(new SpellCheckTextSegment
            {
                StartOffset = absoluteStart,
                Length = issue.Length,
                Action = issue.Action,
                Replacement = issue.Replacement,
            });
        }

        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void ClearIssues()
    {
        CancelPendingCheck();
        _issues?.Clear();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void CancelPendingCheck()
    {
        _checkCancellation?.Cancel();
        _checkCancellation?.Dispose();
        _checkCancellation = null;
    }

    private void CancelPendingAutocorrect()
    {
        _autocorrectCancellation?.Cancel();
        _autocorrectCancellation?.Dispose();
        _autocorrectCancellation = null;
    }

    private void ClearAutocorrection(TextDocument? document)
    {
        if (document is null || !_autocorrections.Remove(document))
        {
            return;
        }

        if (ReferenceEquals(document, _document))
        {
            RaiseAutocorrectionChanged();
        }
    }

    private void RaiseAutocorrectionChanged()
    {
        var correction = CurrentAutocorrection;
        _renderer.SetAutocorrection(correction);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        AutocorrectionChanged?.Invoke(
            this,
            new AutocorrectionChangedEventArgs(correction));
    }

    private readonly record struct VisibleRegion(int Start, int Length, string Text);
}

public sealed record AutocorrectionChange(int Start, string Original, string Replacement);

public sealed class AutocorrectionChangedEventArgs(AutocorrectionChange? correction) : EventArgs
{
    public AutocorrectionChange? Correction { get; } = correction;
}

internal sealed class SpellCheckTextSegment : TextSegment
{
    public SpellingCorrectionAction Action { get; init; }

    public string? Replacement { get; init; }
}

internal sealed class SpellCheckBackgroundRenderer : IBackgroundRenderer
{
    private static readonly Pen ErrorPen = new(
        new SolidColorBrush(Color.Parse("#E81123")),
        1.15,
        lineCap: PenLineCap.Round,
        lineJoin: PenLineJoin.Round);
    private static readonly Pen AutocorrectionPen = new(
        new SolidColorBrush(Color.Parse("#0A84FF")),
        1.1,
        lineCap: PenLineCap.Round,
        lineJoin: PenLineJoin.Round);

    private TextSegmentCollection<SpellCheckTextSegment>? _issues;
    private AutocorrectionChange? _autocorrection;

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetIssues(TextSegmentCollection<SpellCheckTextSegment>? issues) => _issues = issues;

    public void SetAutocorrection(AutocorrectionChange? correction) =>
        _autocorrection = correction;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0)
        {
            return;
        }

        var start = visualLines[0].FirstDocumentLine.Offset;
        var lastLine = visualLines[^1].LastDocumentLine;
        var length = lastLine.Offset + lastLine.TotalLength - start;
        if (_issues is not null)
        {
            foreach (var issue in _issues.FindOverlappingSegments(start, length))
            {
                foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, issue, true))
                {
                    DrawWave(drawingContext, rectangle);
                }
            }
        }

        if (_autocorrection is { } correction
            && correction.Start < start + length
            && correction.Start + correction.Replacement.Length > start)
        {
            var segment = new SimpleSegment(correction.Start, correction.Replacement.Length);
            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, true))
            {
                var y = Math.Max(rectangle.Top + 1, rectangle.Bottom - 0.75);
                drawingContext.DrawLine(
                    AutocorrectionPen,
                    new Point(rectangle.Left, y),
                    new Point(rectangle.Right, y));
            }
        }
    }

    private static void DrawWave(DrawingContext drawingContext, Rect rectangle)
    {
        const double step = 2.4;
        const double amplitude = 1.25;
        var y = Math.Max(rectangle.Top + 1, rectangle.Bottom - 1.3);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(rectangle.Left, y), false);
            var x = rectangle.Left + step;
            var high = true;
            while (x < rectangle.Right)
            {
                context.LineTo(new Point(x, y + (high ? -amplitude : amplitude)));
                x += step;
                high = !high;
            }

            context.LineTo(new Point(rectangle.Right, y));
        }

        drawingContext.DrawGeometry(null, ErrorPen, geometry);
    }
}
