# Redmond Notepad workspace

The `redmond` directory contains the cross-platform application projects while
keeping their references to the Redmond Project's sibling shared libraries
portable and free of machine-specific paths.

## Projects

- `Redmond.Notepad.Avalonia` owns the application surface and platform shell.
- `Redmond.Notepad.Core` owns documents, file lifecycle, shortcuts, and other
  UI-independent behavior.
- `Redmond.Notepad.Editor.AvaloniaEdit` adapts the scalable editor document to
  the core text-buffer contracts.
- `Redmond.Notepad.Verification` exercises portable behavior and file flows.
- `Redmond.Notepad.Performance` guards large-document behavior.

## Build and verify

```sh
dotnet build Redmond.Notepad.slnx
dotnet run --project Redmond.Notepad.Verification/Redmond.Notepad.Verification.csproj
dotnet run --project Redmond.Notepad.Performance/Redmond.Notepad.Performance.csproj
```

The solution intentionally references the sibling `Redmond.Avalonia.Controls`,
`Redmond.Avalonia.Windowing`, and `Redmond.Shortcuts` projects through relative
paths. Keep this repository beside those shared Redmond Project directories.
