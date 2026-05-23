using Application.AppServices;
using Application.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.Messages;

namespace UnitTests.Application;

public class NotificationAppServiceTests
{
    private readonly IMessagePublisher publisher = Substitute.For<IMessagePublisher>();
    private readonly INotificationRepository repository = Substitute.For<INotificationRepository>();
    private readonly ILogger<NotificationAppService> logger = Substitute.For<ILogger<NotificationAppService>>();
    private readonly NotificationAppService sut;

    public NotificationAppServiceTests()
    {
        sut = new NotificationAppService(publisher, logger, repository);
    }

    [Fact]
    public async Task SendAsync_ValidCommand_ReturnsNonEmptyGuid()
    {
        var command = EmailCommand();

        var id = await sut.SendAsync(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task SendAsync_ValidCommand_AddsAndSavesNotification()
    {
        var command = EmailCommand();

        await sut.SendAsync(command, CancellationToken.None);

        await repository.Received(1).AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(NotificationTypeEnum.Email, "high",  "notifications.email.high")]
    [InlineData(NotificationTypeEnum.Email, "low",   "notifications.email.low")]
    [InlineData(NotificationTypeEnum.Push,  "high",  "notifications.push.high")]
    [InlineData(NotificationTypeEnum.Push,  "medium","notifications.push.medium")]
    public async Task SendAsync_BuildsCorrectRoutingKey(
        NotificationTypeEnum type, string priority, string expectedKey)
    {
        var command = new SendNotificationCommand("r@test.com", "S", "B", type, priority);

        await sut.SendAsync(command, CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Any<NotificationMessage>(),
            expectedKey,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_RepositoryThrows_PropagatesException()
    {
        repository.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SendAsync(EmailCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_PublisherThrows_PropagatesException()
    {
        publisher.PublishAsync(Arg.Any<NotificationMessage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Broker unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SendAsync(EmailCommand(), CancellationToken.None));
    }

    private static SendNotificationCommand EmailCommand() =>
        new("user@test.com", "Subject", "Body", NotificationTypeEnum.Email, "high");
}
