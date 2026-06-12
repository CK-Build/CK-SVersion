using NUnit.Framework;
using Shouldly;

namespace CK.Core.Tests;

[TestFixture]
public class SVersionTests
{
    [Test]
    public void the_Zero_SVersion_is_syntactically_valid_and_greater_than_null()
    {
        Assert.That( SVersion.ZeroVersion.IsValid );
        Assert.That( SVersion.ZeroVersion > null );
        Assert.That( null < SVersion.ZeroVersion );
        Assert.That( SVersion.ZeroVersion >= null );
        Assert.That( null <= SVersion.ZeroVersion );

        var aZero = SVersion.Create( 0, 0, 0, "0" );
        Assert.That( aZero == SVersion.ZeroVersion );
        Assert.That( aZero >= SVersion.ZeroVersion );
        Assert.That( aZero <= SVersion.ZeroVersion );
    }

    [TestCase( "0.0.0" )]
    [TestCase( "0.0.0--" )]
    [TestCase( "0.0.0-a" )]
    [TestCase( "0.0.0-A" )]
    [TestCase( "1.0.0-beta2-19367-01" )]
    public void the_Zero_SVersion_is_lower_than_any_other_syntactically_valid_SVersion( string version )
    {
        var v = SVersion.ParseNoThrow( version );
        Assert.That( v.IsValid );
        Assert.That( v > SVersion.ZeroVersion );
        Assert.That( v != SVersion.ZeroVersion );
    }

    [Test]
    public void SVersion_can_be_compared_with_operators()
    {
        Assert.That( SVersion.Create( 0, 0, 0 ) > SVersion.Create( 0, 0, 0, "a" ) );
        Assert.That( SVersion.Create( 0, 0, 0 ) >= SVersion.Create( 0, 0, 0, "a" ) );
        Assert.That( SVersion.Create( 0, 0, 0, "a" ) < SVersion.Create( 0, 0, 0 ) );
        Assert.That( SVersion.Create( 0, 0, 0, "a" ) <= SVersion.Create( 0, 0, 0 ) );
        Assert.That( SVersion.Create( 0, 0, 0, "a" ) != SVersion.Create( 0, 0, 0 ) );
    }

    [TestCase( "01.0.0" )]
    [TestCase( "0.01.0" )]
    [TestCase( "0.0.01" )]
    [TestCase( "12897798127391372937.0.0" )]
    [TestCase( "1.999999999999999999.0" )]
    [TestCase( "1.2.99999999999999999999999" )]
    [TestCase( "0.0" )]
    [TestCase( "0" )]
    [TestCase( null )]
    [TestCase( "not a version at all" )]
    [TestCase( "0.0.0-+" )]
    [TestCase( "0.0.0-." )]
    [TestCase( "0.0.0-.." )]
    [TestCase( "0.0.0-a..b" )]
    [TestCase( "0.0.0-01" )]
    [TestCase( "0.0.0-$" )]
    public void Syntactically_invalid_SVersion_are_greater_than_null_and_lower_than_the_Zero_one( string invalid )
    {
        SVersion notV = SVersion.ParseNoThrow( invalid );
        Assert.That( !notV.IsValid );
        Assert.That( notV != SVersion.ZeroVersion );
        Assert.That( SVersion.ZeroVersion > notV );
        Assert.That( SVersion.ZeroVersion >= notV );
    }


    // Special edge cases.
    [TestCase( null, '=', null )]
    [TestCase( null, '<', "invalid" )]
    [TestCase( "0.0.0-0", '>', "invalid" )]
    [TestCase( "0.0.0-0", '>', null )]

    // allowTrailingSuffix: true.
    [TestCase( "v0.0.0-alpha", '=', "0.0.0-alpha ignored" )]

    // Exploratories are in the "0.0.0-0"-"0.0.0-1" range. 
    [TestCase( "0.0.0-0", '<', "0.0.0-0.explo" )]
    [TestCase( "0.0.0-0", '<', "0.0.0-0.another-explo" )]
    [TestCase( "0.0.0-0.explo", '<', "0.0.0-1" )]
    [TestCase( "0.0.0-1", '<', "0.0.0-alpha" )]

    // Stable -> CI.
    // This uses the double dash trick and the Patch is incremented by one.
    [TestCase( "v1.2.3", '<', "1.2.4--ci.0" )]
    [TestCase( "v1.2.4--ci.0", '<', "1.2.4-alpha" )]

    // Prerelease -> CI.
    // It is the "padding" with the .0 here that does the job.
    [TestCase( "v1.2.3-bravo", '<', "1.2.3-bravo.0.ci.0" )]
    [TestCase( "v1.2.3-bravo.0.ci.0", '<', "1.2.3-bravo.1" )]
    [TestCase( "v1.2.3-bravo.1", '<', "1.2.3-bravo.1.ci.0" )]

