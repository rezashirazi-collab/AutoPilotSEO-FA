using AutopilotSeo.Core;

namespace AutopilotSeo.Tests;

public class ProfileRulesTests
{
    static ProfileData Complete() => new()
    {
        Goal = GoalType.Leads, Kpi = "فرم مشاوره",
        Products = [new() { Name = "a" }, new() { Name = "b" }, new() { Name = "c" }],
        Competitors = [new() { Domain = "x.ir" }, new() { Domain = "y.ir" }, new() { Domain = "z.ir" }],
        NoRedLines = true,
        ToneApproved = true,
    };

    [Fact]
    public void Empty_profile_fails_gate_with_zero_score()
    {
        var c = ProfileRules.Evaluate(new ProfileData());
        Assert.False(c.GatePassed);
        Assert.Equal(0, c.Score);
        Assert.Equal(["goal", "products", "competitors", "redlines", "tone"], c.MissingRequired);
    }

    [Fact]
    public void Required_items_pass_gate_and_give_sixty()
    {
        var c = ProfileRules.Evaluate(Complete());
        Assert.True(c.GatePassed);
        Assert.Equal(60, c.Score);
    }

    [Fact]
    public void Blank_rows_do_not_count_and_silence_is_not_explicit_none()
    {
        var d = Complete();
        d.Products[2].Name = " ";
        d.NoRedLines = false;
        var c = ProfileRules.Evaluate(d);
        Assert.False(c.GatePassed);
        Assert.Equal(["products", "redlines"], c.MissingRequired);
    }
}
