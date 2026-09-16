using AngelscriptApiMcp.Api;
using static AngelscriptApiMcp.Tests.DumpWriter;

namespace AngelscriptApiMcp.Tests;

/// <summary>A small, hand-written stand-in for an engine dump, using well-known engine types.</summary>
internal static class SampleApi
{
    public static IReadOnlyDictionary<string, string> Files { get; } = new Dictionary<string, string>
    {
        ["UObject"] = Write("UObject", documentation: "The base class of all UE objects.",
            functions: [new Function("GetName", "FString GetName() const", "Returns the name of this object.")]),

        ["AActor"] = Write("AActor", "UObject", "Actor is the base class for an Object that can be placed or spawned in a level.",
            properties: [new Property("bHidden", "bool bHidden", "Allows us to only see this Actor in the Editor, and not in the actual game.", "Rendering")],
            functions:
            [
                new Function("GetActorLocation", "FVector GetActorLocation() const", "Returns the location of the RootComponent of this Actor.", "Utilities|Transformation"),
                new Function("SetActorLocation", "bool SetActorLocation(FVector NewLocation, bool bSweep, FHitResult& SweepHitResult, bool bTeleport)",
                    "Move the actor instantly to the specified location.\n\nParameters:\n    NewLocation - The new location to teleport the Actor to.", "Utilities|Transformation"),
                new Function("SetActorLocation", "bool SetActorLocation(FVector NewLocation)", "", "Utilities|Transformation"),
            ]),

        ["APawn"] = Write("APawn", "AActor", "Pawn is the base class of all actors that can be possessed by players or AI."),

        ["ACharacter"] = Write("ACharacter", "APawn", "Characters are Pawns that have a mesh, collision, and built-in movement logic.",
            functions: [new Function("Jump", "void Jump()", "Make the character jump on the next update.", "Character")]),

        ["System"] = Write("System", functions:
        [
            new Function("LineTraceSingle",
                "bool System::LineTraceSingle(FVector Start, FVector End, ETraceTypeQuery TraceChannel, bool bTraceComplex, const TArray<AActor>& ActorsToIgnore, EDrawDebugTrace DrawDebugType, FHitResult& OutHit, bool bIgnoreSelf, float32 DrawTime = 5.0)",
                "Does a collision trace along the given line and returns the first blocking hit encountered.", "Collision", Static: true),
            new Function("SetTimer", "FTimerHandle System::SetTimer(UObject Object, FName FunctionName, float32 Time, bool bLooping)",
                "Set a timer to execute a delegate.", "Utilities|Time", Static: true),
        ]),

        ["Math"] = Write("Math",
            properties: [new Property("PI", "const float64 Math::PI", Static: true)],
            functions: [new Function("Lerp", "float64 Math::Lerp(float64 A, float64 B, float64 Alpha)", "Linearly interpolates between A and B.", Static: true)]),

        ["ECollisionChannel"] = Write("ECollisionChannel", enumValues: ["ECC_WorldStatic", "ECC_Visibility", "ECC_Camera"]),

        ["Global"] = Write("Global", functions:
            [new Function("Print", "void Print(const FString& Message, float32 Duration = 2.0)", "Prints a string to the log and the screen.", Static: true)]),
    };

    /// <summary>Freshly parsed engine types; each call returns new objects.</summary>
    public static List<ApiType> Types => Files.Select(file => DumpParser.Parse(file.Value, file.Key)).ToList();

    public static ApiIndex Build() => new(Types);
}
