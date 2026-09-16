using System.Text.RegularExpressions;
using AngelscriptApiMcp.Api;
using AngelscriptApiMcp.Scripts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngelscriptApiMcp.Tests;

public class ScriptParserTests
{
    private static readonly IReadOnlyList<ApiType> Types = ScriptParser.Parse(SampleScripts.Pickup, SampleScripts.PickupPath);

    private static ApiType TypeNamed(string name) => Types.Single(type => type.Name == name);

    private static ApiMember MemberNamed(string typeName, string memberName) => TypeNamed(typeName).Members.Single(member => member.Name == memberName);

    [Fact]
    public void ReadsClassDeclarationAndDocumentation()
    {
        ApiType pickup = TypeNamed("AExamplePickup");

        Assert.Equal("AActor", pickup.SuperClass);
        Assert.Equal("script class", pickup.KindLabel);
        Assert.Equal("UCLASS(Abstract) class AExamplePickup : AActor", pickup.Declaration);
        Assert.Equal("A pickup that heals whoever collects it.", pickup.Documentation);
        Assert.Equal(new SourceLocation(SampleScripts.PickupPath, SampleScripts.LineOf("class AExamplePickup")), pickup.Source);
    }

    [Fact]
    public void ReadsMembersInOrderAndSkipsDefaultsAccessDeclarationsAndBodies()
    {
        Assert.Equal(
            new[] { "Collision", "HealAmount", "Overlapping", "Unrelated", "Counter", "OnCollected", "BeginPlay", "CanCollect" },
            TypeNamed("AExamplePickup").Members.Select(member => member.Name));
    }

    [Fact]
    public void ReadsPropertiesWithSpecifiersAccessAndInitializers()
    {
        ApiMember collision = MemberNamed("AExamplePickup", "Collision");
        Assert.Equal(ApiMemberKind.Property, collision.Kind);
        Assert.Equal("UPROPERTY(DefaultComponent, RootComponent) USphereComponent Collision", collision.Declaration);
        Assert.Equal("Overlap volume.", collision.Documentation);

        ApiMember healAmount = MemberNamed("AExamplePickup", "HealAmount");
        Assert.Equal("UPROPERTY(EditAnywhere, Category = \"Pickup\") float32 HealAmount = 25.0", healAmount.Declaration);
        Assert.Equal("Pickup", healAmount.Category);

        Assert.Equal("private TArray<AActor> Overlapping", MemberNamed("AExamplePickup", "Overlapping").Declaration);
        Assert.Equal("", MemberNamed("AExamplePickup", "Unrelated").Documentation);
        Assert.Equal("access:Internal int Counter = 0", MemberNamed("AExamplePickup", "Counter").Declaration);
    }

    [Fact]
    public void ReadsFunctionSignatures()
    {
        ApiMember onCollected = MemberNamed("AExamplePickup", "OnCollected");
        Assert.Equal(ApiMemberKind.Function, onCollected.Kind);
        Assert.Equal("UFUNCTION(BlueprintEvent) void OnCollected(AActor Collector)", onCollected.Declaration);
        Assert.Equal("Called when the pickup is collected.", onCollected.Documentation);
        Assert.Equal(SampleScripts.LineOf("void OnCollected"), onCollected.Source!.Line);

        ApiMember canCollect = MemberNamed("AExamplePickup", "CanCollect");
        Assert.Equal("bool CanCollect(const AActor& Other, int Count = 1) const", canCollect.Declaration);
        Assert.Equal("AExamplePickup.CanCollect", canCollect.QualifiedName);
        Assert.False(canCollect.IsStatic);
    }

    [Fact]
    public void BlankLineSeparatesACommentFromTheDeclaration()
    {
        ApiType data = TypeNamed("FExampleData");

        Assert.Equal("", data.Documentation);
        Assert.Equal("script struct", data.KindLabel);
        Assert.Equal("USTRUCT() struct FExampleData", data.Declaration);
        Assert.Equal("UPROPERTY() int Value", Assert.Single(data.Members).Declaration);
    }

    [Fact]
    public void ReadsEnumValues()
    {
        ApiType state = TypeNamed("EExampleState");

        Assert.Equal(ApiTypeKind.Enum, state.Kind);
        Assert.Equal("script enum", state.KindLabel);
        Assert.Equal(new[] { "Idle", "Active", "Done" }, state.EnumValues);
    }

