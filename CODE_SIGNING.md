# Code Signing & Windows SmartScreen

## Understanding SmartScreen

When you run an unsigned executable on Windows, SmartScreen displays a warning:

> "Windows protected your PC"
> 
> "Microsoft Defender SmartScreen prevented an unrecognized app from starting. Running this app might put your PC at risk."

This is normal for unsigned applications. Users can still run the app by clicking:
1. **"More info"** link
2. **"Run anyway"** button

## Why Code Sign?

Code signing provides:
- **Trust** — Verifies the app comes from a known publisher
- **Integrity** — Ensures the app hasn't been tampered with
- **No SmartScreen warnings** — Signed apps skip the warning dialog
- **Professional appearance** — Shows your organization name in the file properties

## Getting a Code Signing Certificate

### Options:

1. **OV (Organization Validation) Certificate** — ~$200-400/year
   - Shows your organization name
   - Suitable for commercial software
   
2. **EV (Extended Validation) Certificate** — ~$400-600/year
   - Shows green bar in some browsers
   - Immediate SmartScreen reputation
   - Required for kernel-mode drivers

3. **Free Options** (limited):
   - [SignPath](https://signpath.io) — Free for open source projects
   - [Certum Open Source Code Signing](https://certum.eu/en/open-source-code-signing) — Free for open source

## Signing Your Build

### Prerequisites:
- A code signing certificate (PFX file or certificate store)
- The certificate thumbprint

### Method 1: Using Certificate Thumbprint (Recommended for CI/CD)

```bash
dotnet publish Winpress/Winpress.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:CertificateThumbprint=YOUR_CERTIFICATE_THUMBPRINT `
  --output ./dist
```

### Method 2: Using signtool.exe

After building, sign the executable manually:

```bash
signtool sign /f certificate.pfx /p PASSWORD /tr http://timestamp.digicert.com /td sha256 /fd sha256 dist/Winpress.exe
```

Or using certificate from Windows Store:

```bash
signtool sign /n "Your Company Name" /tr http://timestamp.digicert.com /td sha256 /fd sha256 dist/Winpress.exe
```

### Method 3: Visual Studio

1. Right-click project → Properties
2. Signing tab → Check "Sign the ClickOnce manifests" and "Sign the assembly"
3. Select or create certificate

## GitHub Actions Code Signing

Add these secrets to your GitHub repository:

- `CERTIFICATE_BASE64` — Base64-encoded PFX file
- `CERTIFICATE_PASSWORD` — PFX password

```yaml
- name: Sign executable
  if: runner.os == 'Windows'
  env:
    CERT_BASE64: ${{ secrets.CERTIFICATE_BASE64 }}
    CERT_PASSWORD: ${{ secrets.CERTIFICATE_PASSWORD }}
  run: |
    echo $env:CERT_BASE64 | Out-File -FilePath cert.pfx.b64
    certutil -decode cert.pfx.b64 cert.pfx
    & "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe" sign /f cert.pfx /p $env:CERT_PASSWORD /tr http://timestamp.digicert.com /td sha256 /fd sha256 dist/Winpress.exe
    Remove-Item cert.pfx
    Remove-Item cert.pfx.b64
```

## Verifying the Signature

After signing, verify the signature:

```bash
signtool verify /pa dist/Winpress.exe
```

Right-click the executable → Properties → Digital Signatures tab to see the signature details.

## Building SmartScreen Reputation

Even with a certificate, new certificates need time to build reputation with SmartScreen. This can take weeks or months.

**EV certificates** get immediate reputation but cost more.

**Regular OV certificates** will gradually build reputation as more users download and run your app without issues.

## Troubleshooting

### "signtool.exe not found"

Install Windows SDK or Windows 10 SDK. The tool is typically at:
```
C:\Program Files (x86)\Windows Kits\10\bin\10.0.xxxxx.0\x64\signtool.exe
```

### "Certificate not found"

- Ensure the certificate is installed in the Current User → Personal store
- Or provide the full path to the PFX file

### "Timestamp server unreachable"

Use a different timestamp server:
- `http://timestamp.digicert.com`
- `http://timestamp.sectigo.com`
- `http://timestamp.globalsign.com`

## Resources

- [Microsoft: Code Signing Overview](https://docs.microsoft.com/windows/win32/appxpkg/code-signing)
- [SignPath Documentation](https://docs.signpath.io)
- [GitHub Actions: Code Signing](https://github.com/Delphier/CodeSigning)
