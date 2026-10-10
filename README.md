# Arama

An instant file-name search tool for Windows, similar to [Everything](https://www.voidtools.com/).

It reads the NTFS Master File Table (MFT) directly to index millions of files in seconds, then watches the USN Journal so that files being created, deleted, renamed or modified show up in the results within about a second. The user interface is in Turkish ("arama" means "search").

## Features

- Results as you type (name search over ~2.3 million files takes 10–20 ms)
- Case-insensitive and Turkish-character-insensitive matching (`ogrenci` finds `Öğrenci.docx`)
- AND / OR / NOT, wildcards, path and regex search
- Filters: All, Files, Folders, Documents, Pictures, Audio, Video, Archives, Programs
- Sort by name, path, size or date modified
- System tray icon, global `Ctrl+Alt+Space` hotkey, "Start with Windows"
- Near-zero CPU usage while idle

## Requirements

- Windows 10 / 11 (64-bit)
- .NET Framework 4.8 (built into Windows; nothing to install)
- Administrator rights, needed to read the disk's file table directly
- NTFS-formatted fixed drives

## Download

Get `Arama.exe` from the [Releases](https://github.com/mahmutka/arama/releases) page. No installation is needed. The executable is not code-signed, so Windows SmartScreen may show a warning on first run ("More info" → "Run anyway").

## Building

```bat
build.bat
```

Uses the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and produces:

- `bin\Arama.exe` — the application (asks for administrator rights on start)
- `bin\Tests.exe` — the test suite

## Tests

```bat
bin\Tests.exe              :: all tests
bin\Tests.exe Query        :: tests whose name contains "Query"
bin\Tests.exe --out result.txt
```

Tests that read a real disk run only when started as administrator; otherwise they are reported as skipped ("ATLANDI").

## Search syntax

| Input | Meaning |
|---|---|
| `report 2024` | space = AND: names containing both |
| `jpg\|png` | OR |
| `!tmp` | NOT |
| `"new folder"` | phrase containing a space |
| `*.pdf`, `report??.docx` | wildcards, matched against the whole name |
| `D:\projects\ .cs` | a term containing `\` is matched against the full path |
| `regex:^IMG_\d+` | regular expression (against the name; against the full path if the pattern contains `\\`) |

## Keyboard shortcuts

| Key | Action |
|---|---|
| Enter / double-click | Open the file |
| Ctrl+Enter | Show the file in its folder |
| Ctrl+C | Copy the full paths of the selected files |
| Esc | Hide the window to the tray |
| Ctrl+Alt+Space | Bring up the window from anywhere |

Because the program runs as administrator, files are opened through Explorer so that the launched programs do not inherit administrator rights.

## Settings and log

- `%APPDATA%\Arama\ayarlar.ini` — window position/size and column widths
- `%APPDATA%\Arama\hata.log` — unexpected errors

## Known limitations

- Only NTFS fixed drives are indexed (FAT32/exFAT, network drives and drives attached after start-up are not).
- File contents are not searched; only names and paths.
- Files with multiple names (hard links) are shown in one location only.
- Because the program runs as administrator, Windows may block drag-and-drop from the result list into a non-elevated Explorer window.
