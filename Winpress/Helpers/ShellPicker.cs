using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Winpress.Helpers;

/// <summary>
/// Wraps the Windows IFileOpenDialog COM API to present one native picker
/// in which the user can multi-select any mix of files and folders.
/// No external NuGet packages are required.
///
/// In the dialog the user can:
///   • Click a file/folder to highlight it.
///   • Ctrl+click to build a multi-item selection that includes folders.
///   • Press Add to confirm.
/// </summary>
internal static class ShellPicker
{
    // ── CLSIDs / IIDs ─────────────────────────────────────────────────────────
    private static readonly Guid CLSID_FileOpenDialog =
        new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog =
        new("D57C7288-D4AD-4768-BE02-9D969532D960");

    // ── IFileDialog option flags ───────────────────────────────────────────────
    private const uint FOS_FORCEFILESYSTEM  = 0x00000040u; // file-system items only
    private const uint FOS_ALLOWMULTISELECT = 0x00000200u; // multi-select enabled

    // ── IShellItem display-name format ────────────────────────────────────────
    private const uint SIGDN_FILESYSPATH = 0x80058000u;    // full path string

    private const int  S_OK          = 0;
    private const uint CLSCTX_INPROC = 1u;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the native Windows Explorer-style picker with multi-select.
    /// Returns <c>null</c> when the user cancels; otherwise the chosen paths.
    /// </summary>
    public static string[]? PickFilesAndFolders(Window owner)
    {
        var clsid = CLSID_FileOpenDialog;
        var iid   = IID_IFileOpenDialog;

        if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC, ref iid,
                             out var dlg) != S_OK)
            return null;

        try
        {
            // Merge FOS_FORCEFILESYSTEM | FOS_ALLOWMULTISELECT into existing options.
            dlg.GetOptions(out uint opts);
            dlg.SetOptions(opts | FOS_FORCEFILESYSTEM | FOS_ALLOWMULTISELECT);
            dlg.SetTitle("Select files and folders to compress");
            dlg.SetOkButtonLabel("Add");

            var hwnd = new WindowInteropHelper(owner).Handle;
            if (dlg.Show(hwnd) != S_OK) return null;   // user cancelled

            dlg.GetResults(out var items);
            items.GetCount(out uint count);

            var paths = new string[(int)count];
            for (uint i = 0; i < count; i++)
            {
                items.GetItemAt(i, out var item);
                item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                paths[i] = path;
            }
            return paths;
        }
        finally
        {
            Marshal.ReleaseComObject(dlg);
        }
    }

    // ── COM import ────────────────────────────────────────────────────────────

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter,
        uint dwClsContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IFileOpenDialog ppv);

    // ── COM interface declarations ────────────────────────────────────────────
    // Method order must match the COM vtable exactly:
    //   [0-2] IUnknown (handled by runtime)
    //   [3]   IModalWindow.Show
    //   [4-26] IFileDialog (23 methods)
    //   [27-28] IFileOpenDialog (2 additional methods)

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr hwndOwner);

        // IFileDialog — 23 methods (stubs; only SetOptions/GetOptions/
        // SetTitle/SetOkButtonLabel/GetResults are called at runtime).
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void AddPlace([MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);

        // IFileOpenDialog — 2 additional methods
        void GetResults([MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppenum);
        void GetSelectedItems([MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppenum);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void GetDisplayName(uint sigdnName,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare([MarshalAs(UnmanagedType.Interface)] IShellItem psi,
            uint hint, out int piOrder);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(int Flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int AttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }
}