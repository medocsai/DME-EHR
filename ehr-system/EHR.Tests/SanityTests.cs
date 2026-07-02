using FluentAssertions;
using Xunit;

namespace EHR.Tests;

/// <summary>
/// Sanity tests.
///
/// Why: prove the testing infrastructure itself works. If these fail, nothing else will.
/// What: trivial assertions that should always pass. They test the framework, not the app.
/// Who calls: dotnet test, GitHub Actions CI.
/// Returns: if these pass, the test pipeline is healthy.
/// </summary>
public class SanityTests
{
    [Fact]
    public void Math_TwoPlusTwo_ShouldBeFour()
    {
        // Arrange
        var a = 2;
        var b = 2;

        // Act
        var result = a + b;

        // Assert
        result.Should().Be(4);
    }

    [Fact]
    public void String_Concat_ShouldJoin()
    {
        "hello ".Should().StartWith("hello");
        ("hello " + "world").Should().Be("hello world");
    }

    [Fact]
    public void Xunit_AndFluentAssertions_AreWiredUp()
    {
        var list = new[] { 1, 2, 3 };
        list.Should().HaveCount(3).And.Contain(2);
    }
}
