using AngelscriptApiMcp.Api;

namespace AngelscriptApiMcp.Tests;

public class ApiIndexTests
{
    private readonly ApiIndex _index = SampleApi.Build();

    [Theory]
    [InlineData("AActor", "AActor")]
    [InlineData("actor", "AActor")]
    [InlineData("Object", "UObject")]
    [InlineData("system", "System")]
    public void FindTypeIgnoresCaseAndUnrealPrefix(string query, string expected)
    {
        Assert.Equal(expected, _index.FindType(query)?.Name);
    }

    [Fact]
    public void AncestorsFollowTheSuperClassChain()
    {
        IReadOnlyList<ApiType> ancestors = _index.GetAncestors(_index.FindType("ACharacter")!);

        Assert.Equal(new[] { "APawn", "AActor", "UObject" }, ancestors.Select(type => type.Name));
    }

    [Fact]
    public void IndexesDirectSubclasses()
    {
        Assert.Equal("APawn", Assert.Single(_index.GetSubclasses(_index.FindType("AActor")!)).Name);
    }

    [Fact]
    public void ExactMemberNameRanksFirstAndFoldsOverloads()
    {
        SearchHit top = _index.Search("SetActorLocation")[0];

        Assert.Equal("SetActorLocation", top.Member?.Name);
        Assert.Equal(2, top.Overloads);
    }

    [Fact]
    public void TypeNameWithoutPrefixFindsTheType()
    {
        SearchHit top = _index.Search("Actor")[0];

        Assert.Null(top.Member);
        Assert.Equal("AActor", top.Type.Name);
    }

    [Fact]
    public void QualifiedQueryIncludesInheritedMembers()
    {
        SearchHit top = _index.Search("ACharacter.SetActorLocation")[0];

        Assert.Equal("AActor", top.Type.Name);
        Assert.Equal("SetActorLocation", top.Member?.Name);
    }

    [Fact]
    public void NamespaceQualifiedQueryMatchesMemberPrefix()
    {
        Assert.Equal("LineTraceSingle", _index.Search("System::LineTrace")[0].Member?.Name);
    }

    [Fact]
    public void FindsEnumValues()
    {
        SearchHit top = _index.Search("ECC_Visibility")[0];

        Assert.Equal("ECollisionChannel", top.Type.Name);
        Assert.Equal("ECC_Visibility", top.EnumValue);
    }

    [Fact]
    public void KindFilterRestrictsResults()
    {
        IReadOnlyList<SearchHit> hits = _index.Search("Actor", ApiSearchKind.Function);

        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.Equal(ApiMemberKind.Function, hit.Member?.Kind));
    }

    [Fact]
    public void MultiWordQueryMatchesNamesAndDocumentation()
    {
        Assert.Contains(_index.Search("line trace"), hit => hit.Member?.Name == "LineTraceSingle");
        Assert.Contains(_index.Search("blocking hit"), hit => hit.Member?.Name == "LineTraceSingle");
    }

    [Fact]
    public void FormatTypeShowsInheritedMembersOnRequest()
    {
        ApiType character = _index.FindType("ACharacter")!;

        string withoutInherited = ApiFormatter.FormatType(_index, character, includeInherited: false, filter: "", includeDocs: false);
        string withInherited = ApiFormatter.FormatType(_index, character, includeInherited: true, filter: "", includeDocs: false);

        Assert.Contains("Inheritance: ACharacter → APawn → AActor → UObject", withoutInherited);
        Assert.DoesNotContain("SetActorLocation", withoutInherited);
        Assert.Contains("## Inherited from AActor", withInherited);
        Assert.Contains("- `bool SetActorLocation(FVector NewLocation)`", withInherited);
    }

    [Fact]
    public void FormatMemberWalksParentsAndSuggestsAlternatives()
    {
        string found = ApiFormatter.FormatMember(_index, "Character", "setactorlocation");
        string missing = ApiFormatter.FormatMember(_index, "ACharacter", "SetLocation");

        Assert.Contains("# AActor.SetActorLocation (2 declaration(s))", found);
        Assert.Contains("NewLocation - The new location to teleport the Actor to.", found);
        Assert.Contains("has no member named \"SetLocation\"", missing);
    }
}
