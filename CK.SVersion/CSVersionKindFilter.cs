using System;
using System.Text;

namespace CK.Core;

/// <summary>
/// Defines a filter for version that relies on the <see cref="CSVersionKind"/> and the CI build indicator.
/// <para>
/// The <c>default</c> accepts everything and there is no filter that rejects everything: a null nullable filter should be used for
/// the "reject all" case.
/// </para>
/// <para>
/// <list type="bullet">
///     <item><c>[,],AllowCI</c><term></term>
///     <description>
///     Any version is accepted (Min = <see cref="CSVersionKind.Exploratory"/>, Max = <see cref="CSVersionKind.Stable"/>, AllowCI = true).
///     This is the <c>default</c> that can be used to configure a "preview" feed (in addition to the
///     official https://www.nuget.org/ that is more restrictive).
///     </description>
///     </item>
///     <item>[papa,]<term></term>
///     <description>
///     Any "papa" version and above including stable ones but not their CI builds (Min = <see cref="CSVersionKind.Papa"/>,
///     Max = <see cref="CSVersionKind.Stable"/>, AllowCI = false).
///     This is the regular configuration for the official https://www.nuget.org/ feed (it cannot handle package version
///     with the "--ci" double dash prerelease name and we don't want to pollute this open feed with really unstable versions).
///     </description>
///     </item>
///     <item><c>[,oscar],AllowCI</c><term></term>
///     <description>
///     Any prerelease up to "papa" (but excluding it) with their CI builds (Min = <see cref="CSVersionKind.Exploratory"/>, Max = <see cref="CSVersionKind.Oscar"/>,
///     AllowCI = true).
///     This is a possible configuration for a feed that complements the official https://www.nuget.org/ feed (versions are on one or the other feed, not on both).
///     </description>
///     </item>
///     <item><c>[,explo],AllowCI</c><term></term>
///     <description>
///     Defines a pure "Exploratory" feed that contains only exploratory versions and their CI builds. Min = Max = <see cref="CSVersionKind.Exploratory"/>
///     and AllowCI = true.
///     </description>
///     </item>
/// </list>
/// </para>
/// </summary>
public readonly struct CSVersionKindFilter
{
    readonly CSVersionKind _min;
    readonly CSVersionKind _max;
    readonly bool _noCI;

    /// <summary>
    /// Gets the minimal version kind.
    /// </summary>
    public CSVersionKind Min => _min == CSVersionKind.None ? CSVersionKind.Exploratory : _min;

    /// <summary>
    /// Gets the maximal version kind.
    /// </summary>
    public CSVersionKind Max => _max == CSVersionKind.None ? CSVersionKind.Stable : _max;

    /// <summary>
    /// Gets whether CI versions are allowed.
    /// </summary>
    public bool AllowCI => !_noCI;

    /// <summary>
    /// Gets whether stable versions are accepted.
    /// </summary>
    public bool AllowStable => _max == CSVersionKind.None || _max == CSVersionKind.Stable;

    /// <summary>
    /// Checks whether this range allows the specified <see cref="CSVersionKind"/> (regardless of <see cref="AllowCI"/>).
    /// </summary>
    /// <param name="kind">The version kind to challenge.</param>
    /// <returns>Whether <paramref name="kind"/> is accepted or not.</returns>
    public bool Accepts( CSVersionKind kind ) => kind != CSVersionKind.None && kind >= _min && (_max == CSVersionKind.None || kind <= _max);

    /// <summary>
    /// Checks whether this range allows the specified <see cref="CSVersionKind"/> and CI version.
    /// </summary>
    /// <param name="kind">The version kind to challenge.</param>
    /// <param name="isCI">Whether a CI version must be considered.</param>
    /// <returns>Whether <paramref name="kind"/> and <paramref name="isCI"/> are accepted or not.</returns>
    public bool Accepts( CSVersionKind kind, bool isCI ) => !(isCI && _noCI)
                                                            && kind != CSVersionKind.None
                                                            && kind >= _min
                                                            && (_max == CSVersionKind.None || kind <= _max);

    /// <summary>
    /// Checks whether this range allows the specified version.
    /// </summary>
    /// <param name="v">The version to challenge.</param>
    /// <returns>Whether <paramref name="v"/> is accepted or not.</returns>
    public bool Accepts( SVersion v ) => Accepts( v.VersionKind, v.IsCI );

    /// <summary>
    /// Initializes a new filter.
    /// Min must be lower or equal to max otherwise an <see cref="ArgumentException"/> is thrown.
    /// </summary>
    /// <param name="min">See <see cref="Min"/>. When <see cref="CSVersionKind.None"/>, this is normalized to <see cref="CSVersionKind.Exploratory"/>.</param>
    /// <param name="max">See <see cref="Max"/>. When <see cref="CSVersionKind.None"/>, this is normalized to <see cref="CSVersionKind.Stable"/>.</param>
    /// <param name="allowCI">See <see cref="AllowCI"/>.</param>
    public CSVersionKindFilter( CSVersionKind min, CSVersionKind max, bool allowCI )
    {
        if( min == CSVersionKind.None ) min = CSVersionKind.Exploratory;
        if( max == CSVersionKind.None ) max = CSVersionKind.Stable;
        ArgumentOutOfRangeException.ThrowIfNegative( max - min );
        _min = min;
        _max = max;
        _noCI = !allowCI;
    }

    /// <summary>
    /// Initializes a new filter from a "[Min,Max],AllowCI" string where Min, Max and AllowCI are optional.
    /// Throws an <see cref="ArgumentException"/> on invalid syntax.
    /// Simply uses <see cref="TryParse(ReadOnlySpan{char}, out CSVersionKindFilter)"/>) to handle invalid syntax.
    /// </summary>
    /// <param name="s">The string.</param>
    public CSVersionKindFilter( ReadOnlySpan<char> s )
    {
        if( !TryParse( s, out CSVersionKindFilter p ) ) throw new ArgumentException( "Invalid CSVersionKindFilter syntax." );
        _min = p._min;
        _max = p._max;
        _noCI = p._noCI;
    }

    /// <summary>
    /// Attempts to parse a string ("[Min,Max],AllowCI" string where Min, Max and AllowCI are optional) as a <see cref="CSVersionKindFilter"/>.
    /// White spaces are silently ignored.
    /// </summary>
    /// <param name="head">The string to parse (leading and internal white spaces between tokens are skipped).</param>
    /// <param name="filter">The result.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryParse( ReadOnlySpan<char> head, out CSVersionKindFilter filter ) => TryMatch( ref head, out filter );

    /// <summary>
    /// Attempts to match a string as a <see cref="CSVersionKindFilter"/> (<paramref name="head"/> is forwarded on success).
    /// White spaces are silently ignored.
    /// The format is "[Min,Max],AllowCI" string where Min, Max and AllowCI are optional.
    /// </summary>
    /// <param name="head">The string to parse (leading and internal white spaces between tokens are skipped).</param>
    /// <param name="filter">The result.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out CSVersionKindFilter filter )
    {
        filter = default;

        var h = head.TrimStart();
        if( !h.TryMatch( '[' ) ) return false;

        var min = CSVersionKind.Exploratory;
        h.SkipWhiteSpaces();
        CSVersionKindExtensions.TryMatch( ref h, ref min );

        h.SkipWhiteSpaces();
        if( !h.TryMatch( ',' ) ) return false;

        var max = CSVersionKind.Stable;
        h.SkipWhiteSpaces();
        CSVersionKindExtensions.TryMatch( ref h, ref max );

        if( max < min ) return false;

        h.SkipWhiteSpaces();
        if( !h.TryMatch( ']' ) ) return false;

        bool allowCI = false;
        var lookup = h.TrimStart();
        if( lookup.TryMatch( ',' )
            && lookup.SkipWhiteSpaces()
            && lookup.TryMatch( "AllowCI", StringComparison.OrdinalIgnoreCase ) )
        {
            allowCI = true;
            h = lookup;
        }

        head = h;
        filter = new CSVersionKindFilter( min, max, allowCI );
        return true;
    }

    /// <summary>
    /// Overridden to return "[<see cref="Min"/>,<see cref="Max"/>],AllowCI" string representation.
    /// </summary>
    /// <returns>The filter string representation.</returns>
    public override string ToString()
    {
        StringBuilder b = new StringBuilder();
        b.Append( '[' ).Append( Min.ToKindName() ).Append( ',' ).Append( Max.ToKindName() ).Append( ']' );
        if( !_noCI ) b.Append( ",AllowCI" );
        return b.ToString();
    }
}
