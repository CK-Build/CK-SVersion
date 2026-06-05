namespace CK.Core;

/// <summary>
/// Models the change level between two versions.
/// </summary>
public enum SVersionChange
{
    /// <summary>
    /// No change.
    /// </summary>
    None,

    /// <summary>
    /// Minimal impact (fix).
    /// </summary>
    Patch,

    /// <summary>
    /// Minor change (feature).
    /// </summary>
    Minor,

    /// <summary>
    /// Major change (breaking).
    /// </summary>
    Major
}

