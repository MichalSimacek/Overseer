using Microsoft.EntityFrameworkCore;
using Overseer.Api.Domain;

namespace Overseer.Api.Infrastructure;

public sealed class OverseerDbContext(DbContextOptions<OverseerDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ServerNode> ServerNodes => Set<ServerNode>();
    public DbSet<AvailabilityCheck> AvailabilityChecks => Set<AvailabilityCheck>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<LicenseComplianceRecord> LicenseComplianceRecords => Set<LicenseComplianceRecord>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<SubscriptionOrder> SubscriptionOrders => Set<SubscriptionOrder>();
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(x => x.ContactEmail);
        modelBuilder.Entity<ServerNode>().HasIndex(x => new { x.TenantId, x.Hostname }).IsUnique();
        modelBuilder.Entity<AlertRule>().HasIndex(x => new { x.TenantId, x.AlertType, x.TargetEmail }).IsUnique();
        modelBuilder.Entity<ApiClient>().HasIndex(x => x.ApiKeyHash).IsUnique();
        modelBuilder.Entity<SubscriptionOrder>().Property(x => x.AmountUsd).HasPrecision(10, 2);
    }
}
