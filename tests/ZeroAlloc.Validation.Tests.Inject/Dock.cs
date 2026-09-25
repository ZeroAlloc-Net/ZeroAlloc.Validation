namespace ZeroAlloc.Validation.Tests.Inject;

// No [Validate]: validated through [ValidateWith(typeof(DockValidator))] on Warehouse.Dock.
public sealed class Dock
{
    public int Number { get; set; }
}
