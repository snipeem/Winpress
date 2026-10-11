# Winpress Updates - October 2026

## Summary of Changes

This document outlines all the changes made to Winpress to address the requested improvements.

---

## 1. Code Signing & SmartScreen Warning

**Issue:** Windows SmartScreen shows "unknown publisher" warning when running the executable.

**Solution:**
- Updated `Winpress.csproj` with proper assembly information:
  - Company: `snipeem`
  - Copyright: `Copyright © snipeem`
  - Added code signing configuration support via `CertificateThumbprint` property
- Created comprehensive `CODE_SIGNING.md` documentation explaining:
  - Why SmartScreen warnings appear
  - How to obtain code signing certificates
  - Step-by-step signing instructions
  - GitHub Actions integration
  - Troubleshooting common issues

**Usage:**
```bash
dotnet publish Winpress/Winpress.csproj `
  -p:CertificateThumbprint=YOUR_THUMBPRINT `
  --configuration Release
```

---

## 2. Drag-and-Drop Extraction

**Issue:** Need to drag files from archive directly to Windows Explorer or other applications.

**Solution:**
- Added `ExtractFlatAsync` method to `ExtractionService.cs`:
  - Extracts selected files/folders without wrapping them in a subfolder
  - Preserves original file names and folder structures
  - Returns list of extracted file paths for drag operation
  
- Updated `MainViewModel.cs`:
  - Added `ExtractFlatRequested` callback
  - Added `ExtractSelectedToPathAsync` method for drag-and-drop
  
- Updated `MainWindow.xaml.cs`:
  - Added `MouseMove` and `MouseDown` event handlers on ListView
  - Implemented drag detection with minimum movement threshold
  - Creates DataObject with FileDrop format containing extracted files
  - Supports drag to Windows Explorer, desktop, and other applications
  
- Updated `App.xaml.cs`:
  - Pass `ExtractionService` to MainWindow constructor

**Behavior:**
1. User selects file(s)/folder(s) in archive
2. Clicks and drags outside Winpress window
3. Files are extracted to temporary location
4. Drag operation creates FileDrop data object
5. Drop completes and files appear at destination
6. No wrapping folder created - files drop as-is

---

## 3. Go Back Button

**Issue:** Need prominent back button while keeping breadcrumbs.

**Solution:**
- Enhanced back button in `MainWindow.xaml`:
  - Added "Back" text label next to arrow icon
  - Changed icon to `&#xE72B;` (Back arrow) for better clarity
  - Increased button size with better padding
  - Added visual separator before and after
  
- Back button already existed with `NavigateBackCommand`
- Works with Alt+← keyboard shortcut
- Navigates through nested archive layers properly

---

## 4. Layered/Nested Archives

**Issue:** Need to ensure nested archives work correctly (a.zip > b.zip > c.zip > file).

**Solution:**
- Verified existing implementation in `MainViewModel.cs`:
  - Uses stack-based navigation (`_nestedStack`)
  - Pushes current state when entering nested archive
  - Pops state when navigating back
  - Breadcrumbs show full path through all layers
  
- `NavigateBack()` method properly handles:
  - Going up one level within current archive
  - Exiting nested archive back to parent
  - Cleaning up temporary extraction directories
  
- Breadcrumbs automatically update to show current path
- Double-click or Enter navigates into nested archives
- Works seamlessly with drag-and-drop from any layer

---

## 5. UI/UX Modernization

**Issue:** Need more modern, minimal design with better spacing.

**Solution:**

### Colors & Theme (`Styles.xaml`):
- Softened border color from `#FF3C3C3C` to `#FF333333`
- Reduced selection color opacity for subtler selection
- Maintained Windows 11 dark theme aesthetic

### Toolbar Buttons:
- Increased padding from `10,6` to `12,8` for better click targets
- Increased corner radius from `4` to `6` for softer appearance
- Better spacing between icons and text

### List Items:
- Increased padding from `4,3` to `8,6` for better readability
- Added white text color when item is both selected and hovered
- Smoother hover transitions

### Column Headers:
- Removed right border for cleaner look
- Increased padding from `8,5` to `12,8`
- Better visual hierarchy

### Breadcrumbs:
- Increased padding from `10,5` to `16,10`
- Lighter separator character (`›`) with better spacing
- Cleaner button styling

### Empty State:
- Larger icon (72px vs 64px)
- Lighter text colors for better contrast
- Increased spacing between elements

### Status Bar:
- Increased padding from `12,4` to `16,6`
- Larger font size (12px vs 11px)
- Light font weight for modern look

### Overall:
- Removed tacky hard borders where possible
- Consistent spacing throughout
- Modern Windows 11 aesthetic
- Subtle hover and selection states

---

## 6. README Updates

**Changes:**
- Added **Drag-and-Drop Extraction** to features list
- Changed "Drag & drop" to "Drop to open" for clarity
- Added **Modern UI** feature mention
- Added detailed **Drag-and-Drop Extraction** section explaining usage
- Added **Code Signing & SmartScreen** section with:
  - Explanation of SmartScreen warnings
  - How to sign the application
  - Link to detailed `CODE_SIGNING.md`
- Fixed **Acknowledgements** section:
  - Updated MacPacker link to GitHub: `https://github.com/sarensw/MacPacker`
  - Updated author link to GitHub: `https://github.com/sarensw`

---

## Files Modified

1. `Winpress/Winpress.csproj` - Assembly info and code signing config
2. `Winpress/Services/ExtractionService.cs` - Added ExtractFlatAsync method
3. `Winpress/ViewModels/MainViewModel.cs` - Added drag-and-drop support
4. `Winpress/MainWindow.xaml.cs` - Implemented drag handlers
5. `Winpress/MainWindow.xaml` - UI modernization
6. `Winpress/Resources/Styles.xaml` - Modern theme updates
7. `Winpress/App.xaml.cs` - Pass ExtractionService to MainWindow
8. `README.md` - Documentation updates

## Files Created

1. `CODE_SIGNING.md` - Comprehensive code signing guide
2. `CHANGES.md` - This file

---

## Testing Recommendations

1. **Code Signing:**
   - Build without signing and verify SmartScreen appears
   - Build with certificate and verify signature in file properties

2. **Drag-and-Drop:**
   - Select single file and drag to desktop
   - Select multiple files and drag to Windows Explorer
   - Select folder and drag to verify structure is preserved
   - Test from nested archive layers

3. **Navigation:**
   - Navigate into nested archives (zip within zip)
   - Use back button to return to parent
   - Use breadcrumbs to jump to specific level
   - Verify Alt+← shortcut works

4. **UI:**
   - Verify all buttons have proper spacing
   - Test hover states are smooth
   - Verify selection states are visible but not jarring
   - Test empty state appearance

---

## Known Limitations

1. **SmartScreen:** Without code signing, SmartScreen will still appear. Users must click "More info" → "Run anyway".

2. **Drag-and-Drop:** Currently extracts to temp directory first, then creates FileDrop. Large files may have brief delay before drag cursor appears.

3. **Code Signing:** Requires purchasing a certificate ($200-600/year) or using free open-source options.

---

## Future Enhancements (Optional)

1. **Direct Drag from Archive:** Instead of extracting to temp first, stream files directly during drag (more complex implementation)
2. **Drag Preview:** Show count of items being dragged in custom drag cursor
3. **Progress Indicator:** Show extraction progress during drag operation
4. **Multi-Selection Drag:** Better visual feedback when dragging multiple items
5. **Drop Zone Highlighting:** Highlight window when dragging files over it
