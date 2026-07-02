namespace EHR.Models.Generated;

public partial class PatientPortalInvitation
{
    public int PatientPortalInvitationId { get; set; }
    public int TenantId { get; set; }
    public int LocationId { get; set; }
    public int PatientId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public int Status { get; set; } // 0=Pending, 1=Registered, 2=Expired
    public int InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public int ResentCount { get; set; }

    public virtual Tenant Tenant { get; set; }
    public virtual Location Location { get; set; }
    public virtual Patient Patient { get; set; }
}
