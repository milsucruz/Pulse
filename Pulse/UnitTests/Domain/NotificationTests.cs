using Domain.Entities;
using Domain.Enums;

namespace UnitTests.Domain;

public class NotificationTests
{
    [Fact]
    public void Create_SetsInitialState()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal("user@test.com", notification.Recipient);
        Assert.Equal("Subject", notification.Subject);
        Assert.Equal("Body", notification.Body);
        Assert.Equal(NotificationTypeEnum.Email, notification.Type);
        Assert.Equal(NotificationStatusEnum.Pending, notification.Status);
        Assert.Null(notification.ProcessedAt);
        Assert.Null(notification.ErrorMessage);
        Assert.False(notification.IsDispatched);
    }

    [Fact]
    public void MarkAsProcessed_SetsStatusAndProcessedAt()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);
        var before = DateTime.UtcNow;

        notification.MarkAsProcessed();

        Assert.Equal(NotificationStatusEnum.Sent, notification.Status);
        Assert.NotNull(notification.ProcessedAt);
        Assert.True(notification.ProcessedAt >= before);
    }

    [Fact]
    public void MarkAsFailed_SetsStatusAndErrorMessage()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);

        notification.MarkAsFailed("SMTP timeout");

        Assert.Equal(NotificationStatusEnum.Failed, notification.Status);
        Assert.Equal("SMTP timeout", notification.ErrorMessage);
    }

    [Fact]
    public void MarkAsDispatched_SetsIsDispatchedTrue()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);

        notification.MarkAsDispatched();

        Assert.True(notification.IsDispatched);
    }

    [Fact]
    public void MarkAsProcessed_WhenAlreadySent_IsIdempotent()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);
        notification.MarkAsProcessed();
        var firstProcessedAt = notification.ProcessedAt;

        notification.MarkAsProcessed();

        Assert.Equal(NotificationStatusEnum.Sent, notification.Status);
        Assert.Equal(firstProcessedAt, notification.ProcessedAt);
    }

    [Fact]
    public void MarkAsProcessed_WhenFailed_Throws()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);
        notification.MarkAsFailed("error");

        Assert.Throws<InvalidOperationException>(() => notification.MarkAsProcessed());
    }

    [Fact]
    public void MarkAsFailed_WhenAlreadyFailed_Throws()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);
        notification.MarkAsFailed("first error");

        Assert.Throws<InvalidOperationException>(() => notification.MarkAsFailed("second error"));
    }

    [Fact]
    public void MarkAsFailed_WhenAlreadySent_Throws()
    {
        var notification = Notification.Create("user@test.com", "Subject", "Body", NotificationTypeEnum.Email);
        notification.MarkAsProcessed();

        Assert.Throws<InvalidOperationException>(() => notification.MarkAsFailed("error"));
    }
}
