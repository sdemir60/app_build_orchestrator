using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// Supervisor'ın stdout'unu okumanın TEK yeri: her satır <see cref="IpcEvent"/> olarak çözülür — çözülemeyen
/// bir satır [D4] ihlalidir (stdout YALNIZ NDJSON) ve testi anında düşürür. Host-in-memory testleri bu
/// ayrıştırmayı kendi içlerinde yeniden yazmaz.
/// </summary>
internal static class NdjsonWire
{
    public static List<IpcEvent> Parse(string text) => text
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(l => JsonSerializer.Deserialize<IpcEvent>(l, IpcJson.Options)
                     ?? throw new InvalidOperationException("NDJSON olmayan satır [D4]: " + l))
        .ToList();
}
