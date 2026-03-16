namespace ZValidation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class StopOnFirstFailureAttribute : Attribute { }
