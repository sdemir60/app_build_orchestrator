using System;
using System.IO;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [files-in-use · Task 3] Kilitli bir dosyayı tutan program (<see cref="FileHolders.Find"/>, Restart Manager): konsol satırı
/// "held by Acme.Host.exe (PID 42)" diyebilsin diye. Cevap yalnız bilgidir — taşıma kararı kilit yoklamasınındır — bu yüzden
/// arama hiçbir koşulda fırlatmaz ve zaman sınırını aşarsa boş döner.
/// </summary>
[Trait("Category", "MsBuild")]
public sealed class FileHoldersTests
{
    [Fact]
    public void A_file_this_process_holds_names_this_process_by_its_image()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "Acme.Types.dll");
        File.WriteAllText(path, "loaded");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            var holders = FileHolders.Find([path], FileHolders.DefaultTimeout);

            var holder = Assert.Single(holders, h => h.ProcessId == Environment.ProcessId);
            Assert.EndsWith(".exe", holder.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_file_nobody_holds_names_no_one()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "Acme.Types.dll");
        File.WriteAllText(path, "free");

        Assert.Empty(FileHolders.Find([path], FileHolders.DefaultTimeout));
    }

    [Fact]
    public void A_missing_file_an_empty_list_and_a_spent_time_limit_name_no_one_without_throwing()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "Acme.Types.dll");
        File.WriteAllText(path, "x");

        Assert.Empty(FileHolders.Find([Path.Combine(temp.Path, "absent.dll")], FileHolders.DefaultTimeout));
        Assert.Empty(FileHolders.Find([], FileHolders.DefaultTimeout));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            Assert.Empty(FileHolders.Find([path], TimeSpan.Zero));
    }
}
