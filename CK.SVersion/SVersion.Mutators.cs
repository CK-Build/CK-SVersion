using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CK.Core;

public partial class SVersion
{
    /// <summary>
    /// Sets the <see cref="Major"/>, <see cref="Minor"/> and <see cref="Patch"/> numbers.
    /// Other properties remains unchanged. See also <see cref="ForwardVersionNumbers(SVersionChange)"/>.
    /// </summary>
    /// <param name="major">The new major. Must not be negative.</param>
    /// <param name="minor">The new minor. Must not be negative.</param>
    /// <param name="patch">The new patch. Must not be negative.</param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetVersionNumbers( int major, int minor, int patch )
    {
        if( major == _major && minor == _minor && patch == _patch )
        {
            return this;
        }
        ArgumentOutOfRangeException.ThrowIfNegative( major );
        ArgumentOutOfRangeException.ThrowIfNegative( minor );
        ArgumentOutOfRangeException.ThrowIfNegative( patch );
        return new SVersion( null,
                             null,
                             major,
                             minor,
                             patch,
                             _prerelease,
                             _buildMetaData,
                             _csKind,
                             _csPrereleaseNumber,
                             _ciNumber,
                             _hasFakeMetadata,
                             _hasDeprecatedMetadata,
                             _hasInvalidMetadata,
                             _fourthPart );
    }

    /// <summary>
    /// Applies the <see cref="SVersionChange"/> to the <see cref="Major"/>.<see cref="Minor"/>.<see cref="Patch"/> numbers.
    /// Other properties remains unchanged.
    /// <para>
    /// When <see cref="Major"/> is 0, a <see cref="SVersionChange.Major"/> change impacts the <see cref="Minor"/> (applies the Semantic
    /// Versioning rule of the 0 initial version).
    /// </para>
    /// </summary>
    /// <param name="change">The change to apply. <see cref="SVersionChange.None"/> returns this version.</param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion ForwardVersionNumbers( SVersionChange change )
    {
        if( change is SVersionChange.None )
        {
            return this;
        }
        var (major, minor, patch) = change switch
        {
            SVersionChange.Major => _major == 0
                                        ? (0, _minor + 1, 0)
                                        : (_major + 1, 0, 0),
            SVersionChange.Minor => (_major, _minor + 1, 0),
            _ => (_major, _minor, _patch + 1)
        };
        return new SVersion( null,
                             null,
                             major,
                             minor,
                             patch,
                             _prerelease,
                             _buildMetaData,
                             _csKind,
                             _csPrereleaseNumber,
                             _ciNumber,
                             _hasFakeMetadata,
                             _hasDeprecatedMetadata,
                             _hasInvalidMetadata,
                             _fourthPart );
    }


    /// <summary>
    /// Returns a new <see cref="SVersion"/> with the specified <see cref="ParsedPrefix"/>.
    /// </summary>
    /// <param name="prefix">The prefix to set.</param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetParsedPrefix( string prefix )
    {
        prefix ??= string.Empty;
        if( _parsedPrefix == null )
        {
            if( _parsedText != null )
            {
                Debug.Assert( _parsedVersion != null );
                int len = _parsedText.Length - _parsedVersion.Length;
                if( _parsedText.AsSpan( 0, len ).Equals( prefix, StringComparison.Ordinal ) )
                {
                    return this;
                }
            }
        }
        else if( _parsedPrefix == prefix )
        {
            return this;
        }
        return new SVersion( prefix, this );
    }

    /// <summary>
    /// Returns a new <see cref="SVersion"/> with a potentially new <see cref="BuildMetaData"/>.
    /// <para>
    /// <see cref="IsValid"/> must be true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="buildMetaData">The new build meta data or null to remove it.</param>
    /// <param name="checkSyntax">False to opt-out of strict <see cref="BuildMetaData"/> compliance.</param>
    /// <returns>This or a new SVersion (may be invalid if <paramref name="checkSyntax"/> is true).</returns>
    public SVersion SetBuildMetaData( string? buildMetaData, bool checkSyntax = true )
    {
        buildMetaData ??= String.Empty;
        if( buildMetaData == _buildMetaData )
        {
            return this;
        }
        bool hasFakeMetadata = false;
        bool hasDeprecatedMetadata = false;
        bool hasInvalidMetadata = false;
        if( buildMetaData.Length > 0 )
        {
            var error = ParseBuildMetadata( buildMetaData, checkSyntax, ref hasFakeMetadata, ref hasDeprecatedMetadata, ref hasInvalidMetadata );
            if( error != null ) return new SVersion( error, buildMetaData );
        }

        return new SVersion( null,
                             null,
                             _major,
                             _minor,
                             _patch,
                             _prerelease,
                             buildMetaData,
                             _csKind,
                             _csPrereleaseNumber,
                             _ciNumber,
                             hasFakeMetadata,
                             hasDeprecatedMetadata,
                             hasInvalidMetadata,
                             _fourthPart );
    }

