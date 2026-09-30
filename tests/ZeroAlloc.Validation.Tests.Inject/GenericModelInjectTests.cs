using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Inject;

// Issue #238: AddZeroAllocValidators registers every closing of a generic model that a
// registered validator takes, closed, and lists each as an IModelValidator. The generated
// AddPageValidator<TItem>() does the same for a closing used only at the root.
public class GenericModelInjectTests
{
    private static ServiceProvider BuildProvider(System.Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        before?.Invoke(services);
        services.AddZeroAllocValidators();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void AddZeroAllocValidators_ResolvesAModelComposingGenericClosings()
    {
        using var sp = BuildProvider();

        var validator = sp.GetRequiredService<ValidatorFor<Shelf>>();
        var shelf = new Shelf { Code = "A1", Page = new Page<Dock> { Title = "t" } };
        Assert.True(validator.Validate(shelf).IsValid);

        shelf.Page.Title = "";
        shelf.Page.Lines.Add(new Line<Dock> { Quantity = 0, Item = new Dock { Number = 1 } });
        var result = validator.Validate(shelf);
        Assert.Equal(["Page.Title", "Page.Lines[0].Quantity"], result.Failures.ToArray().Select(f => f.PropertyName), System.StringComparer.Ordinal);

        Assert.IsType<PageValidator<Dock>>(sp.GetRequiredService<ValidatorFor<Page<Dock>>>());
        Assert.IsType<LineValidator<Dock>>(sp.GetRequiredService<ValidatorFor<Line<Dock>>>());
    }

    [Fact]
    public void AClosingNothingRegistered_IsNotResolved()
    {
        using var sp = BuildProvider();

        // ZeroAlloc.Mediator skips validation when GetService returns null; an open-generic
        // registration would have thrown here instead.
        Assert.Null(sp.GetService<ValidatorFor<Page<Shelf>>>());
        Assert.Null(sp.GetService<ValidatorFor<string>>());
    }

    [Fact]
    public void EachRegisteredClosing_IsListedOnceAsAModelValidator()
    {
        using var sp = BuildProvider();

        var registry = sp.GetServices<IModelValidator>().ToList();

        Assert.Equal(2, registry.Count);
        Assert.Same(sp.GetRequiredService<ValidatorFor<Page<Dock>>>(), registry.First(v => v.ModelType == typeof(Page<Dock>)));
        Assert.Same(sp.GetRequiredService<ValidatorFor<Line<Dock>>>(), registry.First(v => v.ModelType == typeof(Line<Dock>)));
    }

    [Fact]
    public void ARegistrationTheApplicationMadeFirst_Wins_AndIsTheOneListed()
    {
        using var sp = BuildProvider(services => services.AddSingleton<ValidatorFor<Page<Dock>>, AcceptingPageValidator>());

        var registered = sp.GetRequiredService<ValidatorFor<Page<Dock>>>();
        Assert.IsType<AcceptingPageValidator>(registered);
        Assert.Same(registered, sp.GetServices<IModelValidator>().First(v => v.ModelType == typeof(Page<Dock>)));

        var shelf = new Shelf { Code = "A1", Page = new Page<Dock> { Title = "" } };
        Assert.True(sp.GetRequiredService<ValidatorFor<Shelf>>().Validate(shelf).IsValid);
    }

    // Phase 2: the generated AddPageValidator<TItem>() registers a closing used only at the root,
    // with every closing its validator takes.
    [Fact]
    public void AddPageValidator_RegistersARootClosingAndTheClosingsItTakes()
    {
        var services = new ServiceCollection();
        services.AddPageValidator<Dock>();
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var validator = sp.GetRequiredService<ValidatorFor<Page<Dock>>>();
        Assert.IsType<PageValidator<Dock>>(validator);
        Assert.IsType<LineValidator<Dock>>(sp.GetRequiredService<ValidatorFor<Line<Dock>>>());

        var page = new Page<Dock> { Title = "" };
        page.Lines.Add(new Line<Dock> { Quantity = 0 });
        var result = validator.Validate(page);
        Assert.Equal(
            ["Title", "Lines[0].Quantity", "Lines[0].Item"],
            result.Failures.ToArray().Select(f => f.PropertyName),
            System.StringComparer.Ordinal);

        Assert.Equal(
            [typeof(Page<Dock>), typeof(Line<Dock>)],
            sp.GetServices<IModelValidator>().Select(v => v.ModelType));
    }

    [Fact]
    public void AddPageValidator_RegistersOnlyTheClosingItIsCalledWith()
    {
        var services = new ServiceCollection();
        services.AddPageValidator<Dock>();
        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<ValidatorFor<Page<Shelf>>>());
        Assert.Null(sp.GetService<ValidatorFor<Line<Shelf>>>());
    }

    [Fact]
    public void AddPageValidator_BesideAddZeroAllocValidators_RegistersEachClosingOnce()
    {
        using var sp = BuildProvider(services => services.AddPageValidator<Dock>().AddPageValidator<Dock>().AddLineValidator<Dock>());

        Assert.Collection(sp.GetServices<ValidatorFor<Page<Dock>>>(), static _ => { });
        Assert.Collection(sp.GetServices<ValidatorFor<Line<Dock>>>(), static _ => { });
        Assert.Equal(2, sp.GetServices<IModelValidator>().Count());
    }

    [Fact]
    public void AddPageValidator_KeepsARegistrationTheApplicationMadeFirst()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<Page<Dock>>, AcceptingPageValidator>();
        services.AddPageValidator<Dock>();
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var registered = sp.GetRequiredService<ValidatorFor<Page<Dock>>>();
        Assert.IsType<AcceptingPageValidator>(registered);
        Assert.Same(registered, sp.GetServices<IModelValidator>().First(v => v.ModelType == typeof(Page<Dock>)));
    }

    private sealed class AcceptingPageValidator : ValidatorFor<Page<Dock>>
    {
        public override ValidationResult Validate(Page<Dock> instance) => new([]);
    }
}
