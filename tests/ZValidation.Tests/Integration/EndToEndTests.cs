using Xunit;
using ZValidation;
using ZValidation.Testing;

namespace ZValidation.Tests.Integration;

[Validate]
public class Person
{
    [NotEmpty(Message = "Name is required.")]
    [ZValidation.MaxLength(100)]
    public string Name { get; set; } = "";

    [ZValidation.EmailAddress]
    public string Email { get; set; } = "";

    [GreaterThan(0)]
    [LessThan(120)]
    public int Age { get; set; }
}

public class EndToEndTests
{
    private readonly PersonValidator _validator = new();

    [Fact]
    public void Valid_Person_PassesValidation()
    {
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.NoErrors(result);
    }

    [Fact]
    public void Empty_Name_FailsWithCustomMessage()
    {
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "Name");
        Assert.Equal("Name is required.", result.Failures[0].ErrorMessage);
    }

    [Fact]
    public void Name_TooLong_FailsValidation()
    {
        var person = new Person { Name = new string('x', 101), Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Name");
    }

    [Fact]
    public void Empty_Name_Only_ReportsOneFailureForNameProperty()
    {
        var person = new Person { Name = "", Email = "alice@example.com", Age = 30 };
        var result = _validator.Validate(person);
        Assert.Equal(1, result.Failures.ToArray().Count(f => f.PropertyName == "Name"));
    }

    [Fact]
    public void Invalid_Email_FailsValidation()
    {
        var person = new Person { Name = "Alice", Email = "not-an-email", Age = 30 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Email");
    }

    [Fact]
    public void Age_Zero_FailsGreaterThan()
    {
        var person = new Person { Name = "Alice", Email = "alice@example.com", Age = 0 };
        var result = _validator.Validate(person);
        ValidationAssert.HasError(result, "Age");
    }

    [Fact]
    public void Multiple_Invalid_Properties_ReportsAllFailures()
    {
        var person = new Person { Name = "", Email = "bad", Age = -1 };
        var result = _validator.Validate(person);
        Assert.False(result.IsValid);
        ValidationAssert.HasError(result, "Name");
        ValidationAssert.HasError(result, "Email");
        ValidationAssert.HasError(result, "Age");
    }
}
