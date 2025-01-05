using GitContext;

namespace Tests;

/// <summary>
/// Test properties are set
/// </summary>
public class TestPropertiesAreSet
{
    /// <summary>
    /// Basic properties test
    /// </summary>
    [Fact]
    public void TestBasic()
    {
        Assert.NotNull(Git.Hash);
        Assert.NotNull(Git.Author);
        Assert.NotNull(Git.Date);

        Assert.NotEqual(string.Empty, Git.Hash);
        Assert.NotEqual(string.Empty, Git.Author);
        Assert.NotEqual(default, Git.Date.Value);
    }
}
