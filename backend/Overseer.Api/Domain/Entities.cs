namespace Overseer.Api.Domain;

public enum PlanType
{
    Trial = 0,
    Subscription = 1,
    Enterprise = 2
}

public enum AlertType
{
    Downtime = 0,
    SecurityIncident = 1,
    LicenseCompliance = 2
}

public sealed class Tenant
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string ContactEmail { get; set; }
    public PlanType PlanType { get; set; } = PlanType.Trial;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? TrialEndsAtUtc { get; set; } = DateTime.UtcNow.AddDays(14);
    public bool IsActive { get; set; } = true;
}

public sealed class ServerNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Hostname { get; set; }
    public int Port { get; set; } = 443;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class AvailabilityCheck
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ServerNodeId { get; set; }
    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsUp { get; set; }
    public int ResponseTimeMs { get; set; }
    public int StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SecurityEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Source { get; set; }
    public required string Severity { get; set; }
    public required string Description { get; set; }
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsResolved { get; set; }
}

public sealed class LicenseComplianceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int WindowsServerCoreLicenses { get; set; }
    public int RequiredCoreLicenses { get; set; }
    public int RdsCalAssigned { get; set; }
    public int RdsCalRequired { get; set; }
    public DateTime EvaluatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class AlertRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public AlertType AlertType { get; set; }
    public required string TargetEmail { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class SubscriptionOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public PlanType RequestedPlan { get; set; }
    public decimal AmountUsd { get; set; }
    public required string PaymentProvider { get; set; }
    public required string ProviderReference { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class ApiClient
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string ApiKeyHash { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
