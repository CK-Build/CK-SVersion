using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CK.Core;

/// <summary>
/// Semantic version implementation.
/// Strictly conforms to http://semver.org/ v2.0.0 (with a capture of the <see cref="ErrorMessage"/>
/// when <see cref="IsValid"/> is false) except that the 'v' prefix is allowed and handled transparently
/// and a <see cref="ParsedPrefix"/> can be handled.
/// </summary>
public partial class SVersion : IEquatable<SVersion>, IComparable<SVersion>
{
    /// <summary>
    /// The zero version is "0.0.0-0". It is syntactically valid and 
    /// its precedence is greater than null and lower than any other syntactically valid <see cref="SVersion"/>.
    /// </summary>
    static public readonly SVersion ZeroVersion = new SVersion( null, null, 0, 0, 0, "0", String.Empty );

    /// <summary>
    /// The last SemVer version possible has <see cref="int.MaxValue"/> as its Major, Minor and Patch and has no prerelease.
    /// It is syntactically valid and its precedence is greater than any other <see cref="SVersion"/>.
    /// </summary>
    static public readonly SVersion LastVersion = new SVersion( null, null, int.MaxValue, int.MaxValue, int.MaxValue, prerelease: string.Empty, buildMetaData: String.Empty );

    readonly int _major;
    readonly int _minor;
    readonly int _patch;
    // -1 when not applicable (almost always).
    readonly int _fourthPart;
    // Normalized to the empty string (stable version).
    readonly string _prerelease;
    // Normalized to the empty string.
    readonly string _buildMetaData;
    // _parsedText and _parsedVersion are both null or non null.
    readonly string? _parsedText;
    readonly string? _parsedVersion;
    // No 'v' prefix. Always available when IsValid.
    readonly string _normalizedText;
    // !IsValid => !null.
    readonly string? _errorMessage;
    // Computed on demand (from _parsedText and _parsedVersion).
    string? _parsedPrefix;

    // CSVersion version extension:
    // Defaults to 0.  
    readonly int _csReleaseNumber;
    // -1 for non-CI (ci number 0 is valid).
    readonly int _ciNumber;
    // CSVersion kind. None (the default) when IsValid is false.
    readonly CSVersionKind _csKind;

    SVersion( string? parsedText,
              string? parsedVersion,
              int major,
              int minor,
              int patch,
              string? prerelease,
              string? buildMetaData,
              int fourthPart = -1 )
    {
        prerelease ??= string.Empty;
        Debug.Assert( prerelease.Length == 0 || prerelease[0] != '-' );

        buildMetaData ??= string.Empty;
        Debug.Assert( buildMetaData.Length == 0 || buildMetaData[0] != '+' );

        _major = major;
        _minor = minor;
        _patch = patch;
        _fourthPart = fourthPart;
        _prerelease = prerelease;
        _buildMetaData = buildMetaData;
        _parsedText = parsedText;
        _parsedVersion = parsedVersion;
        _normalizedText = ComputeNormalizedText( major, minor, patch, fourthPart, prerelease, buildMetaData );
    }

