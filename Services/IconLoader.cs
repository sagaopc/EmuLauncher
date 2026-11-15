using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EmuLauncher.Services;

/// <summary>
/// Loads the tile image of an emulator: an image file the user chose, or the largest icon stored
/// in the executable (usually 256 px, which stays sharp on a television).
/// </summary>
public static class IconLoader
{
    /// <summary>Safe to call from a background thread; the result is frozen. Null when nothing usable is found.</summary>
    public static ImageSource? Load(string? imagePath, string? exePath)
    {
        return FromImageFile(imagePath) ?? FromExecutable(exePath);
    }

    private static ImageSource? FromImageFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (new FileInfo(path).Length > 32 * 1024 * 1024) return null;

            return Decode(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? FromExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var module = IntPtr.Zero;
        try
        {
            module = LoadLibraryEx(path, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
            if (module == IntPtr.Zero) return null;

            // The first icon group of a program is the one Windows shows for it.
            byte[]? group = null;
            var loaded = module;
            EnumResourceNames(loaded, RtGroupIcon, (_, _, name, _) =>
            {
                group = ReadResource(loaded, RtGroupIcon, name);
                return false; // stop after the first group
            }, IntPtr.Zero);

            if (group is null || group.Length < 6) return null;

            // GRPICONDIR: reserved(2) type(2) count(2), then 14-byte entries:
            // width(1) height(1) colours(1) reserved(1) planes(2) bitCount(2) bytesInRes(4) id(2)
            int count = BitConverter.ToUInt16(group, 4);
            var bestOffset = -1;
            var bestScore = -1;

            for (var i = 0; i < count; i++)
            {
                var offset = 6 + i * 14;
                if (offset + 14 > group.Length) break;

                int width = group[offset] == 0 ? 256 : group[offset];
                int bits = BitConverter.ToUInt16(group, offset + 6);
                var score = width * 100 + Math.Min(bits, 99);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = offset;
                }
            }

            if (bestOffset < 0) return null;

            int id = BitConverter.ToUInt16(group, bestOffset + 12);
            var image = ReadResource(module, RtIcon, (IntPtr)id);
            if (image is null || image.Length < 16) return null;

            // Large icons are stored as PNG and can be decoded as they are.
            if (image[0] == 0x89 && image[1] == 0x50 && image[2] == 0x4E && image[3] == 0x47)
                return Decode(image);

            // Otherwise wrap the bitmap in a one-image .ico file, which the icon decoder understands.
            using var ico = new MemoryStream();
            using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((ushort)0);            // reserved
                writer.Write((ushort)1);            // type: icon
                writer.Write((ushort)1);            // one image
                writer.Write(group, bestOffset, 8); // width, height, colours, reserved, planes, bitCount
                writer.Write((uint)image.Length);   // size of the image data
                writer.Write((uint)22);             // offset of the image data: 6 + 16
                writer.Write(image);
            }

            return Decode(ico.ToArray());
        }
        catch
        {
            return null;
        }
        finally
        {
            if (module != IntPtr.Zero) FreeLibrary(module);
        }
    }

    private static ImageSource? Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        BitmapFrame? best = null;
        foreach (var frame in decoder.Frames)
        {
            if (best is null || frame.PixelWidth > best.PixelWidth) best = frame;
        }

        if (best is null) return null;
        if (best.CanFreeze) best.Freeze();
        return best;
    }

    private static byte[]? ReadResource(IntPtr module, IntPtr type, IntPtr name)
    {
        var info = FindResource(module, name, type);
        if (info == IntPtr.Zero) return null;

        var size = SizeofResource(module, info);
        var handle = LoadResource(module, info);
        if (handle == IntPtr.Zero || size == 0) return null;

        var pointer = LockResource(handle);
        if (pointer == IntPtr.Zero) return null;

        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, (int)size);
        return bytes;
    }

    // ------------------------------------------------------------ Win32

    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;

    private static readonly IntPtr RtIcon = (IntPtr)3;
    private static readonly IntPtr RtGroupIcon = (IntPtr)14;

    private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr param);

    [DllImport("kernel32.dll", EntryPoint = "FindResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceInfo);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resourceData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resourceInfo);
}
