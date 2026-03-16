namespace ZeroAlloc.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class StopOnFirstFailureAttribute : Attribute { }