    static string ComputeNormalizedText( int major, int minor, int patch, int fourthPart, string prerelease, string buildMetaData )
    {
        var t = fourthPart == -1
                    ? String.Format( CultureInfo.InvariantCulture, "{0}.{1}.{2}", major, minor, patch )
                    : String.Format( CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", major, minor, patch, fourthPart );
        if( prerelease.Length > 0 ) t += '-' + prerelease;
        if( buildMetaData.Length > 0 ) t += '+' + buildMetaData;
        return t;
    }

    /// <summary>
    /// Initializes a new invalid instance.
    /// </summary>
    /// <param name="error">Error message. Must not be null, empty or whitespace.</param>
    /// <param name="parsedText">Optional parsed text.</param>
    public SVersion( string error, string? parsedText )
    {
        if( String.IsNullOrWhiteSpace( error ) ) throw new ArgumentNullException( nameof( error ) );
        _errorMessage = error;
        _major = _minor = _patch = _fourthPart = -1;
        _prerelease = String.Empty;
        _buildMetaData = String.Empty;
        _parsedText = parsedText;
        _normalizedText = error;
        _ciNumber = -1;
    }

    /// <summary>
    /// Gets the major version.
    /// When <see cref="IsValid"/> is true, necessarily greater or equal to 0, otherwise -1.
    /// </summary>
    public int Major => _major;

    /// <summary>
    /// Gets the minor version.
    /// When <see cref="IsValid"/> is true, necessarily greater or equal to 0, otherwise -1.
    /// </summary>
    public int Minor => _minor;

    /// <summary>
    /// Gets the patch version.
    /// When <see cref="IsValid"/> is true, necessarily greater or equal to 0, otherwise -1.
    /// </summary>
    public int Patch => _patch;

    /// <summary>
    /// This unfortunate property handles the deviant use of a 4th part after the minor.
    /// Its default value is -1 (for true SemVer version).
    /// </summary>
    public int FourthPart => _fourthPart;

    /// <summary>
    /// Gets the prerelease tag without the leading '-'.
    /// This is the empty string when this is a Stable release or when <see cref="IsValid"/> is false.
    /// </summary>
    public string Prerelease => _prerelease;

    /// <summary>
    /// Gets whether this is a prerelease: a prerelease -tag exists.
    /// If this version is valid and is not a prerelease then this is a Stable release.
    /// </summary>
    public bool IsPrerelease => _prerelease.Length > 0;

    /// <summary>
    /// Gets whether this is a Stable release (valid and not a prerelease).
    /// </summary>
    public bool IsStable => IsValid && _prerelease.Length == 0;

    /// <summary>
    /// Gets whether this version belongs to the "CSVersion" subset.
    /// </summary>
    public bool IsCSVersion => _csKind != CSVersionKind.None;

    /// <summary>
    /// Gets the "Conformant SVersion" kind if this version follows these conventions, <see cref="CSVersionKind.None"/> otherwise.
    /// </summary>
    public CSVersionKind VersionKind => _csKind;

    /// <summary>
    /// Gets the release number. Defaults to 0.
    /// <para>
    /// This applies to <see cref="CSVersionKind.Exploratory"/> and to all prerelease versions (from <see cref="CSVersionKind.Alpha"/>
    /// to <see cref="CSVersionKind.Zulu"/>).
    /// </para>
    /// </summary>
    public int ReleaseNumber => _csReleaseNumber;

    /// <summary>
    /// Gets whether this version is a CI build version (a "post-build" version).
    /// </summary>
    public bool IsCI => _ciNumber >= 0;

    /// <summary>
    /// Gets the CI build number. -1 when this version is not a "post-build" version.
    /// This applies to any <see cref="CSVersionKind"/>.
    /// <para>
    /// Note that 0 is a valid CI number (<c>1.2.3--ci.0</c> is a valid version).
    /// </para>
    /// </summary>
    public int CINumber => _ciNumber;

    /// <summary>
    /// Gets the build meta data (without the leading '+').
    /// Never null, always normalized to the empty string.
    /// </summary>
    public string BuildMetaData => _buildMetaData;

    /// <summary>
    /// An error message that describes the error if <see cref="IsValid"/> is false. Null otherwise.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>
    /// Gets whether this <see cref="SVersion"/> is valid (<see cref="NormalizedText"/> is not null).
    /// When false, then <see cref="ErrorMessage"/> is not null (and NormalizedText is null).
    /// </summary>
    [MemberNotNullWhen( false, nameof( ErrorMessage ) )]
    [MemberNotNullWhen( true, nameof( NormalizedText ) )]
    public bool IsValid => _errorMessage == null;

    /// <summary>
    /// Gets whether this version is the <see cref="ZeroVersion"/> (0.0.0-0).
    /// </summary>
    public bool IsZeroVersion => _major == 0 && _minor == 0 && _patch == 0 && _prerelease == "0";

    /// <summary>
    /// Gets the parsed text (concatenation of <see cref="ParsedPrefix"/> and <see cref="ParsedVersion"/>). 
    /// Available even if <see cref="IsValid"/> is false.
    /// <para>
    /// It is null if the original parsed string was null or this version has been explicitly created and not parsed.
    /// </para>
    /// </summary>
    public string? ParsedText => _parsedText;

    /// <summary>
    /// Gets the parsed version. May start with the 'v' allowed initial.
    /// <para>
    /// It is null if the original parsed string was null or this version has been explicitly created and not parsed.
    /// </para>
    /// </summary>
    public string? ParsedVersion => _parsedVersion;

    /// <summary>
    /// Gets the parsed prefix that may appear before the <see cref="ParsedVersion"/>.
    /// <para>
    /// It is null if the original parsed string was null or this version has been explicitly created and not parsed.
    /// When this is not empty, this doesn't contain the optional 'v' initial of the <see cref="ParsedVersion"/>.
    /// </para>
    /// </summary>
    public string? ParsedPrefix
    {
        get
        {
            if( _parsedPrefix == null && _parsedText != null )
            {
                Debug.Assert( _parsedVersion != null );
                int len = _parsedText.Length - _parsedVersion.Length;
                _parsedPrefix = len > 0 ? _parsedText.Substring( 0, len ) : string.Empty;
            }
            return _parsedPrefix;
        }
    }

    /// <summary>
    /// Gets the normalized version as a string.
    /// Null if <see cref="IsValid"/> is false.
    /// </summary>
    public string? NormalizedText => _normalizedText;

    /// <summary>
    /// Returns a new <see cref="SVersion"/> with a potentially new <see cref="BuildMetaData"/>.
    /// <see cref="IsValid"/> must be true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    /// <param name="buildMetaData">The new build meta data or null to remove it.</param>
    /// <returns>The version.</returns>
    public SVersion SetBuildMetaData( string? buildMetaData )
    {
        buildMetaData ??= String.Empty;
        return buildMetaData == _buildMetaData
                ? this
                : new SVersion( null, null, _major, _minor, _patch, _prerelease, buildMetaData, _fourthPart );
    }

    /// <summary>
    /// Creates a new instance of the <see cref="SVersion" />.
    /// The created version may not be <see cref="IsValid"/>.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    /// <param name="prerelease">The optional prerelease version (without leading '-': "alpha", "r.1.ci.2", etc.).</param>
    /// <param name="buildMetaData">The optional build meta data (without leading '+').</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="fourthPart">Optional, non standard, fourth version part.</param>
    /// <returns>The <see cref="SVersion"/>.</returns>
    public static SVersion Create( int major,
                                   int minor,
                                   int patch,
                                   string? prerelease = null,
                                   string? buildMetaData = null,
                                   bool checkBuildMetaDataSyntax = true,
                                   int fourthPart = -1 )
    {
        return DoCreate( null,
                         null,
                         major,
                         minor,
                         patch,
                         fourthPart,
                         prerelease ?? String.Empty,
                         buildMetaData ?? String.Empty,
                         checkBuildMetaDataSyntax );
    }

    /// <summary>
    /// Tries to match a version pattern and forward the head on success.
    /// </summary>
    /// <param name="head">The head to parse.</param>
    /// <param name="version">
    /// The resulting version. 
    /// On failure, this is null when the version pattern has not matched at all but other errors will provide a non null 
    /// result with a false <see cref="IsValid"/> (and a <see cref="ErrorMessage"/>).
    /// </param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// By default the version can appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// </param>
    /// <returns>True on success (the head is forwarded), false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head,
                                 [NotNullWhen( true )] out SVersion? version,
                                 bool checkBuildMetaDataSyntax = true,
                                 bool allowPrefix = true )
    {
        var m = SVersionRegEx().EnumerateMatches( head );
        if( !m.MoveNext() )
        {
            version = null;
            return false;
        }
        int vLength = m.Current.Index + m.Current.Length;
        version = ParseNoThrow( new string( head.Slice( 0, vLength ) ), checkBuildMetaDataSyntax, allowPrefix );
        return version.IsValid;
    }

