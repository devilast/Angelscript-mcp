using System.Text.RegularExpressions;
using AngelscriptApiMcp.Api;

namespace AngelscriptApiMcp.Tests;

/// <summary>A test that runs only when an environment variable points it at real data, which never lives in this repository.</summary>
public sealed class RequiresEnvironmentFactAttribute : FactAttribute
{
    public RequiresEnvironmentFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = $"Set {variable} to run this test against real data.";
    }
}

public partial class RealDumpTests
{
    [GeneratedRegex(@"^/\* (Function|Variable): \S+ ?$", RegexOptions.Multiline)]
    private static partial Regex MemberHeader();

    [RequiresEnvironmentFact(ServerOptions.DumpDirEnv)]
    public void EveryDocumentedMemberIsParsed()
    {
        string directory = ApiIndexProvider.ResolveDumpDirectory(Environment.GetEnvironmentVariable(ServerOptions.DumpDirEnv));
        var problems = new List<string>();
        int files = 0;

        foreach (string file in Directory.EnumerateFiles(directory, "*.hpp"))
        {
            files++;
            string text = File.ReadAllText(file).Replace("\r\n", "\n");
            string fileName = Path.GetFileNameWithoutExtension(file);
            ApiType type = DumpParser.Parse(text, fileName);

            if (type.Name != fileName)
                problems.Add($"{fileName}: parsed type name is '{type.Name}'");

            int headers = MemberHeader().Matches(text).Count;
            if (type.Members.Count != headers)
                problems.Add($"{fileName}: {headers} member comments but {type.Members.Count} parsed members");

            foreach (ApiMember member in type.Members)
            {
                bool suspicious = member.Declaration.Length == 0
                    || member.Declaration.Contains('\n')
                    || member.Documentation.Contains("/* Function:", StringComparison.Ordinal)
                    || member.Documentation.Contains("/* Variable:", StringComparison.Ordinal)
                    || (member.Kind == ApiMemberKind.Function && !member.Declaration.Contains('('));
                if (suspicious)
                    problems.Add($"{fileName}.{member.Name}: suspicious declaration '{member.Declaration}'");
            }
        }

        Assert.True(files > 0, "The dump folder has no .hpp files.");
        Assert.True(problems.Count == 0, $"{problems.Count} problem(s) in {files} files:\n" + string.Join('\n', problems.Take(40)));
    }
}
