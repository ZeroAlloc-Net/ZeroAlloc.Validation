using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.Validation.Testing;

namespace ZeroAlloc.Validation.Tests.Integration;

public class AsyncRuleAttributeTests
{
    private readonly AsyncRuleModelValidator _validator = new();

    private static AsyncRuleModel Valid() => new()
    {
        Name = null,
        Alias = "taken",
        Code = "free",
        Handle = "handle",
        Nickname = "nick",
    };

    private static ValidationFailure[] Failures(ValidationResult result) => result.Failures.ToArray();

    private static ValidationFailure OnlyFailure(ValidationResult result)
    {
        Assert.Equal(1, result.Failures.Length);
        return result.Failures[0];
    }

    [Fact]
    public async Task Valid_model_has_no_failures()
    {
        ValidationAssert.NoErrors(await _validator.ValidateAsync(Valid()));
    }

    [Fact]
    public async Task Rules_run_and_report_in_declaration_order_across_sync_and_async()
    {
        var model = Valid();
        model.Name = "taken";

        var failures = Failures(await _validator.ValidateAsync(model));

        Assert.Equal(3, failures.Length);
        Assert.All(failures, f => Assert.Equal("Name", f.PropertyName));
        Assert.Equal("Name must be at least 6 characters.", failures[0].ErrorMessage);
        Assert.Equal("Name 'taken' is taken.", failures[1].ErrorMessage);
        Assert.Equal("TAKEN", failures[1].ErrorCode);
        Assert.Equal("Name must not exceed 3 characters.", failures[2].ErrorMessage);
    }

    [Fact]
    public async Task Async_rules_of_different_properties_run_in_declaration_order()
    {
        var log = AsyncRuleLog.Start();
        var model = Valid();
        model.Name = "n";
        model.CheckAlias = true;

        await _validator.ValidateAsync(model);

        Assert.Equal(
            new[] { "UniqueName:n", "UniqueName:taken", "UniqueName:free", "UniqueName:handle", "UniqueName:nick" },
            log,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task When_false_skips_the_async_rule_without_calling_it()
    {
        var log = AsyncRuleLog.Start();
        var model = Valid();
        model.CheckAlias = false;

        ValidationAssert.NoErrors(await _validator.ValidateAsync(model));
        Assert.DoesNotContain("UniqueName:taken", log, StringComparer.Ordinal);
    }

    [Fact]
    public async Task When_true_runs_the_async_rule()
    {
        var model = Valid();
        model.CheckAlias = true;

        ValidationAssert.HasErrorWithMessage(await _validator.ValidateAsync(model), "Alias", "Alias 'taken' is taken.");
    }

    [Fact]
    public async Task Unless_true_skips_the_async_rule_without_calling_it()
    {
        var log = AsyncRuleLog.Start();
        var model = Valid();
        model.Code = "taken-code";
        model.SkipCode = true;

        ValidationAssert.NoErrors(await _validator.ValidateAsync(model));
        Assert.DoesNotContain("UniqueName:taken-code", log, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Unless_false_runs_the_async_rule()
    {
        var model = Valid();
        model.Code = "taken-code";

        ValidationAssert.HasError(await _validator.ValidateAsync(model), "Code");
    }

    [Fact]
    public async Task Property_stop_on_first_failure_skips_the_async_rule_after_a_failure()
    {
        var log = AsyncRuleLog.Start();
        var model = Valid();
        model.Handle = "taken";

        var failure = OnlyFailure(await _validator.ValidateAsync(model));
        Assert.Equal("Handle must be at least 6 characters.", failure.ErrorMessage);
        Assert.DoesNotContain("UniqueName:taken", log, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Property_stop_on_first_failure_reports_the_async_rule_when_it_fails_first()
    {
        var model = Valid();
        model.Handle = "taken-handle";

        var failure = OnlyFailure(await _validator.ValidateAsync(model));
        Assert.Equal("Handle 'taken-handle' is taken.", failure.ErrorMessage);
    }

    [Fact]
    public async Task Usage_message_error_code_and_severity_win_over_RuleMessage()
    {
        var model = Valid();
        model.Nickname = "taken-nick";

        var failure = OnlyFailure(await _validator.ValidateAsync(model));
        Assert.Equal("Nickname", failure.PropertyName);
        Assert.Equal("Nickname is in use.", failure.ErrorMessage);
        Assert.Equal("IN_USE", failure.ErrorCode);
        Assert.Equal(Severity.Warning, failure.Severity);
    }

    [Fact]
    public async Task Model_stop_on_first_failure_does_not_run_later_async_rules()
    {
        var log = AsyncRuleLog.Start();
        var validator = new AsyncRuleFailFastModelValidator();

        var result = await validator.ValidateAsync(new AsyncRuleFailFastModel { First = "taken-1", Second = "taken-2" });

        var failure = OnlyFailure(result);
        Assert.Equal("First", failure.PropertyName);
        Assert.Equal(new[] { "UniqueName:taken-1" }, log, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Model_stop_on_first_failure_runs_the_next_async_rule_when_the_first_passes()
    {
        var validator = new AsyncRuleFailFastModelValidator();

        var result = await validator.ValidateAsync(new AsyncRuleFailFastModel { First = "free", Second = "taken-2" });

        Assert.Equal("Second", OnlyFailure(result).PropertyName);
    }

    [Fact]
    public void Sync_Validate_throws_instead_of_skipping_the_async_rules()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _validator.Validate(Valid()));
        Assert.Contains("ValidateAsync", ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(AsyncRuleModel).FullName!, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_Validate_throws_through_the_base_type_too()
    {
        ValidatorFor<AsyncRuleModel> validator = _validator;
        Assert.Throws<NotSupportedException>(() => validator.Validate(Valid()));
    }

    [Fact]
    public async Task The_token_reaches_the_async_rule()
    {
        var validator = new AsyncRuleCancellationModelValidator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => validator.ValidateAsync(new AsyncRuleCancellationModel { Name = "x" }, cts.Token).AsTask());
    }

    [Fact]
    public async Task An_uncancelled_token_lets_validation_complete()
    {
        var validator = new AsyncRuleCancellationModelValidator();
        using var cts = new CancellationTokenSource();

        ValidationAssert.NoErrors(await validator.ValidateAsync(new AsyncRuleCancellationModel { Name = "x" }, cts.Token));
    }
}
