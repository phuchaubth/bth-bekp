using BEKP.Domain.Common;
using BEKP.Domain.Transfusion;

namespace BEKP.Domain.Tests;

public sealed class BloodTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc);

    private static BloodRequest NewRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), BloodProductType.RedCells, AboGroup.O, true, 2, BloodRequestUrgency.Routine, BloodProductModifiers.Leukoreduced, Now);

    [Fact]
    public void Request_FollowsTransfusionWorkflow_AndRecordsTimestamps()
    {
        var request = NewRequest();
        request.ChangeStatus(BloodRequestStatus.CrossMatching, Now.AddMinutes(5));
        request.ChangeStatus(BloodRequestStatus.Approved, Now.AddMinutes(20));
        request.ChangeStatus(BloodRequestStatus.Issued, Now.AddMinutes(30));
        Assert.True(request.IsPending);
        request.ChangeStatus(BloodRequestStatus.Transfusing, Now.AddMinutes(45));

        Assert.False(request.IsPending);
        Assert.Equal(Now.AddMinutes(30), request.IssuedAtUtc);
        Assert.Equal(Now.AddMinutes(45), request.TransfusionStartedAtUtc);
    }

    [Theory]
    [InlineData(BloodRequestStatus.Issued)]
    [InlineData(BloodRequestStatus.Transfusing)]
    [InlineData(BloodRequestStatus.Completed)]
    public void Request_CannotSkipApproval(BloodRequestStatus target) =>
        Assert.Throws<DomainException>(() => NewRequest().ChangeStatus(target, Now.AddMinutes(1)));

    [Fact]
    public void Request_CancelledIsFinal()
    {
        var request = NewRequest();
        request.ChangeStatus(BloodRequestStatus.Cancelled, Now.AddMinutes(1));
        Assert.Throws<DomainException>(() => request.ChangeStatus(BloodRequestStatus.Approved, Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Request_RejectsUnitsOutOfRange(int units) =>
        Assert.Throws<DomainException>(() => new BloodRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), BloodProductType.Platelets, AboGroup.A, true, units, BloodRequestUrgency.Urgent, BloodProductModifiers.None, Now));

    [Theory]
    [InlineData(9, 20, StockLevel.Critical)]
    [InlineData(10, 20, StockLevel.Low)]
    [InlineData(19, 20, StockLevel.Low)]
    [InlineData(20, 20, StockLevel.Adequate)]
    [InlineData(0, 0, StockLevel.Adequate)]
    public void Inventory_Level(int units, int minimum, StockLevel expected) =>
        Assert.Equal(expected, new BloodInventoryItem(Guid.NewGuid(), BloodProductType.Platelets, AboGroup.O, units, minimum, Now).Level);

    [Fact]
    public void Inventory_WithdrawNeverGoesNegative()
    {
        var item = new BloodInventoryItem(Guid.NewGuid(), BloodProductType.RedCells, AboGroup.AB, 1, 30, Now);
        item.Withdraw(2, Now.AddMinutes(1));
        Assert.Equal(0, item.UnitsAvailable);
    }
}
