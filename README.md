# TaoSFV

[![MIT License](http://img.shields.io/:license-mit-blue.svg)](https://github.com/judwhite/ilSFV/blob/master/LICENSE) 

SFV, MD5, SHA-1, SHA-256 and SHA-512 file verification utility for Windows, with a command-line version.

TaoSFV is a tribute to ilSFV and rekSFV. It continues [ilSFV](https://github.com/judwhite/ilSFV) by Jud White (itself a tribute to hkSFV), ported to .NET 10 with a dark mode.

- Multi-language support (currently English, German, Chinese, Russian)
- Supports SFV, MD5, SHA-1, SHA-256 and SHA-512 (md5sum/sha256sum, BSD `--tag` and hkSFV formats)
- Batch file verification
- Create and verify checksums for files in subdirectories
- Optional cache for quickly re-verifying files
- Ability to run in the background
- Reads BSD style checksum files and other less-known formats
- Option to find renamed files based on checksum files
- Dark mode (Preferences → General → Theme: System default / Light / Dark)
- Checks several files at once on SSDs (one at a time on hard drives and network shares)
- Verify a whole folder tree: File → Verify Folder, drag a folder in, or right-click a folder → TaoSFV → Verify checksum files
- `taosfv-cli` for scripts and scheduled checks

## Command line

`taosfv-cli.exe` sits next to `TaoSFV.exe`. Output and exit codes follow `md5sum -c`.

```
taosfv-cli verify D:\Music\album.sfv
taosfv-cli verify D:\Music --quiet        # every checksum file under D:\Music
taosfv-cli create sha256 D:\Music\album\album.sha256 D:\Music\album
```

- Exit code 0 when every file is OK, 1 when any is bad, missing or unreadable, 2 for bad arguments or an unreadable checksum file, 130 on Ctrl+C.
- `--quiet` prints only problems and the summary; `--status` prints nothing.
- `--threads N` overrides how many files are read at once.
- Verifying never renames, deletes or creates files and doesn't use the result cache.
- `create` writes md5sum-style files with `/` separators and LF line endings, so `md5sum -c` and `sha256sum -c` read them.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the app itself needs the .NET 10 Desktop Runtime).

```
dotnet build TaoSFV\TaoSFV.csproj -c Release
dotnet publish TaoSFV\TaoSFV.csproj -c Release -o publish
dotnet publish TaoSFV.Cli\TaoSFV.Cli.csproj -c Release -o publish
dotnet test TaoSFV.Tests
```

`TaoSFVLanguage` regenerates `TaoSFV/Localization/*.Generated.cs` after adding strings to `TaoSFV/languages/english.txt`; other language files fall back to English for missing keys.

### Changes from ilSFV 1.11 (.NET Framework 3.5)

- Ported to .NET 10 WinForms (SDK-style project, system-aware high DPI).
- Dark mode via WinForms' built-in color modes, with fixes for the group headers, status bar and tab pages.
- Settings and the checksum cache moved from SQL Server Compact (`*.sdf`) to SQLite (`settings.db`, `cache.db` in `%LOCALAPPDATA%\TaoSFV`). Old `.sdf` settings are not migrated; defaults are used on first run.
- Stopping a check no longer relies on `Thread.Abort` (unsupported on .NET); MD5/SHA-1 hashing is now thread-safe.
- Checksum files: UTF-8 is detected (with or without a BOM, falling back to Windows-1252), and files are written as UTF-8 when a name doesn't fit Windows-1252, so non-Latin file names work.
- md5sum/sha1sum text-mode lines (`hash  name`, the GNU default) are now read correctly; previously every file showed as missing.
- CRC32 uses the vectorized `System.IO.Hashing.Crc32` with 1 MB reads (about 4x faster); MD5/SHA-1 also read in 1 MB blocks.
- File associations and the Explorer "TaoSFV" menu are registered per user (HKCU), with no administrator prompt; Tools > Unregister File Types removes them.
- Checking runs on background threads, several files at once on SSDs; Pause now really pauses reading.
- SHA-256 and SHA-512 files (`.sha256`, `.sha512`), including in the cache, Explorer menus and File → New.
- Fixed an intermittent startup error ("A generic error occurred in GDI+") that left the main window invisible.
- Links that pointed at the defunct cdtag.com (web, update check, bug reports) now go to the original ilSFV GitHub repository.

## Project Status

Maintained. Issues and Pull Requests will be responded to.

## License

TaoSFV, like ilSFV, is under the MIT license. See the [LICENSE](https://github.com/judwhite/ilSFV/blob/master/LICENSE) file for details.
