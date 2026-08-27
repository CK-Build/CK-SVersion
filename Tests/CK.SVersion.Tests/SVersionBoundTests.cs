using NUnit.Framework;
using Shouldly;
using System;

namespace CK.Core.Tests;

[TestFixture]
public class SVersionBoundTests
{
    static readonly SVersion V100 = SVersion.Create( 1, 0, 0 );
    static readonly SVersion V101 = SVersion.Create( 1, 0, 1 );
    static readonly SVersion V110 = SVersion.Create( 1, 1, 0 );
    static readonly SVersion V111 = SVersion.Create( 1, 1, 1 );
    static readonly SVersion V200 = SVersion.Create( 2, 0, 0 );
    static readonly SVersion V210 = SVersion.Create( 2, 1, 0 );

    [Test]
    public void basic_union_operations()
    {
        SVersionBound.None.Union( SVersionBound.None ).ShouldBe( SVersionBound.None );
        SVersionBound.None.Union( SVersionBound.All ).ShouldBe( SVersionBound.All );
        SVersionBound.All.Union( SVersionBound.None ).ShouldBe( SVersionBound.All );

        var b1 = new SVersionBound( SVersion.FirstCSVersion );
        var b2 = new SVersionBound( SVersion.LastVersion );

        SVersionBound.None.Union( b1 ).ShouldBe( b1 );
        b1.Union( SVersionBound.None ).ShouldBe( b1 );

        SVersionBound.None.Union( b2 ).ShouldBe( b2 );
        b2.Union( SVersionBound.None ).ShouldBe( b2 );

        b1.Contains( b2 ).ShouldBeTrue( "VeryFirstVersion bound contains VeryLastVersion bound." );
        b2.Contains( b1 ).ShouldBeFalse( "VeryLastVersion bound doen't contain VeryFirstVersion." );

        b1.Union( b2 ).ShouldBe( b1 );
        b2.Union( b1 ).ShouldBe( b1 );

        CheckRoundTrippableToStringParse( SVersionBound.None, SVersionBound.All, b1, b2 );
    }

    [Test]
    public void basic_intersect_operations()
    {
        SVersionBound.None.Intersect( SVersionBound.None ).ShouldBe( SVersionBound.None );
        SVersionBound.None.Intersect( SVersionBound.All ).ShouldBe( SVersionBound.None );
        SVersionBound.All.Intersect( SVersionBound.None ).ShouldBe( SVersionBound.None );

        var b1 = new SVersionBound( SVersion.FirstCSVersion );
        var b2 = new SVersionBound( SVersion.LastVersion );

        b1.Intersect( b1 ).ShouldBe( b1 );
        SVersionBound.None.Intersect( b1 ).ShouldBe( SVersionBound.None );
        b1.Intersect( SVersionBound.None ).ShouldBe( SVersionBound.None );
        b1.Intersect( SVersionBound.All ).ShouldBe( b1 );
        SVersionBound.All.Intersect( b1 ).ShouldBe( b1 );

        b2.Intersect( b2 ).ShouldBe( b2 );
        SVersionBound.None.Intersect( b2 ).ShouldBe( SVersionBound.None );
        b2.Intersect( SVersionBound.None ).ShouldBe( SVersionBound.None );
        b2.Intersect( SVersionBound.All ).ShouldBe( b2 );
        SVersionBound.All.Intersect( b2 ).ShouldBe( b2 );

        b1.Intersect( b2 ).ShouldBe( b2 );
        b2.Intersect( b1 ).ShouldBe( b2 );

        CheckRoundTrippableToStringParse( b1, b2 );
    }

    [Test]
    public void partial_ordering_only()
    {
        var b1 = new SVersionBound( V100, SVersionLock.NoLock, "papa" );
        var b11 = new SVersionBound( V110, SVersionLock.NoLock );

        b1.Contains( b11 ).ShouldBeFalse( "b1 only accepts -papa and b11 accepts everything." );
        b11.Contains( b1 ).ShouldBeFalse( "b11.Base version is greater than b1.Base version." );

        var u = b1.Union( b11 );
        b11.Union( b1 ).ShouldBe( u );

        u.Contains( b1 ).ShouldBeTrue();
        u.Contains( b11 ).ShouldBeTrue();

        var i = b1.Intersect( b11 );
        b11.Intersect( b1 ).ShouldBe( i );
        i.Contains( b1 ).ShouldBeFalse();
        i.Contains( b11 ).ShouldBeFalse();

        CheckRoundTrippableToStringParse( b1, b11, u, i );
    }

    [Test]
    public void SVersionLock_tests()
    {
        var b1LockMinor = new SVersionBound( V100, SVersionLock.LockMinor );
        b1LockMinor.Satisfy( V100 ).ShouldBeTrue( "Same as the base version." );
        b1LockMinor.Satisfy( V101 ).ShouldBeTrue( "The patch can increase." );
        b1LockMinor.Satisfy( V110 ).ShouldBeFalse( "The minor is locked." );
        b1LockMinor.Satisfy( V200 ).ShouldBeFalse( "Major is of course also locked." );

        var b11 = new SVersionBound( V110, SVersionLock.LockMajor );
        b11.Satisfy( V100 ).ShouldBeFalse( "Cannot downgrade minor." );
        b11.Satisfy( V110 ).ShouldBeTrue();
        b11.Satisfy( V111 ).ShouldBeTrue();
        b11.Satisfy( V200 ).ShouldBeFalse( "Cannot upgrade major." );

        var b1LockMajor = b1LockMinor.SetLock( SVersionLock.LockMajor );
        b1LockMajor.Contains( b1LockMinor ).ShouldBeTrue();
        b1LockMajor.Contains( b11 ).ShouldBeTrue( "Same major is locked." );

        var b2 = new SVersionBound( V200, SVersionLock.Lock );
        b1LockMinor.Contains( b2 ).ShouldBeFalse();
        b1LockMajor.Contains( b2 ).ShouldBeFalse();

        CheckRoundTrippableToStringParse( b1LockMinor, b1LockMajor, b11, b2 );
    }

