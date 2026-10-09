<h1 align="center">
  <br>📦 Winpress<br>
</h1>

<p align="center">
  <b>The archive manager Windows should have shipped with.</b><br>
  Browse archives like folders. Preview without extracting. Pull out exactly what you need.
</p>

<p align="center">
  <a href="https://github.com/YOUR_GITHUB_USERNAME/Winpress/releases/latest">
    <img alt="Latest Release" src="https://img.shields.io/github/v/release/YOUR_GITHUB_USERNAME/Winpress?style=flat-square&color=2B7FD4&label=release"/>
  </a>
  <a href="https://github.com/YOUR_GITHUB_USERNAME/Winpress/releases">
    <img alt="Downloads" src="https://img.shields.io/github/downloads/YOUR_GITHUB_USERNAME/Winpress/total?style=flat-square&color=2B7FD4"/>
  </a>
  <img alt="Platform" src="https://img.shields.io/badge/Windows-10%2F11-2B7FD4?style=flat-square"/>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-2B7FD4?style=flat-square"/>
  <a href="LICENSE">
    <img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-2B7FD4?style=flat-square"/>
  </a>
</p>

---

**Winpress** is a free, open source archive manager built natively for Windows 10 and 11. Open 40+ archive formats, navigate them like folders, drill into nested archives, and extract only what you need — all from a single portable `.exe` with no installer.

## Features

- **Browse without extracting** — Navigate inside archives as if they were folders in File Explorer.
- **Nested archives** — Archives inside archives open seamlessly; navigate back with one click.
- **Selective extraction** — Multi-select files and folders, then extract only those.
- **Over-the-air updates** — Winpress checks for new releases on startup and updates itself in one click.
- **Explorer context menus** — Right-click any archive to *Open with Winpress*, *Extract Here*, or *Extract to Folder…*
- **Drag & drop** — Drop an archive onto the window to open it instantly.
- **Encrypted archives** — Password prompt appears only when needed.
- **Magic-byte format detection** — Correctly identifies formats even when the extension is wrong or missing.
- **Sortable columns** — Sort by name, type, size, packed size, ratio, or date.
- **Keyboard-first** — Full keyboard navigation: Enter, Backspace, Alt+←, Ctrl+O, Ctrl+E, Ctrl+A.
- **Zero dependencies** — One self-contained `.exe`, no runtime installation required.

## Supported Formats

| Category | Formats |
|---|---|
| **Archives** | ZIP · ZIPX · 7z · RAR (v4 + v5) · TAR · CAB · CPIO · AR · DEB · RPM · ARJ |
| **Tarballs** | .tar.gz / .tgz · .tar.bz2 / .tbz2 · .tar.xz / .txz · .tar.lz4 · .tar.zst |
| **Compression** | GZip · BZip2 · XZ · LZ4 · Zstandard · LZMA |
| **Disk images** | ISO · WIM · IMG |
| **Packages** | DEB · RPM · CAB · MSI |

## Installation

