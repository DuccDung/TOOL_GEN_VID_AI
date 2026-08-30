using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class SchemaVersion
{
    public int SchemaVersionId { get; set; }

    public string Version { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime AppliedAtUtc { get; set; }
}
