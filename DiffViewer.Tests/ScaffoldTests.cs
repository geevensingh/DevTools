using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Scaffold_BuildSucceeds()
    {
        true.Should().BeTrue();
    }
}
