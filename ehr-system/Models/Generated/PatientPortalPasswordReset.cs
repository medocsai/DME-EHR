namespace EHR.Models.Generated;

public partial class PatientPortalPasswordReset
{
    public int PatientPortalPasswordResetId { get; set; }
    public int AccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual PatientPortalAccount Account { get; set; }
}
