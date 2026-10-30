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

## Recent files

Successful file opens are persisted newest-first in the application settings,
de-duplicated, and capped at ten entries. The File menu shows each file name
with its complete path in a tooltip, removes missing entries when selected, and
provides a separated Clear list command. The Recent Files settings switch can
hide the menu and pause collection without discarding the saved list.

## Keyboard shortcuts

`NotepadShortcutCatalog` in `Redmond.Notepad.Core` defines every shortcut once,
with the Windows Notepad gesture written platform-neutrally (`Primary+N`,
`Primary+Shift+S`, `F3`). There are no per-platform gesture tables: every
platform uses the same keys, and only the `Primary` modifier differs. It is
Ctrl on Windows and Linux and Command on macOS; `Redmond.Shortcuts` in
`redmond-commons` resolves it. So Find next is F3, Replace is ⌘H and Go to is
⌘G on macOS, just like F3, Ctrl+H and Ctrl+G on Windows.

Menus, tooltips and the native macOS menu bar take their gesture text from the
shared `ShortcutService`, which labels keys for the platform and the keyboard
layout's language (`Strg+Umschalt+S` on German Windows, `⇧⌘S` on macOS). The
Fluent menus show it through `MenuItemShortcut.Text`; native menu items get
the resolved gesture and AppKit draws it. Never write gesture text by hand.

On macOS, Settings → Appearance → *Shortcut modifier key* chooses what
`Primary` means: *Command (⌘)*, the default, or *Control (⌃)*. With Control,
every shortcut is exactly its Windows gesture. The choice applies live to every
open window and is stored in `macos-shortcut-modifier.txt` in the settings
directory. The card is hidden on Windows and Linux, where the preference has no
effect. System items in the macOS application menu, such as Settings… (⌘,),
are not Notepad shortcuts and keep Command.

## Build and verify

```sh
dotnet build Redmond.Notepad.slnx
dotnet run --project Redmond.Notepad.Verification/Redmond.Notepad.Verification.csproj
dotnet run --project Redmond.Notepad.Performance/Redmond.Notepad.Performance.csproj
```

The solution references `Redmond.Avalonia.Controls`,
`Redmond.Avalonia.Windowing`, and `Redmond.Shortcuts` from the
[`port-redmond-commons`](https://github.com/Dynamotivation/port-redmond-commons)
Git submodule through relative project paths.

Clone `port-redmond-notepad` with its pinned Commons revision:

```sh
git clone --recurse-submodules https://github.com/Dynamotivation/port-redmond-notepad.git
```
