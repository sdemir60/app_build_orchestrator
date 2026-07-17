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

    [Fact]
    public void MapRefs_carries_solution_path_not_just_name()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-slnrefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            string csproj = Path.Combine(root, "sub", "A.csproj");
            File.WriteAllText(csproj, "<Project />");
            string sln = Path.Combine(root, "Osys.sln");
            File.WriteAllText(sln,
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"sub\\A.csproj\", \"{1}\"\nEndProject\n");

            var refs = SolutionMapper.MapRefs([sln], [csproj]);

            var one = Assert.Single(refs[csproj]);
            Assert.Equal("Osys", one.Name);
            Assert.Equal(Path.GetFullPath(sln), one.Path);
            // Map(), MapRefs üzerinden aynı adları vermeye devam eder (davranış korunur)
            Assert.Equal(["Osys"], SolutionMapper.Map([sln], [csproj])[csproj]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Map_collapses_same_name_solutions_from_different_paths_while_MapRefs_keeps_both()
    {
        // İki farklı dizindeki aynı base filename'e sahip .sln aynı csproj'u referans ediyor.
        // Map (ad bazlı) tek "Osys" döner; MapRefs (yol bazlı) iki ayrı SolutionRef döner.
        string root = Path.Combine(Path.GetTempPath(), "bo-slnmap-samename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "branchA"));
        Directory.CreateDirectory(Path.Combine(root, "branchB"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            string csproj = Path.Combine(root, "sub", "A.csproj");
            File.WriteAllText(csproj, "<Project />");
            string slnLine = "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"..\\sub\\A.csproj\", \"{1}\"\nEndProject\n";
            string slnA = Path.Combine(root, "branchA", "Osys.sln");
            string slnB = Path.Combine(root, "branchB", "Osys.sln");
            File.WriteAllText(slnA, slnLine);
            File.WriteAllText(slnB, slnLine);

            var map = SolutionMapper.Map([slnA, slnB], [csproj]);
            var refs = SolutionMapper.MapRefs([slnA, slnB], [csproj]);

            Assert.Equal(["Osys"], map[csproj]);
            Assert.Equal(2, refs[csproj].Count);
            Assert.Contains(refs[csproj], r => r.Path.Equals(Path.GetFullPath(slnA), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(refs[csproj], r => r.Path.Equals(Path.GetFullPath(slnB), StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
