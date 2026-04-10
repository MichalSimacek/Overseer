using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Overseer.Api.Common;
using Overseer.Api.Domain;
using Overseer.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<OverseerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IEmailAlertService, EmailAlertService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IPricingService, PricingService>();
builder.Services.AddSingleton<IPaymentService, StripePaymentService>();
builder.Services.AddHttpClient<ISiemWebhookService, SiemWebhookService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OverseerDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.MapGet("/api/public/pricing", (IPricingService pricingService) =>
{
    var rows = Enum.GetValues<PlanType>().Select(plan => new
    {
        Plan = plan.ToString(),
        PriceUsdMonthly = pricingService.GetPrice(plan),
        Features = plan switch
        {
            PlanType.Trial => new[] { "14-day trial", "5 servers", "email alerts", "basic dashboard" },
            PlanType.Subscription => new[] { "20 servers", "SIEM webhook ingest", "license tracking", "SLA reports" },
            _ => new[] { "unlimited servers", "SSO/SAML", "priority support", "custom compliance exports" }
        }
    });

    return Results.Ok(rows);
});

var api = app.MapGroup("/api");

api.MapPost("/tenants", async (CreateTenantRequest request, IValidator<CreateTenantRequest> validator, OverseerDbContext dbContext, IApiKeyService apiKeyService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenant = new Tenant
    {
        Name = request.Name,
        ContactEmail = request.ContactEmail,
        PlanType = PlanType.Trial,
        TrialEndsAtUtc = DateTime.UtcNow.AddDays(14)
    };

    var rawApiKey = apiKeyService.GenerateApiKey();
    var apiClient = new ApiClient
    {
        Name = "default",
        TenantId = tenant.Id,
        ApiKeyHash = apiKeyService.HashApiKey(rawApiKey)
    };

    dbContext.Tenants.Add(tenant);
    dbContext.ApiClients.Add(apiClient);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/tenants/{tenant.Id}", new { tenant.Id, ApiKey = rawApiKey, tenant.TrialEndsAtUtc });
}).AllowAnonymous();

