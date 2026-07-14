using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class ImmersiveRuntimeFileDialog
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnNoChangeDirectory = 0x00000008;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnExplorer = 0x00080000;
    private const string JsonFilter = "JSON configuration (*.json)\0*.json\0All files (*.*)\0*.*\0\0";

    public static bool IsSupported => true;

    public static bool TrySaveJson(string title, string suggestedPath, out string path)
    {
        var dialog = CreateDialog(title, suggestedPath);
        dialog.flags |= OfnOverwritePrompt;
        if (!GetSaveFileName(dialog))
        {
            path = null;
            return false;
        }

        path = dialog.file.ToString();
        return !string.IsNullOrWhiteSpace(path);
    }

    public static bool TryOpenJson(string title, string initialDirectory, out string path)
    {
        var dialog = CreateDialog(title, null);
        dialog.initialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null;
        dialog.flags |= OfnFileMustExist;
        if (!GetOpenFileName(dialog))
        {
            path = null;
            return false;
        }

        path = dialog.file.ToString();
        return !string.IsNullOrWhiteSpace(path);
    }

    private static OpenFileName CreateDialog(string title, string initialPath)
    {
        var fileBuffer = new StringBuilder(4096);
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            fileBuffer.Append(initialPath);
        }

        return new OpenFileName
        {
            structSize = Marshal.SizeOf(typeof(OpenFileName)),
            owner = GetActiveWindow(),
            filter = JsonFilter,
            filterIndex = 1,
            file = fileBuffer,
            maxFile = fileBuffer.Capacity,
            fileTitle = new StringBuilder(512),
            maxFileTitle = 512,
            initialDirectory = string.IsNullOrWhiteSpace(initialPath) ? null : Path.GetDirectoryName(initialPath),
            title = title,
            defaultExtension = "json",
            flags = OfnExplorer | OfnNoChangeDirectory | OfnPathMustExist
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class OpenFileName
    {
        public int structSize;
        public IntPtr owner;
        public IntPtr instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string filter;
        public StringBuilder customFilter;
        public int maxCustomFilter;
        public int filterIndex;
        public StringBuilder file;
        public int maxFile;
        public StringBuilder fileTitle;
        public int maxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string initialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string defaultExtension;
        public IntPtr customData;
        public IntPtr hook;
        [MarshalAs(UnmanagedType.LPWStr)] public string templateName;
        public IntPtr reserved;
        public int reservedValue;
        public int flagsExtended;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName([In, Out] OpenFileName dialog);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
#else
    public static bool IsSupported => false;

    public static bool TrySaveJson(string title, string suggestedPath, out string path)
    {
        path = null;
        return false;
    }

    public static bool TryOpenJson(string title, string initialDirectory, out string path)
    {
        path = null;
        return false;
    }
#endif
}