    // Exploratory -> CI.
    // Like Prerelease (the "padding" with the .0).
    [TestCase( "v0.0.0-0.explo", '<', "v0.0.0-0.explo.0.ci.0" )]
    [TestCase( "v0.0.0-0.explo.0.ci.0", '<', "0.0.0-0.explo.0.ci.1" )]
    [TestCase( "v0.0.0-0.explo.0.ci.1", '<', "0.0.0-0.explo.1" )]
    [TestCase( "v0.0.0-0.explo.1", '<', "0.0.0-0.explo.1.ci.0" )]
    [TestCase( "v0.0.0-0.explo.1.ci.0", '<', "0.0.0-0.explo.2" )]

    public void comparison_operators( string left, char op, string right )
    {
        SVersion? vL = left != null ? SVersion.ParseNoThrow( left, allowTrailingSuffix: true ) : null;
        SVersion? vR = right != null ? SVersion.ParseNoThrow( right, allowTrailingSuffix: true ) : null;
        switch( op )
        {
            case '>':
                (vL == vR).ShouldBeFalse();
                (vL != vR).ShouldBeTrue();
                (vL > vR).ShouldBeTrue();
                (vL <= vR).ShouldBeFalse();
                break;
            case '<':
                (vL < vR).ShouldBeTrue();
                (vL >= vR).ShouldBeFalse();
                break;
            case '=':
                (vL == vR).ShouldBeTrue();
                (vL != vR).ShouldBeFalse();
                (vL < vR).ShouldBeFalse();
                (vL > vR).ShouldBeFalse();
                (vL <= vR).ShouldBeTrue();
                (vL >= vR).ShouldBeTrue();
                break;
            default: throw new ArgumentException( "Unsupported operator.", nameof( op ) );
        }
    }

    [TestCase( "1.0.0-beta2 after", "", "1.0.0-beta2" )]
    [TestCase( "v1.0.0-AZE,after", "", "v1.0.0-AZE" )]
    [TestCase( "prefix comes here0.0.0,after", "prefix comes here", "prefix comes here0.0.0" )]
    [TestCase( "Plopv0.0.0-rc.1.2,after", "Plop", "Plopv0.0.0-rc.1.2" )]
    [TestCase( "00.0.0-rc.1.2 after", "0", "00.0.0-rc.1.2" )]
    public void parsing_works_on_prefix_and_ParsedText_covers_the_version( string t, string parsedPrefix, string parsedText )
    {
        t.ShouldStartWith( parsedText );

        var v = SVersion.ParseNoThrow( t, allowPrefix: true, allowTrailingSuffix: true );
        v.IsValid.ShouldBeTrue();
        v.ErrorMessage.ShouldBeNull();
        v.ParsedPrefix.ShouldBe( parsedPrefix );
        v.ParsedText.ShouldBe( parsedText );
    }

    [TestCase( "1.2.3.4" )]
    [TestCase( "0.0.0.0" )]
    [TestCase( "1.2.3.4-alpha" )]
    public void parsing_fourth_part_should_be_positive_when_fourth_part_is_here( string sv )
    {
        var head = sv.AsSpan();
        var v = SVersion.ParseNoThrow( sv );
        v.FourthPart.ShouldBeGreaterThanOrEqualTo( 0 );
        v.IsCSVersion.ShouldBeFalse();
    }


    [TestCase( "1.2.3" )]
    [TestCase( "0.0.0" )]
    [TestCase( "1.2.3-alpha" )]
    public void parsing_fourth_part_should_be_negative_when_fourth_part_is_not_here( string sv )
    {
        var v = SVersion.ParseNoThrow( sv );
        v.FourthPart.ShouldBeNegative();
        v.IsCSVersion.ShouldBeTrue();
    }

    // Not CSVersion.
    [TestCase( "0.0.0-0", CSVersionKind.None, 0, -1 )]
    [TestCase( "1.2.3-alpha.0", CSVersionKind.None, 0, -1 )]
    [TestCase( "1.2.3-a", CSVersionKind.None, 0, -1 )]
    [TestCase( "1.2.3-a.ci.1", CSVersionKind.None, 0, -1 )]
    [TestCase( "0.0.0-0.explo.0", CSVersionKind.None, 0, -1 )]

    // Stable & Stable CI.
    [TestCase( "1.2.3", CSVersionKind.Stable, 0, -1 )]
    [TestCase( "1.2.3--ci.0", CSVersionKind.Stable, 0, 0 )]
    [TestCase( "1.2.3--ci.3712", CSVersionKind.Stable, 0, 3712 )]
    // Extra stuff => not a CSVersion.
    [TestCase( "1.2.3--ci.0.1", CSVersionKind.None, 0, -1 )]
    [TestCase( "1.2.3--ci.3712.a", CSVersionKind.None, 0, -1 )]

    // Prerelease.
    [TestCase( "1.2.3-alpha", CSVersionKind.Alpha, 0, -1 )]
    [TestCase( "1.2.3-alpha.0.ci.0", CSVersionKind.Alpha, 0, 0 )]
    [TestCase( "1.2.3-alpha.0.ci.42", CSVersionKind.Alpha, 0, 42 )]
    [TestCase( "1.2.3-alpha.1", CSVersionKind.Alpha, 1, -1 )]
    [TestCase( "1.2.3-alpha.100.ci.42", CSVersionKind.Alpha, 100, 42 )]

