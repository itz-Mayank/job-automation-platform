namespace JobForge.Domain.Enums;

/// <summary>
/// Only Http is implemented in the MVP. The column exists so a new executor
/// type (e.g. Shell, Webhook-signed) could be added later without a schema change.
/// </summary>
public enum JobType
{
    Http = 0
}
