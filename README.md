<h1 align="center">
  <br>📦 Winpress<br>
</h1>

<p align="center">
  <b>The archive manager Windows should have shipped with.</b><br>
  Browse · Extract · <strong>Compress</strong> · Encrypt — all from one portable <code>.exe</code>
</p>

<p align="center">
  <a href="https://github.com/snipeem/Winpress/releases/latest">
    <img alt="Latest Release"
         src="https://img.shields.io/github/v/release/snipeem/Winpress?style=flat-square&color=2B7FD4&label=release"/>
  </a>
  <a href="https://github.com/snipeem/Winpress/releases">
    <img alt="Total Downloads"
         src="https://img.shields.io/github/downloads/snipeem/Winpress/total?style=flat-square&color=22c55e&label=downloads"/>
  </a>
  <a href="https://github.com/snipeem/Winpress/actions/workflows/ci.yml">
    <img alt="CI"
         src="https://img.shields.io/github/actions/workflow/status/snipeem/Winpress/ci.yml?style=flat-square&label=build&color=22c55e"/>
  </a>
  <img alt="Platform"
       src="https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows&logoColor=white"/>
  <img alt=".NET 10"
       src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white"/>
  <img alt="AES-256"
       src="https://img.shields.io/badge/encryption-AES--256-7c3aed?style=flat-square"/>
  <a href="LICENSE">
    <img alt="License"
         src="https://img.shields.io/badge/license-GPL--3.0-d97706?style=flat-square"/>
  </a>
</p>

---

**Winpress** is a free, open-source archive manager built natively for Windows 10 and 11.
Open and browse 40+ archive formats like folders, extract exactly what you need, and now
**create and encrypt archives** in any format — all from a single portable `.exe` with
no installer, no runtime, no admin rights.

<details open>
<summary><b>✨ What's new in v1.2.1</b></summary>

- **UI Polish** — Fixed ComboBox dropdown styling (white text on dark background) and CTA button heights across all dialogs
- **Improved Compress Flow** — Create Archive dialog now includes a built-in source file/folder picker
- **Context Menu Refresh** — Explorer context menus now update instantly via `SHChangeNotify` without requiring an Explorer restart
- **Status Feedback** — "Register Explorer Context Menu" now shows a confirmation message in the status bar
</details>

<details>
<summary><b>📦 What's new in v1.2.0</b></summary>

- **Compression** — Create ZIP, 7z, TAR.GZ, TAR.BZ2, TAR.XZ, TAR.ZST, TAR, GZip, BZip2, and Zstandard archives
- **AES-256 encryption** — Password-protect ZIP and 7z archives with WinZip-standard AES-256
- **Context menu compression** — Right-click any file, folder, or archive → Winpress → *Compress to ZIP / 7z / TAR.GZ / ...*
- **One-click compress** — Right-click → *Compress to ZIP* places the archive next to the source instantly, no dialog
- **Password strength meter** — Live red/amber/green bar plus eye-icon reveal toggle on every password field
- **Session password memory** — Enter the password once, unlock every other encrypted archive in the same session without re-typing
- **ARM64 builds** — Native executables for Surface and Snapdragon X laptops
- **ReadyToRun** — ~40 % faster cold launch thanks to pre-JIT native code baked into the executable
</details>

---

## Features

<details open>
<summary><b>📂 Browse &amp; Extract</b></summary>

- **Browse without extracting** — Navigate inside archives as if they were File Explorer folders
- **Nested archives** — Archives inside archives open seamlessly; navigate back with one click
- **Selective extraction** — Multi-select files and folders, then extract only those
- **Drag-and-drop extraction** — Drag files out of Winpress directly into Explorer or any app
- **Marquee (rubber-band) selection** — Click empty space and drag to select a rectangle of files, just like Windows Explorer
- **Encrypted archive support** — Password prompt appears only when needed; session memory avoids repeated prompts
- **Magic-byte format detection** — Correctly identifies formats even when the extension is wrong or missing
- **Sortable columns** — Sort by name, type, size, packed size, ratio, or date
- **Keyboard-first** — Full keyboard navigation: Enter, Backspace, Alt+←, Ctrl+O, Ctrl+E, Ctrl+A
</details>

<details open>
<summary><b>🗜️ Compress &amp; Encrypt</b></summary>

- **10 output formats** — ZIP · 7z · TAR.GZ · TAR.BZ2 · TAR.XZ · TAR.ZST · TAR · GZip · BZip2 · Zstandard
- **AES-256 encryption** — Password-protect ZIP (WinZip AES standard) and 7z archives
- **Password strength meter** — Live red/amber/green bar driven by a pure-C# entropy estimate; no telemetry, no libraries
- **Show/hide password toggle** — Segoe MDL2 eye icon overlaid on the field, toggles plain-text view
- **AES-256 badge** — Small blue 🔒 label appears when a non-empty password is typed for ZIP or 7z
- **Three compression levels** — Fast / Default / Best to balance speed vs. file size
- **Silent one-click compress** — Right-click → *Compress to ZIP* (or 7z / TAR.GZ) places the archive next to the source, no dialog
- **Full dialog** — *File → New Archive…* or *Add to Archive…* for full control: format, level, output path, password
</details>

