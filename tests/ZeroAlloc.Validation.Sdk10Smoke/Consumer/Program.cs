using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Validation;
using ZeroAlloc.Validation.Options;
using ZeroAlloc.Validation.Sdk10Smoke;

// Each call below exists only if its package's generator ran: OrderValidator from
// ZeroAlloc.Validation, AddZeroAllocValidators from .Inject, ValidateWithZeroAlloc from
// .Options and AddZeroAllocAspNetCoreValidation from .AspNetCore.
var services = new ServiceCollection();
services.AddZeroAllocValidators();
services.AddOptions<SmtpOptions>().ValidateWithZeroAlloc();
services.AddZeroAllocAspNetCoreValidation();

var result = new OrderValidator().Validate(new Order { Reference = "" });
if (result.IsValid)
{
    Console.Error.WriteLine("FAIL: an empty Reference passed validation");
    return 1;
}

Console.WriteLine("SDK 10 consumer: all four generators loaded, validation ran.");
return 0;

namespace ZeroAlloc.Validation.Sdk10Smoke
{
    [Validate]
    public class Order
    {
        [NotEmpty] public string Reference { get; set; } = "";
    }

    [Validate]
    public class SmtpOptions
    {
        [NotEmpty] public string Host { get; set; } = "";
    }
}