api.MapGet("/dashboard", async (HttpContext httpContext, OverseerDbContext dbContext, CancellationToken cancellationToken) =>
{
    var tenantId = httpContext.GetTenantId();

    var totalServers = await dbContext.ServerNodes.CountAsync(x => x.TenantId == tenantId, cancellationToken);
    var serversUp = await dbContext.AvailabilityChecks
        .Where(x => dbContext.ServerNodes.Where(s => s.TenantId == tenantId).Select(s => s.Id).Contains(x.ServerNodeId))
        .OrderByDescending(x => x.CheckedAtUtc)
        .Take(100)
        .CountAsync(x => x.IsUp, cancellationToken);

    var unresolvedSecurityEvents = await dbContext.SecurityEvents.CountAsync(x => x.TenantId == tenantId && !x.IsResolved, cancellationToken);
    var lastLicense = await dbContext.LicenseComplianceRecords.Where(x => x.TenantId == tenantId)
        .OrderByDescending(x => x.EvaluatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(new
    {
        TotalServers = totalServers,
        RecentHealthyChecks = serversUp,
        UnresolvedSecurityEvents = unresolvedSecurityEvents,
        LicenseRisk = lastLicense is null ? "unknown" : (lastLicense.RequiredCoreLicenses > lastLicense.WindowsServerCoreLicenses || lastLicense.RdsCalRequired > lastLicense.RdsCalAssigned ? "high" : "low")
    });
});

api.MapGet("/reports/sla", async (HttpContext httpContext, OverseerDbContext dbContext, int days, CancellationToken cancellationToken) =>
{
    var tenantId = httpContext.GetTenantId();
    var normalizedDays = days is > 0 and <= 90 ? days : 30;
    var fromUtc = DateTime.UtcNow.AddDays(-normalizedDays);
    var serverIds = await dbContext.ServerNodes.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync(cancellationToken);

    var checks = await dbContext.AvailabilityChecks
        .Where(x => serverIds.Contains(x.ServerNodeId) && x.CheckedAtUtc >= fromUtc)
        .ToListAsync(cancellationToken);

    var total = checks.Count;
    var up = checks.Count(x => x.IsUp);
    var uptimePercent = total == 0 ? 100 : Math.Round((double)up / total * 100, 2);

    return Results.Ok(new
    {
        PeriodDays = normalizedDays,
        TotalChecks = total,
        HealthyChecks = up,
        UptimePercent = uptimePercent
    });
});

api.MapGet("/audit-logs", async (HttpContext httpContext, OverseerDbContext dbContext, int take, CancellationToken cancellationToken) =>
{
    var tenantId = httpContext.GetTenantId();
    var limit = take is > 0 and <= 200 ? take : 50;
    var data = await dbContext.AuditLogEntries.Where(x => x.TenantId == tenantId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(limit)
        .ToListAsync(cancellationToken);
    return Results.Ok(data);
});

api.MapPost("/servers", async (HttpContext httpContext, CreateServerNodeRequest request, IValidator<CreateServerNodeRequest> validator, OverseerDbContext dbContext, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var tenant = await dbContext.Tenants.FirstAsync(x => x.Id == tenantId, cancellationToken);
    var currentServers = await dbContext.ServerNodes.CountAsync(x => x.TenantId == tenantId, cancellationToken);
    var limit = GetServerLimit(tenant);
    if (currentServers >= limit)
    {
        return Results.BadRequest(new { error = $"Server limit reached for current plan ({limit})." });
    }

    var node = new ServerNode
    {
        TenantId = tenantId,
        Name = request.Name,
        Hostname = request.Hostname,
        Port = request.Port
    };

    dbContext.ServerNodes.Add(node);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/servers/{node.Id}", node);
});

api.MapPost("/security-events", async (HttpContext httpContext, CreateSecurityEventRequest request, IValidator<CreateSecurityEventRequest> validator, OverseerDbContext dbContext, IEmailAlertService emailAlertService, IAuditLogService auditLogService, ISiemWebhookService siemWebhookService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var item = new SecurityEvent
    {
        TenantId = tenantId,
        Source = request.Source,
        Severity = request.Severity,
        Description = request.Description
    };

    dbContext.SecurityEvents.Add(item);

    var alertRules = await dbContext.AlertRules.Where(x => x.TenantId == tenantId && x.AlertType == AlertType.SecurityIncident && x.Enabled).ToListAsync(cancellationToken);
    foreach (var rule in alertRules)
    {
        await emailAlertService.SendAlertAsync(rule.TargetEmail, "Overseer security incident", $"{item.Severity} - {item.Description}", cancellationToken);
    }

    await dbContext.SaveChangesAsync(cancellationToken);

    var siem = await dbContext.SiemIntegrations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Enabled, cancellationToken);
    if (siem is not null)
    {
        await siemWebhookService.ForwardSecurityEventAsync(siem, item, cancellationToken);
    }

    await auditLogService.WriteAsync(tenantId, "security_event_created", "api_key", new { item.Id, item.Severity }, cancellationToken);
    return Results.Created($"/api/security-events/{item.Id}", item);
});

api.MapPost("/availability-checks", async (HttpContext httpContext, CreateAvailabilityCheckRequest request, IValidator<CreateAvailabilityCheckRequest> validator, OverseerDbContext dbContext, IEmailAlertService emailAlertService, IAuditLogService auditLogService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var serverNode = await dbContext.ServerNodes.FirstOrDefaultAsync(x => x.Id == request.ServerNodeId && x.TenantId == tenantId, cancellationToken);
    if (serverNode is null)
    {
        return Results.NotFound(new { error = "Server node was not found for this tenant." });
    }

    var check = new AvailabilityCheck
    {
        ServerNodeId = request.ServerNodeId,
        IsUp = request.IsUp,
        ResponseTimeMs = request.ResponseTimeMs,
        StatusCode = request.StatusCode,
        ErrorMessage = request.ErrorMessage
    };

    dbContext.AvailabilityChecks.Add(check);

    if (!check.IsUp)
    {
        var alerts = await dbContext.AlertRules.Where(x => x.TenantId == tenantId && x.AlertType == AlertType.Downtime && x.Enabled).ToListAsync(cancellationToken);
        foreach (var alert in alerts)
        {
            await emailAlertService.SendAlertAsync(alert.TargetEmail, "Overseer downtime alert", $"{serverNode.Name} ({serverNode.Hostname}:{serverNode.Port}) is down.", cancellationToken);
        }
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    await auditLogService.WriteAsync(tenantId, "availability_check_created", "api_key", new { check.Id, check.IsUp }, cancellationToken);
    return Results.Created($"/api/availability-checks/{check.Id}", check);
});

api.MapPost("/license-compliance", async (HttpContext httpContext, UpsertLicenseComplianceRequest request, IValidator<UpsertLicenseComplianceRequest> validator, OverseerDbContext dbContext, IEmailAlertService emailAlertService, IAuditLogService auditLogService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var record = new LicenseComplianceRecord
    {
        TenantId = tenantId,
        WindowsServerCoreLicenses = request.WindowsServerCoreLicenses,
        RequiredCoreLicenses = request.RequiredCoreLicenses,
        RdsCalAssigned = request.RdsCalAssigned,
        RdsCalRequired = request.RdsCalRequired
    };

    dbContext.LicenseComplianceRecords.Add(record);

    if (record.RequiredCoreLicenses > record.WindowsServerCoreLicenses || record.RdsCalRequired > record.RdsCalAssigned)
    {
        var alerts = await dbContext.AlertRules.Where(x => x.TenantId == tenantId && x.AlertType == AlertType.LicenseCompliance && x.Enabled).ToListAsync(cancellationToken);
        foreach (var alert in alerts)
        {
            await emailAlertService.SendAlertAsync(alert.TargetEmail, "Overseer license compliance risk", "Potential licensing non-compliance detected.", cancellationToken);
        }
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    await auditLogService.WriteAsync(tenantId, "license_compliance_updated", "api_key", new { record.Id }, cancellationToken);
    return Results.Ok(record);
});

api.MapPost("/alerts", async (HttpContext httpContext, CreateAlertRuleRequest request, IValidator<CreateAlertRuleRequest> validator, OverseerDbContext dbContext, IAuditLogService auditLogService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var alert = new AlertRule
    {
        TenantId = httpContext.GetTenantId(),
        AlertType = request.AlertType,
        TargetEmail = request.TargetEmail
    };

    dbContext.AlertRules.Add(alert);
    await dbContext.SaveChangesAsync(cancellationToken);
    await auditLogService.WriteAsync(alert.TenantId, "alert_rule_created", "api_key", new { alert.Id, alert.AlertType }, cancellationToken);

    return Results.Created($"/api/alerts/{alert.Id}", alert);
});

api.MapPut("/integrations/siem", async (HttpContext httpContext, UpsertSiemIntegrationRequest request, IValidator<UpsertSiemIntegrationRequest> validator, OverseerDbContext dbContext, IAuditLogService auditLogService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var integration = await dbContext.SiemIntegrations.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
    if (integration is null)
    {
        integration = new SiemIntegration
        {
            TenantId = tenantId,
            WebhookUrl = request.WebhookUrl,
            SharedSecret = request.SharedSecret,
            Enabled = request.Enabled
        };
        dbContext.SiemIntegrations.Add(integration);
    }
    else
    {
        integration.WebhookUrl = request.WebhookUrl;
        integration.SharedSecret = request.SharedSecret;
        integration.Enabled = request.Enabled;
        integration.UpdatedAtUtc = DateTime.UtcNow;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    await auditLogService.WriteAsync(tenantId, "siem_integration_upserted", "api_key", new { integration.Enabled }, cancellationToken);
    return Results.Ok(integration);
});

api.MapPut("/identity/sso-saml", async (HttpContext httpContext, UpsertSsoSamlRequest request, IValidator<UpsertSsoSamlRequest> validator, OverseerDbContext dbContext, IAuditLogService auditLogService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var ssoConfig = await dbContext.SsoSamlConfigurations.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
    if (ssoConfig is null)
    {
        ssoConfig = new SsoSamlConfiguration
        {
            TenantId = tenantId,
            EntityId = request.EntityId,
            MetadataUrl = request.MetadataUrl,
            EnforceSso = request.EnforceSso
        };
        dbContext.SsoSamlConfigurations.Add(ssoConfig);
    }
    else
    {
        ssoConfig.EntityId = request.EntityId;
        ssoConfig.MetadataUrl = request.MetadataUrl;
        ssoConfig.EnforceSso = request.EnforceSso;
        ssoConfig.UpdatedAtUtc = DateTime.UtcNow;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    await auditLogService.WriteAsync(tenantId, "sso_saml_updated", "api_key", new { ssoConfig.EnforceSso }, cancellationToken);
    return Results.Ok(ssoConfig);
});

api.MapPost("/billing/checkout", async (HttpContext httpContext, CheckoutRequest request, IValidator<CheckoutRequest> validator, OverseerDbContext dbContext, IPricingService pricingService, IPaymentService paymentService, IAuditLogService auditLogService, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var tenantId = httpContext.GetTenantId();
    var amount = pricingService.GetPrice(request.PlanType);
    var checkout = await paymentService.CreateCheckoutAsync(tenantId, request.PlanType, amount, cancellationToken);

    var order = new SubscriptionOrder
    {
        TenantId = tenantId,
        RequestedPlan = request.PlanType,
        AmountUsd = amount,
        PaymentProvider = "Stripe",
        ProviderReference = checkout.ProviderReference
    };

    dbContext.SubscriptionOrders.Add(order);

    var tenant = await dbContext.Tenants.FirstAsync(x => x.Id == tenantId, cancellationToken);
    tenant.PlanType = request.PlanType;
    tenant.TrialEndsAtUtc = null;
    tenant.ServerLimitOverride = request.PlanType switch
    {
        PlanType.Trial => 5,
        PlanType.Subscription => 20,
        _ => 0
    };

    await dbContext.SaveChangesAsync(cancellationToken);
    await auditLogService.WriteAsync(tenantId, "billing_checkout_created", "api_key", new { order.Id, request.PlanType }, cancellationToken);

    return Results.Ok(new { order.Id, order.AmountUsd, CheckoutReference = order.ProviderReference, checkout.CheckoutUrl });
});

await app.RunAsync();

internal sealed record CreateTenantRequest(string Name, string ContactEmail);
internal sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
    }
}

internal sealed record CreateServerNodeRequest(string Name, string Hostname, int Port);
internal sealed class CreateServerNodeRequestValidator : AbstractValidator<CreateServerNodeRequest>
{
    public CreateServerNodeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
    }
}

internal sealed record CreateSecurityEventRequest(string Source, string Severity, string Description);
internal sealed class CreateSecurityEventRequestValidator : AbstractValidator<CreateSecurityEventRequest>
{
    public CreateSecurityEventRequestValidator()
    {
        RuleFor(x => x.Source).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Severity).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
    }
}

internal sealed record CreateAvailabilityCheckRequest(Guid ServerNodeId, bool IsUp, int ResponseTimeMs, int StatusCode, string? ErrorMessage);
internal sealed class CreateAvailabilityCheckRequestValidator : AbstractValidator<CreateAvailabilityCheckRequest>
{
    public CreateAvailabilityCheckRequestValidator()
    {
        RuleFor(x => x.ServerNodeId).NotEmpty();
        RuleFor(x => x.ResponseTimeMs).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StatusCode).InclusiveBetween(0, 999);
        RuleFor(x => x.ErrorMessage).MaximumLength(600);
    }
}

