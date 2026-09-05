using TOOL_SHARED.Contracts.Common;

namespace TOOL_TESTS.Common;

public sealed class PagedResponseTests
{
    [Fact]
    public void CalculatesPageMetadataAndNavigationFlags()
    {
        var response = new PagedResponse<int>([1, 2], 2, 2, 5);

        Assert.Equal(3, response.TotalPages);
        Assert.True(response.HasPrevious);
        Assert.True(response.HasNext);
    }

    [Fact]
    public void EmptyCollectionHasNoPages()
    {
        var response = new PagedResponse<int>([], 1, 20, 0);

        Assert.Equal(0, response.TotalPages);
        Assert.False(response.HasPrevious);
        Assert.False(response.HasNext);
    }
}