No installer. [Download the latest release](https://github.com/YOUR_GITHUB_USERNAME/Winpress/releases/latest), unzip, and run `Winpress.exe` from anywhere.

| File | When to use |
|---|---|
| `Winpress.exe` | Standalone — just run it, everything included |
| `Winpress-vX.X.X-win-x64-portable.zip` | Same binary in a zip (for sharing or scripted deployment) |
| `Winpress-vX.X.X-win-x64-requires-dotnet10.zip` | Smaller download; requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

**System requirements:** Windows 10 (1809) or later · 64-bit · ~30 MB disk

### Optional: Windows Explorer context menus

Launch Winpress → **Tools → Register Explorer Context Menus**.
This adds *Open with Winpress*, *Extract Here*, and *Extract to Folder…* to the right-click menu for all supported archive types. Writes only to `HKEY_CURRENT_USER` — no administrator prompt.

---

## Over-the-Air Updates

Winpress updates itself. On each launch it checks GitHub for a newer release in the background (no startup delay). When one is found:

1. A non-intrusive dialog shows the version number and changelog.
2. Click **Download Update** — Winpress fetches the new binary.
3. Click **Apply & Restart** — Winpress replaces itself and relaunches.

No installer, no UAC prompt (as long as Winpress.exe lives in a folder you own, such as Desktop or Downloads). If you placed it in `Program Files`, move it to a user-owned folder first.

---

## Building from Source

**Prerequisites:** Windows 10/11 · [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) · Git

```bash
# Clone
git clone https://github.com/YOUR_GITHUB_USERNAME/Winpress.git
cd Winpress

# Run in development
dotnet run --project Winpress/Winpress.csproj
```

Or open `Winpress.sln` in Visual Studio 2022 / JetBrains Rider and press **F5**.

### Build a standalone .exe

```powershell
dotnet publish Winpress/Winpress.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  --output ./dist
# Output: ./dist/Winpress.exe
```

---

## Release Process (maintainers)

Releases are fully automated. Push a version tag and GitHub Actions does the rest:

```bash
git tag v1.2.0
git push origin v1.2.0
```

The workflow will:
- Build `Winpress.exe` (self-contained, single file)
- Build a smaller framework-dependent variant
- Package both as `.zip` archives
- Create a GitHub Release with all assets attached
- Auto-generate a changelog from commit messages

The OTA updater in all installed copies will detect the new release within 3 seconds of the user's next launch and offer the upgrade.

---

## Project Structure

```
Winpress/
├── Winpress.sln
├── Winpress/
│   ├── Winpress.csproj                 # .NET 10 WPF project
│   ├── App.xaml / App.xaml.cs          # Entry point + CLI args + OTA check on startup
│   ├── MainWindow.xaml / .cs           # Archive browser window
│   ├── Models/
│   │   ├── ArchiveEntry.cs             # File/folder entry inside an archive
│   │   └── BreadcrumbItem.cs           # Breadcrumb navigation model
│   ├── ViewModels/
│   │   ├── MainViewModel.cs            # Navigation, extraction, sort, search
│   │   └── UpdateViewModel.cs          # Download progress + apply logic
│   ├── Services/
│   │   ├── ArchiveService.cs           # Opens archives → flat entry list
│   │   ├── ExtractionService.cs        # Extracts all / selected / single entry
│   │   ├── ShellIntegrationService.cs  # Explorer context menus (HKCU registry)
│   │   └── UpdateService.cs            # GitHub API check + download + self-replace
│   ├── Helpers/
│   │   ├── FormatDetector.cs           # Magic-byte archive type detection
│   │   └── FileSizeHelper.cs           # Human-readable byte formatting
│   ├── Converters/
│   │   └── ValueConverters.cs          # WPF value converters
│   ├── Views/
│   │   ├── PasswordDialog.xaml / .cs   # Password prompt for encrypted archives
│   │   ├── ProgressWindow.xaml / .cs   # Extraction progress + cancel
│   │   └── UpdateDialog.xaml / .cs     # OTA update dialog
│   └── Resources/
│       └── Styles.xaml                 # Dark theme, button and list styles
└── .github/
    └── workflows/
        ├── ci.yml                      # Build check on every PR / push to main
        └── release.yml                 # Build + publish assets on version tag push
```

---

## Contributing

1. Fork the repository and create a feature branch.
2. Make your changes following the existing MVVM pattern.
3. Run locally to confirm the feature or fix works.
4. Open a pull request with a clear description.

For larger changes, open an issue first to discuss the approach.

---

## Acknowledgements

Winpress draws design inspiration from **[MacPacker](https://macpacker.app)** by [Saren Wohlfahrt](https://sarensw.de) — a beautifully crafted archive manager for macOS. If you use a Mac, check it out.

---

## License

Winpress is licensed under the [GNU General Public License v3.0](LICENSE).
You are free to use, modify, and distribute it under the same terms.