internal sealed record UpsertLicenseComplianceRequest(int WindowsServerCoreLicenses, int RequiredCoreLicenses, int RdsCalAssigned, int RdsCalRequired);
internal sealed class UpsertLicenseComplianceRequestValidator : AbstractValidator<UpsertLicenseComplianceRequest>
{
    public UpsertLicenseComplianceRequestValidator()
    {
        RuleFor(x => x.WindowsServerCoreLicenses).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RequiredCoreLicenses).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RdsCalAssigned).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RdsCalRequired).GreaterThanOrEqualTo(0);
    }
}

internal sealed record CreateAlertRuleRequest(AlertType AlertType, string TargetEmail);
internal sealed class CreateAlertRuleRequestValidator : AbstractValidator<CreateAlertRuleRequest>
{
    public CreateAlertRuleRequestValidator()
    {
        RuleFor(x => x.TargetEmail).NotEmpty().EmailAddress();
    }
}

internal sealed record UpsertSiemIntegrationRequest(string WebhookUrl, string? SharedSecret, bool Enabled);
internal sealed class UpsertSiemIntegrationRequestValidator : AbstractValidator<UpsertSiemIntegrationRequest>
{
    public UpsertSiemIntegrationRequestValidator()
    {
        RuleFor(x => x.WebhookUrl).NotEmpty().Must(x => Uri.TryCreate(x, UriKind.Absolute, out _)).WithMessage("WebhookUrl must be a valid absolute URL.");
        RuleFor(x => x.SharedSecret).MaximumLength(200);
    }
}