    /// <summary>
    /// Parses the specified string to a semantic version and returns a <see cref="SVersion"/> that 
    /// may not be <see cref="IsValid"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// By default the version can appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// </param>
    /// <param name="allowTrailingSuffix">
    /// True to allow <paramref name="s"/> to be longer than the version: on success, the exact parsed version length is 
    /// given by the <see cref="ParsedText"/>'s length.
    /// </param>
    /// <returns>The SVersion object that may not be <see cref="IsValid"/>.</returns>
    public static SVersion ParseNoThrow( string? s,
                                         bool checkBuildMetaDataSyntax = true,
                                         bool allowPrefix = true,
                                         bool allowTrailingSuffix = false )
    {
        if( string.IsNullOrEmpty( s ) ) return new SVersion( "Null or empty version string.", s );
        Match m = SVersionRegEx().Match( s );
        if( !m.Success )
        {
            return new SVersion( "Pattern not matched.", s );
        }
        string parsedText = s;
        int parsedLength = m.Index + m.Length;
        if( s.Length > parsedLength )
        {
            if( !allowTrailingSuffix )
            {
                // Provides the full string for the error.
                return new SVersion( "Unexpected characters after version.", s );
            }
            parsedText = s.Substring( 0, parsedLength );
        }
        string sMajor = m.Groups[1].Value;
        string sMinor = m.Groups[2].Value;
        string sPatch = m.Groups[3].Value;
        string sFourthPart = m.Groups[4].Value;
        if( !int.TryParse( sMajor, out int major ) ) return new SVersion( "Invalid Major.", s );
        if( !int.TryParse( sMinor, out int minor ) ) return new SVersion( "Invalid Major.", s );
        if( !int.TryParse( sPatch, out int patch ) ) return new SVersion( "Invalid Patch.", s );

        int fourthPart = -1;
        if( !string.IsNullOrEmpty( sFourthPart ) )
        {
            if( !int.TryParse( sFourthPart, out fourthPart ) ) return new SVersion( "Invalid FourthPart.", s );
        }

        return DoCreate( parsedText, m.Value, major, minor, patch, fourthPart, m.Groups[5].Value, m.Groups[6].Value, checkBuildMetaDataSyntax );
    }

