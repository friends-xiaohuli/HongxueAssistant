using System.Text.Json;
using HongxueAssistant.Core.GameState;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class EquipmentCompletionTests
{
    [Theory]
    [InlineData(false, TrackedEquipmentKind.Planned)]
    [InlineData(false, TrackedEquipmentKind.Priority)]
    [InlineData(false, TrackedEquipmentKind.Candidate)]
    [InlineData(true, TrackedEquipmentKind.Planned)]
    [InlineData(true, TrackedEquipmentKind.Priority)]
    [InlineData(true, TrackedEquipmentKind.Candidate)]
    public void OperatorCompletionKeepsPendingEquipmentAndItsShopMarker(bool elite, TrackedEquipmentKind kind)
    {
        var pool = new DeployPoolState();
        var vm = new TrackingViewModel();
        vm.SetDeployPool(pool);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new()
        {
            OperatorName = "角峰", IsElite = elite,
            Equipments = kind == TrackedEquipmentKind.Planned ? ["不退者之盾"] : [],
            CandidateEquipments = kind == TrackedEquipmentKind.Planned ? [] : [new() { Name = "不退者之盾", IsPriority = kind == TrackedEquipmentKind.Priority }]
        }] });
        pool.SetOwned("角峰", true);
        pool.SetElite("角峰", elite);
        vm.SyncDeployPool();

        vm.IsTracked("角峰").Should().BeFalse();
        vm.TrackedEquipment.Should().ContainSingle().Which.Should().Match<TrackingEntry>(e => !e.IsAcquired && e.EquipmentKind == kind);
        vm.EquipmentWanters("不退者之盾").Should().Equal(("角峰", kind));
        var shop = new List<ShopItem> { new() { Name = "角峰" }, new() { Name = "不退者之盾" } };
        GameStateUpdater.UpdateShopTracking(shop, vm.ActiveEntries.ToList());
        shop[0].IsTracked.Should().BeFalse();
        shop[1].IsTracked.Should().BeTrue();

        var restored = new TrackingViewModel();
        restored.SetDeployPool(pool);
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        restored.IsTracked("角峰").Should().BeFalse();
        restored.IsTracked("不退者之盾").Should().BeTrue();
        restored.EquipmentWanters("不退者之盾").Should().Equal(("角峰", kind));
    }

    [Fact]
    public void CompletedSharedSourcesKeepEquipmentUntilEachSourceIsRemoved()
    {
        var pool = new DeployPoolState { Owned = ["角峰", "山"] };
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.SetDeployPool(pool);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", Equipments = ["不退者之盾"] }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "山", CandidateEquipments = [new() { Name = "不退者之盾", IsPriority = true }] }] });
        vm.TrackedEquipment.Should().ContainSingle().Which.EquipmentKind.Should().Be(TrackedEquipmentKind.Planned);
        vm.EquipmentWanters("不退者之盾").Should().HaveCount(2);

        vm.ApplyTrackedLineup(new Lineup { Id = "a" });
        vm.TrackedEquipment.Should().ContainSingle().Which.EquipmentKind.Should().Be(TrackedEquipmentKind.Priority);
        vm.EquipmentWanters("不退者之盾").Should().Equal(("山", TrackedEquipmentKind.Priority));
        vm.RemoveTracking("山");
        vm.TrackedEquipment.Should().BeEmpty();
        vm.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        vm.TrackedEquipment.Should().BeEmpty();
    }

    [Fact]
    public void AcquiredEquipmentStaysCompletedWhenOperatorOwnershipChangesAndReloads()
    {
        var pool = new DeployPoolState();
        var vm = new TrackingViewModel();
        vm.SetDeployPool(pool);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", Equipments = ["不退者之盾"] }] });
        vm.ToggleAcquired("不退者之盾");
        foreach (var owned in new[] { true, false, true })
        {
            pool.SetOwned("角峰", owned);
            vm.SyncDeployPool();
            vm.IsTracked("不退者之盾").Should().BeFalse();
            vm.TrackedEquipment.Should().ContainSingle().Which.IsAcquired.Should().BeTrue();
        }
        var restored = new TrackingViewModel();
        restored.SetDeployPool(pool);
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        restored.IsTracked("不退者之盾").Should().BeFalse();
        restored.StartNewRun();
        restored.IsTracked("不退者之盾").Should().BeTrue();
    }
}
