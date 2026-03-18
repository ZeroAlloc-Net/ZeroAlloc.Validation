---
id: aspnetcore
title: ASP.NET Core Integration
slug: /docs/aspnetcore
description: Add automatic request validation with AddZeroAllocAspNetCoreValidation() and a generated action filter.
sidebar_position: 7
---

## Installation

Two NuGet packages are required:

```bash
dotnet add package ZeroAlloc.Validation
dotnet add package ZeroAlloc.Validation.AspNetCore
```

## Setup in Program.cs

```csharp
builder.Services.AddControllers();
builder.Services.AddZeroAllocAspNetCoreValidation();
```

`AddZeroAllocAspNetCoreValidation()` is source-generated — it lives in the generated code, not in a library method. It:

- Registers each discovered validator as `Singleton` via `TryAddSingleton<ValidatorFor<T>, TValidator>()`
- Registers `ZeroAllocValidationActionFilter` as `Transient`
- Adds `ZeroAllocValidationActionFilter` to `MvcOptions.Filters`

## How it works

The generated `ZeroAllocValidationActionFilter` implements `IAsyncActionFilter` and intercepts every incoming request:

- `OnActionExecutionAsync` iterates over `context.ActionArguments.Values`
- For each argument, calls a generated type-switch `Dispatch(arg)` method that resolves the validator via `ValidatorFor<T>` from DI
- If validation fails: short-circuits the request and returns HTTP **422 Unprocessable Entity** with `ValidationProblemDetails`
- On success: the request proceeds to the controller

The type-switch is generated at build time — there is no reflection and no dictionary lookup at runtime.

```mermaid
sequenceDiagram
    participant Client
    participant Filter as ZeroAllocValidationActionFilter
    participant Validator as Generated Validator
    participant Controller

    Client->>Filter: HTTP Request
    Filter->>Validator: Validate(actionArgument)
    alt IsValid
        Validator-->>Filter: ValidationResult (valid)
        Filter->>Controller: proceed
        Controller-->>Client: 200 OK
    else Has failures
        Validator-->>Filter: ValidationResult (failures)
        Filter-->>Client: 422 Unprocessable Entity + ValidationProblemDetails
    end
```

## Response format

On validation failure, the filter returns:

- HTTP **422 Unprocessable Entity**
- Body: `ValidationProblemDetails` with an `errors` dictionary
- Each key is the `PropertyName` from the failure (e.g., `"Email"`, `"ShippingAddress.Street"`)
- Each value is an array of error message strings

Example response:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "One or more validation errors occurred.",
  "status": 422,
  "errors": {
    "Email": ["Email must not be empty.", "Email must be a valid email address."],
    "Amount": ["Amount must be greater than 0."]
  }
}
```

## DI lifetimes

All validators are registered as `Singleton` via `TryAddSingleton`. This is safe because generated validators are stateless. The registrations are idempotent — calling `AddZeroAllocAspNetCoreValidation()` multiple times or alongside `AddZeroAllocValidators()` produces no duplicates.

## Accessing the result manually

You can also inject and call validators directly in a controller, without using the action filter:

```csharp
public class OrdersController : ControllerBase
{
    private readonly ValidatorFor<CreateOrderRequest> _validator;

    public OrdersController(ValidatorFor<CreateOrderRequest> validator) => _validator = validator;

    [HttpPost]
    public IActionResult Create([FromBody] CreateOrderRequest request)
    {
        var result = _validator.Validate(request);
        if (!result.IsValid)
        {
            var pd = new ValidationProblemDetails();
            foreach (ref readonly var f in result.Failures)
            {
                if (!pd.Errors.ContainsKey(f.PropertyName))
                    pd.Errors[f.PropertyName] = Array.Empty<string>();
                pd.Errors[f.PropertyName] = [.. pd.Errors[f.PropertyName], f.ErrorMessage];
            }
            return UnprocessableEntity(pd);
        }
        // process valid request...
        return Ok();
    }
}
```
