using System;
using System.Diagnostics;

namespace CK.Core;

public readonly partial struct SVersionBound
{
    /// <summary>
    /// Attempts to parse a nuget version range. See  https://docs.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges.
    /// There is nothing really simple here. Check for instance: https://github.com/NuGet/Home/issues/6434#issuecomment-358782297 
    /// </summary>
    /// <param name="s">The span to parse.</param>
    /// <returns>The result of the parse that can be invalid.</returns>
    public static ParseResult NugetTryParse( ReadOnlySpan<char> s ) => NugetTryMatch( ref s );

    /// <summary>
    /// Attempts to match a nuget version range. See  https://docs.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges.
    /// There is nothing really simple here. Check for instance: https://github.com/NuGet/Home/issues/6434#issuecomment-358782297 
    /// </summary>
    /// <param name="head">The span to parse.</param>
    /// <returns>The result of the parse that can be invalid.</returns>
    public static ParseResult NugetTryMatch( ref ReadOnlySpan<char> head )
    {
        // Parsing syntactically invalid version is not common: we analyze existing stuff that are supposed
        // to have already been parsed.
        // Instead of handling such errors explicitly, we trap any IndexOutOfRangeException that will eventually be raised.
        var sSaved = head;
        try
        {
            if( Trim( ref head ).Length == 0 ) return new ParseResult( "Version range expected." );
            bool begExclusive = head.TryMatch( '(' );
            bool begInclusive = !begExclusive && head.TryMatch( '[' );
            if( begInclusive || begExclusive )
            {
                SVersion? v1 = null;
                SVersion? v2 = null;
                bool endInclusive;

                if( Trim( ref head ).Length == 0 ) return new ParseResult( "Expected comma or version." );
                bool hasComma = head.TryMatch(',');
                if( !hasComma )
                {
                    if( head.Length == 0 ) return new ParseResult( "Expected nuget version." );
                    v1 = TryParseLooseVersion( ref head );
                    if( v1.ErrorMessage != null ) return new ParseResult( v1.ErrorMessage );
                    if( Trim( ref head ).Length > 0 )
                    {
                        hasComma = head.TryMatch(',');
                    }
                }
                if( Trim( ref head ).Length == 0 ) return new ParseResult( "Unclosed nuget version range." );

                if( !hasComma && v1 != null )
                {
                    if( !begInclusive || !head.TryMatch(']') )
                    {
                        return new ParseResult( "Invalid singled version range. Must only be '[version]'." );
                    }
                    return new ParseResult( new SVersionBound( v1, SVersionLock.Lock, "", false ), false );
                }
                Debug.Assert( hasComma || v1 == null );
                endInclusive = head.TryMatch(']');
                if( !endInclusive && !head.TryMatch(')') )
                {
                    v2 = TryParseLooseVersion( ref head );
                    if( v2.ErrorMessage != null ) return new ParseResult( v2.ErrorMessage );
                    if( Trim( ref head ).Length == 0
                        || (!(endInclusive = head.TryMatch(']')) && !head.TryMatch(')')) )
                    {
                        return new ParseResult( "Unclosed nuget version range." );
                    }
                }
                else if( v1 == null )
                {
                    return new ParseResult( "Invalid nuget version range." );
                }
                Debug.Assert( v1 != null || v2 != null );
                return CreateResult( begInclusive, v1, v2, endInclusive );
            }
            return TryParseVersionWithWildcards( ref head );
        }
        catch( IndexOutOfRangeException )
        {
            return new ParseResult( $"Invalid nuget version: '{sSaved.ToString()}'." );
        }

    }

    static ParseResult CreateResult( bool begInclusive, SVersion? v1, SVersion? v2, bool endInclusive )
    {
        if( v1 == null ) v1 = SVersion.ZeroVersion;

        // Special case for [x,x] or [x,x). This is a locked version.
        if( v1 == v2 )
        {
            return new ParseResult( new SVersionBound( v1, SVersionLock.Lock ), false );
        }
        // Currently, we have no way to handle exclusive bounds.
        // The only non approximative projections are:
        //   - [Major.Minor.Patch[-whatever],(Major+1).0.0) => LockMajor
        //   - [Major.Minor.Patch[-whatever],Major.(Minor+1).0) => LockMinor
        //   - [Major.Minor.Patch[-whatever],Major.Minor.(Patch+1)) => LockPatch
        // This is really not an approximation if v2 is actually "v2-0".
        // If v2 is "v2-a" this is less perfect... and when v2 has no prerelease, this is not exact BUT
        // captures the real intent behind the range: we clearly don't want any prerelease of the next major (or
        // minor or patch) to be satisfied!
        //
        // About exclusive lower bound: this doesn't make a lot of sense... That would mean that you release a package
        // that depends on a package "A" (so you necessarily use a given version of it: "vBase") and say: "I can't work with the
        // package "A" is version "vBase". I need a future version... Funny isn't it?
        // So, we deliberately forget the "begInclusive" parameter. It still appears in the parameters of this method for the sake of completeness. 
        //
        if( v2 != null && !endInclusive && (!v2.IsPrerelease || v2.Prerelease == "0" || v2.Prerelease == "a" || v2.Prerelease == "A") )
        {
            if( v1.Major + 1 == v2.Major && v2.Minor == 0 && v2.Patch == 0 )
            {
                return new ParseResult( new SVersionBound( v1, SVersionLock.LockMajor ), false );
            }
            if( v1.Major == v2.Major && v1.Minor + 1 == v2.Minor && v2.Patch == 0 )
            {
                return new ParseResult( new SVersionBound( v1, SVersionLock.LockMinor ), false );
            }
            if( v1.Major == v2.Major && v1.Minor == v2.Minor && v1.Patch + 1 == v2.Patch )
            {
                return new ParseResult( new SVersionBound( v1, SVersionLock.LockPatch ), false );
            }
        }
        // Only if v2 is not null is this an approximation since we ignore the notion of "exclusive lower bound".
        return new ParseResult( new SVersionBound( v1 ), v2 != null );
    }

    static ParseResult TryParseVersionWithWildcards( ref ReadOnlySpan<char> s )
    {
        Debug.Assert( s.Length > 0 );
        if( SVersion.TryMatch( ref s, out var v, checkBuildMetaDataSyntax: false, allowPrefix: false ) )
        {
            return new ParseResult( new SVersionBound( v ), false );
        }
        // The version is NOT a valid SVersion: there may be wildcards or a "shorten" version (like "1" or "1.2").
        // In NuGet, "1.0" is not the same as "1.0.*":
        //  - 1.0 => 1.0.0 (Minimum version, inclusive)
        //  - 1.0.* => 1.1.0[LockMinor,Stable]
        //  - 1.0.*-* => 1.1.0[LockMinor,AllowCI]
        // We allow 'x' or '*' for the wildcard (even if 'x' won't appear in a NuGet version range). 
        if( !TryMatchXStarInt( ref s, out var major ) )
        {
            // No major nor wildcard. This is definitely invalid.
            return new ParseResult( v?.ErrorMessage ?? "Pattern not matched." );
        }
        if( major == -1 )
        {
            // Skips ".minor.patch" if any.
            var skip = s.TryMatch('.')
                        && TryMatchXStarInt( ref s, out var _ )
                        && s.TryMatch('.')
                        && TryMatchXStarInt( ref s, out var _ );
            // "*-*" is all versions. 
            if( s.TryMatch('-') && s.TryMatch('*') )
            {
                return new ParseResult( SVersionBound.All, false );
            }
            // "*" alone implies only Stable versions.
            return new ParseResult( new SVersionBound( _000Version, SVersionLock.NoLock, "", false ), false );
        }
        Debug.Assert( major >= 0 );
        bool expectNextPart = s.TryMatch('.');
        if( !expectNextPart )
        {
            // "Major" only: (Minimum version, inclusive)
            var bound = new SVersionBound( SVersion.Create( major, 0, 0 ) );
            return new ParseResult( bound, false );
        }
        bool allowCI = false;
        string minPrerelease = "";
        bool hasNextPart;
        if( ((hasNextPart = TryMatchXStarInt( ref s, out var minor )) && minor == -1) )
        {
            // Skips any ".patch".
            var skip = s.TryMatch('.')
                       && TryMatchXStarInt( ref s, out var _ );

            if( s.TryMatch('-') && s.TryMatch('*') )
            {
                allowCI = true;
                minPrerelease = "0";
            }
            var bound = new SVersionBound( SVersion.Create( major, 0, 0 ), SVersionLock.LockMajor, minPrerelease, allowCI );
            return new ParseResult( bound, false );
        }
        if( !hasNextPart )
        {
            return new ParseResult( "Missing expected Minor." );
        }
        Debug.Assert( major >= 0 && minor >= 0 );
        expectNextPart = s.TryMatch('.');
        if( !expectNextPart )
        {
            // "Major.Minor" only: (Minimum version, inclusive)
            var bound = new SVersionBound( SVersion.Create( major, minor, 0 ) );
            return new ParseResult( bound, false );
        }
        if( (hasNextPart = TryMatchXStarInt( ref s, out var patch )) && patch == -1 )
        {
            if( s.TryMatch('-') && s.TryMatch('*') )
            {
                allowCI = true;
                minPrerelease = "0";
            }
            var bound = new SVersionBound( SVersion.Create( major, minor, 0 ), SVersionLock.LockMinor, minPrerelease, allowCI );
            return new ParseResult( bound, false );
        }
        if( !hasNextPart )
        {
            return new ParseResult( "Missing expected Patch." );
        }
        Debug.Assert( major >= 0 && minor >= 0 && patch >= 0 );
        // We have a Major.Minor.Patch here but it has failed to be parsed.
        // The single possibility to be valid is the "-*" pattern:
        // this is a [LockPatch].
        if( s.TryMatch('-') && s.TryMatch('*') )
        {
            var bound = new SVersionBound( SVersion.Create( major, minor, patch ), SVersionLock.LockPatch, "0", true );
            return new ParseResult( bound, false );
        }
        return new ParseResult( v?.ErrorMessage ?? "Pattern not matched." );
    }

    static SVersion TryParseLooseVersion( ref ReadOnlySpan<char> s )
    {
        Debug.Assert( s.Length > 0 );
        if( !SVersion.TryMatch( ref s, out var v, checkBuildMetaDataSyntax: false, allowPrefix: false ) )
        {
            if( TryMatchNonNegativeInt( ref s, out int major ) )
            {
                if( s.Length == 0 || !s.TryMatch('.') )
                {
                    return SVersion.Create( major, 0, 0 );
                }
                if( !TryMatchNonNegativeInt( ref s, out int minor ) )
                {
                    return new SVersion( "Expected Nuget minor part.", null );
                }
                // Try to save the fourth part: in such case the patch is read.
                int patch = 0;

                return SVersion.Create( major, minor, patch );
            }
        }
        return v ?? new SVersion( "Expected Nuget major part.", null );
    }

}

