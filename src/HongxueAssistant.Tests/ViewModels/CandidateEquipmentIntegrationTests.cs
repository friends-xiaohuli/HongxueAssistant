using System.Text.Json;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class CandidateEquipmentIntegrationTests
{
    [Fact]
    public async Task StoreLoadsLegacyDataAndRoundTripsCandidatesInEveryPhase()
    {
        var folder = Path.Combine(Path.GetTempPath(), "candidate-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "old.json"), """{"name":"旧阵容","slots":[{"operatorName":"角峰"}]}""");
            var store = new LineupStore(folder, EquipmentTestData.Rules);
            var slot = new LineupSlot { OperatorName = "角峰", CandidateEquipments =
                [new() { Name = "歌利亚头盔", IsPriority = true }, new() { Name = "不退者之盾" }] };
            await store.SaveAsync(new Lineup { Name = "候选阵容", Slots = [slot], EarlySlots = [slot], MidSlots = [slot], SupportSlots = [slot] });
            var loaded = await store.LoadAllAsync();
            loaded.Single(l => l.Name == "旧阵容").Slots.Single().CandidateEquipments.Should().BeEmpty();
            var modern = loaded.Single(l => l.Name == "候选阵容");
            foreach (var phase in new[] { modern.Slots, modern.EarlySlots, modern.MidSlots, modern.SupportSlots })
                phase.Single().CandidateEquipments.Select(c => (c.Name, c.IsPriority))
                    .Should().Equal(("歌利亚头盔", true), ("不退者之盾", false));
        }
        finally
        {
            Path.GetDirectoryName(Path.GetFullPath(folder)).Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/'));
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void EditingOneLineupKeepsOtherSourcesAndSurvivesTrackingRoundTrip()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "共享", IsPriority = true }, new() { Name = "旧候选" }] }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "共享" }, new() { Name = "乙来源" }] }] });
        vm.TrackedOperators.Single().CandidateEquipments.Select(c => (c.Name, c.IsPriority))
            .Should().BeEquivalentTo(new[] { ("共享", true), ("旧候选", false), ("乙来源", false) });
        vm.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "新候选" }] }] });
        vm.TrackedEquipment.Select(e => (e.Name, e.EquipmentKind)).Should().BeEquivalentTo(new[]
        {
            ("共享", TrackedEquipmentKind.Candidate), ("乙来源", TrackedEquipmentKind.Candidate),
            ("新候选", TrackedEquipmentKind.Candidate)
        });
        var restored = new TrackingViewModel();
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        restored.ApplyTrackedLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲" }] });
        restored.TrackedEquipment.Should().ContainSingle().Which.Name.Should().Be("新候选");
        restored.TrackedLineupIds.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void AcquiringCandidateClearsEveryOriginAndKeepsPlannedSlots()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "共享" }] }, new() { OperatorName = "乙", Equipments = ["共享"] }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "共享", IsPriority = true }] }] });
        vm.ToggleAcquired("共享");
        vm.TrackedOperators.Should().OnlyContain(e => e.CandidateEquipments.Count == 0);
        vm.TrackedOperators.SelectMany(e => e.Origins).Should().OnlyContain(o => o.CandidateEquipments.Count == 0);
        vm.TrackedOperators.Single(e => e.Name == "乙").Equipments.Should().Equal("共享");
        vm.ApplyTrackedLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "乙", Equipments = ["共享"] }] });
        vm.TrackedOperators.Single(e => e.Name == "甲").CandidateEquipments.Should().BeEmpty();
        vm.IsTracked("共享").Should().BeFalse();
    }

    [Fact]
    public void PlannedSlotMasksCandidateWithoutDeletingTheOtherSource()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "共享", IsPriority = true }] }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲", Equipments = ["共享"] }] });
        vm.TrackedOperators.Single().CandidateEquipments.Should().BeEmpty();
        vm.TrackedEquipment.Single().EquipmentKind.Should().Be(TrackedEquipmentKind.Planned);
        vm.ApplyTrackedLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲" }] });
        vm.TrackedEquipment.Should().ContainSingle().Which.EquipmentKind.Should().Be(TrackedEquipmentKind.Priority);
    }

    [Fact]
    public void AcquiringPlannedEquipmentAlsoClearsMaskedCandidateOrigins()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "共享" }] }] });
        vm.TrackLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲", Equipments = ["共享"] }] });
        vm.ToggleAcquired("共享");
        vm.TrackedOperators.Single().Origins.Should().OnlyContain(o => o.CandidateEquipments.Count == 0);
        vm.ApplyTrackedLineup(new Lineup { Id = "b", Slots = [new() { OperatorName = "甲" }] });
        vm.TrackedOperators.Single().CandidateEquipments.Should().BeEmpty();
    }

    [Fact]
    public void CandidateOnlyMechSurvivesSaveAndReset()
    {
        var source = new Lineup { Strategies = [new() { StrategyId = LineupEquipmentService.MechStrategy }],
            Slots = [new() { OperatorName = LineupEquipmentService.MechName,
                CandidateEquipments = [new() { Name = "候选盾", IsPriority = true }] }] };
        var vm = new LineupEditorViewModel(source, [], [], new CovenantCalculator([], [], new Dictionary<string, string>()));
        vm.ToLineup().Slots.Single().CandidateEquipments.Single().Name.Should().Be("候选盾");
        vm.SetSlotCandidates(vm.MechSlotIndex, [new() { Name = "另一候选" }]);
        vm.Reset();
        vm.ToLineup().Slots.Single().CandidateEquipments.Single().Name.Should().Be("候选盾");
        vm.SetStrategy(0, "");
        vm.ToLineup().Slots.Single().CandidateEquipments.Single().Name.Should().Be("候选盾");
    }

    [Fact]
    public void ChoosingTransformationRemovesItsEquipmentFromCandidates()
    {
        Operator[] operators = [new() { Name = "甲" }];
        var source = new Lineup { Slots = [new() { OperatorName = "甲", CandidateEquipments =
            [new() { Name = "变形同构体" }, new() { Name = "炎国短刀", IsPriority = true }] }] };
        var vm = new LineupEditorViewModel(source, operators, [],
            new CovenantCalculator(operators, [new() { Name = "炎", BondType = "SEASON", ActivateCount = 1 }],
                new Dictionary<string, string> { ["炎国短刀"] = "炎" }));
        vm.ActiveStats.Should().BeEmpty("candidate equipment does not count toward covenants");
        vm.EquipmentRules.Transformations(vm.Slots[0]).Should().BeEmpty();
        vm.SetSlotTransformation(0, 0, "炎国短刀", []);
        vm.Slots[0].Equipments.Should().Equal("变形同构体", "炎国短刀");
        vm.Slots[0].CandidateEquipments.Should().BeEmpty();
        vm.ActiveStats.Single(s => s.Name == "炎").Count.Should().Be(1);
    }

    [Fact]
    public void LegacyTrackingOriginStillCombinesWithNewCandidates()
    {
        var state = JsonSerializer.Deserialize<TrackingState>("""
            {"Entries":[{"name":"甲","type":0,"origins":[{"LineupId":"old","Equipments":["旧装备"]}]}],
             "Lineups":[{"Id":"old","Name":"旧阵容"}]}
            """)!;
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.LoadState(state);
        vm.TrackLineup(new Lineup { Id = "new", Slots = [new() { OperatorName = "甲",
            CandidateEquipments = [new() { Name = "新候选" }] }] });
        vm.TrackedOperators.Single().Equipments.Should().Equal("旧装备");
        vm.TrackedOperators.Single().CandidateEquipments.Single().Name.Should().Be("新候选");
    }
}
