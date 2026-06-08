using System;
using System.Runtime.CompilerServices;

namespace CK.Core;

static class ParseHelper
{
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool TryMatch( this ref ReadOnlySpan<char> s, char c )
    {
        if( s.Length > 0 && s[0] == c )
        {
            s = s.Slice( 1 );
            return true;
        }
        return false;
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool SkipWhiteSpaces( this ref ReadOnlySpan<char> s )
    {
        s = s.TrimStart();
        return true;
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool TryMatch( this ref ReadOnlySpan<char> head, ReadOnlySpan<char> value, StringComparison comparison = StringComparison.Ordinal )
    {
        if( head.StartsWith( value, comparison ) )
        {
            head = head.Slice( value.Length );
            return true;
        }
        return false;
    }
}