    [Test]
    public void union_with_lock_and_MinQuality()
    {
        var b10 = new SVersionBound( V100, SVersionLock.LockMinor );
        var b11 = new SVersionBound( V110, SVersionLock.LockMajor, "" );

        b10.Contains( b11 ).ShouldBeFalse( "The 1.0 minor is locked." );
        b11.Contains( b10 ).ShouldBeFalse( "The 1.1 base version is greater than the 1.0 base version." );

        var u = b10.Union( b11 );
        u.ShouldBe( b11.Union( b10 ) );

        u.Base.ShouldBe( SVersion.Create( 1, 0, 0 ) );
        u.Lock.ShouldBe( SVersionLock.LockMajor );
        u.MinPrerelease.ShouldBe( "0" );
        u.AllowCI.ShouldBeTrue();

        var b21 = new SVersionBound( V210, SVersionLock.LockMajor, "alpha" );

        var u2 = b21.Union( b11 );
        u2.ShouldBe( b11.Union( b21 ) );

        u2.Base.ShouldBe( SVersion.Create( 1, 1, 0 ) );
        u2.Lock.ShouldBe( SVersionLock.LockMajor );
        u2.MinPrerelease.ShouldBe( "alpha" );

        CheckRoundTrippableToStringParse( b10, b11, u, b21, u2 );
    }

    // Based on: https://github.com/npm/node-semver#advanced-range-syntax.
    // See also: https://semver.npmjs.com/.

    // Syntax: "1.2.3 - 2.3" ==> ">=1.2.3 <2.4.0-0".
    //          We approximate this with 1.2.3. 
    [TestCase( "1.2.3 - 2.3.4", "includePrerelease", "1.2.3[AllowCI]", "Approx" )]
    [TestCase( "1.2.3 - 2.3.4", "", "1.2.3[Stable]", "Approx" )]

    [TestCase( "1.2 - 2.3.4", "", "1.2.0[Stable]", "Approx" )]

    // Syntax: "*" or "" is >=0.0.0 (Any version satisfies). 
    //         No approximation here (when includePrerelease is true). 
    [TestCase( "*", "", "0.0.0[Stable]", "Approx" )]
    [TestCase( "", "includePrerelease", "0.0.0-0[AllowCI]", "" )]
    [TestCase( "*", "includePrerelease", "0.0.0-0[AllowCI]", "" )]

    // Syntax: "1.x" is ">=1.0.0 <2.0.0-0" (Matching major version).
    //         No approximation here (when includePrerelease is true). 
    [TestCase( "1.x", "", "1.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "1.X", "", "1.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "1.2.x", "", "1.2.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "1.2.X", "", "1.2.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "1.X", "includePrerelease", "1.0.0[LockMajor,AllowCI]", "" )]
    [TestCase( "1.2.X", "includePrerelease", "1.2.0[LockMinor,AllowCI]", "" )]

    // Syntax: "A partial version range is treated as an X-Range, so the special character is in fact optional."
    //         No approximation here (when includePrerelease is true). 
    [TestCase( "1", "", "1.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "1.2", "", "1.2.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "1", "includePrerelease", "1.0.0[LockMajor,AllowCI]", "" )]
    [TestCase( "1.2", "includePrerelease", "1.2.0[LockMinor,AllowCI]", "" )]

    // Syntax: Tilde Ranges
    //         Allows patch-level changes if a minor version is specified on the comparator. Allows minor-level changes if not.
    //
    //         "~1.2.3" is ">=1.2.3 <1.(2+1).0", that is ">=1.2.3 <1.3.0-0"
    //         This is not an approximation (when includePrerelease is true): this locks the minor.
    [TestCase( "~1.2.3", "", "1.2.3[LockMinor,Stable]", "Approx" )]
    [TestCase( "~1.2.3", "includePrerelease", "1.2.3[LockMinor,AllowCI]", "" )]