<details open>
<summary><b>🔗 Windows Integration</b></summary>

- **Explorer context menus** — *Open with Winpress*, *Extract Here*, *Extract to Folder…*, and all compress verbs on every file, folder, and archive type
- **Set as default app** — Register Winpress as your default archive handler through Windows Settings
- **Over-the-air updates** — Checks GitHub on startup; one click to download and apply
- **Zero dependencies** — One self-contained `.exe`, no runtime installation, no admin prompt
</details>

---

## Supported Formats

<details>
<summary><b>Click to expand full format list</b></summary>

| Category | Formats |
|---|---|
| **Create (write)** | ZIP · 7z · TAR · TAR.GZ · TAR.BZ2 · TAR.XZ · TAR.ZST · GZip · BZip2 · Zstandard |
| **Open (read)** | ZIP · ZIPX · 7z · RAR (v4 + v5) · TAR · CAB · CPIO · AR · DEB · RPM · ARJ |
| **Tarballs** | .tar.gz / .tgz · .tar.bz2 / .tbz2 · .tar.xz / .txz · .tar.lz4 · .tar.zst |
| **Compression** | GZip · BZip2 · XZ · LZ4 · Zstandard · LZMA |
| **Disk images** | ISO · WIM · IMG |
| **Packages** | DEB · RPM · CAB · MSI |

</details>

---

## Installation

