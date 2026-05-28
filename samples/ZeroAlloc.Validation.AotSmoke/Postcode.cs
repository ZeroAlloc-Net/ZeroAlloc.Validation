namespace ZeroAlloc.Validation.AotSmoke;

// External (non-[Validate]) type — the generator can't emit a validator
// for it directly. [ValidateWith] points at a hand-written ValidatorFor<T>.
public sealed class Postcode
{
    public string Value { get; set; } = "";
}
