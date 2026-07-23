using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CK.Core;

/// <summary>
/// Semantic version implementation.
/// Strictly conforms to http://semver.org/ v2.0.0 (with a capture of the <see cref="ErrorMessage"/>
/// when <see cref="IsValid"/> is false) except that the 'v' prefix is allowed and handled transparently
/// and a <see cref="ParsedPrefix"/> can be handled.
/// <para>
/// Conformant SVersions are a subset of valid SVersion identified by the <see cref="CSVersionKind"/> <see cref="VersionKind"/>
/// property that is not <see cref="CSVersionKind.None"/>.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public partial class SVersion : IEquatable<SVersion>, IComparable<SVersion>
{
    /// <summary>
    /// The zero version is "0.0.0-0". It is syntactically valid and 
    /// its precedence is greater than null and lower than any other syntactically valid <see cref="SVersion"/>.
    /// </summary>
    static public readonly SVersion ZeroVersion = new SVersion( null, null, 0, 0, 0, "0", String.Empty, CSVersionKind.None, 0, -1, false, false, false );

    /// <summary>
    /// The supremum of the exploratory versions is "0.0.0-1". All <see cref="CSVersionKind.Exploratory"/> versions
    /// are between "0.0.0-0" and "0.0.0-1".
    /// </summary>
    static public readonly SVersion SupExploratoryVersion = new SVersion( null, null, 0, 0, 0, "1", String.Empty, CSVersionKind.None, 0, -1, false, false, false );

    /// <summary>
    /// The first Conformant SVersion is "0.0.0-alpha".
    /// </summary>
    static public readonly SVersion FirstCSVersion = new SVersion( null, null, 0, 0, 0, "alpha", String.Empty, CSVersionKind.Alpha, 0, -1, false, false, false );

    /// <summary>
    /// The last SemVer version possible has <see cref="int.MaxValue"/> as its Major, Minor and Patch and has no prerelease.
    /// It is syntactically valid and its precedence is greater than any other <see cref="SVersion"/>.
    /// </summary>
    static public readonly SVersion LastVersion = new SVersion( null,
                                                                null,
                                                                int.MaxValue,
                                                                int.MaxValue,
                                                                int.MaxValue,
                                                                prerelease: string.Empty,
                                                                buildMetaData: String.Empty,
                                                                CSVersionKind.Stable,
                                                                csPrereleaseNumber: 0,
                                                                ciNumber: -1,
                                                                false,
                                                                false,
                                                                false );

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
    // Always available (the version without 'v' prefix when IsValid and the "ErrorMessage (ParsedText)" on error).
    readonly string _toString;
    // !IsValid => !null.
    readonly string? _errorMessage;
    // Computed on demand (from _parsedText and _parsedVersion).
    string? _parsedPrefix;

    // CSVersion version extension:
    // Defaults to 0.  
    readonly int _csPrereleaseNumber;
    // -1 for non-CI (ci number 0 is valid).
    readonly int _ciNumber;
    // CSVersion kind. None (the default) when IsValid is false.
    readonly CSVersionKind _csKind;
    // This applies to regular SVersion but for a CSVersion to be valid, at most
    // one of them must be true and IsCI must be false.
    readonly bool _hasFakeMetadata;
    readonly bool _hasDeprecatedMetadata;
    readonly bool _hasInvalidMetadata;

    SVersion( string? parsedText,
              string? parsedVersion,
              int major,
              int minor,
              int patch,
              string? prerelease,
              string? buildMetaData,
              CSVersionKind csKind,
              int csPrereleaseNumber,
              int ciNumber,
              bool hasFakeMetadata,
              bool hasDeprecatedMetadata,
              bool hasInvalidMetadata,
              int fourthPart = -1 )
    {
        // prerelease may start with '-' (we use this for the double dash --ci trick). 
        prerelease ??= string.Empty;

        // But buildMetaData cannot start with a '+'.
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
        _csKind = csKind;
        _csPrereleaseNumber = csPrereleaseNumber;
        _ciNumber = ciNumber;
        _hasFakeMetadata = hasFakeMetadata;
        _hasDeprecatedMetadata = hasDeprecatedMetadata;
        _hasInvalidMetadata = hasInvalidMetadata;
        if( fourthPart == -1 )
        {
            _toString = prerelease.Length > 0
                ? String.Format( CultureInfo.InvariantCulture, "{0}.{1}.{2}-{3}", major, minor, patch, prerelease )
                : String.Format( CultureInfo.InvariantCulture, "{0}.{1}.{2}", major, minor, patch );
        }
        else
        {
            // Uncommon case.
            _toString = String.Format( CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", major, minor, patch, fourthPart );
            if( prerelease.Length > 0 ) _toString += '-' + prerelease;
        }
        // Uncommon case.
        if( buildMetaData.Length > 0 ) _toString += '+' + buildMetaData;
    }

    SVersion( string prefix, SVersion o )
    {
        _major = o._major;
        _minor = o._minor;
        _patch = o._patch;
        _fourthPart = o._fourthPart;
        _prerelease = o._prerelease;
        _buildMetaData = o._buildMetaData;
        _csKind = o._csKind;
        _csPrereleaseNumber = o._csPrereleaseNumber;
        _ciNumber = o._ciNumber;
        _parsedPrefix = prefix;
        if( (_parsedVersion = o._parsedVersion) != null )
        {
            _parsedText = prefix + _parsedVersion;
        }
        _toString = o._toString;
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
        _ciNumber = -1;
        _toString = string.IsNullOrWhiteSpace( parsedText )
                        ? error
                        :  $"{error} ({parsedText})";
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
    [MemberNotNullWhen( true, nameof( BranchName ) )]
    public bool IsCSVersion => _csKind != CSVersionKind.None;

    /// <summary>
    /// Gets the "Conformant SVersion" kind if this version follows these conventions, <see cref="CSVersionKind.None"/> otherwise.
    /// </summary>
    public CSVersionKind VersionKind => _csKind;

    /// <summary>
    /// Gets the prerelease number. Defaults to 0.
    /// <para>
    /// This applies to <see cref="CSVersionKind.Exploratory"/> and to all prerelease versions (from <see cref="CSVersionKind.Alpha"/>
    /// to <see cref="CSVersionKind.Zulu"/>).
    /// This is always 0 for <see cref="CSVersionKind.Stable"/> versions.
    /// </para>
    /// </summary>
    public int PrereleaseNumber => _csPrereleaseNumber;

    /// <summary>
    /// Returns the exploratory name if <see cref="VersionKind"/> is <see cref="CSVersionKind.Exploratory"/>, the empty span otherwise.
    /// </summary>
    public ReadOnlySpan<char> ExploratoryName
    {
        get
        {
            if( _csKind is CSVersionKind.Exploratory )
            {
                Debug.Assert( _prerelease.StartsWith( "0.", StringComparison.Ordinal ) );
                if( _csPrereleaseNumber == 0 && _ciNumber == -1 )
                    return _prerelease.AsSpan( 2 );
                var p = _prerelease.AsSpan( 2 );
                return p.Slice( 0, p.IndexOf( '.' ) );
            }
            return default;
        }
    }

    /// <summary>
    /// Gets a branch name for this version:
    /// <list type="bullet">
    ///     <item>null for <see cref="CSVersionKind.None"/> (when <see cref="IsCSVersion"/> is false).</item>
    ///     <item>The empty string for <see cref="CSVersionKind.Stable"/>.</item>
    ///     <item>Prereleases are simply their lowercase name (see <see cref="CSVersionKindExtensions.ToBranchName(CSVersionKind)"/>).</item>
    ///     <item>Exploratory branches are "explo/<see cref="ExploratoryName"/>".</item>
    /// </list>
    /// </summary>
    public string? BranchName => _csKind == CSVersionKind.None
                                    ? null
                                    : _csKind == CSVersionKind.Exploratory
                                        ? $"explo/{ExploratoryName}"
                                        : _csKind.ToBranchName();


    /// <summary>
    /// Gets whether this version is a CI build version (a "post-build" version):
    /// <see cref="CINumber"/> is 0 or positive.
    /// </summary>
    public bool IsCI => _ciNumber >= 0;

    /// <summary>
    /// Gets the CI build number. -1 when this version is not a "post-build" version.
    /// <para>
    /// This applies to any <see cref="CSVersionKind"/> except <see cref="CSVersionKind.None"/>
    /// (<see cref="IsCSVersion"/> must be true).
    /// </para>
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
    /// Gets whether a "fake" dotted identifier appears in <see cref="BuildMetaData"/> (case insensitive).
    /// </summary>
    public bool HasFakeMetadata => _hasFakeMetadata;

    /// <summary>
    /// Gets whether a "deprecated" dotted identifier appears in <see cref="BuildMetaData"/> (case insensitive).
    /// </summary>
    public bool HasDeprecatedMetadata => _hasDeprecatedMetadata;

    /// <summary>
    /// Gets whether a "invalid" dotted identifier appears in <see cref="BuildMetaData"/> (case insensitive).
    /// </summary>
    public bool HasInvalidMetadata => _hasInvalidMetadata;

    /// <summary>
    /// Gets whether this version is a "rough base" of the target. This version MUST be <see cref="SVersion.IsStable"/> otherwise
    /// an <see cref="InvalidOperationException"/> is thrown. The target is roughly based on this version if it has
    /// the same Major.Minor.Patch or any valid increment (Major+1.0.0, Major.Minor+1.0 or Major.Minor.Patch+1).
    /// <para>
    /// This accepts any prerelease of this version and any version that immediately follow this version, including their prereleases,
    /// so this accepts any "post release" of this version (with the double dash trick).
    /// </para>
    /// <para>
    /// This is used by the fake version:
    /// <list type="bullet">
    ///     <item>
    ///         For CI build versions: "1.0.0" is a rough base of "1.0.0--ci.1" (that is
    ///         an artificial CI build version that is used only for fake versions) and real CI build like 1.0.1--ci.0, 1.1.0--ci.4 or 2.0.0--ci.4.
    ///     </item>
    ///     <item>
    ///         For regular versions: "1.0.0" is a rough base of itself, of any of its prelease versions like "1.0.0-any", of its successors "1.0.1",
    ///         "1.1.0", "2.0.0" and any of their prereleases.
    ///     </item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="version">This stable version.</param>
    /// <param name="target">The target that may be roughly based on this stable version.</param>
    /// <returns></returns>
    public bool IsStableRoughBaseOf( SVersion target )
    {
        if( !IsStable ) throw new InvalidOperationException( $"Version '{_toString}' must be stable." );
        if( _major == target._major )
        {
            return _minor == target._minor
                    ? _patch == target._patch || _patch == target._patch + 1
                    : _minor == target._minor + 1 && target._patch == 0;
        }
        return _major == target._major + 1 && target._minor == 0 && target._patch == 0;
    }

    /// <summary>
    /// An error message that describes the error if <see cref="IsValid"/> is false. Null otherwise.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>
    /// Gets whether this <see cref="SVersion"/> is valid.
    /// When false, then <see cref="ErrorMessage"/> is not null.
    /// </summary>
    [MemberNotNullWhen( false, nameof( ErrorMessage ) )]
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
    /// Gets the parsed prefix that may appear before the <see cref="ParsedVersion"/> (without the optional 'v'
    /// initial of the <see cref="ParsedVersion"/>).
    /// <para>
    /// It is null if the original parsed string was null or this version has been explicitly created and not parsed
    /// (and <see cref="SetParsedPrefix(string)"/> has not been used to set it).
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
    /// Creates a new instance of the <see cref="SVersion" />.
    /// The created version may not be <see cref="IsValid"/>.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    /// <param name="prerelease">The optional prerelease version (without leading '-': "alpha", "r.1.ci.2", etc.).</param>
    /// <param name="buildMetaData">The optional build meta data (without leading '+').</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="mustBeCSVersion">
    /// True to only accept valid CSVersion (see <see cref="CSVersionKind"/>).
    /// By default, any version that follows the Semantic Versioning rules are accepted. 
    /// </param>
    /// <param name="fourthPart">Optional, non standard, fourth version part.</param>
    /// <returns>The <see cref="SVersion"/>.</returns>
    public static SVersion Create( int major,
                                   int minor,
                                   int patch,
                                   string? prerelease = null,
                                   string? buildMetaData = null,
                                   bool checkBuildMetaDataSyntax = true,
                                   bool mustBeCSVersion = false,
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
                         checkBuildMetaDataSyntax,
                         mustBeCSVersion );
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
    /// True to allow the version to appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// <para>
    /// Caution: in this mode, "001.0.0" is a valid version with a "00" prefix (but "v001.0.0" is not).
    /// </para>
    /// </param>
    /// <param name="mustBeCSVersion">
    /// True to only accept valid CSVersion (see <see cref="CSVersionKind"/>).
    /// By default, any version that follows the Semantic Versioning rules are accepted. 
    /// </param>
    /// <returns>True on success (the head is forwarded), false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head,
                                 [NotNullWhen( true )] out SVersion? version,
                                 bool checkBuildMetaDataSyntax = true,
                                 bool allowPrefix = false,
                                 bool mustBeCSVersion = false )
    {
        var m = SVersionRegEx().EnumerateMatches( head );
        if( !m.MoveNext() )
        {
            version = null;
            return false;
        }
        int vLength = m.Current.Index + m.Current.Length;
        version = ParseNoThrow( new string( head.Slice( 0, vLength ) ),
                                checkBuildMetaDataSyntax,
                                allowPrefix,
                                allowTrailingSuffix: false,
                                mustBeCSVersion );
        if( version.IsValid )
        {
            head = head.Slice( vLength );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parses the specified string to a semantic version and returns a <see cref="SVersion"/> that 
    /// may not be <see cref="IsValid"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// True to allow the version to appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// <para>
    /// Caution: in this mode, "001.0.0" is a valid version with a "00" prefix (but "v001.0.0" is not).
    /// </para>
    /// </param>
    /// <param name="allowTrailingSuffix">
    /// True to allow <paramref name="s"/> to be longer than the version: on success, the exact parsed version length is 
    /// given by the <see cref="ParsedText"/>'s length.
    /// </param>
    /// <param name="mustBeCSVersion">
    /// True to only accept valid CSVersion (see <see cref="CSVersionKind"/>).
    /// By default, any version that follows the Semantic Versioning rules are accepted. 
    /// </param>
    /// <returns>The SVersion object that may not be <see cref="IsValid"/>.</returns>
    public static SVersion ParseNoThrow( string? s,
                                         bool checkBuildMetaDataSyntax = true,
                                         bool allowPrefix = false,
                                         bool allowTrailingSuffix = false,
                                         bool mustBeCSVersion = false )
    {
        if( string.IsNullOrEmpty( s ) ) return new SVersion( "Null or empty version string.", s );
        Match m = SVersionRegEx().Match( s );
        if( !m.Success )
        {
            // Provides the full string as the ParsedText for the error.
            return new SVersion( "Pattern not matched.", s );
        }
        if( m.Index > 0 && !allowPrefix )
        {
            return new SVersion( "Disallowed prefix before version.", s );
        }
        string parsedText = s;
        int parsedLength = m.Index + m.Length;
        if( s.Length > parsedLength )
        {
            if( !allowTrailingSuffix )
            {
                return new SVersion( "Disallowed characters after version.", s );
            }
            parsedText = s.Substring( 0, parsedLength );
        }
        var sMajor = m.Groups[1].ValueSpan;
        var sMinor = m.Groups[2].ValueSpan;
        var sPatch = m.Groups[3].ValueSpan;
        var sFourthPart = m.Groups[4].ValueSpan;
        if( !int.TryParse( sMajor, NumberStyles.None, CultureInfo.InvariantCulture, out int major ) ) return new SVersion( "Invalid Major.", s );
        if( !int.TryParse( sMinor, NumberStyles.None, CultureInfo.InvariantCulture, out int minor ) ) return new SVersion( "Invalid Major.", s );
        if( !int.TryParse( sPatch, NumberStyles.None, CultureInfo.InvariantCulture, out int patch ) ) return new SVersion( "Invalid Patch.", s );

        int fourthPart = -1;
        if( sFourthPart.Length > 0 )
        {
            if( !int.TryParse( sFourthPart, NumberStyles.None, CultureInfo.InvariantCulture, out fourthPart ) ) return new SVersion( "Invalid FourthPart.", s );
        }

        return DoCreate( parsedText,
                         m.Value,
                         major,
                         minor,
                         patch,
                         fourthPart,
                         m.Groups[5].Value,
                         m.Groups[6].Value,
                         checkBuildMetaDataSyntax,
                         mustBeCSVersion );
    }

    /// <summary>
    /// Standard TryParse pattern that returns a boolean rather than the resulting <see cref="SVersion"/>.
    /// See <see cref="ParseNoThrow(string?, bool, bool, bool, bool)"/>.
    /// </summary>
    /// <param name="s">String to parse.</param>
    /// <param name="v">Resulting version.</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// True to allow the version to appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// <para>
    /// Caution: in this mode, "001.0.0" is a valid version with a "00" prefix (but "v001.0.0" is not).
    /// </para>
    /// </param>
    /// <param name="allowTrailingSuffix">
    /// True to allow <paramref name="s"/> to be longer than the version: on success, the exact parsed version length is 
    /// given by the <see cref="ParsedText"/>'s length.
    /// </param>
    /// <param name="mustBeCSVersion">
    /// True to only accept valid CSVersion (see <see cref="CSVersionKind"/>).
    /// By default, any version that follows the Semantic Versioning rules are accepted. 
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? s,
                                 out SVersion v,
                                 bool checkBuildMetaDataSyntax = true,
                                 bool allowPrefix = false,
                                 bool allowTrailingSuffix = false,
                                 bool mustBeCSVersion = false )
    {
        v = ParseNoThrow( s, checkBuildMetaDataSyntax, allowPrefix, allowTrailingSuffix, mustBeCSVersion );
        return v.IsValid;
    }

    /// <summary>
    /// Parses the specified string to a semantic version and throws an <see cref="ArgumentException"/> 
    /// it the resulting <see cref="IsValid"/> is false.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="checkBuildMetaDataSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <param name="allowPrefix">
    /// True to allow the version to appear after any prefix. On success, the <see cref="ParsedText"/> contains 
    /// a non empty <see cref="ParsedPrefix"/> followed by the <see cref="ParsedVersion"/>.
    /// <para>
    /// Caution: in this mode, "001.0.0" is a valid version with a "00" prefix (but "v001.0.0" is not).
    /// </para>
    /// </param>
    /// <param name="allowTrailingSuffix">
    /// True to allow <paramref name="s"/> to be longer than the version: on success, the exact parsed version length is 
    /// given by the <see cref="ParsedText"/>'s length.
    /// </param>
    /// <param name="mustBeCSVersion">
    /// True to only accept valid CSVersion (see <see cref="CSVersionKind"/>).
    /// By default, any version that follows the Semantic Versioning rules are accepted. 
    /// </param>
    /// <returns>The SVersion object.</returns>
    public static SVersion Parse( string? s,
                                  bool checkBuildMetaDataSyntax = true,
                                  bool allowPrefix = false,
                                  bool allowTrailingSuffix = false,
                                  bool mustBeCSVersion = false )
    {
        var v = ParseNoThrow( s, checkBuildMetaDataSyntax, allowPrefix, allowTrailingSuffix, mustBeCSVersion );
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
                              bool checkBuildMetaDataSyntax,
                              bool mustBeCSVersion )
    {
        Debug.Assert( (parsedText != null) == (parsedVersion != null) );
        Debug.Assert( prerelease != null && buildMetaData != null );
        if( major < 0 || minor < 0 || patch < 0 ) return new SVersion( "Major, minor and patch must positive or 0.", parsedText );

        bool hasFakeMetadata = false;
        bool hasDeprecatedMetadata = false;
        bool hasInvalidMetadata = false;
        if( buildMetaData.Length > 0 )
        {
            var error = ParseBuildMetadata( buildMetaData, checkBuildMetaDataSyntax, ref hasFakeMetadata, ref hasDeprecatedMetadata, ref hasInvalidMetadata );
            if( error != null ) return new SVersion( error, parsedText );
        }
        // Validate the prerelease.
        // When the 4th part is set, kind is None.
        CSVersionKind kind;
        if( fourthPart < 0 )
        {
            kind = CSVersionKind.Stable;
        }
        else
        {
            kind = CSVersionKind.None;
            if( mustBeCSVersion )
            {
                return new SVersion( "A four-part version is not a CSVersion.", parsedText );
            }
        }
        int csReleaseNumber = 0;
        int ciNumber = -1;

        if( prerelease.Length > 0 )
        {
            kind = CSVersionKind.None;
            var error = ValidateDottedIdentifiers( prerelease, "pre-release", out var captures );
            if( error != null ) return new SVersion( error, parsedText );
            Debug.Assert( captures != null && captures.Count > 0 );

            if( fourthPart < 0 )
            {
                var first = captures[0].ValueSpan;
                if( first.Equals( "-ci", StringComparison.OrdinalIgnoreCase ) )
                {
                    if( captures.Count == 2 && int.TryParse( captures[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out ciNumber ) )
                    {
                        kind = CSVersionKind.Stable;
                    }
                    else if( mustBeCSVersion )
                    {
                        return new SVersion( "Invalid CSVersion: error in --ci syntax.", parsedText );
                    }
                }
                else if( first.Length == 1 && first[0] == '0' )
                {
                    // "-0.XXX" (count = 2)
                    // "-0.XXX.1" (count = 3)
                    // "-0.XXX.1.ci.1" (count = 5)
                    // => count = 4 cannot be a CSVersion.
                    //    And when count = 3, then the PrereleaseNumber cannot be 0 ("-0.XXX.0" is not a CSVersion).
                    if( captures.Count is 2 or 3 or 5 )
                    {
                        if( captures[1].ValueSpan.ContainsAnyExcept( "0123456789" ) )
                        {
                            kind = CSVersionKind.Exploratory;
                            if( captures.Count > 2 )
                            {
                                error = ValidatePrereleaseAndCINumber( kind, ref csReleaseNumber, ref ciNumber, captures, 2 );
                                if( error != null )
                                {
                                    kind = CSVersionKind.None;
                                    if( mustBeCSVersion )
                                    {
                                        return new SVersion( error, parsedText );
                                    }
                                }
                            }
                        }
                        else if( mustBeCSVersion )
                        {
                            return new SVersion( "Invalid Exploratory CSVersion: name must not be only numeric.", parsedText );
                        }
                    }
                    else if( mustBeCSVersion )
                    {
                        return new SVersion( "Invalid potential Exploratory CSVersion.", parsedText );
                    }
                }
                else
                {
                    // CSVersion prerelease name?
                    // Same pattern as above but without the first "0.".
                    if( CSVersionKindExtensions.TryParse( first, out kind ) )
                    {
                        if( captures.Count is 2 or 4 )
                        {
                            error = ValidatePrereleaseAndCINumber( kind, ref csReleaseNumber, ref ciNumber, captures, 1 );
                            if( error != null )
                            {
                                kind = CSVersionKind.None;
                                if( mustBeCSVersion )
                                {
                                    return new SVersion( error, parsedText );
                                }
                            }
                        }
                        else if( captures.Count != 1 )
                        {
                            if( mustBeCSVersion )
                            {
                                return new SVersion( $"Invalid potential {kind} CSVersion.", parsedText );
                            }
                            kind = CSVersionKind.None;
                        }
                    }
                    else if( mustBeCSVersion )
                    {
                        return new SVersion( $"Invalid prerelease CSVersion: '{first}' is not a conformant prerelease name.", parsedText );
                    }
                }
            }
        }
        // Before concluding, we must ensure the conformance.
        if( kind != CSVersionKind.None )
        {
            // This is a potential CSVersion but to actually be conformant:
            // - Only stable versions can be +fake.
            // - It can be at most one among +fake, +deprecated and +invalid.
            // - If it is a CI version, then it cannot be +fake or +deprecated (but may be +invalid).
            if( hasFakeMetadata && kind != CSVersionKind.Stable )
            {
                if( mustBeCSVersion )
                {
                    return new SVersion( "To be conformant, only stable versions can have +fake metadata.", parsedText ?? buildMetaData );
                }
                kind = CSVersionKind.None;
                csReleaseNumber = 0;
                ciNumber = -1;
            }
            else if( hasFakeMetadata && (hasDeprecatedMetadata || hasInvalidMetadata)
                     || hasDeprecatedMetadata && hasInvalidMetadata )
            {
                if( mustBeCSVersion )
                {
                    return new SVersion( "To be conformant, build metadata must have at most one +fake, +deprecated or +invalid metadata.", parsedText ?? buildMetaData );
                }
                kind = CSVersionKind.None;
                csReleaseNumber = 0;
                ciNumber = -1;
            }
            else if( ciNumber >= 0 && (hasFakeMetadata || hasDeprecatedMetadata) )
            {
                if( mustBeCSVersion )
                {
                    return new SVersion( "A conformant CI build version cannot be +fake, +deprecated.", parsedText ?? buildMetaData );
                }
                kind = CSVersionKind.None;
                csReleaseNumber = 0;
                ciNumber = -1;
            }
        }
        return new SVersion( parsedText,
                             parsedVersion,
                             major,
                             minor,
                             patch,
                             prerelease,
                             buildMetaData,
                             kind,
                             csReleaseNumber,
                             ciNumber,
                             hasFakeMetadata,
                             hasDeprecatedMetadata,
                             hasInvalidMetadata,
                             fourthPart );

        static string? ValidatePrereleaseAndCINumber( CSVersionKind kind,
                                                      ref int csReleaseNumber,
                                                      ref int ciNumber,
                                                      CaptureCollection captures,
                                                      int releaseNumberIndex )
        {
            Debug.Assert( kind is not CSVersionKind.None and not CSVersionKind.Stable );
            Debug.Assert( csReleaseNumber == 0 );
            Debug.Assert( ciNumber == -1 );
            Debug.Assert( captures.Count == releaseNumberIndex + 1 || captures.Count == releaseNumberIndex + 3 );

            // There MUST be a numeric here (the prerelease number).
            if( !int.TryParse( captures[releaseNumberIndex].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out csReleaseNumber ) )
            {
                return $"Invalid release number in {kind} CSVersion.";
            }
            if( captures.Count == releaseNumberIndex + 3 )
            {
                if( captures[releaseNumberIndex + 1].ValueSpan.Equals( "ci", StringComparison.OrdinalIgnoreCase ) )
                {
                    if( !int.TryParse( captures[releaseNumberIndex + 2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out ciNumber ) )
                    {
                        csReleaseNumber = 0;
                        ciNumber = -1;
                        return $"Invalid ci number in {kind} CSVersion.";
                    }
                }
                else
                {
                    csReleaseNumber = 0;
                    return $"Expected .ci.XXX suffix in {kind} CSVersion.";
                }
            }
            // Handles the fact that "-alpha.0" is NOT a CSVersion.
            if( csReleaseNumber == 0 && ciNumber == -1 )
            {
                csReleaseNumber = 0;
                return $"Invalid 0 prerelease number without .ci suffix in {kind} CSVersion.";
            }
            return null;
        }
    }

    static string? ParseBuildMetadata( ReadOnlySpan<char> buildMetadata,
                                       bool checkSyntax,
                                       ref bool hasFakeMetadata,
                                       ref bool hasDeprecatedMetadata,
                                       ref bool hasInvalidMetadata )
    {
        var eM = DottedPartRegEx().EnumerateMatches( buildMetadata );
        if( !eM.MoveNext() )
        {
            return checkSyntax ? "Invalid build metadata" : null;
        }
        var e = buildMetadata.Split( '.' );
        while( e.MoveNext() )
        {
            var p = buildMetadata[e.Current];
            if( checkSyntax && p.Length > 1 && p[0] == '0' )
            {
                int i = 1;
                while( i < p.Length )
                {
                    if( !char.IsDigit( p[i++] ) ) break;
                }
                if( i == p.Length )
                {
                    return $"Numeric identifiers in build metadata must not start with a 0.";
                }
            }
            if( p.Equals( "fake", StringComparison.OrdinalIgnoreCase ) )
            {
                hasFakeMetadata = true;
            }
            else if( p.Equals( "deprecated", StringComparison.OrdinalIgnoreCase ) )
            {
                hasDeprecatedMetadata = true;
            }
            else if( p.Equals( "invalid", StringComparison.OrdinalIgnoreCase ) )
            {
                hasInvalidMetadata = true;
            }
        }
        return null;
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

    internal static string? ValidateDottedIdentifiers( ReadOnlySpan<char> s, string partName )
    {
        var eM = DottedPartRegEx().EnumerateMatches( s );
        if( !eM.MoveNext() )
        {
            return "Invalid " + partName;
        }
        var e = s.Split( '.' );
        while( e.MoveNext() )
        {
            var p = s[e.Current];
            if( p.Length > 1 && p[0] == '0' )
            {
                int i = 1;
                while( i < p.Length )
                {
                    if( !char.IsDigit( p[i++] ) ) break;
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
    /// Overridden to return the version without 'v' prefix on success and, when <see cref="IsValid"/> is
    /// false, the <see cref="ErrorMessage"/> with the <see cref="ParsedText"/> if any.
    /// </summary>
    /// <returns>The textual representation.</returns>
    public override string ToString() => _toString;

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
    /// This (and the overloaded comparison operators) compares Semantic Version form according to the SemVer 2.0 specification.
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
    /// Comparison between alphanumeric identifiers is case insensitive.
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

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static bool operator ==( SVersion? left, SVersion? right ) => left is null ? right is null : left.Equals( right );

    public static bool operator !=( SVersion? left, SVersion? right ) => !(left == right);

    public static bool operator <( SVersion? left, SVersion? right ) => left is null ? right is not null : left.CompareTo( right ) < 0;

    public static bool operator <=( SVersion? left, SVersion? right ) => left is null || left.CompareTo( right ) <= 0;

    public static bool operator >( SVersion? left, SVersion? right ) => left is not null && left.CompareTo( right ) > 0;

    public static bool operator >=( SVersion? left, SVersion? right ) => left is null ? right is null : left.CompareTo( right ) >= 0;
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

    // This checks a SVersion (no initial ^ to handle the potential ParsedPrefix).
    // This is not enough to guaranty that the version is valid.
    [GeneratedRegex( @"v?(?<1>0|[1-9][0-9]*)\.(?<2>0|[1-9][0-9]*)\.(?<3>0|[1-9][0-9]*)(\.(?<4>0|[1-9][0-9]*))?((?!-)|(\-(?<5>[0-9A-Za-z\-\.]+)))(\+(?<6>[0-9A-Za-z\-\.]+))?", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex SVersionRegEx();

    // This applies to PreRelease and BuildMetaData.
    // This is not enough to guaranty that the identifier is valid: the "Numeric identifiers MUST NOT include leading zeroes." is not guaranteed.
    [GeneratedRegex( @"^(?<1>0|[1-9][0-9]*|[0-9A-Za-z\-]+)(\.(?<1>0|[1-9][0-9]*|[0-9A-Za-z\-]+))*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex DottedPartRegEx();
}
