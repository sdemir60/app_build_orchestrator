using System;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D8] TFVC yüzeyi. Kararlar tf.exe'nin LOKALİZE metninden değil, yapısal çıktıdan (XML) ve exit
/// kodundan okunur — Türkçe bir Visual Studio kurulumunda da aynı çalışır.
/// </summary>
public class TfvcServiceTests
{
    private const string Root = @"D:\tfs\Customer";
    private const string TfExe = @"C:\VS\TF.exe";

    private static TfvcService Service(FakeProcessRunner runner) => new(runner, Root, TfExe);

    private const string StatusWithPendingChange = """
        <?xml version="1.0" encoding="utf-8"?>
        <Status>
          <PendingChanges>
            <PendingChange chg="Edit" local="D:\tfs\Customer\Ocr\Reader.cs" />
          </PendingChanges>
        </Status>
        """;

    // "There are no pending changes." metni lokalize olur; yapı olmaz — boş kap da geçerli bir cevaptır.
    private const string StatusWithoutPendingChanges = """
        <?xml version="1.0" encoding="utf-8"?>
        <Status>
          <PendingChanges />
        </Status>
        """;

    [Fact]
    public async Task Pending_changes_are_read_from_the_xml_structure()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(StatusWithPendingChange));

        var result = await Service(runner).HasPendingChangesAsync();

        Assert.True(result.Success);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task A_status_without_pending_change_elements_is_clean()
    {
        // <PendingChanges> kabının kendisi ham metinde "PendingChange" alt dizesini İÇERİR — düz metin
        // araması burada yanlış pozitif verirdi.
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(StatusWithoutPendingChanges));

        var result = await Service(runner).HasPendingChangesAsync();

        Assert.True(result.Success);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task Status_asks_for_xml_over_the_whole_root()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(StatusWithoutPendingChanges));

        await Service(runner).HasPendingChangesAsync();

        Assert.Equal(TfExe, runner.LastSpec.FileName);
        Assert.Equal(Root, runner.LastSpec.WorkingDirectory);
        Assert.Equal(["vc", "status", ".", "/recursive", "/noprompt", "/format:xml"], runner.LastSpec.Arguments);
    }

    [Fact]
    public async Task A_failing_status_query_comes_back_as_data()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Failure(100, "TF30063: You are not authorized."));

        var result = await Service(runner).HasPendingChangesAsync();

        Assert.False(result.Success);
        Assert.Contains("TF30063", result.Error);
    }

    [Fact]
    public async Task Unparsable_status_output_comes_back_as_data()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Output("not xml at all"));

        var result = await Service(runner).HasPendingChangesAsync();

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task Get_latest_passes_the_canonical_arguments()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Output(""));

        var result = await Service(runner).GetLatestAsync();

        Assert.True(result.Success);
        Assert.Equal(["vc", "get", ".", "/recursive", "/noprompt"], runner.LastSpec.Arguments);
    }

    [Fact]
    public async Task A_failing_get_comes_back_as_data_not_an_exception()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Failure(100, "TF14098: Access denied."));

        var result = await Service(runner).GetLatestAsync();

        Assert.False(result.Success);
        Assert.Contains("TF14098", result.Error);
    }
}
