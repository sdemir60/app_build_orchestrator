using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// <see cref="OsysIncrementalAcceptanceTests"/> ile <see cref="OsysRebuildAcceptanceTests"/> gerçek OSYS çalışma ağacını
/// (<c>D:\Projects\Delta\OSYS</c>) ve onun ortak çıktı klasörlerini paylaşır. Paralel koştukları zaman aynı DLL'leri aynı
/// anda yazarlar (MSB3026). Ayrıca karar artık başkasının derlediği çıktının zamanını okuduğu için (zaman kipi), bir
/// sınıfın Rebuild'i diğerinin ikinci koşusundaki projeleri defter kipinden zaman kipine itebilir. Bu yüzden iki sınıf
/// sırayla koşar.
/// </summary>
[CollectionDefinition("OSYS acceptance (serial)", DisableParallelization = true)]
public class OsysAcceptanceSerialCollection
{
}
