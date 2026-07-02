namespace EHR.Models.Generated;

public partial class PatientPortalAccount
{
    public int PatientPortalAccountId { get; set; }
    public int TenantId { get; set; }
    public int LocationId { get; set; }
    public int PatientId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndAt { get; set; }

    public virtual Tenant Tenant { get; set; }
    public virtual Location Location { get; set; }
    public virtual Patient Patient { get; set; }
}
