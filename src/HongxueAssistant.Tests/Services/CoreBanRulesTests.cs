using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

public class CoreBanRulesTests
{
    [Theory]
    [InlineData("炎", "精准", "炎", true)]
    [InlineData("炎", "精准", "精准", false)]
    [InlineData("炎|谢拉格", "突袭", "炎", false)]
    [InlineData("炎|谢拉格", "突袭", "炎|谢拉格", true)]
    [InlineData("炎|谢拉格", "突袭", "炎|突袭", false)]
    [InlineData("", "精准|奥术", "精准", false)]
    [InlineData("", "精准|奥术", "精准|奥术", true)]
    [InlineData("", "", "炎", false)]
    [InlineData("莱塔尼亚", "聚合|远见", "莱塔尼亚", true)]
    public void CoreCovenantsDetermineAvailabilityBeforeAdditionalCovenants(
        string core, string additional, string banned, bool excluded)
    {
        var op = new Operator { CoreCovenants = [.. core.Split('|', StringSplitOptions.RemoveEmptyEntries)],
            AdditionalCovenants = [.. additional.Split('|', StringSplitOptions.RemoveEmptyEntries)] };
        CovenantBanRules.IsExcluded(op, banned.Split('|').ToHashSet()).Should().Be(excluded);
    }
}
