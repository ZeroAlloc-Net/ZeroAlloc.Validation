using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZValidation.Generator;

[Generator]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    private const string ValidateAttributeFqn = "ZValidation.ValidateAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validateClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Where(static s => s is not null);

        context.RegisterSourceOutput(validateClasses, Emit);
    }

    private static void Emit(SourceProductionContext ctx, INamedTypeSymbol classSymbol)
    {
        // Emission implemented in Task 7.
        ctx.AddSource($"{classSymbol.Name}Validator.g.cs", "// placeholder");
    }
}