    [Fact]
    public void ReadsDelegatesAndEvents()
    {
        Assert.Equal("delegate void FExampleDelegate(UObject Object, float32 Value)", TypeNamed("FExampleDelegate").Declaration);
        Assert.Equal("event", TypeNamed("FExampleEvent").ScriptKeyword);
    }

    [Fact]
    public void ReadsNamespacesAndGlobalFunctions()
    {
        ApiType utilities = TypeNamed("ExampleUtils");
        Assert.Equal(ApiTypeKind.Namespace, utilities.Kind);
        Assert.Equal("const float32 Gravity = 980.0", utilities.Members.Single(member => member.Name == "Gravity").Declaration);
        Assert.Equal("ExampleUtils::Double", utilities.Members.Single(member => member.Name == "Double").QualifiedName);

        ApiType globals = TypeNamed(ApiType.GlobalNamespace);
        Assert.Equal(new[] { "ResetExample", "EditorOnlyHelper", "TopLevelFunction" }, globals.Members.Select(member => member.Name));
        Assert.Equal("mixin void ResetExample(AExamplePickup Self)", globals.Members[0].Declaration);
        Assert.Equal("ResetExample", globals.Members[0].QualifiedName);
    }

    [Fact]
    public void RecoversFromCodeItDoesNotUnderstand()
    {
        Assert.Empty(ScriptParser.Parse("}}} ((( class ; enum { UFUNCTION( \"unterminated", "Broken.as"));

        IReadOnlyList<ApiType> types = ScriptParser.Parse("asset ExampleAsset of UDataAsset { Value = 1; }\nclass AAfterUnknown : AActor { int X; }", "Mixed.as");
        Assert.Equal("AAfterUnknown", Assert.Single(types).Name);
    }
}

public sealed class ScriptIndexTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "angelscript-api-mcp-tests", Guid.NewGuid().ToString("N"));

    private string ScriptRoot => Path.Combine(_workspace, "Script");

    public ScriptIndexTests() => Directory.CreateDirectory(ScriptRoot);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
            // Temp files; leftovers are harmless.
        }
    }

    [Fact]
    public void PicksUpAddedChangedAndRemovedFiles()
    {
        string file = Path.Combine(ScriptRoot, "Abilities", "ExampleDash.as");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "class UExampleDash : UGameplayAbility { void Activate() {} }");
        var index = new ScriptIndex(new ServerOptions { ScriptDirs = [ScriptRoot] }, NullLogger<ScriptIndex>.Instance);

        ScriptSnapshot first = index.Refresh(force: true);
        Assert.Equal("Script/Abilities/ExampleDash.as", Assert.Single(first.Types).Source!.Path);
        Assert.Same(first, index.Refresh(force: true));

        File.WriteAllText(file, "class UExampleDash : UGameplayAbility { void Activate() {} float32 Cooldown = 1.0; }");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        ScriptSnapshot second = index.Refresh(force: true);
        Assert.True(second.Version > first.Version);
        Assert.Contains(Assert.Single(second.Types).Members, member => member.Name == "Cooldown");

        File.Delete(file);
        Assert.Empty(index.Refresh(force: true).Types);
    }

    [Fact]
    public void ReportsMissingFolders()
    {
        var index = new ScriptIndex(new ServerOptions { ScriptDirs = [Path.Combine(_workspace, "Missing")] }, NullLogger<ScriptIndex>.Instance);

        Assert.Contains("does not exist", Assert.Single(index.Refresh().Warnings));
    }
}

public class ScriptMergeTests
{
    private static List<ApiType> ScriptTypes => ScriptParser.Parse(SampleScripts.Pickup, SampleScripts.PickupPath).ToList();

    [Fact]
    public void ScriptClassesJoinTheEngineInheritanceTree()
    {
        var index = new ApiIndex(SampleApi.Types.Concat(ScriptTypes));

        ApiType pickup = index.FindType("ExamplePickup")!;
        Assert.Equal(new[] { "AActor", "UObject" }, index.GetAncestors(pickup).Select(type => type.Name));
        Assert.Contains(index.GetSubclasses(index.FindType("AActor")!), type => type.Name == "AExamplePickup");
    }

