using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;

namespace HongxueAssistant.Tests.Services;

public sealed class RoundUpstreamCompatibilityTests
{
    private static readonly Operator[] Operators =
    [new() { Name = "甲", CoreCovenants = ["炎"] }, new() { Name = "乙", AdditionalCovenants = ["精准"] },
     new() { Name = "丙", CoreCovenants = ["炎"] }];
    private static readonly CovenantInfo[] Covenants = [new() { Name = "炎", BondType = "SEASON", ActivateCount = 1 }];
    private static CovenantCalculator Calculator() => new(Operators, Covenants, new Dictionary<string, string>());
    private static Lineup Source() => new()
    {
        Name = "真实回合兼容", Slots = [new() { OperatorName = "甲" }],
        Guide = new() { Tabs = [new() { TabType = 1 }, new() { TabType = 2, StartRound = 1, EndRound = 4 }] },
        TargetCovenants = [new() { Name = "炎", Tier = 1 }],
        RoundStages = [new() { StartRound = 1, EndRound = 4, SourceTabIndex = 1,
            Slots = [new() { OperatorName = "乙", Equipments = ["计划装备"],
                CandidateEquipments = [new() { Name = "候选盾", IsPriority = true }] }] }]
    };

    [Fact]
    public void RoundCandidatesAndTargetsStayIndependentThroughEditingCopyAndReset()
    {
        var source = Source();
        var before = JsonSerializer.Serialize(source);
        var clone = source.RoundStages[0].Clone();
        clone.Slots[0].CandidateEquipments[0].IsPriority = false;
        source.RoundStages[0].Slots[0].CandidateEquipments[0].IsPriority.Should().BeTrue();
        var vm = new LineupEditorViewModel(source, Operators, [], Calculator());
        vm.SelectRoundStage(0);
        vm.CopySlotToPhase(0, LineupPhase.后期).Should().BeTrue("a round is distinct from the previously selected final phase");
        vm.ToggleCandidatePriority(0, "候选盾");
        var saved = vm.ToLineup();
        saved.Slots.Single(s => s.OperatorName == "乙").CandidateEquipments.Single().IsPriority.Should().BeTrue();
        saved.RoundStages[0].Slots[0].CandidateEquipments.Single().IsPriority.Should().BeFalse();
        vm.RemoveTarget("炎");
        vm.Reset();
        vm.ToLineup().RoundStages.Should().BeEquivalentTo(source.RoundStages);
        vm.ToLineup().TargetCovenants.Should().BeEquivalentTo(source.TargetCovenants);
        vm.ToLineup().Slots.Should().ContainSingle().Which.OperatorName.Should().Be("甲");
        JsonSerializer.Serialize(source).Should().Be(before);
    }

    [Fact]
    public async Task StoreAndJsonImportPreserveRoundCandidatesAndTargetCovenants()
    {
        var directory = Directory.CreateTempSubdirectory("hongxue-round-19-").FullName;
        try
        {
            var source = Source();
            var rules = Calculator().EquipmentRules;
            var store = new LineupStore(directory, rules);
            await store.SaveAsync(source);
            var loaded = (await store.LoadAllAsync()).Single();
            var share = new LineupShareService(rules, Path.Combine(directory, "share"));
            var imported = await share.ImportAsync(JsonSerializer.Serialize(loaded));
            imported.RoundStages.Should().BeEquivalentTo(source.RoundStages);
            imported.TargetCovenants.Should().BeEquivalentTo(source.TargetCovenants);
            var vm = new LineupEditorViewModel(imported, Operators, [], Calculator());
            vm.SelectRoundStage(0);
            vm.Slots[0].CandidateEquipments.Should().ContainSingle().Which.IsPriority.Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void UpstreamRepairRetainsIndependentRoundDataWithoutSharingMutableObjects()
    {
        var source = Source();
        var calc = Calculator();
        var vm = new LineupPlannerViewModel(new LineupPlanner(Operators, Covenants), Operators, new BanViewModel(), calc.EquipmentRules);
        vm.BeginRepair(source, source.TargetCovenants, ["甲"]);
        Lineup? repaired = null;
        vm.OverwriteRequested += result => repaired = result;
        vm.OverwriteLineupCommand.Execute(new PlannerLineupItem
        {
            Source = new PlannedLineup(1, 0, [new("丙", null, ["炎"])], new Dictionary<string, int> { ["炎"] = 1 }, false)
        });
        repaired.Should().NotBeNull();
        repaired!.Slots.Should().ContainSingle().Which.OperatorName.Should().Be("丙");
        repaired.RoundStages.Should().BeEquivalentTo(source.RoundStages);
        repaired.TargetCovenants.Should().BeEquivalentTo(source.TargetCovenants);
        repaired.RoundStages[0].Slots[0].CandidateEquipments.Clear();
        source.RoundStages[0].Slots[0].CandidateEquipments.Should().ContainSingle();
    }

    [Fact]
    public void AcquiredRoundOnlyOperatorKeepsItsUnacquiredCandidateEquipment()
    {
        var source = Source();
        var vm = new TrackingViewModel();
        vm.TrackLineup(source);
        vm.TrackedOperators.Single(e => e.Name == "乙").Stages.Should().Contain("1").And.Contain("4");
        vm.ToggleAcquired("乙");
        vm.TrackedEquipment.Single(e => e.Name == "候选盾").EquipmentKind.Should().Be(TrackedEquipmentKind.Priority);
        var restored = new TrackingViewModel();
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(JsonSerializer.Serialize(vm.CaptureState()))!);
        restored.TrackedEquipment.Single(e => e.Name == "候选盾").IsAcquired.Should().BeFalse();
        restored.TrackedOperators.Single(e => e.Name == "乙").IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task CandidatePriorityAndTargetsBothInvalidateRoundShareCache()
    {
        var directory = Directory.CreateTempSubdirectory("hongxue-round-share-").FullName;
        try
        {
            var source = Source();
            var json = JsonSerializer.Serialize(source);
            using var handler = new Links();
            using var http = new HttpClient(handler);
            var share = new LineupShareService(Calculator().EquipmentRules, directory, http);
            var first = await share.ExportAsync(source);
            source.RoundStages[0].Slots[0].CandidateEquipments[0].IsPriority = false;
            var second = await share.ExportAsync(source);
            second.Should().NotBe(first);
            source.TargetCovenants[0].Tier = 2;
            (await share.ExportAsync(source)).Should().NotBe(second);
            (await share.ExportAsync(JsonSerializer.Deserialize<Lineup>(json)!)).Should().Be(first);
            handler.Calls.Should().Be(3);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExistingLocalProfileWinsWithoutSwitchingToTheUpstreamFolder()
    {
        var directory = Directory.CreateTempSubdirectory("hongxue-profile-19-").FullName;
        try
        {
            var local = Path.Combine(directory, "HongxueAssistant");
            var upstream = Path.Combine(directory, "HongxueCompanion");
            Directory.CreateDirectory(local); Directory.CreateDirectory(upstream);
            File.WriteAllText(Path.Combine(local, "tracking.json"), "local");
            File.WriteAllText(Path.Combine(upstream, "tracking.json"), "upstream");
            UserSettingsDirectory.Prepare(directory, null).Should().Be(local);
            File.ReadAllText(Path.Combine(local, "tracking.json")).Should().Be("local");
            File.ReadAllText(Path.Combine(upstream, "tracking.json")).Should().Be("upstream");
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Links : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("https://dpaste.com/ROUND19" + ++Calls) });
    }
}
