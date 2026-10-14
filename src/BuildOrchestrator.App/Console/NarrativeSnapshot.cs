namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.2] Anlatı belgesinin tohumu: backlog ve o anki satır sayısı. Tam metin TAŞINMAZ — görünüm
/// belgeyi backlog'un SON diliminden kurar (<c>Join(Count − 200, Count)</c>), öncesini tepeye kaydırınca aynı backlog'dan ister.
/// Üretimde backlog VM'in anlatı store'udur ve <see cref="Count"/> VM'in kilidi altında, reseed-drop sentinel'iyle birlikte
/// okunur (<c>RunViewModel.SeedRunDocument</c>): sayıya giren her satır sentinel'den ÖNCE pompadadır, sonrakiler batch'le gelir.
/// </summary>
public sealed record NarrativeSnapshot(IConsoleBacklog Backlog, int Count);
