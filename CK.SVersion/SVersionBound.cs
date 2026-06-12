using System;
using System.Diagnostics;
using System.Text;

namespace CK.Core;

/// <summary>
/// Aims to define a sensible response to one of the dependency management issue: how to specify "version ranges".
/// <para>
/// The <see cref="Union(in SVersionBound)"/> binary operation defines a partial order (materialized by <see cref="Contains"/>)
/// on the set of all possible bounds: <see cref="None"/> is the identity element (and the greatest element of the whole set)
/// and <see cref="All"/> is the absorbing element of the <see cref="Union(in SVersionBound)"/> operation and the lowest element
/// of the set.
/// </para>
/// <para>
/// The text format (<see cref="ToString"/> method) is the "Base" version that may be followed by <see cref="Lock"/>, <see cref="MinPrerelease"/> 
/// and/or <see cref="AllowCI"/> specifications enclosed in angle brackets. <see cref="TryParse(ReadOnlySpan{char}, out SVersionBound)"/>
/// methods parse them back.
/// </para>
/// <para>
/// Npm and Nuget version range syntax can be parsed with <see cref="NpmTryParse(ReadOnlySpan{char}, bool)"/> or <see cref="NugetTryParse(ReadOnlySpan{char})"/> 
/// methods: they return a <see cref="ParseResult"/> that can be invalid or <see cref="ParseResult.IsApproximated"/>.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public readonly partial struct SVersionBound : IEquatable<SVersionBound>
{
    static readonly SVersion _000Version = SVersion.Create( 0, 0, 0 );

    readonly SVersion? _base;
    readonly string? _minPrerelease;
    readonly SVersionLock _lock;
    readonly bool _noCI;

    /// <summary>
    /// "All" bound allows any <see cref="SVersion"/> (no restriction): 
    /// <list type="bullet">
    ///     <item><term>Base</term><description>is <see cref="SVersion.ZeroVersion"/>.</description></item>
    ///     <item><term>Lock</term><description>is <see cref="SVersionLock.NoLock"/>.</description></item>
    ///     <item><term>MinPrerelease</term><description>is "0".</description></item>
    ///     <item><term>AllowCI</term><description>is true.</description></item>
    /// </list>
    /// <para>
    /// This bound is the absorbing element of the <see cref="Union(in SVersionBound)"/> operation and the neutral element
    /// of the <see cref="Intersect(in SVersionBound)"/>.
    /// </para>
    /// This is the <c>default</c> of this <see cref="SVersionBound"/> value type.
    /// </summary>
    public static readonly SVersionBound All = default;

    /// <summary>
    /// None bound: 
    /// <list type="bullet">
    ///     <item><term>Base</term><description>is <see cref="SVersion.LastVersion"/>.</description></item>
    ///     <item><term>Lock</term><description>is the strongest possible (<see cref="SVersionLock.Lock"/>).</description></item>
    ///     <item><term>MinPrerelease</term><description>is "" - only accepts stable versions.</description></item>
    ///     <item><term>AllowCI</term><description>is false.</description></item>
    /// </list>
    /// <see cref="Satisfy(in SVersion)"/> is true only for the last version (that is unfortunate but is acceptable as 
    /// the LastVersion is more theoretical than actual).
    /// <para>
    /// This bound is the identity element of the <see cref="Union(in SVersionBound)"/> operation and the absorbing element 
    /// of the <see cref="Intersect(in SVersionBound)"/>.
    /// </para>
    /// </summary>
    public static readonly SVersionBound None = new SVersionBound( SVersion.LastVersion, SVersionLock.Lock, "", false );

    /// <summary>
    /// Gets the base version (inclusive minimum version). <see cref="SVersion.IsValid"/> is necessarily true.
    /// </summary>
    public SVersion Base => _base ?? SVersion.ZeroVersion;

    /// <summary>
    /// Gets whether only the same Major, Minor, Patch (or the exact version) of <see cref="Base"/> must be considered.
    /// </summary>
    public SVersionLock Lock => _lock;

    /// <summary>
    /// Gets the smallest accepted prerelease name: "0" is the minimum (weakest).
    /// <para>
    /// When <see cref="string.Empty"/>, this excludes any prerelease (only stable are accepted), this is the strongest.
    /// </para>
    /// </summary>
    public string MinPrerelease => _minPrerelease ?? "0";

    /// <summary>
    /// Gets whether CI versions are accepted.
    /// Defaults to true.
    /// </summary>
    public bool AllowCI => !_noCI;

    /// <summary>
    /// Initializes a new version bound on a valid <see cref="Base"/> version.
    /// </summary>
    /// <param name="version">The base version that must be valid (defaults to <see cref="SVersion.ZeroVersion"/>).</param>
    /// <param name="lock">The lock to apply.</param>
    /// <param name="minPrerelease">The minimal quality to accept.</param>
    /// <param name="allowCI">Whether CI versions (greater than <paramref name="version"/>) are accepted.</param>
    public SVersionBound( SVersion? version = null,
                          SVersionLock @lock = SVersionLock.NoLock,
                          string minPrerelease = "0",
                          bool allowCI = true )
    {
        ArgumentNullException.ThrowIfNull( minPrerelease, nameof( minPrerelease ) );
        if( version == null )
        {
            _base = SVersion.ZeroVersion;
        }
        else
        {
            if( !version.IsValid ) throw new ArgumentException( "Must be valid. Error: " + version.ErrorMessage, nameof( version ) );
            _base = version;
        }
        // Handle "stable" only constraint and normalize MinPrerelease.
        if( minPrerelease.Length == 0 )
        {
            // LockPatch on Stable is Lock.
            if( @lock == SVersionLock.LockPatch )
            {
                @lock = SVersionLock.Lock;
            }
            _minPrerelease = string.Empty;
        }
        else
        {
            _minPrerelease = minPrerelease;
        }
        _lock = @lock;
        _noCI = !allowCI;
    }

    /// <summary>
    /// Sets a lock by returning this or a new <see cref="SVersionBound"/>.
    /// </summary>
    /// <param name="r">The lock to set.</param>
    /// <returns>This or a version bound.</returns>
    public SVersionBound SetLock( SVersionLock r ) => r != _lock ? new SVersionBound( _base, r, MinPrerelease, !_noCI ) : this;

    /// <summary>
    /// Sets a minimal prerelease name by returning this or a new <see cref="SVersionBound"/>.
    /// <para>
    /// The weakest prerelease is "0", the strongest is the empty string "" (stable versions).
    /// </para>
    /// </summary>
    /// <param name="prerelease">The minimal prerelease to set.</param>
    /// <returns>This or a new version bound.</returns>
    public SVersionBound SetMinPrerelease( string prerelease )
    {
        var p = prerelease.ToLowerInvariant();
        return p == _minPrerelease
                ? this
                : new SVersionBound( _base, _lock, p, !_noCI );
    }

    /// <summary>
    /// Sets <see cref="AllowCI"/> by returning this or a new <see cref="SVersionBound"/>.
    /// </summary>
    /// <param name="allowCI">Whether CI build versions must be accepted.</param>
    /// <returns>This or a new version bound.</returns>
    public SVersionBound SetAllowCI( bool allowCI )
    {
        return _noCI == !allowCI
                ? this
                : new SVersionBound( _base, _lock, MinPrerelease, allowCI );
    }

    static string UnionPrerelease( string? p1, string? p2 )
    {
        return p1 is null or "0" || p2 is null or "0"
                ? "0"
                : SVersion.ComparePrerelease( p1, p2 ) < 0
                    ? p1
                    : p2;
    }

    /// <summary>
    /// Merges this version bound with another one: smallest <see cref="Base"/>, weakest <see cref="Lock"/>, true AllowCI  wins and  version wins.
    /// </summary>
    /// <param name="other">The other bound.</param>
    /// <returns>The union of this and the other.</returns>
    public SVersionBound Union( in SVersionBound other )
    {
        var minBase = _base > other._base ? other : this;
        return minBase.SetLock( _lock.Union( other._lock ) )
                      .SetMinPrerelease( UnionPrerelease( _minPrerelease, other._minPrerelease ) )
                      .SetAllowCI( AllowCI || other.AllowCI );
    }

    static string IntersectPrerelease( string? p1, string? p2 )
    {
        return p1 is null or "0"
                ? (p2 ?? "0")
                : p2 is null or "0"
                    ? p1
                    : StringComparer.Ordinal.Compare( p1, p2 ) > 0
                        ? p1
                        : p2;
    }

    /// <summary>
    /// Intersects this version bound with another one.
    /// </summary>
    /// <param name="other">The other bound.</param>
    /// <returns>The intersection of this and the other.</returns>
    public SVersionBound Intersect( in SVersionBound other )
    {
        var maxBase = _base > other._base ? this : other;
        return maxBase.SetLock( _lock.Intersect( other._lock ) )
                      .SetMinPrerelease( IntersectPrerelease( _minPrerelease, other._minPrerelease ) )
                      .SetAllowCI( AllowCI && other.AllowCI );
    }

    /// <summary>
    /// Applies this bound to a version and returns whether it satisfies this <see cref="SVersionBound"/>.
    /// </summary>
    /// <param name="v">The version to challenge.</param>
    /// <returns>True if this version fits in this bound, false otherwise.</returns>
    public bool Satisfy( in SVersion v )
    {
        if( _base == null ) return true;
        // Applies allowCI before base comparison: if AllowCI is false, a CI version must be rejected, even if
        // base is a CI version. 
        if( _noCI && v.IsCI )
        {
            return false;
        }
        // Same for the MinPreRelease: this applies regardless of the base's prerelease.
        if( SVersion.ComparePrerelease( _minPrerelease, v.Prerelease ) > 0 )
        {
            return false;
        }
        int cmp = _base.CompareTo( v );
        // If v is lower than this Base, it's over.
        if( cmp > 0 ) return false;
        // If v is the Base, it's trivially okay (because we handled the AllowCI and MinPreRelease above). 
        if( cmp == 0 ) return true;
        // Is the greater v "reachable"?
        Debug.Assert( v.IsValid, "Since v is greater than this Base and this Base is valid." );
        return SatisfyVersionLock( _base, _lock, v );
    }

    static bool SatisfyVersionLock( SVersion baseVersion, SVersionLock l, SVersion v )
    {
        switch( l )
        {
            case SVersionLock.Lock:
            case SVersionLock.LockPatch:
                if( v.Major != baseVersion.Major || v.Minor != baseVersion.Minor || v.Patch != baseVersion.Patch ) return false; break;
            case SVersionLock.LockMinor:
                if( v.Major != baseVersion.Major || v.Minor != baseVersion.Minor ) return false; break;
            case SVersionLock.LockMajor:
                if( v.Major != baseVersion.Major ) return false; break;
        }
        return true;
    }

    /// <summary>
    /// Returns <see cref="All"/> if this bound is the "*" or "" of npm.
    /// <para>
    /// For npm, "*" and "" are ">=0.0.0[Stable]" when the "includePrerelease" is not used and this is the default.
    /// In such case, this will return the <see cref="All"/> bound that is ">=0.0.0-0".
    /// </para>
    /// </summary>
    /// <returns>This bound or the <see cref="All"/>.</returns>
    public SVersionBound NormalizeNpmVersionBoundAll()
    {
        return _base == _000Version && _lock == SVersionLock.NoLock && _minPrerelease == ""
                ? All
                : this;
    }

    /// <summary>
    /// Checks whether this version bound supersedes another one.
    /// </summary>
    /// <param name="other">The other bound.</param>
    /// <returns>True if this version bound supersedes the other one.</returns>
    public bool Contains( in SVersionBound other )
    {
        // If the other bound allows CI but this one disallows it, it's over.
        if( other.AllowCI && !AllowCI ) return false;
        // If the other allows lowest prerelease, it's over.
        if( SVersion.ComparePrerelease( _minPrerelease, other._minPrerelease ) > 0 ) return false;
        // Trivial case for locks: this Lock is stronger than the other one: it's over (for
        // instance, this locks the Minor and the other one only locks the Major: it will allow
        // versions that this one disallows).
        if( _lock > other._lock ) return false;
        // If the other.Base version is smaller, it's over.
        var thisBase = Base;
        var otherBase = other.Base;
        if( thisBase > otherBase )
        {
            // This is not PERFECTLY right!
            return false;
        }
        // Because we chose to not alter the base version (to make it fit the prerelease & CI constraint),
        // we must test our lock against the other base version.
        return SatisfyVersionLock( thisBase, _lock, otherBase );
    }

    /// <summary>
    /// Equality is based on <see cref="Base"/>, <see cref="Lock"/>, <see cref="MinPrerelease"/> and <see cref="AllowCI"/>.
    /// </summary>
    /// <param name="other">The other range.</param>
    /// <returns>True if they are the same version and restrictions.</returns>
    public bool Equals( SVersionBound other ) => Base == other.Base
                                                 && MinPrerelease == other.MinPrerelease
                                                 && _lock == other._lock
                                                 && _noCI == other._noCI;

    /// <summary>
    /// Equality is based on <see cref="Base"/>, <see cref="Lock"/>, <see cref="MinPrerelease"/> and <see cref="AllowCI"/>.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if the specified object is equal to this instance; otherwise, false.</returns>
    public override bool Equals( object? obj ) => obj is SVersionBound r && Equals( r );

    /// <summary>
    /// Equality is based on <see cref="Base"/>, <see cref="Lock"/>, <see cref="MinPrerelease"/> and <see cref="AllowCI"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine( Base, _lock, MinPrerelease, _noCI );

    /// <summary>
    /// Support == operator.
    /// </summary>
    /// <param name="b1">The left bound.</param>
    /// <param name="b2">The right bound.</param>
    /// <returns>True if equal, false otherwise.</returns>
    public static bool operator ==( in SVersionBound b1, in SVersionBound b2 ) => b1.Equals( b2 );

    /// <summary>
    /// Support != operator.
    /// </summary>
    /// <param name="b1">The left bound.</param>
    /// <param name="b2">The right bound.</param>
    /// <returns>True if different, false when the are equal.</returns>
    public static bool operator !=( in SVersionBound b1, in SVersionBound b2 ) => !b1.Equals( b2 );

    /// <summary>
    /// Overridden to return the base version and the restrictions.
    /// <list type="bullet">
    ///     <item>
    ///     For the default values: <see cref="SVersionLock.NoLock"/>, <see cref="MinPrerelease"/> is "0" and <see cref="AllowCI"/> is true: 
    ///     only Base version is returned ("1.2.3").
    ///     </item>
    ///     <item>
    ///     When <see cref="MinPrerelease"/> is "0", it never appears. When it can be parsed as a <see cref="CSVersionKind"/> then we use the
    ///     enumeration name (Alpha, Bravo,...etc.) else the MinPrelease is added between single quotes ('a.1').
    ///     </item>
    ///     <item>
    ///     When <see cref="AllowCI"/> is true, it never appears. When it is false, "NoCI" appears.
    ///     </item>
    /// </list>
    /// This has been designed to be roundtripable: it can always be parsed back by <see cref="TryParse(ReadOnlySpan{char}, out SVersionBound)"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString()
    {
        if( _lock == SVersionLock.NoLock && _minPrerelease is null or "0" && _noCI )
        {
            return Base.ToString();
        }
        bool hasPart = false;
        StringBuilder b = new StringBuilder( Base.ToString() );
        b.Append( '[' );
        if( _lock != SVersionLock.NoLock )
        {
            Debug.Assert( _lock is SVersionLock.Lock or SVersionLock.LockMajor or SVersionLock.LockMinor or SVersionLock.LockPatch );
            b.Append( _lock switch
            {
                SVersionLock.Lock => "Lock",
                SVersionLock.LockMajor => "LockMajor",
                SVersionLock.LockMinor => "LockMinor",
                _ => "LockPatch"
            } );
            hasPart = true;
        }
        if( _minPrerelease is not null and not "0" )
        {
            if( hasPart ) b.Append( ',' );
            var head = _minPrerelease.AsSpan();
            if( head.Length == 0 )
            {
                b.Append( "Stable" );
            }
            else
            {
                b.Append( ">=" ).Append( head );
            }
            hasPart = true;
        }
        if( !_noCI )
        {
            if( hasPart ) b.Append( ',' );
            b.Append( "AllowCI" );
        }
        b.Append( ']' );
        return b.ToString();
    }

    /// <summary>
    /// [Highly perfectible!] Returns the best possible NuGet version range for this bound.
    /// <para>
    /// What we can guaranty here is that if the initial parse result gives us a <see cref="SVersionBound"/>,
    /// its <see cref="ToNuGetString()"/> parsed back provides the exact same SVersionBound... but no more.
    /// </para>
    /// <list type="bullet">
    ///     <item><see cref="SVersionLock.Lock"/> is expressed in brackets: [5.1.2].</item>
    ///     <item>
    ///     <see cref="SVersionLock.LockMajor"/> is "5.*" when <see cref="MinPrerelease"/> is empty
    ///     and "5.*-*" for all other prerelease or if CI is allowed.
    ///     </item>
    ///     <item>
    ///     <see cref="SVersionLock.LockMinor"/> is "5.3.*" when <see cref="MinPrerelease"/> is empty
    ///     and "5.3.*-*" for all other prerelease or if CI is allowed.
    ///     </item>
    ///     <item>
    ///     <see cref="SVersionLock.LockPatch"/> is "5.3.1-*" because LockPatch can only be not stable
    ///     (the [LockPatch,Stable] combination is normalized as [Lock,Stable]).
    ///     </item>
    ///     <item>
    ///     The "0.0.0" version when Stable is expressed as "*".
    ///     </item>
    ///     <item>
    ///     The "0.0.0-0" version when NOT Stable is expressed as "*-*".
    ///     </item>
    /// </list>
    /// All other versions are simply the <see cref="Base"/> version: this uses the NuGet "min version inclusive" range.
    /// </summary>
    /// <returns>The NuGet version range.</returns>
    public string ToNuGetString()
    {
        if( _lock == SVersionLock.Lock )
        {
            return $"[{Base}]";
        }
        if( _lock == SVersionLock.LockMajor )
        {
            var suffix = _minPrerelease == "" ? "" : "-*";
            return $"{Base.Major}.*{suffix}";

        }
        var b = Base;
        if( _lock == SVersionLock.LockMinor )
        {
            var suffix = _minPrerelease == "" ? "" : "-*";
            return $"{b.Major}.{b.Minor}.*{suffix}";
        }
        if( Lock == SVersionLock.LockPatch )
        {
            Debug.Assert( _minPrerelease != "", "Normalized in ctor." );
            return $"{b.Major}.{b.Minor}.{b.Patch}-*";
        }
        // There is no Lock. There is unfortunately no way to express
        // the quality. The "min version inclusive" is the only way except
        // for the special case "0.0.0" when Stable that is "*" and the
        // ZeroVersion "0.0.0-0" when NOT stable that is "*-*".
        if( b.IsZeroVersion && _minPrerelease != "" ) return "*-*";
        if( _minPrerelease == ""
            && b.Major == 0
            && b.Minor == 0
            && b.Patch == 0
            && !b.IsPrerelease )
        {
            return "*";
        }
        return b.ToString();
    }

    /// <summary>
    /// [Highly perfectible!] Returns an approximation as a npm version range.
    /// <para>
    /// What we can guaranty here is that if the initial parse result gives us a <see cref="SVersionBound"/>,
    /// its <see cref="ToNpmString()"/> parsed back provides the exact same SVersionBound... but no more.
    /// </para>
    /// </summary>
    /// <returns>An approximated npm version range (but round-trippable).</returns>
    public string ToNpmString()
    {
        // If we have a prerealease tag and accept CI, we can use the "not includePreRelease" default
        // behavior: see https://github.com/npm/node-semver?tab=readme-ov-file#caret-ranges-123-025-004
        // This is very loose... But since prerelease tags are not use in the npm ecosystem (IMO because
        // it is unusable), we don't really care...
        //
        // There is a single exception to this...
        // Whatever the includePrerelease is, when we parse ">=0.0.0-0" we obtain the SVersionBound.All
        // (whereas "*" or "" give "^0.0.0" with the default false includePrerelease).
        // When this is the All (0.0.0-0[NoLock]) then we return the ">=0.0.0-0": the SVersionBound.All expressed as
        // ">=0.0.0-0" is roundtripable and this is important.
        // 
        // We could have also extended the exception to more "0.0.0" version but it is safer avoid exceptions
        // (the npm ecosystem uses the 0 major a lot). This Zero issue is better handled by using
        // the NormalizeNpmVersionBoundAll() helper.
        //
        var b = Base;
        if( b.IsPrerelease && _minPrerelease != "" )
        {
            if( b.IsZeroVersion && _lock == SVersionLock.NoLock && AllowCI )
            {
                return ">=0.0.0-0";
            }
            return $"^{b}";
        }
        // If we are locked, use the "=".
        if( _lock == SVersionLock.Lock )
        {
            return $"={b}";
        }
        if( _lock == SVersionLock.LockMajor )
        {
            if( b.Patch == 0 )
            {
                if( b.Minor == 0 )
                {
                    return $"^{b.Major}";
                }
                return $"^{b.Major}.{b.Minor}";
            }
            return $"^{b.Major}.{b.Minor}.{b.Patch}";
        }
        if( Lock == SVersionLock.LockMinor )
        {
            if( b.Patch == 0 )
            {
                if( b.Minor == 0 )
                {
                    return $"~{b.Major}";
                }
                return $"~{b.Major}.{b.Minor}";
            }
            return $"~{b.Major}.{b.Minor}.{b.Patch}";
        }
        return $">={b}";
    }
}
