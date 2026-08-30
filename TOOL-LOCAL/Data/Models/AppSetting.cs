using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class AppSetting
{
    public Guid AppSettingId { get; set; }

    public string SettingKey { get; set; } = null!;

    public string? ValueJson { get; set; }

    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;
}
