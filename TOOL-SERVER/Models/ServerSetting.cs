using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class ServerSetting
{
    public Guid ServerSettingId { get; set; }

    public string SettingKey { get; set; } = null!;

    public string? ValueJson { get; set; }

    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;
}
