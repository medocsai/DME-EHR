using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Tenant
{
    public int TenantId { get; set; }

    public string Name { get; set; }

    public string Subdomain { get; set; }

    public string LogoUrl { get; set; }

    public string Phone { get; set; }

    public string Email { get; set; }

    public string Address { get; set; }

    public string City { get; set; }

    public string State { get; set; }

    public string ZipCode { get; set; }

    public string TaxId { get; set; }

    public string Npi { get; set; }

    public int? Plan { get; set; }

    public int? Status { get; set; }

    public DateTime? SubscriptionStartDate { get; set; }

    public DateTime? SubscriptionEndDate { get; set; }

    public int? MaxUsers { get; set; }

    public int? MaxPatients { get; set; }

    public string Settings { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool? IsDeleted { get; set; }


    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();









    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    // Note: Legacy Notes navigation removed - use ClinicalNotes instead







    public virtual ICollection<User> Users { get; set; } = new List<User>();

}
