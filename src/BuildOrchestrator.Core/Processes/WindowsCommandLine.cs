namespace BuildOrchestrator.Core.Processes;

/// <summary>
/// Windows CreateProcessW komut satırı quoting kuralları (MSVCRT argv parse algoritmasıyla uyumlu).
/// </summary>
public static class WindowsCommandLine
{
    public static string Build(string exePath, params string[] args)
    {
        var sb = new System.Text.StringBuilder();
        Append(sb, exePath);
        foreach (var a in args) { sb.Append(' '); Append(sb, a); }
        return sb.ToString();
    }

    private static void Append(System.Text.StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) { sb.Append(arg); return; }
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
            sb.Append('\\', backslashes).Append(c); backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
    }
}
