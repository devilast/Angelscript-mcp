namespace AngelscriptApiMcp.Api;

public enum ApiTypeKind
{
    /// <summary>A class or struct (or, for scripts, a delegate type).</summary>
    Type,

    /// <summary>A namespace of global functions and variables, such as System or Math.</summary>
    Namespace,

    Enum,
}

public enum ApiMemberKind
{
    Function,
    Property,
}

/// <summary>Where a script declaration lives, e.g. <c>Script/Pickups/Pickup.as:12</c>.</summary>
public sealed record SourceLocation(string Path, int Line)
{
    public override string ToString() => $"{Path}:{Line}";
}

public sealed class ApiMember
{
    public ApiMember(string name, ApiMemberKind kind, string declaration, string documentation, string category, bool isStatic, SourceLocation? source = null)
    {
        Name = name;
        Kind = kind;
        Declaration = declaration;
        Documentation = documentation;
        Category = category;
        IsStatic = isStatic;
        Source = source;
    }

    public string Name { get; }

    public ApiMemberKind Kind { get; }

    /// <summary>
    /// Script declaration. From the dump: without its <c>static</c> prefix and <c>{}</c> or <c>;</c> suffix.
    /// From project scripts: as written, including UFUNCTION/UPROPERTY specifiers.
    /// </summary>
    public string Declaration { get; }

    public string Documentation { get; }

    public string Category { get; }

    public bool IsStatic { get; }

    /// <summary>Set for members declared in project scripts; null for engine members from the dump.</summary>
    public SourceLocation? Source { get; }

    /// <summary>The type or namespace this member belongs to. Set by <see cref="ApiType"/>.</summary>
    public ApiType DeclaringType { get; internal set; } = null!;

    /// <summary>How script code names it: <c>AActor.SetActorLocation</c>, <c>System::LineTraceSingle</c>, or bare for globals.</summary>
    public string QualifiedName =>
        DeclaringType.Name == ApiType.GlobalNamespace ? Name
        : DeclaringType.Name + (IsStatic ? "::" : ".") + Name;

    /// <summary>A copy not yet owned by any type, for merging same-named types.</summary>
    internal ApiMember Detach() => new(Name, Kind, Declaration, Documentation, Category, IsStatic, Source);
}

public sealed class ApiType
{
    /// <summary>The type that holds functions and variables of the global namespace.</summary>
    public const string GlobalNamespace = "Global";

    public ApiType(
        string name,
        string superClass,
        string documentation,
        IReadOnlyList<ApiMember> members,
        IReadOnlyList<string> enumValues,
        SourceLocation? source = null,
        string declaration = "",
        string scriptKeyword = "")
    {
        Name = name;
        SuperClass = superClass;
        Documentation = documentation;
        Members = members;
        EnumValues = enumValues;
        Source = source;
        Declaration = declaration;
        ScriptKeyword = scriptKeyword;

        foreach (ApiMember member in members)
            member.DeclaringType = this;

        // Classes, structs, namespaces and enums share one shape; tell them apart by content.
        if (enumValues.Count > 0 && members.Count == 0)
            Kind = ApiTypeKind.Enum;
        else if (superClass.Length == 0 && members.Count > 0 && members.All(m => m.IsStatic))
            Kind = ApiTypeKind.Namespace;
        else
            Kind = ApiTypeKind.Type;
    }

    public string Name { get; }

    /// <summary>Script name of the parent class, or empty. The parent is not necessarily indexed.</summary>
    public string SuperClass { get; }

    public string Documentation { get; }

    public IReadOnlyList<ApiMember> Members { get; }

    public IReadOnlyList<string> EnumValues { get; }

    public ApiTypeKind Kind { get; }

    /// <summary>Set for types declared in project scripts; null for engine types from the dump.</summary>
    public SourceLocation? Source { get; }

    /// <summary>For script types, the declaration line as written, e.g. <c>UCLASS(Abstract) class APickup : AActor</c>.</summary>
    public string Declaration { get; }

    /// <summary>For script types: class, struct, enum, namespace, delegate or event.</summary>
    public string ScriptKeyword { get; }

    public bool IsScript => Source is not null;

    public string KindLabel => IsScript
        ? "script " + ScriptKeyword
        : Kind switch
        {
            ApiTypeKind.Namespace => "namespace",
            ApiTypeKind.Enum => "enum",
            _ => "type",
        };
}
