using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Cptcode
{
    public string Code { get; set; }

    public string Description { get; set; }

    public string Category { get; set; }

    public int? DefaultMinutes { get; set; }

    public bool? IsTimeBased { get; set; }

    public bool? IsActive { get; set; }
}
