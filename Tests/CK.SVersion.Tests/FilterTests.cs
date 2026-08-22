using NUnit.Framework;
using Shouldly;

namespace CK.Core.Tests;

[TestFixture]
public class FilterTests
{
    [TestCase( "[,]", "[explo,stable]", "v1.0.0-0.some-explo", "v1.2.3-raw-version" )]
    [TestCase( "[,alpha]", "[explo,alpha]", "v1.0.0-0.some-explo", "v1.2.3-papa" )]
    [TestCase( "[bravo,alpha]", null, null, null )]
    [TestCase( "[alpha,]", "[alpha,stable]", "v0.0.0-alpha", "v1.0.0-0.some-explo" )]
    public void CSVersionKindFilter_parse_and_check( string sv, string? toString, string? accepted, string? rejected )
    {
        Run( sv, toString, accepted, rejected );

        var ciAccepted = accepted == null ? null : SVersion.Parse( accepted ).SetCINumber( 45 );

        var ciRejected = rejected == null ? null : SVersion.Parse( rejected );
        ciRejected = ciRejected?.IsCSVersion is true ? ciRejected.SetCINumber( 42 ) : null;

        Run( sv+", allowci", toString != null ? toString + ",AllowCI" : null, ciAccepted?.ToString(), ciRejected?.ToString() );

        static void Run( string sv, string? toString, string? accepted, string? rejected )
        {
            var success = CSVersionKindFilter.TryParse( sv, out var f );
            success.ShouldBe( toString != null );
            if( success )
            {
                var t = f.ToString();
                t.ShouldBe( toString );
                CSVersionKindFilter.TryParse( t, out var back ).ShouldBeTrue();
                back.ShouldBe( f );
                if( accepted != null )
                {
                    f.Accepts( SVersion.Parse( accepted ) ).ShouldBeTrue();
                }
                if( rejected != null )
                {
                    f.Accepts( SVersion.Parse( rejected ) ).ShouldBeFalse();
                }
            }
        }
    }

}