No installer. [**Download the latest release →**](https://github.com/snipeem/Winpress/releases/latest)

| File | Description |
|---|---|
| `Winpress.exe` | ✅ **Recommended** — x64, self-contained, just run it |
| `Winpress-vX.X.X-win-x64-portable.zip` | Same x64 binary in a zip |
| `Winpress-vX.X.X-win-arm64-portable.zip` | 🆕 ARM64 — Surface / Snapdragon X laptops |
| `Winpress-vX.X.X-win-x64-requires-dotnet10.zip` | Smaller; requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

**System requirements:** Windows 10 (1809) or later · x64 or ARM64 · ~30 MB disk

---

## Usage

### Extracting archives

1. Double-click any archive to open it in Winpress, **or** right-click → **Winpress → Open with Winpress**
2. Navigate folders inside the archive with double-click or keyboard
3. Select files → **Extract Selected** (Ctrl+Shift+E), or **Extract All** (Ctrl+E)
4. Drag files directly from the Winpress window into Explorer

### Creating archives

**Via dialog** — `File → New Archive…` (Ctrl+N) or right-click → **Winpress → Add to Archive…**
> Choose format, compression level, output path, and optional AES-256 password.

**One-click from context menu** — Right-click any file or folder → **Winpress → Compress to ZIP** (or 7z / TAR.GZ / TAR+Zstandard)
> The archive is created next to the source instantly with no dialog.

### Password encryption

When creating a ZIP or 7z archive, enter a password in the *Create Archive* dialog. Leave blank for no encryption.

- **ZIP** uses WinZip AES-256 — compatible with 7-Zip, WinRAR, and all modern archive tools
- **7z** uses AES-256 with header encryption — the file list is also hidden from anyone without the password
- 🔒 **AES-256 badge** appears next to the password field when you type one
- **Password strength meter** (red → amber → green) updates live as you type
- **Eye icon** toggles plain-text visibility of the password field

When opening an encrypted archive, Winpress prompts for the password once and remembers it for the rest of the session — no re-typing required.

### Drag-and-drop extraction

1. Select one or more files/folders inside an open archive (Ctrl / Shift / marquee-drag)
2. Click and drag them out of the Winpress window
3. Drop onto Explorer, the desktop, or any app that accepts files
4. Files are extracted and placed as real files — no wrapping folder

Works through nested archives too.

---

## Explorer context menus

Launch Winpress → **Tools → Register Explorer Context Menus**.

This adds a **Winpress** submenu to the right-click menu of:

- Every supported **archive file** — Open · Extract Here · Extract to Folder · Compress to ZIP/7z/TAR.GZ/TAR.ZST · Add to Archive
- **Every file** — Compress to ZIP/7z/TAR.GZ/TAR.ZST · Add to Archive
- **Every folder** — Compress to ZIP/7z/TAR.GZ/TAR.ZST · Add to Archive
- **Folder background** — same compress verbs

All writes go to `HKEY_CURRENT_USER` — no administrator prompt required.

---

## Over-the-Air Updates

Winpress updates itself. On each launch it checks GitHub for a newer release in the background (no startup delay). When one is found:

1. A non-intrusive dialog shows the version number and changelog
2. Click **Download Update** — Winpress fetches the new binary
3. Click **Apply & Restart** — Winpress replaces itself and relaunches

No installer, no UAC prompt (as long as Winpress.exe lives in a folder you own, such as Desktop or Downloads).

---

## Building from Source

**Prerequisites:** Windows 10/11 · [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) · Git

```bash
git clone https://github.com/snipeem/Winpress.git
cd Winpress
dotnet run --project Winpress/Winpress.csproj
```

Or open `Winpress.sln` in Visual Studio 2022 / JetBrains Rider and press F5.

<details>
<summary><b>Build a standalone .exe</b></summary>

```powershell
# x64
dotnet publish Winpress/Winpress.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:EnableCompressionInSingleFile=true `
  --output ./dist

# ARM64
dotnet publish Winpress/Winpress.csproj `
  --configuration Release `
  --runtime win-arm64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:EnableCompressionInSingleFile=true `
  --output ./dist-arm64
```

</details>

<details>
<summary><b>Code signing (SmartScreen)</b></summary>

Windows SmartScreen may warn on unsigned executables. To sign:

```powershell
dotnet publish Winpress/Winpress.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:CertificateThumbprint=YOUR_CERTIFICATE_THUMBPRINT `
  --output ./dist
```

Without a certificate, users can still run the app by clicking *More info → Run anyway*.

See [CODE_SIGNING.md](CODE_SIGNING.md) for full instructions.

</details>

---

## Release Process

Releases are fully automated. Push a version tag:

```bash
git tag v1.2.0
git push origin v1.2.0
```

GitHub Actions will build x64 and ARM64 self-contained executables, package them, generate a changelog from commit messages, and publish a GitHub Release. The OTA updater in all installed copies will detect the release within 3 seconds of the next launch.

---

## Project Structure

<details>
<summary><b>Click to expand</b></summary>

```
Winpress/
├── Winpress.sln
├── Winpress/
│   ├── Winpress.csproj                 # .NET 10 WPF project
│   ├── App.xaml / App.xaml.cs          # Entry point · CLI args · OTA check
│   ├── MainWindow.xaml / .cs           # Archive browser window
│   ├── Models/
│   │   ├── ArchiveEntry.cs             # File/folder entry inside an archive
│   │   ├── BreadcrumbItem.cs           # Breadcrumb navigation model
│   │   └── CompressionFormat.cs        # 🆕 Enum + helpers for output formats
│   ├── ViewModels/
│   │   ├── MainViewModel.cs            # Navigation, extraction, compression, sort
│   │   └── UpdateViewModel.cs          # Download progress + apply logic
│   ├── Services/
│   │   ├── ArchiveService.cs           # Opens archives → flat entry list
│   │   ├── ExtractionService.cs        # Extracts all / selected / single entry
│   │   ├── CompressionService.cs       # 🆕 Creates ZIP/7z/TAR/GZip/Zstd archives
│   │   ├── ShellIntegrationService.cs  # Explorer context menus (HKCU registry)
│   │   ├── FileAssociationService.cs   # Default app registration (HKCU registry)
│   │   └── UpdateService.cs            # GitHub API check + download + self-replace
│   ├── Helpers/
│   │   ├── FormatDetector.cs           # Magic-byte archive type detection
│   │   └── FileSizeHelper.cs           # Human-readable byte formatting
│   ├── Converters/
│   │   └── ValueConverters.cs          # WPF value converters
│   ├── Views/
│   │   ├── CreateArchiveDialog.xaml    # 🆕 Format · level · password picker
│   │   ├── PasswordDialog.xaml / .cs   # Password prompt for encrypted archives
│   │   ├── ProgressWindow.xaml / .cs   # Extraction/compression progress + cancel
│   │   └── UpdateDialog.xaml / .cs     # OTA update dialog
│   └── Resources/
│       └── Styles.xaml                 # Dark theme, button and list styles
└── .github/
    └── workflows/
        ├── ci.yml                      # Build check on every PR / push to main
        └── release.yml                 # Build x64+ARM64 + publish on version tag
```

</details>

---

## Contributing

1. Fork and create a feature branch
2. Follow the existing MVVM pattern
3. Run locally to confirm the change works
4. Open a pull request with a clear description

For larger changes, open an issue first to discuss the approach.

---

## Acknowledgements

Winpress draws utility design inspiration from [MacPacker](https://github.com/sarensw/MacPacker) by [Stephan Arenswald](https://github.com/sarensw) — a beautifully crafted archive manager for macOS.

---

## License

Winpress is licensed under the [GNU General Public License v3.0](LICENSE).
You are free to use, modify, and distribute it under the same terms.
