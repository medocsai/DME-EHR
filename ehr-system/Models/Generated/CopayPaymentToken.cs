using System.ComponentModel.DataAnnotations;

namespace EHR.Models.Generated;

/// <summary>
/// Token sent in copay reminder emails. Links patient to portal billing page.
/// Smart routing: if patient has portal account → login page; if not → create account page.
/// </summary>
public class CopayPaymentToken
{
    [Key]
    public int TokenId { get; set; }
    public int TenantId { get; set; }
    public int PatientId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Tenant Tenant { get; set; }
    public virtual Patient Patient { get; set; }
}
