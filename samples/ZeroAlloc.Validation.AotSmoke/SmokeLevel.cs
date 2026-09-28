namespace ZeroAlloc.Validation.AotSmoke;

/// <summary>An enum validated by [IsInEnum], directly and as a nullable, and by a custom rule.</summary>
public enum SmokeLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
}
