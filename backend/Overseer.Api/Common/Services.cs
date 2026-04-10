using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Overseer.Api.Domain;
using Overseer.Api.Infrastructure;

namespace Overseer.Api.Common;

public interface IApiKeyService
{
    string GenerateApiKey();
    string HashApiKey(string apiKey);
    Task<ApiClient?> FindClientAsync(string apiKey, CancellationToken cancellationToken);
}

public sealed class ApiKeyService(OverseerDbContext dbContext) : IApiKeyService
{
    public string GenerateApiKey()
    {
        var buffer = Guid.NewGuid().ToByteArray();
        return Convert.ToBase64String(buffer).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }

    public string HashApiKey(string apiKey) => BCrypt.Net.BCrypt.HashPassword(apiKey);

    public async Task<ApiClient?> FindClientAsync(string apiKey, CancellationToken cancellationToken)
    {
        var clients = await dbContext.ApiClients.Where(c => !c.IsRevoked).ToListAsync(cancellationToken);
        return clients.FirstOrDefault(client => BCrypt.Net.BCrypt.Verify(apiKey, client.ApiKeyHash));
    }
}

public interface IEmailAlertService
{
    Task SendAlertAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public sealed class EmailAlertService(IConfiguration configuration, ILogger<EmailAlertService> logger) : IEmailAlertService
{
    public async Task SendAlertAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"] ?? throw new InvalidOperationException("Email:SmtpHost is missing");
        var port = int.Parse(configuration["Email:SmtpPort"] ?? "25");
        var from = configuration["Email:From"] ?? throw new InvalidOperationException("Email:From is missing");
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];

        using var message = new MailMessage(from, to, subject, body);
        using var client = new SmtpClient(host, port);

        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            client.Credentials = new System.Net.NetworkCredential(username, password);
        }

        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation("Alert email sent to {Recipient}", to);
    }
}

public interface IPricingService
{
    decimal GetPrice(PlanType planType);
}

public sealed class PricingService : IPricingService
{
    private static readonly IReadOnlyDictionary<PlanType, decimal> PriceMap = new Dictionary<PlanType, decimal>
    {
        [PlanType.Trial] = 0,
        [PlanType.Subscription] = 49,
        [PlanType.Enterprise] = 299
    };

    public decimal GetPrice(PlanType planType) => PriceMap[planType];
}

public interface IPaymentService
{
    Task<string> CreatePaymentIntentAsync(Guid tenantId, PlanType planType, decimal amountUsd, CancellationToken cancellationToken);
}

public sealed class MockPaymentService : IPaymentService
{
    public Task<string> CreatePaymentIntentAsync(Guid tenantId, PlanType planType, decimal amountUsd, CancellationToken cancellationToken)
    {
        var reference = $"mock_{tenantId:N}_{planType}_{amountUsd:0.00}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        return Task.FromResult(reference);
    }
}