    /// <summary>
    /// Returns a new <see cref="SVersion"/> with the specified CI build number or clears it by setting it to -1.
    /// <para>
    /// If <see cref="IsCI"/> is already true, this replaces the current <see cref="CINumber"/>, <see cref="CSVersionKind.Stable"/>
    /// versions use the "--ci.X" prerelease form, <see cref="CSVersionKind.Exploratory"/> and other prereleases use ".ci.X" suffix.
    /// </para>
    /// <para>
    /// <see cref="IsCSVersion"/> must be true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="ciNumber">-1 to remove the CI build number, must be 0 or positive otherwise.</param>
    /// <param name="impactStablePatchNumber">
    /// By default, this updates the <see cref="Patch"/> number if this version is <see cref="CSVersionKind.Stable"/>:
    /// <list type="bullet">
    ///     <item>When this <see cref="IsCI"/> is false and <paramref name="ciNumber"/> is 0 or positive, the returned <see cref="Patch"/> is incremented.</item>
    ///     <item>When this <see cref="IsCI"/> is true and <paramref name="ciNumber"/> is -1, the returned <see cref="Patch"/> is decremented.</item>
    /// </list>
    /// Exploratory and prerelease conformant versions rely on the <see cref="PrereleaseNumber"/> and the ".ci." suffix: the CINumber applies to the
    /// (last non CI) version, there is no need to adjust it.
    /// </param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetCINumber( int ciNumber, bool impactStablePatchNumber = true )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan( ciNumber, -1 );
        if( !IsCSVersion ) throw new InvalidOperationException( "Can be called only on Conformant SVersion." );

        if( ciNumber == _ciNumber )
        {
            return this;
        }

