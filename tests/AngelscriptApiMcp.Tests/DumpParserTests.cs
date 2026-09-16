using AngelscriptApiMcp.Api;
using static AngelscriptApiMcp.Tests.DumpWriter;

namespace AngelscriptApiMcp.Tests;

public class DumpParserTests
{
    [Fact]
    public void ParsesClassHeaderAndSuperClass()
    {
        ApiType type = DumpParser.Parse(Write("AActor", "UObject", "Actor is the base class."), "fallback");

        Assert.Equal("AActor", type.Name);
        Assert.Equal("UObject", type.SuperClass);
        Assert.Equal("Actor is the base class.", type.Documentation);
        Assert.Equal(ApiTypeKind.Type, type.Kind);
        Assert.Empty(type.Members);
    }

    [Fact]
    public void ParsesPropertiesAndFunctionsWithCategories()
    {
        string text = Write("AActor", "UObject",
            properties: [new Property("bHidden", "bool bHidden", "Hidden in game.", "Rendering")],
            functions:
            [
                new Function("SetActorLocation", "bool SetActorLocation(FVector NewLocation, bool bSweep = false)",
                    "Move the actor.\n\nParameters:\n    NewLocation - Where to.", "Utilities|Transformation"),
                new Function("GetDefaultObject", "const AActor GetDefaultObject()", Static: true),
            ]);

        ApiType type = DumpParser.Parse(text, "AActor");

        ApiMember property = Assert.Single(type.Members, member => member.Kind == ApiMemberKind.Property);
        Assert.Equal("bHidden", property.Name);
        Assert.Equal("bool bHidden", property.Declaration);
        Assert.Equal("Rendering", property.Category);
        Assert.Equal("Hidden in game.", property.Documentation);

        ApiMember setLocation = type.Members.Single(member => member.Name == "SetActorLocation");
        Assert.Equal("bool SetActorLocation(FVector NewLocation, bool bSweep = false)", setLocation.Declaration);
        Assert.Equal("Move the actor.\n\nParameters:\n    NewLocation - Where to.", setLocation.Documentation);
        Assert.Equal("Utilities|Transformation", setLocation.Category);
        Assert.False(setLocation.IsStatic);
        Assert.Same(type, setLocation.DeclaringType);
        Assert.Equal("AActor.SetActorLocation", setLocation.QualifiedName);

        ApiMember defaultObject = type.Members.Single(member => member.Name == "GetDefaultObject");
        Assert.True(defaultObject.IsStatic);
        Assert.Equal("const AActor GetDefaultObject()", defaultObject.Declaration);
        Assert.Equal("Static Functions", defaultObject.Category);
        Assert.Equal("", defaultObject.Documentation);
    }

    [Fact]
    public void CommentCloserInsideDocumentationDoesNotEndTheEntry()
    {
        string text = Write("UKismetMathLibrary", functions:
        [
            new Function("Divide", "float32 Divide(float32 A, float32 B)", "Computes A */ B.\nSee also Multiply {}"),
            new Function("Multiply", "float32 Multiply(float32 A, float32 B)", "Computes A * B."),
        ]);

        ApiType type = DumpParser.Parse(text, "UKismetMathLibrary");

        Assert.Equal(2, type.Members.Count);
        Assert.Equal("Computes A */ B.\nSee also Multiply {}", type.Members[0].Documentation);
        Assert.Equal("float32 Multiply(float32 A, float32 B)", type.Members[1].Declaration);
    }

    [Fact]
    public void ParsesEnumValues()
    {
        ApiType type = DumpParser.Parse(Write("ECollisionChannel", enumValues: ["ECC_WorldStatic", "ECC_Visibility"]), "ECollisionChannel");

        Assert.Equal(ApiTypeKind.Enum, type.Kind);
        Assert.Equal(new[] { "ECC_WorldStatic", "ECC_Visibility" }, type.EnumValues);
    }

    [Fact]
    public void RecognisesNamespacesOfStaticMembers()
    {
        string text = Write("Math",
            properties: [new Property("PI", "const float64 Math::PI", Static: true)],
            functions: [new Function("Lerp", "float64 Math::Lerp(float64 A, float64 B, float64 Alpha)", "Linearly interpolates.", Static: true)]);

        ApiType type = DumpParser.Parse(text, "Math");

        Assert.Equal(ApiTypeKind.Namespace, type.Kind);
        ApiMember pi = type.Members.Single(member => member.Name == "PI");
        Assert.Equal("const float64 Math::PI", pi.Declaration);
        Assert.Equal("Static Variables", pi.Category);
        Assert.Equal("Math::Lerp", type.Members.Single(member => member.Name == "Lerp").QualifiedName);
    }

    [Fact]
    public void GlobalFunctionsHaveUnqualifiedNames()
    {
        ApiType type = DumpParser.Parse(SampleApi.Files["Global"], "Global");

        Assert.Equal("Print", Assert.Single(type.Members).QualifiedName);
    }

    [Fact]
    public void AcceptsWindowsLineEndings()
    {
        string text = Write("AActor", "UObject", "Doc.", functions: [new Function("DestroyActor", "void DestroyActor()", "Destroy.")])
            .Replace("\n", "\r\n");

        ApiType type = DumpParser.Parse(text, "AActor");

        Assert.Equal("UObject", type.SuperClass);
        Assert.Equal("void DestroyActor()", Assert.Single(type.Members).Declaration);
    }

    [Fact]
    public void UsesFallbackNameWithoutClassHeader()
    {
        Assert.Equal("Orphan", DumpParser.Parse("", "Orphan").Name);
    }
}
