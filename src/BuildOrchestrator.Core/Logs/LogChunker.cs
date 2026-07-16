namespace BuildOrchestrator.Core.Logs;

public readonly record struct LogChunk(int Sequence, string Text, bool IsLast);

public static class LogChunker
{
    public const int MaxChunkChars = 64 * 1024;

    public static IEnumerable<LogChunk> Chunk(string text)
    {
        if (text.Length == 0) { yield return new LogChunk(0, "", true); yield break; }
        int seq = 0;
        for (int pos = 0; pos < text.Length; )
        {
            int len = Math.Min(MaxChunkChars, text.Length - pos);
            if (pos + len < text.Length)
            {
                int nl = text.LastIndexOf('\n', pos + len - 1, len);
                if (nl >= pos) len = nl - pos + 1; // satır sınırında böl (satır 64K'yı aşarsa sert böl)
            }
            bool last = pos + len >= text.Length;
            yield return new LogChunk(seq++, text.Substring(pos, len), last);
            pos += len;
        }
    }
}
