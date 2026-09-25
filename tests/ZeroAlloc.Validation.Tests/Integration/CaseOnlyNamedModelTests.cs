using System.Collections.Generic;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

// Two nested or collection properties whose names differ only in case, such as Address and
// address, used to give the generated validator two fields and two constructor parameters of
// one name. This compiles only if the later one of each pair is renamed, and the tests check
// that each property is still validated through its own validator.
public class CaseOnlyNamedModelTests
{
    private readonly CaseOnlyNamedModelValidator _validator = new(
        addressValidator: new KeywordNamedChildValidator(),
        address2Validator: new KeywordNamedChildValidator(),
        itemsValidator: new KeywordNamedChildValidator(),
        items2Validator: new KeywordNamedChildValidator());

    private static KeywordNamedChild Valid() => new() { @namespace = "n" };

    private static KeywordNamedChild Invalid() => new() { @namespace = "" };

    [Fact]
    public void ValidModel_Passes() =>
        ValidationAssert.NoErrors(_validator.Validate(new CaseOnlyNamedModel
        {
            Address = Valid(),
            address = Valid(),
            Items = new List<KeywordNamedChild> { Valid() },
            items = new List<KeywordNamedChild> { Valid() },
        }));

    [Fact]
    public void EachNestedProperty_IsValidatedOnItsOwn()
    {
        var upper = _validator.Validate(new CaseOnlyNamedModel { Address = Invalid(), address = Valid() });
        var lower = _validator.Validate(new CaseOnlyNamedModel { Address = Valid(), address = Invalid() });

        ValidationAssert.HasError(upper, "Address.namespace");
        Assert.Equal(1, upper.Failures.Length);
        ValidationAssert.HasError(lower, "address.namespace");
        Assert.Equal(1, lower.Failures.Length);
    }

    [Fact]
    public void EachCollectionProperty_IsValidatedOnItsOwn()
    {
        var result = _validator.Validate(new CaseOnlyNamedModel
        {
            Items = new List<KeywordNamedChild> { Valid() },
            items = new List<KeywordNamedChild> { Valid(), Invalid() },
        });

        ValidationAssert.HasError(result, "items[1].namespace");
        Assert.Equal(1, result.Failures.Length);
    }
}
