using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence.Repositories;
using IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests;

[Collection("SqlServer")]
public class NotificationRepositoryTests : IAsyncLifetime
{
    private readonly SqlServerFixture fixture;
    private Infrastructure.Persistence.NotificationDbContext dbContext = null!;
    private NotificationRepository sut = null!;

    public NotificationRepositoryTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        dbContext = fixture.CreateDbContext();
        sut = new NotificationRepository(dbContext);
        await dbContext.Notifications.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await dbContext.DisposeAsync();

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_RoundTrip()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);

        await sut.AddAsync(notification, CancellationToken.None);
        await sut.SaveChangesAsync(CancellationToken.None);

        var retrieved = await sut.GetByIdAsync(notification.Id, CancellationToken.None);

        Assert.NotNull(retrieved);
        Assert.Equal(notification.Id, retrieved.Id);
        Assert.Equal("user@test.com", retrieved.Recipient);
        Assert.Equal("Subject", retrieved.Subject);
        Assert.Equal(NotificationTypeEnum.Email, retrieved.Type);
        Assert.Equal(NotificationStatusEnum.Pending, retrieved.Status);
        Assert.False(retrieved.IsDispatched);
        Assert.Null(retrieved.ProcessedAt);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveChangesAsync_AfterMarkAsProcessed_PersistsStatusChange()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);
        await sut.AddAsync(notification, CancellationToken.None);
        await sut.SaveChangesAsync(CancellationToken.None);

        notification.MarkAsProcessed();
        await sut.SaveChangesAsync(CancellationToken.None);

        var retrieved = await sut.GetByIdAsync(notification.Id, CancellationToken.None);

        Assert.NotNull(retrieved);
        Assert.Equal(NotificationStatusEnum.Sent, retrieved.Status);
        Assert.NotNull(retrieved.ProcessedAt);
    }
}
