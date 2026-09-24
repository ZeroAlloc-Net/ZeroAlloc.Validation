namespace ZeroAlloc.Validation.PackSmoke;

/// <summary>The packages a scaffolded consumer references, besides the core two.</summary>
[Flags]
public enum ConsumerPackages
{
    None       = 0,
    Options    = 1,
    Inject     = 2,
    AspNetCore = 4,
}