    //         "~1.2" is ">=1.2.0 <1.(2+1).0", that is ">=1.2.0 <1.3.0-0"
    //         This is not an approximation (when includePrerelease is true): this locks the minor.
    [TestCase( "~1.2", "", "1.2.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "~1.2", "includePrerelease", "1.2.0[LockMinor,AllowCI]", "" )]

    //         "~1" is ">=1.0.0 <(1+1).0.0" that is ">=1.0.0 <2.0.0-0" (Same as 1.x)
    //         This is not an approximation: this locks the major.
    [TestCase( "~1", "", "1.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "~1", "includePrerelease", "1.0.0[LockMajor,AllowCI]", "" )]

    //         "~0.2.3" is ">=0.2.3 <0.(2+1).0" that is ">=0.2.3 <0.3.0-0"
    //         This is not an approximation (when includePrerelease is true): this locks the minor.
    [TestCase( "~0.2.3", "", "0.2.3[LockMinor,Stable]", "Approx" )]
    [TestCase( "~0.2.3", "includePrerelease", "0.2.3[LockMinor,AllowCI]", "" )]

    //         "~0.2" is ">=0.2.0 <0.(2+1).0" that is ">=0.2.0 <0.3.0-0" (Same as 0.2.x)
    //         This is not an approximation: this locks the minor.
    [TestCase( "~0.2", "", "0.2.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "~0.2", "includePrerelease", "0.2.0[LockMinor,AllowCI]", "" )]

    //         "~0" is ">=0.0.0 <(0+1).0.0" that is ">=0.0.0 <1.0.0-0" (Same as 0.x)
    //         This is not an approximation (when includePrerelease is true): this locks the major.
    [TestCase( "~0", "", "0.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "~0", "includePrerelease", "0.0.0[LockMajor,AllowCI]", "" )]

    //         "~1.2.3-beta.2" is ">=1.2.3-beta.2 <1.3.0-0"
    //         This is NEVER an approximation!
    //         Even if for npm:
    //            "For example, the range >1.2.3-alpha.3 would be allowed to match the version 1.2.3-alpha.7, but it
    //             would not be satisfied by 3.4.5-alpha.9, even though 3.4.5-alpha.9 is technically "greater than"
    //             1.2.3-alpha.3 according to the SemVer sort rules. The version range only accepts prerelease tags
    //             on the 1.2.3 version. The version 3.4.5 would satisfy the range, because it does not have a prerelease
    //             flag, and 3.4.5 is greater than 1.2.3-alpha.7."
    //         
    //         We lock the patch and the MinQuality is automatically set to CI (any prerelease satisfies)
    //         even if includePrerelease is not specified: this is exactly the npm way of working.
    //
    [TestCase( "~1.2.3-beta.2", "", "1.2.3-beta.2[LockPatch,AllowCI]", "" )]
    [TestCase( "~1.2.3-beta.2", "includePrerelease", "1.2.3-beta.2[LockPatch,AllowCI]", "" )]

    // Syntax: Caret Ranges
    //
    //         "^1.2.3" is ">=1.2.3 <2.0.0-0"
    //         This is not an approximation (when includePrerelease is true): this locks the major.
    [TestCase( "^1.2.3", "", "1.2.3[LockMajor,Stable]", "Approx" )]
    [TestCase( "^1.2.3", "includePrerelease", "1.2.3[LockMajor,AllowCI]", "" )]

    //         "^0.2.3" is ">=0.2.3 <0.3.0-0"
    //         This is not an approximation (when includePrerelease is true): this locks the minor (because the major is 0).
    [TestCase( "^0.2.3", "", "0.2.3[LockMinor,Stable]", "Approx" )]
    [TestCase( "^0.2.3", "includePrerelease", "0.2.3[LockMinor,AllowCI]", "" )]

    //         "^0.0.3" is ">=0.0.3 <0.0.4-0"
    //         This is NEVER an approximation: this locks the whole version OR authorizes prerelease (when includePrerelease is true).
    [TestCase( "^0.0.3", "", "0.0.3[Lock,Stable]", "" )]
    [TestCase( "^0.0.3", "includePrerelease", "0.0.3[LockPatch,AllowCI]", "" )]

    //        "^1.2.3-beta.2" is ">=1.2.3-beta.2 <2.0.0-0"
    //         This is not an approximation (when includePrerelease is true): this locks the major and allows CI build, but
    //         this is still an approximation when includePrerelease is false because prereleases for a different [major, minor, patch]
    //         are forbidden by npm.
    [TestCase( "^1.2.3-beta.2", "", "1.2.3-beta.2[LockMajor,AllowCI]", "Approx" )]
    [TestCase( "^1.2.3-beta.2", "includePrerelease", "1.2.3-beta.2[LockMajor,AllowCI]", "" )]

    //        "^0.0.3-beta" is ">=0.0.3-beta <0.0.4-0"
    //         This is NEVER an approximation.
    [TestCase( "^0.0.3-beta", "", "0.0.3-beta[LockPatch,AllowCI]", "" )]
    [TestCase( "^0.0.3-beta", "includePrerelease", "0.0.3-beta[LockPatch,AllowCI]", "" )]

    //        "^1.2.x" is ">=1.2.0 <2.0.0-0"
    //         This is not an approximation (when includePrerelease is true).
    [TestCase( "^1.2.x", "", "1.2.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "^1.2.x", "includePrerelease", "1.2.0[LockMajor,AllowCI]", "" )]

    //        "^0.0.x" is ">=0.0.0 <0.1.0-0"
    [TestCase( "^0.0.x", "", "0.0.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "^0.0.x", "includePrerelease", "0.0.0[LockMinor,AllowCI]", "" )]

    //        "^0.0" is ">=0.0.0 <0.1.0-0"
    [TestCase( "^0.0", "", "0.0.0[LockMinor,Stable]", "Approx" )]
    [TestCase( "^0.0", "includePrerelease", "0.0.0[LockMinor,AllowCI]", "" )]

    //        "^1.x" is ">=1.0.0 <2.0.0-0"
    [TestCase( "^1.x", "", "1.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "^1.x", "includePrerelease", "1.0.0[LockMajor,AllowCI]", "" )]

    //        "^0.x" is ">=0.0.0 <1.0.0-0"
    //        Same as "^0".
    [TestCase( "^0.x", "", "0.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "^0.x", "includePrerelease", "0.0.0[LockMajor,AllowCI]", "" )]
    [TestCase( "^0", "", "0.0.0[LockMajor,Stable]", "Approx" )]
    [TestCase( "^0", "includePrerelease", "0.0.0[LockMajor,AllowCI]", "" )]

    // 
    [TestCase( "^0.0.0", "", "0.0.0[Lock,Stable]", "" )]
    [TestCase( "^0.0.0", "includePrerelease", "0.0.0[LockPatch,AllowCI]", "" )]

    // Syntax: ">=1.2.9 <2.0.0" is approximated.
    [TestCase( ">=1.2.9 <2.0.0", "", "1.2.9[Stable]", "Approx" )]
    [TestCase( ">=1.2.9 <2.0.0", "includePrerelease", "1.2.9[AllowCI]", "Approx" )]

    // Syntax: "1.2.7 || >=1.1.9 <2.0.0" is approximated.
    [TestCase( "1.2.7 || >=1.1.9 <2.0.0", "", "1.1.9[Stable]", "Approx" )]
    [TestCase( "1.2.7 || >=1.1.9 <2.0.0", "includePrerelease", "1.1.9[AllowCI]", "Approx" )]

    // Syntax: "<1.2.7" is ignored.
    [TestCase( "<1.2.7", "", "0.0.0[Stable]", "Approx" )]
    [TestCase( "<1.2.7", "includePrerelease", "0.0.0[AllowCI]", "Approx" )]

    // Syntax: "<=1.2.7" is like "=1.2.7".
    [TestCase( "<=1.2.7", "", "1.2.7[Lock,Stable]", "Approx" )]
    [TestCase( "<=1.2.7", "includePrerelease", "1.2.7[Lock,AllowCI]", "Approx" )]

    [TestCase( "1.2.3", "", "1.2.3[Lock,Stable]", "Approx" )]
    [TestCase( "=1.2.3", "", "1.2.3[Lock,Stable]", "Approx" )]
    [TestCase( "1.2.3", "includePrerelease", "1.2.3[Lock,AllowCI]", "" )]
    public void parse_npm_syntax( string p, string includePrerelease, string expected, string approximate )
    {
        var r = SVersionBound.NpmTryParse( p, includePrerelease == "includePrerelease" );
        r.Error.ShouldBeNull();
        r.Result.ToString().ShouldBe( expected );
        r.IsApproximated.ShouldBe( approximate == "Approx" );

        CheckRoundTrippableToStringParse( r.Result );
    }

    [TestCase( "nimp" )]
    [TestCase( "<" )]
    [TestCase( "<=>" )]
    [TestCase( "  <2.0.5 || >" )]
    public void parse_npm_syntax_error( string p )
    {
        var r = SVersionBound.NpmTryParse( p );
        r.IsValid.ShouldBeFalse();
        r.Result.ShouldBe( SVersionBound.None );
    }

    [TestCase( "1.2.3.4 - 2.0.0-0", "1.2.3.4[Stable]", 4 )]
    [TestCase( "1.2.3.4-alpha - 3", "1.2.3.4-alpha[AllowCI]", 4 )]
    [TestCase( "9.8.7.6-alpha || 5.0", "5.0.0[LockMinor,AllowCI]", -1 )]
    public void parse_npm_with_fourth_part_skips_parts_and_prerelease( string p, string expected, int fourthPartExpected )
    {
        ReadOnlySpan<char> head = p;
        var r = SVersionBound.NpmTryMatch( ref head );
        r.Error.ShouldBeNull();
        r.Result.ToString().ShouldBe( expected );
        r.Result.Base.FourthPart.ShouldBe( fourthPartExpected );
        head.Length.ShouldBe( 0 );

        CheckRoundTrippableToStringParse( r.Result );
    }


    // Syntax from: https://docs.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges.

    // 1.0 -- x ≥ 1.0 -- Minimum version, inclusive
    //      Basic version: any greater version satisfies.
    [TestCase( "1", "1.0.0[AllowCI]", "" )]
    [TestCase( "1.0", "1.0.0[AllowCI]", "" )]
    [TestCase( "1.0.0", "1.0.0[AllowCI]", "" )]

    // (1.0,) -- x > 1.0 -- Minimum version, exclusive
    //      We can only approximate this by ignoring the exclusive bound.
    //      We ignore the notion of "exclusive lower bound" (see below): this is not an approximation.

    [TestCase( "(1.0.0,)", "1.0.0[AllowCI]", "" )]
    [TestCase( " ( 1.0.0, ) ", "1.0.0[AllowCI]", "" )]

    // [1.0] -- x == 1.0 -- Exact version match
    //      This is a locked version.
    [TestCase( "[1.0.0]", "1.0.0[Lock,Stable]", "" )]

    // [1.0.0,1.0.0] 
    // [1.0.0,1.0.0)
    //      This is a locked version.
    [TestCase( "[1.0,1.0]", "1.0.0[Lock,AllowCI]", "" )]
    [TestCase( "[1.0,1.0)", "1.0.0[Lock,AllowCI]", "" )]
    [TestCase( "(1.0,1.0)", "1.0.0[Lock,AllowCI]", "" )]

    // (,1.0] -- x ≤ 2.0 -- Maximum version, inclusive
    //      We (badly) approximate this with the lower bound... that is the very first SemVer version.
    [TestCase( "(,2.0]", "0.0.0-0[AllowCI]", "Approx" )]

    // (,1.0) -- x < 2.0 -- Maximum version, exclusive
    //      Same as above since we ignore the notion of "exclusive lower bound" (see below).
    [TestCase( "(,2.0)", "0.0.0-0[AllowCI]", "Approx" )]

    // [1.0,2.0] -- 1.0 ≤ x ≤ 2.0 -- Exact range, inclusive
    //      We approximate this with the lower bound.
    //      
    [TestCase( "[1.0,2.0]", "1.0.0[AllowCI]", "Approx" )]

    // (1.0,2.0) -- 1.0 < x < 2.0 -- Exact range, exclusive
    //      We approximate this with the lower bound.
    //      
    [TestCase( "(6,8)", "6.0.0[AllowCI]", "Approx" )]

    // [1.0,2.0) -- 1.0 ≤ x < 2.0 -- Mixed inclusive minimum and exclusive maximum version
    //      We generally approximate this with the lower bound, but in this special case,
    //      we can capture the intent of the user by locking the major, the minor or the patch.
    //
    //      Note that NuGet is somehow buggy (https://github.com/NuGet/Home/issues/6434#issuecomment-546423937) since
    //      this allows 1.0.0-pre to be satisfied!
    //      The workaround is to use 1.0.0-0 as the upper bound... (nuget.org used to forbid the -0 suffix but this has been fixed).
    //      To overcome this, if CSemVer is used (or the first prerelease always used is alpha), one can use 1.0.0-alpha as the upper bound.
    //
    //      Here we consider that the answer IS to lock parts...
    //
    [TestCase( "[1,2)", "1.0.0[LockMajor,AllowCI]", "" )]
    [TestCase( "[1.2,1.3)", "1.2.0[LockMinor,AllowCI]", "" )]
    [TestCase( "[1.2.3,1.2.4)", "1.2.3[LockPatch,AllowCI]", "" )]
    [TestCase( "[1.2.3,2)", "1.2.3[LockMajor,AllowCI]", "" )]

    //       To be consistent, if the upper bound is a -0 (or -a) prerelease, we do the same (and, at least for -0,
    //       this is perfect projection).
    [TestCase( "[1.2.3,1.2.4-0)", "1.2.3[LockPatch,AllowCI]", "" )]
    [TestCase( "[1.2.3,2.0.0-a)", "1.2.3[LockMajor,AllowCI]", "" )]
    [TestCase( "[1.2.3,2.0.0-A)", "1.2.3[LockMajor,AllowCI]", "" )]

    //      About exclusive lower bound: this doesn't make a lot of sense... That would mean that you release a package
    //      that depends on a package "A" (so you necessarily use a given version of it: "vBase") and say: "I can't work with the
    //      package "A" in version "vBase". I need a future version... Funny isn't it?
    //      ==> We decide to consider '(' as being '[': all that applies before works and we consider that this is NOT an approximation. 
    //      
    [TestCase( "(1,2)", "1.0.0[LockMajor,AllowCI]", "" )]
    [TestCase( "(1.2,1.3)", "1.2.0[LockMinor,AllowCI]", "" )]
    [TestCase( "(1.2.3,1.2.4)", "1.2.3[LockPatch,AllowCI]", "" )]
    [TestCase( "(1.2.3,2)", "1.2.3[LockMajor,AllowCI]", "" )]
    [TestCase( "(1.2.3,1.2.4-0)", "1.2.3[LockPatch,AllowCI]", "" )]
    [TestCase( "(1.2.3,2.0.0-a)", "1.2.3[LockMajor,AllowCI]", "" )]
    [TestCase( "(1.2.3,2.0.0-A)", "1.2.3[LockMajor,AllowCI]", "" )]

    //      When no lower bound is specified and the upper bound is 1.0.0, this is not an approximation.
    [TestCase( "(,1)", "0.0.0-0[LockMajor,AllowCI]", "" )]
    [TestCase( " [ , 1 ) ", "0.0.0-0[LockMajor,AllowCI]", "" )]

    // However, when a prerelease is specified on the upper bound, we cannot be clever anymore...
    [TestCase( "[1.2.3,2.0.0-alpha)", "1.2.3[AllowCI]", "Approx" )]

    public void parse_nuget_syntax( string p, string expected, string approximate )
    {
        var r = SVersionBound.NugetTryParse( p );
        r.Error.ShouldBeNull();
        r.Result.ToString().ShouldBe( expected );
        r.IsApproximated.ShouldBe( approximate == "Approx" );
        r.Result.Base.FourthPart.ShouldBe( -1 );

        CheckRoundTrippableToStringParse( r.Result );
    }

    // Wildcard patterns: https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#floating-version-resolutions
    // The "*" is for Stable only.
    [TestCase( "*", "0.0.0[Stable]", "" )]
    // The "*-*" is all versions.
    [TestCase( "*-*", "0.0.0-0[AllowCI]", "" )]
    [TestCase( "1.*", "1.0.0[LockMajor,Stable]", "" )]
    [TestCase( "1.*-*", "1.0.0[LockMajor,AllowCI]", "" )]
    [TestCase( "1.1.*", "1.1.0[LockMinor,Stable]", "" )]
    [TestCase( "1.1.*-*", "1.1.0[LockMinor,AllowCI]", "" )]
    [TestCase( "1.2.3-*", "1.2.3[LockPatch,AllowCI]", "" )]
    public void parse_nuget_syntax_with_wildcard( string p, string expected, string approximate )
    {
        var r = SVersionBound.NugetTryParse( p );
        r.Error.ShouldBeNull();
        r.Result.ToString().ShouldBe( expected );
        r.IsApproximated.ShouldBe( approximate == "Approx" );
        r.Result.Base.FourthPart.ShouldBe( -1 );

        CheckRoundTrippableToStringParse( r.Result );
    }

    // (1.0) is invalid
    [TestCase( "(1.0)" )]
    [TestCase( "[ 1.0," )]
    [TestCase( "(" )]
    [TestCase( "(," )]
    [TestCase( "(,)" )]
    [TestCase( "()" )]
    [TestCase( "[]" )]
    [TestCase( "" )]
    public void parse_nuget_syntax_error( string p )
    {
        var r = SVersionBound.NugetTryParse( p );
        r.IsValid.ShouldBeFalse();
        r.Error.ShouldNotBeNull();

        CheckRoundTrippableToStringParse( r.Result );
    }


    [TestCase( "[1.2.3.4]", "1.2.3.4[Lock,Stable]" )]
    [TestCase( "1.2.3.4-alpha", "1.2.3.4-alpha[AllowCI]" )]
    [TestCase( "[1.2.3.4-alpha,2)", "1.2.3.4-alpha[LockMajor,AllowCI]" )]
    public void parse_nuget_with_fourth_part( string p, string expected )
    {
        var r = SVersionBound.NugetTryParse( p );
        r.Error.ShouldBeNull();
        r.Result.ToString().ShouldBe( expected );
        r.IsApproximated.ShouldBeFalse();
        r.Result.Base.FourthPart.ShouldBeGreaterThanOrEqualTo( 0 );

        CheckRoundTrippableToStringParse( r.Result );
    }

    [TestCase( "v1.0.0-mmm", "1.0.0-mmm" )]
    [TestCase( "v1.0.0-x[]", "1.0.0-x" )]
     [TestCase( "v1.0.0[>=x.1.y.2]", "1.0.0[>=x.1.y.2]" )]
   [TestCase( "v1.0.0-x[allowci,lockedpatch]", "1.0.0-x[LockPatch,AllowCI]" )]
    [TestCase( "v1.2.3-xx[ lockminor ] ", "1.2.3-xx[LockMinor]" )]
    [TestCase( "v1.2.3[ stable , lockmajor ] ", "1.2.3[LockMajor,Stable]" )]
    [TestCase( "v1.2.3-xxx[>=papa, LockMajor ] ", "1.2.3-xxx[LockMajor,>=papa]" )]
    [TestCase( "v1.2.3-AAA[ >= ZULU, Lock ] ", "1.2.3-AAA[Lock,>=ZULU]" )]
    [TestCase( "v1.2.3-AAA[ allowcI , Locked ] ", "1.2.3-AAA[Lock,AllowCI]" )]
    [TestCase( "v1.2.3-AAA[ NoLock ] ", "1.2.3-AAA" )]
    [TestCase( "v1.2.3-AAA[ ALLOWCI ] ", "1.2.3-AAA[AllowCI]" )]
    public void parse_SVersionBound( string p, string expected )
    {
        SVersionBound.TryParse( p, out var b ).ShouldBeTrue();
        b.ToString().ShouldBe( expected );

        CheckRoundTrippableToStringParse( b );
    }

    [TestCase( "*", "0.0.0[Stable]" )]
    [TestCase( "*-*", "0.0.0-0[AllowCI]" )]
    [TestCase( "5.*", "5.0.0[LockMajor,Stable]" )]
    [TestCase( "5.*-*", "5.0.0[LockMajor,AllowCI]" )]
    [TestCase( "5.2.*", "5.2.0[LockMinor,Stable]" )]
    [TestCase( "5.2.*-*", "5.2.0[LockMinor,AllowCI]" )]
    [TestCase( "5.2.1", "5.2.1[AllowCI]" )]
    [TestCase( "5.2.1-*", "5.2.1[LockPatch,AllowCI]" )]
    public void roundtripable_nuget_versions( string nuget, string bound )
    {

        var rNuGet = SVersionBound.NugetTryParse( nuget );
        rNuGet.IsValid.ShouldBeTrue();
        SVersionBound.TryParse( bound, out var vBound ).ShouldBeTrue();
        rNuGet.Result.ShouldBe( vBound );
        vBound.ToNuGetString().ShouldBe( nuget );

        CheckRoundTrippableToStringParse( vBound );
    }

    [Description( "includePrerelease is not really used in the npm ecosystem." )]
    [TestCase( "=1.2.3", "1.2.3[Lock,AllowCI]" )]
    [TestCase( "=0.0.0", "0.0.0[Lock,AllowCI]" )]
    [TestCase( "=0.0.1", "0.0.1[Lock,AllowCI]" )]
    [TestCase( "=0.1.0", "0.1.0[Lock,AllowCI]" )]
    [TestCase( "^0.1.0-dev", "0.1.0-dev[LockMinor,AllowCI]" )]
    [TestCase( ">=1.2.3", "1.2.3[AllowCI]" )]
    [TestCase( ">=0.0.1", "0.0.1[AllowCI]" )]
    [TestCase( ">=0.1.0", "0.1.0[AllowCI]" )]
    [TestCase( "^1.2.3-beta.2", "1.2.3-beta.2[LockMajor,AllowCI]" )]
    [TestCase( "~0.2.3", "0.2.3[LockMinor,AllowCI]" )]
    [TestCase( "^1.2.3", "1.2.3[LockMajor,AllowCI]" )]
    [TestCase( "^0.0.0-0", "0.0.0-0[LockPatch,AllowCI]" )]
    [TestCase( ">=0.0.0-0", "0.0.0-0[AllowCI]" )]
    public void roundtripable_npm_versions_with_includePrerelease_true( string npm, string bound )
    {
        var rNpm = SVersionBound.NpmTryParse( npm, includePrerelease: true );
        rNpm.IsValid.ShouldBeTrue();
        SVersionBound.TryParse( bound, out var vBound ).ShouldBeTrue();
        rNpm.Result.ShouldBe( vBound );
        vBound.ToNpmString().ShouldBe( npm );

        CheckRoundTrippableToStringParse( vBound );
    }

    [TestCase( "=1.2.3", "1.2.3[Lock,Stable]" )]
    [TestCase( "=0.0.0", "0.0.0[Lock,Stable]" )]
    [TestCase( "=0.0.1", "0.0.1[Lock,Stable]" )]
    [TestCase( "=0.1.0", "0.1.0[Lock,Stable]" )]
    [TestCase( "^0.1.0-dev", "0.1.0-dev[LockMinor,AllowCI]" )]
    [TestCase( ">=1.2.3", "1.2.3[Stable]" )]
    [TestCase( ">=0.0.1", "0.0.1[Stable]" )]
    [TestCase( ">=0.1.0", "0.1.0[Stable]" )]
    [TestCase( "^1.2.3-beta.2", "1.2.3-beta.2[LockMajor,AllowCI]" )]
    [TestCase( "~0.2.3", "0.2.3[LockMinor,Stable]" )]
    [TestCase( "^1.2.3", "1.2.3[LockMajor,Stable]" )]
    [TestCase( "^0.0.0-0", "0.0.0-0[LockPatch,AllowCI]" )]
    [TestCase( ">=0.0.0-0", "0.0.0-0[AllowCI]" )]
    public void roundtripable_npm_versions_with_includePrerelease_false( string npm, string bound )
    {
        var rNpm = SVersionBound.NpmTryParse( npm, includePrerelease: false );
        rNpm.IsValid.ShouldBeTrue();
        SVersionBound.TryParse( bound, out var vBound ).ShouldBeTrue();
        rNpm.Result.ShouldBe( vBound );
        vBound.ToNpmString().ShouldBe( npm );

        CheckRoundTrippableToStringParse( vBound );
    }

    //
    // This test shows that the SVersionBound respects basic npm version range definitions.
    // Luckily ;-), these are the ones used in practice.
    //
    [TestCase( "1.2.3", "=1.2.3", "1.2.3[Lock,Stable]" )]
    [TestCase( ">=1.2.3", ">=1.2.3", "1.2.3[Stable]" )]
    [TestCase( "^1.2.3", "^1.2.3", "1.2.3[LockMajor,Stable]" )]
    // Okay... The documentation here is misleading... (at best): https://github.com/npm/node-semver?tab=readme-ov-file#caret-ranges-123-025-004
    // It states that: ^1.2.3 := >=1.2.3 <2.0.0-0
    // But this is wrong: ^1.2.3 is 1.2.3[Lock]. Prereleases WILL not be accepted (without the includePrerelease flag I imagine).
    //
    // Try: https://semver.npmjs.com/ with "chart.js" and ^4.0.0
    //      The chart.js https://www.npmjs.com/package/chart.js/v/4.0.0-release will not be listed.
    //
    [TestCase( "^0.0.3", "=0.0.3", "0.0.3[Lock,Stable]" )]
    // Try these on https://semver.npmjs.com/ for node (there's a lot of versions).
    [TestCase( "~12.16", "~12.16", "12.16.0[LockMinor,Stable]" )]
    [TestCase( "^0.2.3", "~0.2.3", "0.2.3[LockMinor,Stable]" )]
    [TestCase( "^0.1.93", "~0.1.93", "0.1.93[LockMinor,Stable]" )]
    [TestCase( "^12", "^12", "12.0.0[LockMajor,Stable]" )]
    [TestCase( "^12.8", "^12.8", "12.8.0[LockMajor,Stable]" )]
    [TestCase( "^12.8.1", "^12.8.1", "12.8.1[LockMajor,Stable]" )]
    [TestCase( "~0.1", "~0.1", "0.1.0[LockMinor,Stable]" )]
    [TestCase( "~0.1.15", "~0.1.15", "0.1.15[LockMinor,Stable]" )]
    [TestCase( "~12", "^12", "12.0.0[LockMajor,Stable]" )] // => Equivalent projection.
    [TestCase( "~12.16", "~12.16", "12.16.0[LockMinor,Stable]" )]
    [TestCase( "~12.16.2", "~12.16.2", "12.16.2[LockMinor,Stable]" )]
    [TestCase( ">=0.0.0-0", ">=0.0.0-0", "0.0.0-0[AllowCI]" )] // SVersionBound.All. 
    [TestCase( "*", ">=0.0.0", "0.0.0[Stable]" )] // NOT the SVersionBound.All (since we don't include the prerelease).
    [TestCase( "", ">=0.0.0", "0.0.0[Stable]" )] // NOT the SVersionBound.All (since we don't include the prerelease).
    public void npm_versions_projections_with_includePrerelease_false( string initial, string projected, string bound )
    {
        var rNpm = SVersionBound.NpmTryParse( initial, includePrerelease: false );
        rNpm.IsValid.ShouldBeTrue();
        rNpm.Result.ToNpmString().ShouldBe( projected );

        SVersionBound.TryParse( bound, out var vBound ).ShouldBeTrue();
        rNpm.Result.ShouldBe( vBound );

        CheckRoundTrippableToStringParse( rNpm.Result );
    }

    [Description( "includePrerelease is not really used in the npm ecosystem." )]
    [TestCase( "1.2.3", "=1.2.3", "1.2.3[Lock,AllowCI]" )]
    [TestCase( ">=1.2.3", ">=1.2.3", "1.2.3[AllowCI]" )]
    [TestCase( "^1.2.3", "^1.2.3", "1.2.3[LockMajor,AllowCI]" )]
    [TestCase( "^0.0.3", ">=0.0.3", "0.0.3[LockPatch,AllowCI]" )]
    [TestCase( "~12.16", "~12.16", "12.16.0[LockMinor,AllowCI]" )]
    [TestCase( "^0.2.3", "~0.2.3", "0.2.3[LockMinor,AllowCI]" )]
    [TestCase( "^0.1.93", "~0.1.93", "0.1.93[LockMinor,AllowCI]" )]
    [TestCase( "^12", "^12", "12.0.0[LockMajor,AllowCI]" )]
    [TestCase( "^12.8", "^12.8", "12.8.0[LockMajor,AllowCI]" )]
    [TestCase( "^12.8.1", "^12.8.1", "12.8.1[LockMajor,AllowCI]" )]
    [TestCase( "~0.1", "~0.1", "0.1.0[LockMinor,AllowCI]" )]
    [TestCase( "~0.1.15", "~0.1.15", "0.1.15[LockMinor,AllowCI]" )]
    [TestCase( "~12", "^12", "12.0.0[LockMajor,AllowCI]" )] // => Equivalent projection.
    [TestCase( "~12.16", "~12.16", "12.16.0[LockMinor,AllowCI]" )]
    [TestCase( "~12.16.2", "~12.16.2", "12.16.2[LockMinor,AllowCI]" )]
    [TestCase( ">=0.0.0-0", ">=0.0.0-0", "0.0.0-0[AllowCI]" )] // SVersionBound.All. 
    [TestCase( "*", ">=0.0.0-0", "0.0.0-0[AllowCI]" )] // SVersionBound.All
    [TestCase( "", ">=0.0.0-0", "0.0.0-0[AllowCI]" )] // SVersionBound.All
    public void npm_versions_projections_with_includePrerelease_true( string initial, string projected, string bound )
    {
        var rNpm = SVersionBound.NpmTryParse( initial, includePrerelease: true );
        rNpm.IsValid.ShouldBeTrue();
        rNpm.Result.ToNpmString().ShouldBe( projected );

        SVersionBound.TryParse( bound, out var vBound ).ShouldBeTrue();
        rNpm.Result.ShouldBe( vBound );


        CheckRoundTrippableToStringParse( rNpm.Result );
    }

    static void CheckRoundTrippableToStringParse( params SVersionBound[] bounds )
    {
        foreach( var b in bounds )
        {
            // SVersionBound
            var s = b.ToString();
            if( !SVersionBound.TryParse( s, out var vBound ) )
            {
                throw new Exception( $"Unable to parse '{s}'." );
            }
            if( b != vBound )
            {
                throw new Exception( $"Failed to parse back '{s}'. Expected: {b}, got '{vBound}'." );
            }
            // NuGet
            s = b.ToNuGetString();
            var parseResult = SVersionBound.NugetTryParse( s );
            if( !parseResult.IsValid )
            {
                throw new Exception( $"Unable to NUGET parse '{s}'. Invalid ParseResult." );
            }
            var sAgain = parseResult.Result.ToNuGetString();
            var parseResultAgain = SVersionBound.NugetTryParse( sAgain );
            if( parseResultAgain.Result != parseResult.Result )
            {
                throw new Exception( $"Failed to NUGET parse back '{sAgain}': Expected: '{parseResult.Result}', got '{parseResultAgain.Result}'." );
            }
            // Npm
            s = b.ToNpmString();
            parseResult = SVersionBound.NpmTryParse( s );
            if( !parseResult.IsValid )
            {
                throw new Exception( $"Unable to NPM parse '{s}'. Invalid ParseResult." );
            }
            sAgain = parseResult.Result.ToNpmString();
            parseResultAgain = SVersionBound.NpmTryParse( sAgain );
            if( parseResultAgain.Result != parseResult.Result )
            {
                throw new Exception( $"Failed to NPM parse back '{sAgain}': Expected: '{parseResult.Result}', got '{parseResultAgain.Result}'." );
            }
        }
    }

    [TestCase( ">=0.0.0-0" )]
    [TestCase( "*" )]
    [TestCase( "" )]
    public void use_NormalizeNpmVersionBoundAll_to_handle_SVersionBoundAll_with_or_without_includePrerelease_flag( string bound )
    {
        Check( bound, includePrerelease: true );
        Check( bound, includePrerelease: false );

        static void Check( string bound, bool includePrerelease )
        {
            var rNpm = SVersionBound.NpmTryParse( bound, includePrerelease );
            rNpm.IsValid.ShouldBeTrue();
            (rNpm.Result == SVersionBound.All).ShouldBe( includePrerelease || bound == ">=0.0.0-0" );
            rNpm.Result.NormalizeNpmVersionBoundAll().ShouldBe( SVersionBound.All );
        }
    }
}
