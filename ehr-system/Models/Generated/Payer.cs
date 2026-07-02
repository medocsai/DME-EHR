using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Payer
{
    public int PayerId { get; set; }

    public string Name { get; set; }

    public string PayerIdCode { get; set; }

    public string Address { get; set; }

    public string Phone { get; set; }

    public string Website { get; set; }

    public string EligibilityUrl { get; set; }

    public string ClaimsUrl { get; set; }

    public bool? IsActive { get; set; }
}
