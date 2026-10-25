// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Controllers;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Profile;

namespace Waterfall.Api.Tests;

public class ProfileControllerTests
{
    private readonly FakeProfileService _service = new();
    private readonly ProfileController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public ProfileControllerTests()
    {
        _controller = new ProfileController(_service, NullLogger<ProfileController>.Instance);
    }

    private void Authenticate()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()),
            new Claim(ClaimTypes.Role, "JobSeeker")
        ], "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetAddress_WhenUnauthorized_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        ActionResult<UserAddressDto> result = await _controller.GetAddress(CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAddress_WhenAuthenticated_ReturnsStoredAddress()
    {
        Authenticate();
        _service.StoredAddress = "вул. Хрещатик, 22, Київ";

        ActionResult<UserAddressDto> result = await _controller.GetAddress(CancellationToken.None);
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        UserAddressDto dto = Assert.IsType<UserAddressDto>(ok.Value);
        Assert.Equal("вул. Хрещатик, 22, Київ", dto.Address);
    }

    [Fact]
    public async Task SaveAddress_WhenAuthenticated_UpdatesAddressInDatabase()
    {
        Authenticate();
        UserAddressDto request = new() { Address = "Київ, вул. Польова, 15" };

        ActionResult<UserAddressDto> result = await _controller.SaveAddress(request, CancellationToken.None);
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        UserAddressDto dto = Assert.IsType<UserAddressDto>(ok.Value);

        Assert.Equal("Київ, вул. Польова, 15", dto.Address);
        Assert.Equal("Київ, вул. Польова, 15", _service.StoredAddress);
        Assert.Equal(_userId, _service.LastUserId);
    }

    private sealed class FakeProfileService : IUserProfileService
    {
        public string? StoredAddress { get; set; }
        public Guid? LastUserId { get; private set; }

        public Task<string?> GetUserAddressAsync(Guid userId, CancellationToken ct = default)
        {
            LastUserId = userId;
            return Task.FromResult(StoredAddress);
        }

        public Task SaveUserAddressAsync(Guid userId, string address, CancellationToken ct = default)
        {
            LastUserId = userId;
            StoredAddress = address;
            return Task.CompletedTask;
        }
    }
}
