namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>Why the generated validator cannot declare a type parameter, ZV0029.</summary>
internal enum TypeParameterClash
{
    /// <summary>The validator can declare it.</summary>
    None,

    /// <summary>A type parameter further out along the containing chain has the same name.</summary>
    Repeated,

    /// <summary>It is named like the validator itself.</summary>
    ValidatorName,

    /// <summary>It is named like a member the validator declares or reserves.</summary>
    MemberName,
}
