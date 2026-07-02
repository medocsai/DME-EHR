using EHR.Models.Generated;

namespace EHR.Models.Extensions;

/// <summary>
/// Interface for entities that belong to a tenant
/// </summary>
public interface ITenantEntity
{
    int TenantId { get; set; }
}

/// <summary>
/// Interface for soft-deletable entities
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Interface for auditable entities
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    int? CreatedBy { get; set; }
    int? UpdatedBy { get; set; }
}

/// <summary>
/// Base class for tenant-scoped entities
/// </summary>
public abstract class TenantEntity : ITenantEntity
{
    public int TenantId { get; set; }
    public virtual Tenant? Tenant { get; set; }
}