        int patch = _patch;
        var currentPrerelease = _prerelease.AsSpan();
        string newPrerelease;
        if( ciNumber == -1 )
        {
            if( _csKind is CSVersionKind.Stable )
            {
                newPrerelease = "";
                if( impactStablePatchNumber && patch > 0 )
                {
                    --patch;
                }
            }
            else
            {
                Debug.Assert( currentPrerelease.Length > 0 && Regex.IsMatch( _prerelease, @"\.ci\.\d+$", RegexOptions.CultureInvariant ) );
                // Must remove ".ci" or ".0.ci" suffix. 
                int markerLength = _csPrereleaseNumber > 0 ? 3 : 5;
                newPrerelease = new string( currentPrerelease.Slice( 0, currentPrerelease.LastIndexOf( '.' ) - markerLength ) );
            }
        }
        else if( _ciNumber >= 0 )
        {
            // Patch ci number (in place).
            newPrerelease = string.Create( CultureInfo.InvariantCulture, $"{currentPrerelease.Slice( 0, currentPrerelease.LastIndexOf( '.' ) + 1 )}{ciNumber}" );
        }
        else if( currentPrerelease.Length == 0 )
        {
            Debug.Assert( _csKind is CSVersionKind.Stable );
            newPrerelease = string.Create( CultureInfo.InvariantCulture, $"-ci.{ciNumber}" );
            if( impactStablePatchNumber )
            {
                ++patch;
            }
        }
        else
        {
            if( _csPrereleaseNumber > 0 )
            {
                newPrerelease = string.Create( CultureInfo.InvariantCulture, $"{currentPrerelease}.ci.{ciNumber}" );
            }
            else
            {
                newPrerelease = string.Create( CultureInfo.InvariantCulture, $"{currentPrerelease}.0.ci.{ciNumber}" );
            }
        }
        Debug.Assert( _fourthPart == -1 );
        return new SVersion( null,
                             null,
                             _major,
                             _minor,
                             patch,
                             newPrerelease,
                             _buildMetaData,
                             _csKind,
                             _csPrereleaseNumber,
                             ciNumber,
                             _hasFakeMetadata,
                             _hasDeprecatedMetadata,
                             _hasInvalidMetadata );
    }

    /// <summary>
    /// Returns a new <see cref="SVersion"/> with the specified <see cref="PrereleaseNumber"/>.
    /// <para>
    /// <see cref="VersionKind"/> must be <see cref="CSVersionKind.Exploratory"/> or a prerelease from <see cref="CSVersionKind.Alpha"/>
    /// to <see cref="CSVersionKind.Zulu"/> otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="number">Must be 0 or positive.</param>
    /// <param name="clearCINumber">
    /// False to keep the current <see cref="CINumber"/>, false to clear it (the returned version is not CI build version).
    /// </param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetPrereleaseNumber( int number, bool clearCINumber )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( number );
        if( !IsCSVersion ) throw new InvalidOperationException( "Can be called only on Conformant SVersion." );
        if( _csKind is CSVersionKind.Stable ) throw new InvalidOperationException( "Cannot be called on Stable version." );

        if( number == _csPrereleaseNumber
            && (!clearCINumber || _ciNumber == -1) )
        {
            return this;
        }

        var ciNumber = clearCINumber ? -1 : _ciNumber;
        string newPrerelease;
        if( _csKind is CSVersionKind.Exploratory )
        {
            var name = ExploratoryName;
            if( ciNumber >= 0 )
            {
                newPrerelease = string.Create( CultureInfo.InvariantCulture, $"0.{name}.{number}.ci.{ciNumber}" );
            }
            else
            {
                if( number > 0 )
                {
                    newPrerelease = string.Create( CultureInfo.InvariantCulture, $"0.{name}.{number}" );
                }
                else
                {
                    newPrerelease = string.Create( CultureInfo.InvariantCulture, $"0.{name}" );
                }
            }
        }
        else
        {
            Debug.Assert( _csKind is >= CSVersionKind.Alpha and <= CSVersionKind.Zulu );
            var name = _csKind.ToBranchName();
            if( ciNumber >= 0 )
            {
                newPrerelease = string.Create( CultureInfo.InvariantCulture, $"{name}.{number}.ci.{ciNumber}" );
            }
            else
            {
                if( number > 0 )
                {
                    newPrerelease = string.Create( CultureInfo.InvariantCulture, $"{name}.{number}" );
                }
                else
                {
                    newPrerelease = name;
                }
            }
        }
        return new SVersion( null,
                             null,
                             _major,
                             _minor,
                             _patch,
                             newPrerelease,
                             _buildMetaData,
                             _csKind,
                             number,
                             ciNumber,
                             _hasFakeMetadata,
                             _hasDeprecatedMetadata,
                             _hasInvalidMetadata );
    }

    /// <summary>
    /// Sets the <see cref="BranchName"/> to be "explo/<paramref name="name"/>" and <see cref="VersionKind"/> to <see cref="CSVersionKind.Exploratory"/>.
    /// The <see cref="PrereleaseNumber"/> and <see cref="CINumber"/> are preserved.
    /// <para>
    /// The name parameter may start with "explo/" (will be skipped).
    /// </para>
    /// <para>
    /// <see cref="IsCSVersion"/> must be true otherwise a <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="name">The <see cref="ExploratoryName"/> or the exploratory branch name "explo/<see cref="ExploratoryName"/>".</param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetExploratoryName( string name )
    {
        if( !IsCSVersion ) throw new InvalidOperationException( "Can be called only on Conformant SVersion." );
        ArgumentNullException.ThrowIfNull( name );

        if( name.StartsWith( "explo/", StringComparison.OrdinalIgnoreCase ) )
            name = name.Substring( 6 );

        if( _csKind == CSVersionKind.Exploratory && ExploratoryName.Equals( name, StringComparison.Ordinal ) )
            return this;

        if( name.Length == 0 )
            throw new ArgumentException( "Exploratory name must not be empty.", nameof( name ) );
        if( name.Contains( '.' ) )
            throw new ArgumentException( "Exploratory name must not contain dots.", nameof( name ) );
        var nameError = ValidateDottedIdentifiers( name.AsSpan(), "exploratory name" );
        if( nameError != null )
            throw new ArgumentException( nameError, nameof( name ) );
        if( !name.AsSpan().ContainsAnyExcept( "0123456789" ) )
            throw new ArgumentException( "Exploratory name must not be purely numeric.", nameof( name ) );

        return SetConformantData( CSVersionKind.Exploratory, name, _csPrereleaseNumber, _ciNumber );
    }

    /// <summary>
    /// Sets the <see cref="BranchName"/> and the <see cref="CSVersionKind"/> to one of the <see cref="CSVersionKind.Alpha"/>...<see cref="CSVersionKind.Zulu"/>
    /// Conformant SVersion prerelease name.
    /// </summary>
    /// <param name="prereleaseName">
    /// Must be <see cref="CSVersionKind.Alpha"/> to <see cref="CSVersionKind.Zulu"/> otherwise an <see cref="ArgumentException"/> is thrown.
    /// </param>
    /// The <see cref="PrereleaseNumber"/> and <see cref="CINumber"/> are preserved.
    /// <para>
    /// The name parameter may start with "explo/".
    /// </para>
    /// <para>
    /// <see cref="IsCSVersion"/> must be true otherwise a <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetBranchName( CSVersionKind prereleaseName )
    {
        if( !IsCSVersion ) throw new InvalidOperationException( "Can be called only on Conformant SVersion." );
        if( prereleaseName is < CSVersionKind.Alpha or > CSVersionKind.Zulu )
            throw new ArgumentException( $"Must be between Alpha and Zulu, got '{prereleaseName}'.", nameof( prereleaseName ) );

        return _csKind == prereleaseName
                ? this
                : SetConformantData( prereleaseName, default, _csPrereleaseNumber, _ciNumber );
    }

    /// <summary>
    /// Sets the <see cref="BranchName"/> that must be a valid one: either a "explo/<see cref="ExploratoryName"/>" or one of the
    /// <see cref="CSVersionKindExtensions.ToBranchName(CSVersionKind)"/> string. Setting the empty string returns a <see cref="CSVersionKind.Stable"/>
    /// version with the same <see cref="CINumber"/> and a <see cref="PrereleaseNumber"/> resets to 0.
    /// <para>
    /// <see cref="IsCSVersion"/> must be true otherwise a <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="validBranchName">
    /// The "explo/<see cref="ExploratoryName"/>", "alpha" to "zulu" string or the empty string.
    /// Any other value throws an <see cref="ArgumentException"/>.
    /// </param>
    /// <returns>This or a new SVersion.</returns>
    public SVersion SetBranchName( string validBranchName )
    {
        ArgumentNullException.ThrowIfNull( validBranchName );

        if( validBranchName.Length == 0 )
        {
            if( _csKind == CSVersionKind.Stable ) return this;
            if( !IsCSVersion ) throw new InvalidOperationException( "Can be called only on Conformant SVersion." );
            return SetConformantData( CSVersionKind.Stable, default, 0, _ciNumber );
        }
        if( validBranchName.StartsWith( "explo/", StringComparison.OrdinalIgnoreCase ) )
            return SetExploratoryName( validBranchName );
        if( CSVersionKindExtensions.TryParse( validBranchName, out var kind ) )
            return SetBranchName( kind );

        throw new ArgumentException( $"'{validBranchName}' is not a valid branch name.", nameof( validBranchName ) );
    }

    SVersion SetConformantData( CSVersionKind kind, ReadOnlySpan<char> exploratoryName, int prereleaseNumber, int ciNumber )
    {
        Debug.Assert( IsCSVersion );
        Debug.Assert( _fourthPart == -1 );
        return new SVersion( null, null,
                             _major, _minor, _patch,
                             BuildConformantPrerelease( kind, exploratoryName, prereleaseNumber, ciNumber ),
                             _buildMetaData,
                             kind,
                             prereleaseNumber,
                             ciNumber,
                             _hasFakeMetadata,
                             _hasDeprecatedMetadata,
                             _hasInvalidMetadata );

        static string BuildConformantPrerelease( CSVersionKind kind,
                                                 ReadOnlySpan<char> exploratoryName,
                                                 int prereleaseNumber,
                                                 int ciNumber )
        {
            Debug.Assert( kind != CSVersionKind.None );

            if( kind == CSVersionKind.Stable )
            {
                return ciNumber >= 0
                    ? $"-ci.{ciNumber}"
                    : "";
            }

            string head = kind == CSVersionKind.Exploratory
                            ? $"0.{exploratoryName}"
                            : kind.ToBranchName();

            if( ciNumber >= 0 )
            {
                return prereleaseNumber > 0
                    ? $"{head}.{prereleaseNumber}.ci.{ciNumber}"
                    : $"{head}.0.ci.{ciNumber}";
            }

            return prereleaseNumber > 0
                        ? $"{head}.{prereleaseNumber}"
                        : head;
        }
    }

}
