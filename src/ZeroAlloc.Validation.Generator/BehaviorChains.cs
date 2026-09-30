using System.Collections.Generic;
using System.Text;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Emits the pipeline behavior chains of one validator. A body that reads no instance state is
/// chained through <c>static</c> lambdas, which the compiler creates once. A body that reads the
/// nested validators, <see cref="ReadsInstanceState"/>, cannot use static lambdas, #294, and a
/// lambda capturing the validator would be allocated on every call, so its chain caches each
/// level's delegate in an instance field, #298. Those fields are collected here and declared by
/// <see cref="AppendDeclarations"/>.
/// </summary>
internal sealed class BehaviorChains
{
    /// <summary>The chain of <c>Validate</c>.</summary>
    public static readonly ChainKind Sync = new("__validateNext", isAsync: false);

    /// <summary>The chain of <c>ValidateAsync</c>. A validator emits at most one.</summary>
    public static readonly ChainKind Async = new("__validateAsyncNext", isAsync: true);

    private readonly string _modelName;
    private readonly List<string> _declarations = new();

    public BehaviorChains(string modelName, bool readsInstanceState)
    {
        _modelName = modelName;
        ReadsInstanceState = readsInstanceState;
    }

    /// <summary>Whether the validation body reads the nested validators' instance fields.</summary>
    public bool ReadsInstanceState { get; }

    /// <summary>The chain expression for <paramref name="behaviors"/>, to follow <c>return</c> or <c>=&gt;</c>.</summary>
    public string Emit(List<PipelineBehaviorInfo> behaviors, PipelineShape shape, ChainKind kind)
    {
        if (!ReadsInstanceState)
            return PipelineEmitter.EmitChain(behaviors, shape);

        var nextDelegateType = kind.IsAsync
            ? $"global::System.Func<{_modelName}, global::System.Threading.CancellationToken, {RuleEmitter.AsyncResultType}>"
            : $"global::System.Func<{_modelName}, global::ZeroAlloc.Validation.ValidationResult>";
        var chain = PipelineEmitter.EmitCachedChain(behaviors, shape, nextDelegateType, kind.CacheFieldPrefix);
        _declarations.AddRange(chain.MemberDeclarations);
        return chain.Expression;
    }

    /// <summary>Declares the delegate cache fields of the chains emitted so far, at class scope.</summary>
    public void AppendDeclarations(StringBuilder sb)
    {
        if (_declarations.Count == 0)
            return;

        sb.AppendLine();
        for (var i = 0; i < _declarations.Count; i++)
            sb.AppendLine($"    {_declarations[i]}");
    }

    /// <summary>Which of the validator's chains is emitted, naming its cache fields.</summary>
    internal sealed class ChainKind
    {
        public ChainKind(string cacheFieldPrefix, bool isAsync)
        {
            CacheFieldPrefix = cacheFieldPrefix;
            IsAsync = isAsync;
        }

        public string CacheFieldPrefix { get; }

        public bool IsAsync { get; }
    }
}
