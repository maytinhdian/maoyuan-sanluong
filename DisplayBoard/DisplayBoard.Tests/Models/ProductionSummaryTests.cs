using DisplayBoard.Core.Models;

namespace DisplayBoard.Tests.Models;

public sealed class ProductionSummaryTests
{
    [Theory]
    [InlineData(1200, 1200, 100)]
    [InlineData(980, 1200, 81.666666666666666666666666667)]
    [InlineData(1500, 1200, 125)]
    [InlineData(500, 0, 0)]
    [InlineData(0, 0, 0)]
    public void CalculateCompletionRate_FollowsSpecFormula(decimal quantity, decimal target, decimal expected)
    {
        Assert.Equal(expected, ProductionSummary.CalculateCompletionRate(quantity, target), 10);
    }

    [Fact]
    public void Empty_HasZeroCompletionRate()
    {
        Assert.Equal(0m, ProductionSummary.Empty.CompletionRate);
        Assert.Empty(DisplayDataSnapshot.Empty.Records);
    }

    [Fact]
    public void DepartmentSummary_UsesSameFormula()
    {
        var department = new DepartmentSummary("Ép", 2, 2250m, 2400m);

        Assert.Equal(93.75m, department.CompletionRate);
    }
}
