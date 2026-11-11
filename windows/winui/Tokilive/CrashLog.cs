using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Tokilive;

public static class CrashLog
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tokilive", "logs", "crash.log");

    public static void Write(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(FilePath, Format(ex), Encoding.UTF8);
        }
        catch { /* 日志写失败时仍然把对话框留给调用方 */ }
    }

    public static void Show(Exception ex)
    {
        var text = Format(ex);
        if (text.Length > 1800) text = text[..1800];
        MessageBoxW(IntPtr.Zero, text, "tokilive 无法启动", 0x00000010);
    }

    public static string Format(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---- " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Append(sb, ex, 0);
        return sb.ToString();
    }

    static void Append(StringBuilder sb, Exception ex, int depth)
    {
        var pad = new string(' ', depth * 2);
        sb.AppendLine(pad + ex.GetType().FullName + " HResult=0x" + ex.HResult.ToString("X8"));
        sb.AppendLine(pad + ex.Message);
        foreach (var property in ex.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name is "Message" or "StackTrace" or "InnerException" or "Data" or "TargetSite" or "Source" or "HelpLink" or "HResult") continue;
            try
            {
                var value = property.GetValue(ex);
                if (value != null) sb.AppendLine(pad + property.Name + ": " + value);
            }
            catch { /* 有的异常属性取值会再抛 */ }
        }
        if (!string.IsNullOrWhiteSpace(ex.StackTrace)) sb.AppendLine(ex.StackTrace);
        if (ex.InnerException != null) Append(sb, ex.InnerException, depth + 1);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
