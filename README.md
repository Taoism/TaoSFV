# TaoSFV

[![MIT License](http://img.shields.io/:license-mit-blue.svg)](https://github.com/judwhite/ilSFV/blob/master/LICENSE) 

SFV, MD5 and SHA-1 file verification utility.

TaoSFV is a tribute to ilSFV and rekSFV. It continues [ilSFV](https://github.com/judwhite/ilSFV) by Jud White (itself a tribute to hkSFV), ported to .NET 10 with a dark mode.

- Multi-language support (currently English, German, Chinese, Russian)
- Supports SFV, MD5, and SHA-1
- Batch file verification
- Create and verify checksums for files in subdirectories
- Optional cache for quickly re-verifying files
- Ability to run in the background
- Reads BSD style checksum files and other less-known formats
- Option to find renamed files based on checksum files
- Dark mode (Preferences → General → Theme: System default / Light / Dark)

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the app itself needs the .NET 10 Desktop Runtime).

```
dotnet build TaoSFV\TaoSFV.csproj -c Release
dotnet publish TaoSFV\TaoSFV.csproj -c Release -o publish
```

### Changes from ilSFV 1.11 (.NET Framework 3.5)

- Ported to .NET 10 WinForms (SDK-style project, system-aware high DPI).
- Dark mode via WinForms' built-in color modes, with fixes for the group headers, status bar and tab pages.
- Settings and the checksum cache moved from SQL Server Compact (`*.sdf`) to SQLite (`settings.db`, `cache.db` in `%LOCALAPPDATA%\TaoSFV`). Old `.sdf` settings are not migrated; defaults are used on first run.
- Stopping a check no longer relies on `Thread.Abort` (unsupported on .NET); MD5/SHA-1 hashing is now thread-safe.
- Links that pointed at the defunct cdtag.com (web, update check, bug reports) now go to the original ilSFV GitHub repository.

## Project Status

Maintained. Issues and Pull Requests will be responded to.

## License

TaoSFV, like ilSFV, is under the MIT license. See the [LICENSE](https://github.com/judwhite/ilSFV/blob/master/LICENSE) file for details.
