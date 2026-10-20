using System.Text;

namespace LegendsViewer.Backend.Legends.Parser;

internal static class ExportEncoding
{
    // Premium files can declare CP437 while containing UTF-8. Validate without
    // loading a potentially large export into memory; retain legacy decoding
    // when the bytes are not UTF-8.
    internal static bool IsUtf8(string path)
    {
        try
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false);
            var buffer = new char[16384];
            while (reader.Read(buffer, 0, buffer.Length) != 0) { }
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    internal static Encoding ForText(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return IsUtf8(path) ? Encoding.UTF8 : Encoding.GetEncoding("windows-1252");
    }
}