    [Fact]
    public void EngineAndScriptGlobalsShareOneNamespaceWithoutChangingTheInputs()
    {
        List<ApiType> engineTypes = SampleApi.Types;
        ApiType engineGlobals = engineTypes.Single(type => type.Name == ApiType.GlobalNamespace);

        ApiType merged = new ApiIndex(engineTypes.Concat(ScriptTypes)).FindType(ApiType.GlobalNamespace)!;

        Assert.Contains(merged.Members, member => member.Name == "Print" && member.Source is null);
        Assert.Contains(merged.Members, member => member.Name == "TopLevelFunction" && member.Source is not null);
        Assert.All(merged.Members, member => Assert.Same(merged, member.DeclaringType));
        Assert.Same(engineGlobals, engineGlobals.Members[0].DeclaringType);
    }

    [Fact]
    public void OriginFilterSeparatesEngineAndScriptResults()
    {
        var index = new ApiIndex(SampleApi.Types.Concat(ScriptTypes));

        IReadOnlyList<SearchHit> scriptHits = index.Search("Collect", origin: ApiOrigin.Script);
        Assert.All(scriptHits, hit => Assert.NotNull(hit.Source));
        Assert.Contains(scriptHits, hit => hit.Member?.Name == "CanCollect");
        Assert.Contains(scriptHits, hit => hit.Member?.Name == "OnCollected");

        Assert.Empty(index.Search("Collect", origin: ApiOrigin.Engine));
        Assert.All(index.Search("Actor", origin: ApiOrigin.Engine), hit => Assert.Null(hit.Source));
    }

    [Fact]
    public void ListsScriptTypesDerivingFromABaseClass()
    {
        List<ApiType> scripts = ScriptTypes;
        var snapshot = new ScriptSnapshot(1, scripts, ["Script"], 1, [], default);
        var index = new ApiIndex(SampleApi.Types.Concat(scripts), scripts: snapshot);

        string text = ApiFormatter.FormatScriptTypes(index, filter: "", baseClass: "Object");

        Assert.Contains("class AExamplePickup : AActor", text);
        Assert.DoesNotContain("FExampleData", text);
    }
}

/// <summary>Checks the script parser against real script folders, e.g. the engine's Script-Examples or a project's Script folder.</summary>
public partial class RealScriptTests
{
    [GeneratedRegex(@"^[ \t]*(?:UCLASS\([^\n]*\)\s*)?(?:class|struct)[ \t]+[A-Za-z_]\w*", RegexOptions.Multiline)]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"^[ \t]*(UFUNCTION|UPROPERTY)\(", RegexOptions.Multiline)]
    private static partial Regex MemberSpecifier();

    [RequiresEnvironmentFact(ServerOptions.ScriptDirsEnv)]
    public void EveryClassAndSpecifiedMemberIsFound()
    {
        var problems = new List<string>();
        int files = 0;
        foreach (string root in ServerOptions.Parse([]).ScriptDirs)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.as", SearchOption.AllDirectories))
            {
                files++;
                string text = File.ReadAllText(file);
                IReadOnlyList<ApiType> types = ScriptParser.Parse(text, file);

                int expectedClasses = ClassDeclaration().Matches(text).Count;
                int foundClasses = types.Count(type => type.ScriptKeyword is "class" or "struct");
                if (expectedClasses != foundClasses)
                    problems.Add($"{file}: {expectedClasses} class/struct declarations, {foundClasses} parsed");

                int expectedMembers = MemberSpecifier().Matches(text).Count;
                int foundMembers = types.SelectMany(type => type.Members)
                    .Count(member => member.Declaration.StartsWith("UFUNCTION(", StringComparison.Ordinal) || member.Declaration.StartsWith("UPROPERTY(", StringComparison.Ordinal));
                if (expectedMembers != foundMembers)
                    problems.Add($"{file}: {expectedMembers} UFUNCTION/UPROPERTY members, {foundMembers} parsed");
            }
        }

        Assert.True(files > 0, "The script folders contain no .as files.");
        Assert.True(problems.Count == 0, $"{problems.Count} problem(s) in {files} files:\n" + string.Join('\n', problems));
    }
}
