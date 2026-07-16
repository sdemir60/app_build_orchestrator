using System;
using System.IO;
using BuildOrchestrator.Core.Discovery;
using Xunit;

namespace BuildOrchestrator.Tests.Discovery;

public class SolutionMapperTests
{
    [Fact]
    public void maps_csproj_to_containing_solutions()
    {
        string root = Path.Combine(Path.GetTempPath(), "slnmap-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "B"));
            string aProj = Path.GetFullPath(Path.Combine(root, "A", "A.csproj"));
            string bProj = Path.GetFullPath(Path.Combine(root, "B", "B.csproj"));
            File.WriteAllText(aProj, "<Project/>"); File.WriteAllText(bProj, "<Project/>");
            string sln1 = Path.Combine(root, "One.sln");
            File.WriteAllText(sln1,
                "Project(\"{X}\") = \"A\", \"A\\A.csproj\", \"{1}\"\n" +
                "Project(\"{X}\") = \"B\", \"B\\B.csproj\", \"{2}\"\n");
            string sln2 = Path.Combine(root, "Two.sln");
            File.WriteAllText(sln2, "Project(\"{X}\") = \"A\", \"A\\A.csproj\", \"{1}\"\n");
            var map = SolutionMapper.Map([sln1, sln2], [aProj, bProj]);
            Assert.Equal(["One", "Two"], map[aProj]); // >1, sıralı
            Assert.Equal(["One"], map[bProj]);          // 1
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void csproj_in_no_solution_maps_to_empty()
    {
        string root = Path.Combine(Path.GetTempPath(), "slnmap-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string orphan = Path.GetFullPath(Path.Combine(root, "Orphan.csproj"));
            File.WriteAllText(orphan, "<Project/>");
            var map = SolutionMapper.Map([], [orphan]);
            Assert.Empty(map[orphan]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
