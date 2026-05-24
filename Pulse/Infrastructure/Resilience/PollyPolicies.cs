using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.IO;

namespace Infrastructure.Resilience;

public static class PollyPolicies
{
    public static IServiceCollection AddNotificationSenderPolicies(this IServiceCollection services)
    {
        AddSenderPipeline(services, "email-sender");
        AddSenderPipeline(services, "push-sender");
        return services;
    }

    private static void AddSenderPipeline(IServiceCollection services, string key)
    {
        services.AddResiliencePipeline<string, bool>(key, builder =>
        {
            builder
                .AddRetry(new RetryStrategyOptions<bool>
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder<bool>()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutException>()
                        .Handle<TaskCanceledException>()
                        .Handle<IOException>()
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<bool>
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(60),
                    MinimumThroughput = 5,
                    ShouldHandle = new PredicateBuilder<bool>()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutException>()
                        .Handle<TaskCanceledException>()
                        .Handle<IOException>()
                })
                .AddTimeout(TimeSpan.FromSeconds(10));
        });
    }
}