    /// <summary>
    /// Standard TryParse pattern that returns a boolean rather than the resulting <see cref="SVersion"/>.
    /// See <see cref="TryParse(string,bool,bool)"/>.
    /// </summary>
    /// <param name="s">String to parse.</param>
    /// <param name="v">Resulting version.</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// By default the version can appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// </param>
    /// <param name="allowTrailingSuffix">
    /// True to allow <paramref name="s"/> to be longer than the version: on success, the exact parsed version length is 
    /// given by the <see cref="ParsedText"/>'s length.
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? s, out SVersion v, bool checkBuildMetaDataSyntax = true, bool allowPrefix = true, bool allowTrailingSuffix = false )
    {
        v = ParseNoThrow( s, checkBuildMetaDataSyntax, allowPrefix, allowTrailingSuffix );
        return v.IsValid;
    }

    /// <summary>
    /// Parses the specified string to a semantic version and throws an <see cref="ArgumentException"/> 
    /// it the resulting <see cref="IsValid"/> is false.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// By default the version can appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// </param>
    /// <param name="allowTrailingSuffix">
    /// True to allow <paramref name="s"/> to be longer than the version: on success, the exact parsed version length is 
    /// given by the <see cref="ParsedText"/>'s length.
    /// </param>
    /// <returns>The SVersion object.</returns>
    public static SVersion Parse( string? s, bool checkBuildMetaDataSyntax = true, bool allowPrefix = true, bool allowTrailingSuffix = false )
    {
        var v = ParseNoThrow( s, checkBuildMetaDataSyntax, allowPrefix, allowTrailingSuffix );
        if( !v.IsValid ) throw new ArgumentException( v.ErrorMessage, nameof( s ) );
        return v;
    }

    static SVersion DoCreate( string? parsedText,
                              string? parsedVersion,
                              int major,
                              int minor,
                              int patch,
                              int fourthPart,
                              string prerelease,
                              string buildMetaData,
                              bool checkBuildMetaDataSyntax )
    {
        Debug.Assert( (parsedText != null) == (parsedVersion != null) );
        Debug.Assert( prerelease != null && buildMetaData != null );
        if( major < 0 || minor < 0 || patch < 0 ) return new SVersion( "Major, minor and patch must positive or 0.", parsedText );

        if( buildMetaData.Length > 0 && checkBuildMetaDataSyntax )
        {
            var error = ValidateDottedIdentifiers( buildMetaData, "build metadata", out _ );
            if( error != null ) return new SVersion( error, parsedText );
        }
        // Validate the prerelease.
        if( prerelease.Length > 0 )
        {
            var error = ValidateDottedIdentifiers( prerelease, "pre-release", out var captures );
            if( error != null ) return new SVersion( error, parsedText );

        }
        return new SVersion( parsedText, parsedVersion, major, minor, patch, prerelease, buildMetaData, fourthPart );
    }

    internal static string? ValidateDottedIdentifiers( string s, string partName, out CaptureCollection? captures )
    {
        Match m = DottedPartRegEx().Match( s );
        if( !m.Success )
        {
            captures = null;
            return "Invalid " + partName;
        }
        captures = m.Groups[1].Captures;
        Debug.Assert( captures.Count > 0 );
        foreach( Capture id in captures )
        {
            Debug.Assert( id.Value.Length > 0 );
            string p = id.Value;
            if( p.Length > 1 && p[0] == '0' )
            {
                int i = 1;
                while( i < p.Length )
                {
                    if( !char.IsDigit( p, i++ ) ) break;
                }
                if( i == p.Length )
                {
                    return $"Numeric identifiers in {partName} must not start with a 0.";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Overridden to return the <see cref="ErrorMessage"/> if not null or the <see cref="NormalizedText"/>.
    /// </summary>
    /// <returns>The textual representation.</returns>
    public override string ToString() => ErrorMessage ?? NormalizedText!;

    /// <summary>
    /// Gets the standard Informational version string.
    /// If <see cref="SVersion.IsValid"/> is false this throws an <see cref="InvalidOperationException"/>: 
    /// the constant <see cref="InformationalVersion.ZeroInformationalVersion"/> should be used when IsValid is false.
    /// </summary>
    /// <param name="commitSha">The SHA1 of the commit (must be 40 hex digits).</param>
    /// <param name="commitDateUtc">The commit date (must be in UTC).</param>
    /// <returns>The informational version.</returns>
    public string GetInformationalVersion( string commitSha, DateTime commitDateUtc )
    {
        if( !IsValid ) throw new InvalidOperationException( "IsValid must be true. Use InformationalVersion.ZeroInformationalVersion when IsValid is false." );
        return InformationalVersion.BuildInformationalVersion( this, commitSha, commitDateUtc );
    }

    /// <summary>
    /// Compares this with another <see cref="SVersion"/>.
    /// Null is lower than any version. An invalid version is lower than any valid version.
    /// This (and the overloaded comparison operators) compares Semantic Version form (the <see cref="NormalizedText"/>)
    /// according to the SemVer 2.0 specification.
    /// </summary>
    /// <param name="other">
    /// The other version to compare with this instance. Can be null (null is lower than any version).
    /// </param>
    /// <returns>Standard positive, negative or zero value.</returns>
    public int CompareTo( SVersion? other )
    {
        if( other is null ) return 1;
        if( IsValid )
        {
            if( !other.IsValid ) return 1;
        }
        else if( other.IsValid ) return -1;
        return CompareValid( other );
    }

    int CompareValid( SVersion other )
    {
        var r = _major - other._major;
        if( r != 0 ) return r;

        r = _minor - other._minor;
        if( r != 0 ) return r;

        r = _patch - other._patch;
        if( r != 0 ) return r;

        return ComparePrerelease( _prerelease, other._prerelease );
    }

    /// <summary>
    /// Helper comparison function that implements https://semver.org/#spec-item-11 
    /// (4 - Precedence for two pre-release versions with the same major, minor, and patch.)
    /// </summary>
    /// <param name="x">The first prerelease.</param>
    /// <param name="y">The second prerelease.</param>
    /// <returns>Standard comparison value (positive when <c>x &gt; y</c>).</returns>
    public static int ComparePrerelease( ReadOnlySpan<char> x, ReadOnlySpan<char> y )
    {
        // Fun with Span and allocation-free string parsing.
        if( x.Length == 0 ) return y.Length == 0 ? 0 : 1;
        if( y.Length == 0 ) return -1;

        Span<Range> xStore = stackalloc Range[1 + x.Length >> 1];
        var xParts = xStore.Slice( 0, x.Split( xStore, '.' ) );

        Span<Range> yStore = stackalloc Range[1 + y.Length >> 1];
        var yParts = yStore.Slice( 0, y.Split( yStore, '.' ) );

        int commonParts = xParts.Length;
        int ultimateResult = -1;
        if( yParts.Length < xParts.Length )
        {
            commonParts = yParts.Length;
            ultimateResult = 1;
        }
        else if( yParts.Length == xParts.Length )
        {
            ultimateResult = 0;
        }
        for( int i = 0; i < commonParts; i++ )
        {
            // var xP = xParts[i];
            // var yP = yParts[i];
            // ==> Use the Range indexing to obtain the parts as ReadOnlySpan<char>/
            var xP = x[xParts[i]];
            var yP = y[yParts[i]];
            int r;
            if( int.TryParse( xP, out int xN ) )
            {
                if( int.TryParse( yP, out int yN ) )
                {
                    r = xN - yN;
                    if( r != 0 ) return r;
                }
                else return -1;
            }
            else
            {
                if( int.TryParse( yP, out _ ) ) return 1;
                // r = StringComparer.OrdinalIgnoreCase.Compare( xP, yP );
                // ==> Replace the call to OrdinalIgnoreCase.Compare by the convenient
                //     extension method on ReadOnlySpan<char>.
                r = xP.CompareTo( yP, StringComparison.OrdinalIgnoreCase );
                if( r != 0 ) return r;
            }
        }
        return ultimateResult;
    }

    /// <summary>
    /// Equality ignore this <see cref="BuildMetaData"/>.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if the specified object is equal to this instance; otherwise, false.</returns>
    public override bool Equals( object? obj )
    {
        if( obj is null ) return false;
        if( ReferenceEquals( this, obj ) ) return true;
        return Equals( obj as SVersion );
    }

    /// <summary>
    /// Returns a hash code that ignores the <see cref="BuildMetaData"/>.
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode() => HashCode.Combine( _major, _minor, _patch, _fourthPart, _prerelease );

    /// <summary>
    /// Versions are equal if and only if <see cref="IsValid"/>, <see cref="Major"/>, <see cref="Minor"/>,
    /// <see cref="Patch"/> and <see cref="Prerelease"/> are equals. <see cref="BuildMetaData"/> is ignored.
    /// No other members are used for equality and comparison.
    /// </summary>
    /// <param name="other">Other version.</param>
    /// <returns>True if they are the same regardless of <see cref="BuildMetaData"/>.</returns>
    public bool Equals( SVersion? other )
    {
        if( other is null ) return false;
        if( ReferenceEquals( this, other ) ) return true;
        if( IsValid )
        {
            if( !other.IsValid ) return false;
        }
        else if( other.IsValid ) return false;
        return Major == other.Major &&
               Minor == other.Minor &&
               Patch == other.Patch &&
               Prerelease == other.Prerelease;
    }

    public static bool operator ==( SVersion? left, SVersion? right ) => left is null ? right is null : left.Equals( right );

    public static bool operator !=( SVersion? left, SVersion? right ) => !(left == right);

    public static bool operator <( SVersion? left, SVersion? right ) => left is null ? right is not null : left.CompareTo( right ) < 0;

    public static bool operator <=( SVersion? left, SVersion? right ) => left is null || left.CompareTo( right ) <= 0;

    public static bool operator >( SVersion? left, SVersion? right ) => left is not null && left.CompareTo( right ) > 0;

    public static bool operator >=( SVersion? left, SVersion? right ) => left is null ? right is null : left.CompareTo( right ) >= 0;


    // This checks a SVersion (no initial ^ to handle the potential ParsedPrefix).
    // This is not enough to guaranty that the version is valid.
    [GeneratedRegex( @"v?(?<1>0|[1-9][0-9]*)\.(?<2>0|[1-9][0-9]*)\.(?<3>0|[1-9][0-9]*)(\.(?<4>0|[1-9][0-9]*))?((?!-)|(\-(?<5>[0-9A-Za-z\-\.]+)))(\+(?<6>[0-9A-Za-z\-\.]+))?", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    internal static partial Regex SVersionRegEx();

    // This applies to PreRelease and BuildMetaData.
    // This is not enough to guaranty that the identifier is valid.
    [GeneratedRegex( @"^(?<1>0|[1-9][0-9]*|[0-9A-Za-z\-]+)(\.(?<1>0|[1-9][0-9]*|[0-9A-Za-z\-]+))*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex DottedPartRegEx();
}

