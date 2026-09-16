using System.ComponentModel;
using AngelscriptApiMcp.Api;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AngelscriptApiMcp.Tools;

[McpServerToolType]
public sealed class ApiTools
{
    [McpServerTool(Name = "search_api", Title = "Search the Angelscript API", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Search the Unreal Engine Angelscript API by name: types, namespaces, enums, enum values, functions and properties, " +
                 "from the engine dump and, when configured, the project's own scripts (shown with file and line). " +
                 "Accepts a name fragment ('LineTrace'), a qualified name that also searches inherited members " +
                 "('ACharacter.SetActorLocation', 'System::LineTraceSingle'), or several words matched against names and documentation ('spawn actor').")]
    public static async Task<string> SearchApi(
        ApiIndexProvider api,
        [Description("Name fragment, Type.Member, Namespace::Function, or space-separated words.")] string query,
        [Description("Restrict results to one kind: any, type, namespace, enum, function or property.")] string kind = "any",
        [Description("Restrict results by where they are declared: any, engine (the dump) or script (the project's .as files).")] string origin = "any",
        [Description("Maximum number of results, 1-100.")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        ApiSearchKind searchKind = ParseEnum<ApiSearchKind>(kind, "kind", "any, type, namespace, enum, function or property");
        ApiOrigin searchOrigin = ParseEnum<ApiOrigin>(origin, "origin", "any, engine or script");
        limit = Math.Clamp(limit, 1, 100);
        ApiIndex index = await api.GetAsync(cancellationToken: cancellationToken);
        return ApiFormatter.FormatSearch(query, index.Search(query, searchKind, limit, searchOrigin), limit, index);
    }

    [McpServerTool(Name = "get_type", Title = "Describe an Angelscript type", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Describe a type, namespace or enum: inheritance chain, documentation, direct subclasses (including script classes), " +
                 "enum values, and its properties and functions with signatures. Works for engine types and for script types, " +
                 "which also show where they are declared. The Unreal prefix may be omitted ('Actor' finds AActor). " +
                 "Large classes produce long output; use filter to narrow the member list.")]
    public static async Task<string> GetType(
        ApiIndexProvider api,
        [Description("Type, namespace or enum name, e.g. AActor, UCharacterMovementComponent, FVector, System, ECollisionChannel, or a script class.")] string name,
        [Description("Only list members whose name contains this text (case-insensitive).")] string filter = "",
        [Description("Also list members inherited from parent classes.")] bool includeInherited = false,
        [Description("Show full documentation for each member instead of a one-line summary.")] bool includeDocs = false,
        CancellationToken cancellationToken = default)
    {
        ApiIndex index = await api.GetAsync(cancellationToken: cancellationToken);
        ApiType? type = index.FindType(name);
        return type is null
            ? ApiFormatter.TypeNotFound(index, name)
            : ApiFormatter.FormatType(index, type, includeInherited, filter.Trim(), includeDocs);
    }

    [McpServerTool(Name = "get_member", Title = "Look up an Angelscript function or property", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Show every overload of a function or property with its full signature, category and documentation " +
                 "(and file and line for script declarations). With typeName, looks on that type and its parent classes; " +
                 "without it, lists the member on every type that declares it.")]
    public static async Task<string> GetMember(
        ApiIndexProvider api,
        [Description("Function or property name, e.g. SetActorLocation or LineTraceSingle.")] string memberName,
        [Description("Type or namespace to look in, e.g. AActor or System. Leave empty to search all types.")] string typeName = "",
        CancellationToken cancellationToken = default)
    {
        ApiIndex index = await api.GetAsync(cancellationToken: cancellationToken);
        return ApiFormatter.FormatMember(index, typeName, memberName);
    }

    [McpServerTool(Name = "list_script_types", Title = "List the project's script types", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the classes, structs, enums, delegates, events and namespaces declared in the project's own .as files, grouped by file with line numbers. " +
                 "Use baseClass to find every script type deriving from a class, directly or indirectly (e.g. all abilities: baseClass='UGameplayAbility'). " +
                 "Requires the server to be started with --script-dir.")]
    public static async Task<string> ListScriptTypes(
        ApiIndexProvider api,
        [Description("Only types whose name contains this text (case-insensitive).")] string filter = "",
        [Description("Only types that inherit from this class, directly or through other classes.")] string baseClass = "",
        CancellationToken cancellationToken = default)
    {
        ApiIndex index = await api.GetAsync(cancellationToken: cancellationToken);
        return ApiFormatter.FormatScriptTypes(index, filter.Trim(), baseClass.Trim());
    }

    private static TEnum ParseEnum<TEnum>(string value, string parameter, string allowed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value.Trim(), ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new McpException($"Unknown {parameter} '{value}'. Use {allowed}.");
}
