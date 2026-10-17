using System.ComponentModel.DataAnnotations;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Auth.Tests;

/// <summary>
/// Validation tests for auth request models (DataAnnotations).
/// Login intentionally has no complexity policy so it cannot leak it (THR-01).
/// </summary>
public sealed class AuthValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Register_ValidRequest_Passes()
    {
        var results = Validate(new RegisterRequest { Email = "jobseeker@example.com", Password = "Secret123456", Role = UserRole.Analyst });

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a")]
    [InlineData("")]
    public void Register_InvalidEmail_Fails(string email)
    {
        var results = Validate(new RegisterRequest { Email = email, Password = "Secret123456" });

        Assert.Contains(results, r => r.MemberNames.Contains("Email"));
    }

    [Theory]
    [InlineData("Short1")] // too short (< 12)
    [InlineData("ThisPasswordIsWayTooLong123456789")] // too long (> 32)
    [InlineData("NoDigitsHere!!")] // no digit
    [InlineData("")]
    public void Register_InvalidPassword_Fails(string password)
    {
        var results = Validate(new RegisterRequest { Email = "jobseeker@example.com", Password = password });

        Assert.Contains(results, r => r.MemberNames.Contains("Password"));
    }

    [Fact]
    public void Register_InvalidRole_Fails()
    {
        var results = Validate(new RegisterRequest { Email = "jobseeker@example.com", Password = "Secret123456", Role = (UserRole)999 });

        Assert.Contains(results, r => r.MemberNames.Contains("Role"));
    }

    [Fact]
    public void Login_ValidRequest_Passes_WithoutComplexityCheck()
    {
        // A weak password is still a *valid* login request: complexity must not leak via login (THR-01).
        var results = Validate(new LoginRequest { Email = "jobseeker@example.com", Password = "x" });

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("not-an-email", "Secret123456")]
    [InlineData("", "Secret123456")]
    [InlineData("jobseeker@example.com", "")]
    public void Login_MissingOrBadFields_Fail(string email, string password)
    {
        var results = Validate(new LoginRequest { Email = email, Password = password });

        Assert.NotEmpty(results);
    }
}
