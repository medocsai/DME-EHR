using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class User
{
    public int UserId { get; set; }

    public int? TenantId { get; set; }

    public string Email { get; set; }

    public string PasswordHash { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }

    public string Phone { get; set; }

    public int? Role { get; set; }

    public int? ProviderId { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiry { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string PasswordResetToken { get; set; }

    public DateTime? PasswordResetTokenExpiry { get; set; }

    // OTP login fields (6-digit email OTP, 5-minute expiry, 5-attempt limit, 60-second resend cooldown)
    public string OtpCode { get; set; }

    public DateTime? OtpExpiry { get; set; }

    public int OtpAttempts { get; set; }

    public DateTime? OtpResendCooldownUntil { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
