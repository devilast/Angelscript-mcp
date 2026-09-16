using System.Text;

namespace AngelscriptApiMcp.Tests;

/// <summary>
/// A line-for-line port of the file writer in <c>FAngelscriptDocs::DumpDocumentation</c>
/// (Engine/Plugins/Angelscript/Source/AngelscriptCode/Private/AngelscriptDocs.cpp), so parser tests
/// run against the exact layout the engine produces. Keep the format strings identical to the C++.
/// </summary>
internal static class DumpWriter
{
    public sealed record Property(string Name, string Declaration, string Documentation = "", string Category = "", bool Static = false);

    public sealed record Function(string Name, string Declaration, string Documentation = "", string Category = "", bool Static = false);

    public static string Write(
        string className,
        string superClass = "",
        string documentation = "",
        Property[]? properties = null,
        Function[]? functions = null,
        string[]? enumValues = null)
    {
        var content = new StringBuilder();
        content.Append($"/* Class: {className} \n {documentation} */ \n class {className}");
        if (superClass.Length != 0)
            content.Append($" : public {superClass}");
        content.Append("\n{\npublic:");

        string currentCategory = "";
        foreach (Property property in properties ?? [])
        {
            string category = property.Category.Length != 0 ? property.Category
                : property.Static ? "Static Variables" : "Variables";
            if (currentCategory != category)
            {
                currentCategory = category;
                content.Append($"\n// Group: {category}\n");
            }

            content.Append($"\n/* Variable: {property.Name} \n {property.Documentation} */\n");
            if (property.Static)
                content.Append("static ");
            content.Append($"{property.Declaration};");
        }

        currentCategory = "-";
        foreach (Function function in functions ?? [])
        {
            string category = function.Category.Length != 0 ? function.Category
                : function.Static ? "Static Functions" : "Functions";
            if (currentCategory != category)
            {
                currentCategory = category;
                content.Append($"\n// Group: {category}\n");
            }

            content.Append($"\n/* Function: {function.Name} \n {function.Documentation} */\n");
            if (function.Static)
                content.Append("static ");
            content.Append($"{function.Declaration} {{}}");
        }

        content.Append("\n}\n");

        if (enumValues is { Length: > 0 })
        {
            string docValues = string.Concat(enumValues.Select(value => $"\n    {value} - Enum"));
            string declValues = string.Concat(enumValues.Select(value => $"\n{value},"));
            content.Append($"/* Enum: {className} \n {docValues} */ \n enum {className} {{ {declValues} \n}}");
        }

        return content.ToString();
    }
}
