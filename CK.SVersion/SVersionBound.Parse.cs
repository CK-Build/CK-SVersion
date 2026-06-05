using System;
using System.Diagnostics.CodeAnalysis;

namespace CK.Core;

public readonly partial struct SVersionBound
{
    /// <summary>
    /// Tries to parse a version bound.
    /// </summary>
    /// <param name="head">The string to parse.</param>
    /// <param name="bound">The result. This is <see cref="SVersionBound.None"/> on error.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> head, out SVersionBound bound ) => TryMatch( ref head, out bound );

    /// <summary>
    /// Tries to match a version bound in its <see cref="ToString()"/> form.
    /// The <paramref name="head"/> is forwarded on success.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="bound">The bound on success.</param>
    /// <param name="allowVersionPrefix">True to allow a <see cref="SVersion.ParsedPrefix"/> on the <see cref="Base"/> version.</param>
    /// <returns>True on success, false if unable to parse.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out SVersionBound bound, bool allowVersionPrefix = false )
    {
        var sHead = head;
        if( !SVersion.TryMatch( ref head,
                                out var baseVersion,
                                checkBuildMetaDataSyntax: false,
                                allowVersionPrefix ) )
        {
            goto error;
        }
        SVersionLock versionLock = SVersionLock.NoLock;
        string? minPrerelease = null;
        bool allowCI = false;

        if( head.Length > 0 && head[0] == '[' )
        {
            head = head.Slice( 1 );
            Trim( ref head );
            while( head[0] != ']' )
            {
                if( SVersionLockExtension.TryMatch( ref head, ref versionLock )
                    || (TryMatch( ref head, "Stable" ) && (minPrerelease = "") != null)
                    || TryMatchMinPrerelease( ref head, ref minPrerelease )
                    || (TryMatch( ref head, "AllowCI" ) && (allowCI = true)) )
                {
                    Trim( ref head );
                    if( head.Length > 0 && head[0] == ',' ) head = head.Slice( 1 );
                    Trim( ref head );
                    if( head.Length == 0 ) goto error;
                }
                else goto error;
            }
        }
        bound = new SVersionBound( baseVersion, versionLock, minPrerelease ?? "0", allowCI );
        return true;

        error:
        bound = None;
        head = sHead;
        return false;

        static bool TryMatchMinPrerelease( ref ReadOnlySpan<char> head, ref string? v )
        {
            if( head.Length < 3 || head[0] != '>' || head[1] != '=' ) return false;
            var h = head.Slice( 2 );
            Trim( ref h );
            int iEnd = h.IndexOfAny( ',', ']' );
            if( iEnd <= 0 ) return false;
            var name = h.Slice( 0, iEnd ).TrimEnd();
            if( SVersion.ValidateDottedIdentifiers( name, "pre-release" ) == null )
            {
                v = new string( name );
                head = h.Slice( iEnd );
                return true;
            }
            return false;
        }

        static bool TryMatch( ref ReadOnlySpan<char> head, ReadOnlySpan<char> value )
        {
            if( head.StartsWith( value, StringComparison.OrdinalIgnoreCase ) )
            {
                head = head.Slice( value.Length );
                return true;
            }
            return false;
        }

    }

    /// <summary>
    /// Captures the result of a parse from other syntaxes that can be invalid or <see cref="IsApproximated"/>.
    /// </summary>
    public readonly struct ParseResult
    {
        /// <summary>
        /// The version bound parsed.
        /// </summary>
        public readonly SVersionBound Result;

        /// <summary>
        /// The error if any (<see cref="IsValid"/> is false).
        /// </summary>
        public readonly string? Error;

        /// <summary>
        /// True if the <see cref="Result"/> is an approximation of the parsed string.
        /// </summary>
        public readonly bool IsApproximated;

        /// <summary>
        /// Gets whether this is valid (<see cref="Error"/> is null).
        /// </summary>
        [MemberNotNullWhen( false, nameof( Error ) )]
        public bool IsValid => Error == null;

        /// <summary>
        /// Initializes a new valid <see cref="ParseResult"/>.
        /// </summary>
        /// <param name="result">The version bound.</param>
        /// <param name="isApproximated">Whether the version bound is an approximation.</param>
        public ParseResult( SVersionBound result, bool isApproximated )
        {
            Result = result;
            IsApproximated = isApproximated;
            Error = null;
        }

        /// <summary>
        /// Initializes a new <see cref="ParseResult"/> on error.
        /// </summary>
        /// <param name="error">The error message.</param>
        public ParseResult( string error )
        {
            Result = SVersionBound.None;
            IsApproximated = false;
            Error = error ?? throw new ArgumentNullException( nameof( error ) );
        }

        /// <summary>
        /// Ensures that this result's <see cref="IsApproximated"/> is true if <paramref name="setApproximated"/> is true
        /// and returns this or a new result.
        /// </summary>
        /// <param name="setApproximated">True to ensures that the flag is set. When false, nothing is done.</param>
        /// <returns>This or a new result.</returns>
        public ParseResult EnsureIsApproximated( bool setApproximated = true )
        {
            return setApproximated && IsValid && !IsApproximated
                    ? new ParseResult( Result, true )
                    : this;
        }


        internal ParseResult ClearApproximated()
        {
            return IsApproximated
                    ? new ParseResult( Result, false )
                    : this;
        }

        /// <summary>
        /// Applies a new <see cref="Result"/> and returns this or a new result.
        /// </summary>
        /// <param name="result">The new result.</param>
        /// <returns>This or a new result.</returns>
        public ParseResult SetResult( SVersionBound result ) => result.Equals( Result )
                                                                    ? this
                                                                    : new ParseResult( result, IsApproximated );

        /// <summary>
        /// Sets or concatenates a new <see cref="Error"/> line and returns this or a new result.
        /// </summary>
        /// <param name="error">The error message.</param>
        /// <returns>This or a new result.</returns>
        public ParseResult AddError( string? error ) => error == null || Error == error
                                                        ? this
                                                        : new ParseResult( Error == null ? error : Error + Environment.NewLine + error );

        /// <summary>
        /// Merges another <see cref="ParseResult"/> with this and returns this or a new result.
        /// Note that error wins and <see cref="IsApproximated"/> is propagated.
        /// </summary>
        /// <param name="other">The other result.</param>
        /// <returns>This or a new result.</returns>
        public ParseResult Union( in ParseResult other )
        {
            if( Error != null ) return AddError( other.Error );
            if( other.Error != null ) return other;

            var c = Result.Union( other.Result );
            // The result IsApproximate if any of the 2 is an approximation.
            // If both are exact, then the union-ed result is exact only if one covers the other.
            return SetResult( c )
                    .EnsureIsApproximated( IsApproximated || other.IsApproximated || !(c.Contains( Result ) || c.Contains( other.Result )) );
        }

        /// <summary>
        /// Intersects another <see cref="ParseResult"/> with this and returns this or a new result.
        /// Note that error wins and <see cref="IsApproximated"/> is propagated.
        /// </summary>
        /// <param name="other">The other result.</param>
        /// <returns>This or a new result.</returns>
        public ParseResult Intersect( in ParseResult other )
        {
            if( Error != null ) return AddError( other.Error );
            if( other.Error != null ) return other;

            var c = Result.Intersect( other.Result );
            // The result IsApproximate if any of the 2 is an approximation.
            // If both are exact, then the union-ed result is exact only if one covers the other.
            return SetResult( c )
                    .EnsureIsApproximated( IsApproximated || other.IsApproximated || !(c.Contains( Result ) || c.Contains( other.Result )) );
        }
    }

    static ref ReadOnlySpan<char> Trim( ref ReadOnlySpan<char> s ) { s = s.TrimStart(); return ref s; }

    static bool TryMatch( ref ReadOnlySpan<char> s, char c )
    {
        if( s.Length > 0 && s[0] == c )
        {
            s = s.Slice( 1 );
            return true;
        }
        return false;
    }

    static bool TryMatchNonNegativeInt( ref ReadOnlySpan<char> s, out int i )
    {
        i = 0;
        if( s.Length > 0 )
        {
            int v = s[0] - '0';
            if( v >= 0 && v <= 9 )
            {
                do
                {
                    i = i * 10 + v;
                    s = s.Slice( 1 );
                    if( s.Length == 0 ) break;
                    v = s[0] - '0';
                }
                while( v >= 0 && v <= 9 );
                return true;
            }
        }
        return false;
    }


}

