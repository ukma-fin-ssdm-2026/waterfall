// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

public class EfUserRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;

    public EfUserRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateAsync_AddsUser_AndGetByEmailReturnsIt()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfUserRepository repository = new(db);

        string email = $"user_{Guid.NewGuid()}@waterfall.com";
        UserDB user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hashed_pw",
            Role = "JobSeeker",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.CreateAsync(user);

        UserDB? found = await repository.GetByEmailAsync(email);
        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);
        Assert.Equal(email, found.Email);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesUserProperties()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfUserRepository repository = new(db);

        string email = $"user_{Guid.NewGuid()}@waterfall.com";
        UserDB user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "initial_hash",
            Role = "JobSeeker",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.CreateAsync(user);

        user.PasswordHash = "updated_hash";
        await repository.UpdateAsync(user);

        UserDB? updated = await repository.GetByEmailAsync(email);
        Assert.NotNull(updated);
        Assert.Equal("updated_hash", updated.PasswordHash);
    }

    [Fact]
    public async Task GetByEmailAsync_ReturnsNull_WhenNotFound()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfUserRepository repository = new(db);

        UserDB? result = await repository.GetByEmailAsync("nonexistent@waterfall.com");
        Assert.Null(result);
    }
}
