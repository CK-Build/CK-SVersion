using System;
using System.Collections;
using System.Diagnostics;

namespace CK.Core;

/// <summary>
/// "Conformant SVersion" is a subset of the SVersion that contains:
/// <list type="bullet">
///     <item>
///         All the stable versions (the ones with an empty <see cref="SVersion.Prerelease"/>).
///         Stable versions are always compiled in Release configuration.
///     </item>
///     <item>
///         26 definite prerelease levels based on the OTAN alphabet (from "-alpha" to "-zulu").
///         Prerelease from "-alpha" to "-quebec" are compiled in Debug configuration, versions from "-romeo" to "-zulu"
///         are compiled in Release configuration.
///         <para>
///         These levels correspond to an increasing "quality".
///         </para>
///     </item>
///     <item>
///         An "exploratory" range of zero-based versions: <c>0.0.0-0-explo</c> where <c>explo</c> can be any valid 
///         identifier. There is no ordering among exploratory versions, they are intended to be used explicitly.
///         Exploratory versions are always compiled in Release configuration (like Stable). To have them in Debug, 
///         their CI build versions must be used.
///     </item>
///     <item>
///         An incremental release number that applies to all prerelease and exploratory versions (defaults to 0).
///     </item>
///     <item>
///         CI build versions are "post-build" versions that apply to all kind of versions (stable, prerelease and exploratory).
///         They are always compiled in Debug configuration.
///     </item>
/// </list>
/// Moreover, a valid "Conformant SVersion" ensures that:
/// <list type="bullet">
///     <item>
///     At most one among <see cref="SVersion.HasFakeMetadata"/>, <see cref="SVersion.HasDeprecatedMetadata"/>
///     and <see cref="SVersion.HasInvalidMetadata"/> must be true.
///     </item>
///     <item>
///     If it is a CI version (<see cref="SVersion.IsCI"/> is true), then <see cref="SVersion.HasFakeMetadata"/> and
///     <see cref="SVersion.HasDeprecatedMetadata"/> are false (<see cref="SVersion.HasInvalidMetadata"/> may be true).
///     </item>
/// </list>
/// </summary>
public enum CSVersionKind
{
    /// <summary>
    /// Non applicable (not a CS Version).
    /// </summary>
    None = 0,

    /// <summary>
    /// Exploratory versions are short lived zero-based versions of the form "0.0.0-0.explo".
    /// No ordering applies among them, they are intended to be used explicitly.
    /// </summary>
    Exploratory = 1,

    /// <summary>
    /// Alpha is the first, weakest, conformant prerelease name.
    /// <para>
    /// It should be compiled in Debug and should bot be distributed on the "release channel"
    /// (where stable versions go) but on "preview channels" that distribute <see cref="SVersion.IsCI"/>, <see cref="Exploratory"/>
    /// and any other releases up to <see cref="Papa"/>.
    /// </para>
    /// </summary>
    Alpha = 2,

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    Bravo = 3,
    Charlie = 4,
    Delta = 5,
    Echo = 6,
    Foxtrot = 7,
    Golf = 8,
    Hotel = 9,
    India = 10,
    Juliet = 11,
    Kilo = 12,
    Lima = 13,  
    Mike = 14,
    November = 15,
    Oscar = 16,
#pragma warning restore CS1591

    /// <summary>
    /// Papa is the first recommended "official Prerelease" or "Preview" quality level.
    /// It should use "Debug" configuration (like <see cref="Quebec"/>) but should be
    /// delivered to "release channels" (the channels of the stable versions).
    /// </summary>
    Papa = 17,

    /// <summary>
    /// Quebec is the last conformant prerelease name that should be compiled in Debug.
    /// </summary>
    Quebec = 18,

    /// <summary>
    /// Romeo is the first conformant prerelease name that should be compiled in Release.
    /// </summary>
    Romeo = 19,

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    Sierra = 20,
    Tango = 21,
    Uniform = 22,
    Victor = 23,
    Whiskey = 24,
    XRay = 25,
    Yankee = 26,
#pragma warning restore CS1591 

    /// <summary>
    /// Zulu is the last, strongest prerelease before a stable version.
    /// </summary>
    Zulu = 27,

    /// <summary>
    /// Stable version (not a exploratory nor a prerelease): this corresponds to an empty <see cref="SVersion.Prerelease"/>.
    /// </summary>
    Stable = 28
}

/// <summary>
/// Extends <see cref="CSVersionKind"/>.
/// </summary>
public static class CSVersionKindExtensions
{
    static readonly string[] _names = [
        "",
        "",
        "alpha",
        "bravo",
        "charlie",
        "delta",
        "echo",
        "foxtrot",
        "golf",
        "hotel",
        "india",
        "juliet",
        "kilo",
        "lima",
        "mike",
        "november",
        "oscar",
        "papa",
        "quebec",
        "romeo",
        "sierra",
        "tango",
        "uniform",
        "victor",
        "whiskey",
        "xray",
        "yankee",
        "zulu",
        ""
    ];

    /// <summary>
    /// Gets the <see cref="SVersion.Prerelease"/> representation (in lower case). 
    /// <see cref="CSVersionKind.None"/>, <see cref="CSVersionKind.Exploratory"/> and <see cref="CSVersionKind.Stable"/> are empty string.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>The prerelease name.</returns>
    public static string ToPrerelease( this CSVersionKind kind ) => _names[(int)kind];

    /// <summary>
    /// Tries to parse one of the "alpha"..."zulu" <see cref="CSVersionKind"/> names.
    /// </summary>
    /// <param name="s">This string to parse.</param>
    /// <param name="kind">The resulting kind.</param>
    /// <param name="comparisonType">Comparison type to use.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryParse( ReadOnlySpan<char> s, out CSVersionKind kind, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase )
    {
        kind = CSVersionKind.None;
        if( TryMatch( ref s, ref kind, comparisonType ) )
        {
            if( s.Length == 0 ) return true;
            kind = CSVersionKind.None;
        }
        return false;
    }

    /// <summary>
    /// Tries to match one of the "alpha"..."zulu" <see cref="CSVersionKind"/> names.
    /// The head is fowarded on success.
    /// </summary>
    /// <param name="head">This head.</param>
    /// <param name="kind">The resulting kind. Unchanged on failure.</param>
    /// <param name="comparisonType">Comparison type to use.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, ref CSVersionKind kind, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase )
    {
        Debug.Assert( (int)CSVersionKind.Alpha == 2 && (int)CSVersionKind.Zulu == 27 );
        for( int i = 2; i <= 27; ++i )
        {
            var c = _names[i];
            if( head.StartsWith( c, comparisonType ) )
            {
                head = head.Slice( c.Length );
                kind = (CSVersionKind)i;
                return true;
            }
        }
        return false;
    }


}
