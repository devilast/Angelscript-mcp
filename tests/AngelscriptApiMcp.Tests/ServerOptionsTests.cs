namespace AngelscriptApiMcp.Tests;

public class ServerOptionsTests
{
    [Fact]
    public void ArgumentsOverrideEnvironment()
    {
        var environment = new Dictionary<string, string?>
        {
            [ServerOptions.DumpDirEnv] = "from-environment",
            [ServerOptions.GuideMaxAgeEnv] = "3",
        };

        ServerOptions options = ServerOptions.Parse(["--dump-dir", "from-arguments", "--offline"], name => environment.GetValueOrDefault(name));

        Assert.Equal("from-arguments", options.DumpDir);
        Assert.Equal(TimeSpan.FromDays(3), options.GuideMaxAge);
        Assert.True(options.Offline);
    }

    [Fact]
    public void ScriptDirsComeFromRepeatedArgumentsOrTheEnvironment()
    {
        var environment = new Dictionary<string, string?> { [ServerOptions.ScriptDirsEnv] = $"first{Path.PathSeparator}second" };

        Assert.Equal(new[] { "first", "second" }, ServerOptions.Parse([], name => environment.GetValueOrDefault(name)).ScriptDirs);
        Assert.Equal(new[] { "a", "b" }, ServerOptions.Parse(["--script-dir", "a", "--script-dir", "b"], name => environment.GetValueOrDefault(name)).ScriptDirs);
        Assert.Empty(ServerOptions.Parse([], _ => null).ScriptDirs);
    }

    [Fact]
    public void GuideUrlGetsTrailingSlash()
    {
        ServerOptions options = ServerOptions.Parse(["--guide-url", "https://example.com/docs"], _ => null);

        Assert.Equal("https://example.com/docs/", options.GuideBaseUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--dump-dir")]
    public void RejectsBadArguments(string argument)
    {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse([argument], _ => null));
    }
}