internal sealed record UpsertSsoSamlRequest(string EntityId, string MetadataUrl, bool EnforceSso);
internal sealed class UpsertSsoSamlRequestValidator : AbstractValidator<UpsertSsoSamlRequest>
{
    public UpsertSsoSamlRequestValidator()
    {
        RuleFor(x => x.EntityId).NotEmpty().MaximumLength(300);
        RuleFor(x => x.MetadataUrl).NotEmpty().Must(x => Uri.TryCreate(x, UriKind.Absolute, out _)).WithMessage("MetadataUrl must be a valid absolute URL.");
    }
}

internal sealed record CheckoutRequest(PlanType PlanType);
internal sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(x => x.PlanType).IsInEnum();
    }
}

internal static class HttpContextExtensions
{
    public static Guid GetTenantId(this HttpContext context)
    {
        if (context.Items.TryGetValue("TenantId", out var tenantId) && tenantId is Guid parsed)
        {
            return parsed;
        }

        throw new InvalidOperationException("Missing tenant context.");
    }
}

static int GetServerLimit(Tenant tenant)
{
    if (tenant.ServerLimitOverride > 0)
    {
        return tenant.ServerLimitOverride;
    }

    return tenant.PlanType switch
    {
        PlanType.Trial => 5,
        PlanType.Subscription => 20,
        _ => int.MaxValue
    };
}
