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

## Native printing

Printing keeps document semantics in `Redmond.Notepad.Core` and delegates the
actual user interface and print job to each desktop operating system:

- macOS uses AppKit's `NSPageLayout` and `NSPrintOperation` panels. A native
  accessory exposes the Notepad header, footer, and margin values.
- Windows uses the Win32 common `PageSetupDlg` and `PrintDlgEx` surfaces. The
  page-setup hook adds Notepad's header and footer fields to the shared dialog.
- Other platforms currently report printing as unavailable rather than showing
  a misleading cross-platform imitation of a system printer dialog.

The portable header/footer formatter supports the Windows Notepad field syntax
for file name, current and total page numbers, date, time, escaping, and
left/centre/right alignment.

## Build and verify

```sh
dotnet build Redmond.Notepad.slnx
dotnet run --project Redmond.Notepad.Verification/Redmond.Notepad.Verification.csproj
dotnet run --project Redmond.Notepad.Performance/Redmond.Notepad.Performance.csproj
```

The solution intentionally references the sibling `Redmond.Avalonia.Controls`,
`Redmond.Avalonia.Windowing`, and `Redmond.Shortcuts` projects through relative
paths. Keep this repository beside those shared Redmond Project directories.
