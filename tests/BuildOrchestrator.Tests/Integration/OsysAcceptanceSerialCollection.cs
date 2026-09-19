using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [OSYS kabul testleri sıra sıra koşar] <see cref="OsysIncrementalAcceptanceTests"/> ve
/// <see cref="OsysRebuildAcceptanceTests"/> gerçek OSYS working tree'sini (<c>D:\Projects\Delta\OSYS</c>) paylaşır
/// (kendi obj izolasyonundan bağımsız olarak). Paralel koştukları zaman iki sınıf aynı output dizinine (MSBuild
/// /p:OutDir) yazmaya çalışıyor — MSB3026 çatışmaları + kontention. Ayrıca yeni karar motoru output zaman bilgisini
/// okuyor (incremental hint): bir sınıfın Rebuild'i output'ı güncelleyip, diğerinin ikinci koşusunu time-rebuild
/// moduna sokabilir (yanlış karar). Çözüm: bu collection'a koy, diğer collection'larla eşzamanlı koşmasını engelle.
/// </summary>
[CollectionDefinition("OSYS acceptance (serial)", DisableParallelization = true)]
public class OsysAcceptanceSerialCollection
{
}
