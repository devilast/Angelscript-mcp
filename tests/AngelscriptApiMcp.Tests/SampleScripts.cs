namespace AngelscriptApiMcp.Tests;

/// <summary>A hand-written script exercising the declaration forms the script parser has to handle.</summary>
internal static class SampleScripts
{
    public const string PickupPath = "Script/Pickups/ExamplePickup.as";

    public const string Pickup = """
        // A pickup that heals whoever collects it.
        UCLASS(Abstract)
        class AExamplePickup : AActor
        {
            // Overlap volume.
            UPROPERTY(DefaultComponent, RootComponent)
            USphereComponent Collision;

            UPROPERTY(EditAnywhere, Category = "Pickup")
            float32 HealAmount = 25.0;

            default bReplicates = true;

            private TArray<AActor> Overlapping; // trailing comment, not documentation
            int Unrelated;

            access Internal = private;

            access:Internal
            int Counter = 0;

            /* Called when the pickup is collected. */
            UFUNCTION(BlueprintEvent)
            void OnCollected(AActor Collector) {}

            UFUNCTION(BlueprintOverride)
            void BeginPlay()
            {
                Collision.OnComponentBeginOverlap.AddUFunction(this, n"HandleOverlap");
                Print(f"Heal amount: {HealAmount} }");
                // } a brace in a comment
            }

            bool CanCollect(const AActor& Other, int Count = 1) const
            {
                return Count > 0;
            }
        }

        // This comment is separated from the struct by a blank line.

        USTRUCT()
        struct FExampleData
        {
            UPROPERTY()
            int Value;
        }

        UENUM()
        enum EExampleState
        {
            Idle,
            Active = 2,
            Done UMETA(DisplayName = "Finished"),
        }

        delegate void FExampleDelegate(UObject Object, float32 Value);
        event void FExampleEvent(int Counter);

        namespace ExampleUtils
        {
            const float32 Gravity = 980.0;

            int Double(int Value) { return Value * 2; }
        }

        mixin void ResetExample(AExamplePickup Self) {}

        #if EDITOR
        void EditorOnlyHelper() {}
        #endif

        void TopLevelFunction() {}
        """;

    public static int LineOf(string text) =>
        Pickup.Split('\n').ToList().FindIndex(line => line.Contains(text, StringComparison.Ordinal)) + 1;
}
