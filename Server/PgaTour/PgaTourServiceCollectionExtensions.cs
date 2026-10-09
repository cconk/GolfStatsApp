using System;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace GolfStatsApp.Server.PgaTour
{
    public static class PgaTourServiceCollectionExtensions
    {
        private const string UserAgent = "GolfStatsApp (+https://github.com/cconk/GolfStatsApp)";

        public static IServiceCollection AddPgaTourClient(this IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection(PgaTourOptions.SectionName).Get<PgaTourOptions>() ?? new PgaTourOptions();
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                throw new InvalidOperationException("PgaTour:ApiKey is not configured.");

            // Shared limiter across all PGA hosts: ~10 requests/second, matching pgatourPY.
            var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,
                TokensPerPeriod = 10,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });

            void Register(string name, string baseUrl)
            {
                services.AddHttpClient(name, client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
                    client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
                    client.DefaultRequestHeaders.Add("x-pgat-platform", "web");
                    client.DefaultRequestHeaders.Add("Origin", "https://www.pgatour.com");
                    client.DefaultRequestHeaders.Referrer = new Uri("https://www.pgatour.com/");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                })
                .AddResilienceHandler(name, builder =>
                {
                    builder.AddRateLimiter(limiter);
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = Polly.DelayBackoffType.Exponential,
                        Delay = TimeSpan.FromMilliseconds(500),
                        UseJitter = true,
                    });
                    builder.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
                });
            }

            Register(PgaTourTransport.GraphQlClient, options.GraphQlUrl);
            Register(PgaTourTransport.RestClient, options.RestUrl);
            Register(PgaTourTransport.ConfigClient, options.ConfigUrl);

            services.AddSingleton<PgaTourTransport>();
            services.AddSingleton<PgaTourApiClient>();
            return services;
        }
    }
}
