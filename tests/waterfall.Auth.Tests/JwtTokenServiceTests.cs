using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Waterfall.Api.Options;
using Waterfall.Api.Services;

namespace Waterfall.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="JwtTokenService"/>: issue and validate access-JWTs.
/// </summary>
public sealed class JwtTokenServiceTests
{
    private const string Key = "0123456789abcdef0123456789abcdef"; // 32 chars / 256 bit
    private const string Issuer = "Waterfall";
    private const string Audience = "WaterfallClients";

    private static JwtTokenService CreateService(string key = Key, string issuer = Issuer, string audience = Audience, int expiresMinutes = 15)
    {
        var options = Options.Create(new JwtOptions { Key = key, Issuer = issuer, Audience = audience, ExpiresMinutes = expiresMinutes });
        return new JwtTokenService(options);
    }

    [Fact]
    public void Generate_CarriesExpectedClaims_AndExpiry()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        (string token, DateTimeOffset expiresAt) = service.Generate(userId, "seeker@example.com", "JobSeeker");

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.InRange(expiresAt - before, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt = handler.ReadJwtToken(token);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(userId.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(userId.ToString(), jwt.Claims.First(c => c.Type == "userId").Value);
        Assert.Equal("seeker@example.com", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("JobSeeker", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value));
    }

    [Fact]
    public void Generate_TwoTokens_HaveDifferentJti()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var handler = new JwtSecurityTokenHandler();

        string first = service.Generate(userId, "a@example.com", "JobSeeker").Token;
        string second = service.Generate(userId, "a@example.com", "JobSeeker").Token;

        string Jti(string token) => handler.ReadJwtToken(token).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        Assert.NotEqual(Jti(first), Jti(second));
    }

    [Fact]
    public void Validate_ValidToken_ReturnsPrincipal()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        string token = service.Generate(userId, "seeker@example.com", "Analyst").Token;

        ClaimsPrincipal? principal = service.Validate(token);

        Assert.NotNull(principal);
        // NOTE: JwtSecurityTokenHandler maps inbound "sub" to ClaimTypes.NameIdentifier by default.
        string? subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        Assert.Equal(userId.ToString(), subject);
        Assert.Equal("Analyst", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void Validate_TamperedToken_ReturnsNull()
    {
        var service = CreateService();
        string token = service.Generate(Guid.NewGuid(), "seeker@example.com", "JobSeeker").Token;

        char last = token[^1];
        string tampered = token[..^1] + (last == 'a' ? 'b' : 'a');

        Assert.Null(service.Validate(tampered));
    }

    [Fact]
    public void Validate_WrongIssuerOrAudience_ReturnsNull()
    {
        string token = CreateService().Generate(Guid.NewGuid(), "seeker@example.com", "JobSeeker").Token;

        Assert.Null(CreateService(issuer: "EvilIssuer").Validate(token));
        Assert.Null(CreateService(audience: "EvilAudience").Validate(token));
    }

    [Fact]
    public void Validate_ExpiredToken_ReturnsNull()
    {
        // Issued with a negative lifetime: already expired at validation time (ClockSkew = Zero).
        string token = CreateService(expiresMinutes: -1).Generate(Guid.NewGuid(), "seeker@example.com", "JobSeeker").Token;

        Assert.Null(CreateService().Validate(token));
    }

    [Fact]
    public void Validate_MalformedToken_ReturnsNull()
    {
        var service = CreateService();

        Assert.Null(service.Validate("not-a-token"));
        Assert.Null(service.Validate(string.Empty));
    }
}