    // Exploratory.
    [TestCase( "0.0.0-0.explo", CSVersionKind.Exploratory, 0, -1 )]
    [TestCase( "0.0.0-0.explo.14", CSVersionKind.Exploratory, 14, -1 )]
    [TestCase( "0.0.0-0.explo.0.ci.52", CSVersionKind.Exploratory, 0, 52 )]
    [TestCase( "0.0.0-0.explo.14.ci.0", CSVersionKind.Exploratory, 14, 0 )]
    // Extra stuff => not a CSVersion.
    [TestCase( "0.0.0-0.explo.14.a", CSVersionKind.None, 0, -1 )]
    [TestCase( "0.0.0-0.explo.14.0", CSVersionKind.None, 0, -1 )]
    [TestCase( "0.0.0-0.explo.14.ci.0.1", CSVersionKind.None, 0, -1 )]
    [TestCase( "0.0.0-0.explo.14.ci.0.a", CSVersionKind.None, 0, -1 )]

    // Case insensitive match.
    [TestCase( "1.2.3--CI.0", CSVersionKind.Stable, 0, 0 )]
    [TestCase( "1.2.3-DELTA.0.CI.8", CSVersionKind.Delta, 0, 8 )]
    [TestCase( "1.2.3-PaPa.5.cI.0", CSVersionKind.Papa, 5, 0 )]
    [TestCase( "1.2.3-QuEbeC", CSVersionKind.Quebec, 0, -1 )]
    [TestCase( "1.2.3-ZULU", CSVersionKind.Zulu, 0, -1 )]

    public void Conformant_SVersion_parse( string sv, CSVersionKind kind, int prereleaseNumber, int ciNumber )
    {
        var v = SVersion.ParseNoThrow( sv );
        v.IsValid.ShouldBeTrue();
        v.VersionKind.ShouldBe( kind );
        v.PrereleaseNumber.ShouldBe( prereleaseNumber );
        v.CINumber.ShouldBe( ciNumber );
    }

    [TestCase( "1.0.0", "1.0.1--ci.42" )]
    [TestCase( "0.0.0-0.explo", "0.0.0-0.explo.0.ci.42" )]
    [TestCase( "0.0.0-0.explo.1", "0.0.0-0.explo.1.ci.42" )]
    [TestCase( "1.2.3-alpha", "1.2.3-alpha.0.ci.42" )]
    [TestCase( "1.2.3-alpha.1", "1.2.3-alpha.1.ci.42" )]
    public void SetCINumber_tests( string sFrom, string sTo )
    {
        var to = SVersion.Parse( sTo );
        var from = SVersion.Parse( sFrom );
        to.IsCI.ShouldBe( true );

        var ci = from.SetCINumber( 42 );
        ci.ShouldBe( to );
        var back = ci.SetCINumber( -1 );
        back.ShouldBe( from );

        // SetCINumber adjust the Major.Minor.Patch by default.
        (from < to).ShouldBeTrue();

        var toNoAdjust = from.SetCINumber( 42, impactStablePatchNumber: false );
        if( from.IsStable )
        {
            toNoAdjust.ShouldNotBe( to );
            // The --prerelease is smaller than the stable.
            (from > toNoAdjust).ShouldBeTrue();
        }
        else
        {
            toNoAdjust.ShouldBe( to );
            (from < toNoAdjust).ShouldBeTrue();
        }
    }

    [TestCase( "0.0.0-0.explo", "0.0.0-0.explo.42" )]
    [TestCase( "0.0.0-0.explo.3712", "0.0.0-0.explo.42" )]
    [TestCase( "0.0.0-0.explo.0.ci.1", "0.0.0-0.explo.42.ci.1" )]
    [TestCase( "0.0.0-0.explo.3712.ci.0", "0.0.0-0.explo.42.ci.0" )]

    [TestCase( "1.2.3-alpha", "1.2.3-alpha.42" )]
    [TestCase( "1.2.3-alpha.1", "1.2.3-alpha.42" )]
    [TestCase( "1.2.3-alpha.0.ci.492", "1.2.3-alpha.42.ci.492" )]
    [TestCase( "1.2.3-alpha.123.ci.0", "1.2.3-alpha.42.ci.0" )]
    public void SetPrereleaseNumber_tests( string sFrom, string sTo )
    {
        var to = SVersion.Parse( sTo );
        var from = SVersion.Parse( sFrom );

        var withPrerelease = from.SetPrereleaseNumber( 42, clearCINumber: false );
        withPrerelease.ShouldBe( to );
        var back = withPrerelease.SetPrereleaseNumber( from.PrereleaseNumber, clearCINumber: false );
        back.ShouldBe( from );

        from.SetPrereleaseNumber( 42, clearCINumber: true ).ShouldBe( to.SetCINumber( -1 ) );
    }

}
