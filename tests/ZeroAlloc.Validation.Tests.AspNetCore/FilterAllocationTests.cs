using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.TestHelpers;

#pragma warning disable MA0004 // ConfigureAwait: suppressed because xUnit1030 prohibits ConfigureAwait in test methods

namespace ZeroAlloc.Validation.Tests.AspNetCore;

/// <summary>
/// Pins what the generated action filter itself costs per request on the valid path. MVC hands the
/// filter its arguments as a <c>Dictionary&lt;string, object?&gt;</c> behind
/// <c>IDictionary</c>, and enumerating it through the interface boxed an enumerator on every
/// request, issue #316.
/// </summary>
public class FilterAllocationTests
{
    [Fact]
    public void ValidArguments_FilterAllocatesNothing()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAspNetCoreValidation();
        using var provider = services.BuildServiceProvider();
        var filter = provider.GetRequiredService<ZeroAllocValidationActionFilter>();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var arguments = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = new SampleModel { Name = "Widget", Quantity = 5 },
            ["other"] = new SampleRecordModel { Name = "Gadget" },
        };
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), arguments, controller: new object());
        var executed = Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: new object()));
        ActionExecutionDelegate next = () => executed;

        bool Run()
        {
            var task = filter.OnActionExecutionAsync(executing, next);
            return task.IsCompletedSuccessfully && executing.Result is null;
        }

        Assert.True(Run());

        // Every measured call must complete synchronously and pass, or the count is not the filter's.
        var allPassed = true;
        AllocationGate.AssertBudget(
            0, 10_000, () => allPassed &= Run(), "ZeroAllocValidationActionFilter.OnActionExecutionAsync, valid arguments");
        Assert.True(allPassed);
    }
}
