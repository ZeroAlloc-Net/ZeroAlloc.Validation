using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Pipeline.Generators;
using ZeroAlloc.Validation.Generator.Shared;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Emits the pipeline behavior chains of one validator, through <see cref="PipelineEmitter.EmitChain"/>.
/// Every lambda of a chain is <c>static</c>, so a call through it allocates no delegate. The
/// validator itself is the pipeline state, threaded through every behavior's <c>Handle</c> as its
/// second argument whether or not the validation body reads instance state, so a behavior has one
/// signature on every model. A body that reads the nested validators, <see cref="ReadsInstanceState"/>,
/// reaches them through the state, <c>s{depth}</c>, instead of capturing <c>this</c>, #410.
/// </summary>
internal sealed class BehaviorChains
{
    private readonly string _validatorType;

    /// <param name="validatorType">The validator's own type, with its type parameters when the model is generic.</param>
    /// <param name="readsInstanceState">Whether the validation body reads the nested validators' instance fields.</param>
    public BehaviorChains(string validatorType, bool readsInstanceState)
    {
        _validatorType = validatorType;
        ReadsInstanceState = readsInstanceState;
    }

    /// <summary>Whether the validation body reads the nested validators' instance fields.</summary>
    public bool ReadsInstanceState { get; }

    /// <summary>The chains of the validator generated for <paramref name="model"/>.</summary>
    public static BehaviorChains For(INamedTypeSymbol model, bool readsInstanceState)
        => new(
            GeneratedValidatorNames.ValidatorName(model) + GenericSignature.ParameterList(GenericSignature.TypeParameters(model)),
            readsInstanceState);

    /// <summary>
    /// The expression that reads the validator's members in the innermost lambda of a chain
    /// <paramref name="depth"/> behaviors deep, or <c>null</c> when the body reads none.
    /// </summary>
    public string? StateAt(int depth) => ReadsInstanceState ? $"s{depth}" : null;

    /// <summary>The chain expression for <paramref name="behaviors"/>, to follow <c>return</c> or <c>=&gt;</c>.</summary>
    public string Emit(List<PipelineBehaviorInfo> behaviors, PipelineShape shape)
        => PipelineEmitter.EmitChain(behaviors, shape with
        {
            State = new PipelineState { TypeName = _validatorType, Argument = "this" },
        });
}
