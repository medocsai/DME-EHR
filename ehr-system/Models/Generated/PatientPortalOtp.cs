namespace EHR.Models.Generated;

public partial class PatientPortalOtp
{
    public int PatientPortalOtpId { get; set; }
    public int AccountId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual PatientPortalAccount Account { get; set; }
}
