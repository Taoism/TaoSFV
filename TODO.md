# TaoSFV to-do

Roughly in priority order within each section. Check items off (`[x]`) as they're done.

## Bugs and rough edges

- [ ] **Friendly message for a bad checksum file.** Opening a single malformed `.sfv`/`.md5` shows the crash dialog
      ("An error has occurred", stack trace, Report bug). It should be a plain message naming the file and line.
      Folder checks already skip such files and list them.
- [ ] **Load folders in the background.** Verifying a big tree (e.g. 1,831 sets) scans folders and reads every
      checksum file on the UI thread, pumping events (`Application.DoEvents`, 12 places), so the window stutters
      before checking starts.
- [ ] **Dead update check.** Preferences still offers "Check for updates every N days" and the code calls the
      defunct cdtag.com. Either remove it or point it at GitHub Releases (needs the repo below).
- [ ] **Recycle Bin with long paths.** Deleting failed files via the Recycle Bin uses `SHFileOperation`, which can't
      take paths of 260+ characters; such files are left in place. Switch to `IFileOperation`.
- [ ] **Unreadable files inflate the speed figure.** Their full size is counted as processed, so MB/s and the
      "MB checked" statistic are overstated when files can't be read.
- [ ] **Re-test Remove Duplicates and Truncate File Names.** Not exercised since the .NET 10 port; Remove Duplicates
      also still builds real ListView rows (slow for big lists on this machine, see the virtual list change).

## Features

- [ ] **Scheduled checks.** "Schedule weekly check..." that sets up Task Scheduler with `taosfv-cli verify <folder>`
      and only notifies when something fails.
- [ ] **Save results.** Export a run's results as text, CSV or HTML.
- [ ] **Parallel reads setting.** Preferences option to override the automatic choice (4 on SSDs, 1 on hard
      drives/shares), e.g. for RAID arrays or slow USB sticks. The CLI already has `--threads`.
- [ ] **Faster hash types.** XXH3 and/or BLAKE3 as options for large libraries (less widely supported elsewhere).
- [ ] **CLI cache option.** `taosfv-cli verify --use-cache` to reuse the GUI's checksum cache (it never uses it now).
- [ ] **Where Create puts the file for a folder.** Right-click a folder > Create SFV writes `<parent>\<parent>.sfv`
      (ilSFV's behaviour). Consider writing `<folder>\<folder>.sfv` instead, or asking.
- [ ] **PAR2 repair (large).** Rebuild damaged files from recovery data, QuickPar-style, not just detect them.

## Polish

- [ ] **Translations.** About 25 strings added in 2.0 exist only in English (theme, Verify Folder, SHA-256/512,
      Unregister, Unreadable, import, skipped files); the other five languages fall back to English. Also drop
      "Try running as Administrator" from their registration error text.
- [ ] **Switch theme without restarting.**
- [ ] **Sharper text on high-DPI screens.** Windows are laid out in fixed pixels and run system-DPI-aware.
- [ ] **Comments template after importing.** Offer to update a header that still says "ilSFV" or links to
      cdtag.com. (This machine's TaoSFV template says "Using ilSFV" because its database predates the rename.)
- [ ] **Accessibility of the file list.** Set headers are ordinary rows in the virtual list; give them a distinct
      name/role for screen readers.

## Code health

- [ ] **Remove dead code:** `Tools/Http.cs`, `Tools/PostData.cs`, the registry reflection helpers in `Program.cs`
      (rely on .NET Framework internals), the old update-check code.
- [ ] **Cache lookups.** `GetCache` matches `FileName like @Directory || '%'`; SQLite's case-insensitive LIKE can't
      use the index, so each set scans the whole table (35,000 rows with imported ilSFV settings).
- [ ] **CI.** GitHub Actions: build, run the 103 tests, publish artifacts.

## Release

- [ ] **TaoSFV GitHub repo.** Create it, push the `taosfv` branch, and repoint the Web button, bug reports and
      update link (they still go to judwhite/ilSFV).
- [ ] **Packaging.** Self-contained single-file build (no .NET install needed), code signing (avoids SmartScreen
      warnings), a modernized installer, and a winget listing.
- [ ] **ARM64.** Builds are x64 only; the ilSFV import's SQL Server Compact engine only exists for x64/x86.
