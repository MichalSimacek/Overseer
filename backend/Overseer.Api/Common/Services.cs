using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overseer.Api.Domain;
using Overseer.Api.Infrastructure;
using Stripe;
using Stripe.Checkout;

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
    Task<PaymentCheckoutResult> CreateCheckoutAsync(Guid tenantId, PlanType planType, decimal amountUsd, CancellationToken cancellationToken);
}

public sealed record PaymentCheckoutResult(string ProviderReference, string CheckoutUrl);

public sealed class StripePaymentService(IConfiguration configuration) : IPaymentService
{
    public async Task<PaymentCheckoutResult> CreateCheckoutAsync(Guid tenantId, PlanType planType, decimal amountUsd, CancellationToken cancellationToken)
    {
        var secretKey = configuration["Payments:Stripe:SecretKey"] ?? throw new InvalidOperationException("Stripe secret key is missing.");
        var successUrl = configuration["Payments:Stripe:SuccessUrl"] ?? "http://localhost:5173?checkout=success";
        var cancelUrl = configuration["Payments:Stripe:CancelUrl"] ?? "http://localhost:5173?checkout=cancel";
        StripeConfiguration.ApiKey = secretKey;

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId.ToString(),
                ["plan"] = planType.ToString()
            },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmountDecimal = amountUsd * 100,
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month"
                        },
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Overseer {planType}"
                        }
                    }
                }
            }
        }, cancellationToken: cancellationToken);

        return new PaymentCheckoutResult(session.Id, session.Url ?? string.Empty);
    }
}

public interface IAuditLogService
{
    Task WriteAsync(Guid tenantId, string action, string actor, object? metadata, CancellationToken cancellationToken);
}

public sealed class AuditLogService(OverseerDbContext dbContext) : IAuditLogService
{
    public async Task WriteAsync(Guid tenantId, string action, string actor, object? metadata, CancellationToken cancellationToken)
    {
        var entry = new AuditLogEntry
        {
            TenantId = tenantId,
            Action = action,
            Actor = actor,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata)
        };

        dbContext.AuditLogEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public interface ISiemWebhookService
{
    Task ForwardSecurityEventAsync(SiemIntegration integration, SecurityEvent securityEvent, CancellationToken cancellationToken);
}

public sealed class SiemWebhookService(HttpClient httpClient) : ISiemWebhookService
{
    public async Task ForwardSecurityEventAsync(SiemIntegration integration, SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, integration.WebhookUrl);
        if (!string.IsNullOrWhiteSpace(integration.SharedSecret))
        {
            request.Headers.Add("X-Overseer-Signature", integration.SharedSecret);
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "security_event",
            securityEvent.Id,
            securityEvent.Severity,
            securityEvent.Source,
            securityEvent.Description,
            securityEvent.OccurredAtUtc
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        await httpClient.SendAsync(request, cancellationToken);
    }
}
