namespace AutopilotSeo.Core;

public enum GoalType { None, Sales, Leads, Calls, Bookings, Awareness }

/// <summary>Business profile content, stored as one JSON column. Every later decision reads from it.</summary>
public sealed class ProfileData
{
    public GoalType Goal { get; set; }
    public string Kpi { get; set; } = "";
    public string Target { get; set; } = "";
    public string Timeframe { get; set; } = "";

    public List<Product> Products { get; set; } = [];

    public string Personas { get; set; } = "";
    public string Objections { get; set; } = "";

    public string Cities { get; set; } = "";

    public List<Competitor> Competitors { get; set; } = [];

    public string Advantage { get; set; } = "";

    public bool FormalTone { get; set; } = true;
    public string PreferredWords { get; set; } = "";
    public string BannedWords { get; set; } = "";
    public string GoodSample { get; set; } = "";
    public bool ToneApproved { get; set; }

    /// <summary>Explicit "we have none" counts as filled; silence does not.</summary>
    public bool NoRedLines { get; set; }
    public string BannedClaims { get; set; } = "";
    public string BannedCompetitorMentions { get; set; } = "";
    public string BannedTopics { get; set; } = "";
    public string ProtectedUrls { get; set; } = "";

    public string KnowledgeSources { get; set; } = "";
    public string Authors { get; set; } = "";
    public string BusinessCalendar { get; set; } = "";

    public string Theme { get; set; } = "";
    public string SeoPlugin { get; set; } = "";
    public bool HasDeveloper { get; set; }

    public string ReportEmails { get; set; } = "";
}

public sealed class Product
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>1 (low) to 5 (top). Feeds value_per_click in the priority formula.</summary>
    public int Priority { get; set; } = 3;
}

public sealed class Competitor
{
    public string Domain { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed record Completeness(int Score, bool GatePassed, IReadOnlyList<string> MissingRequired, IReadOnlyList<string> MissingOptional);

public static class ProfileRules
{
    public const int MinProducts = 3;
    public const int MinCompetitors = 3;

    // Required items gate recommendations (60 pts); optional items only raise the score (40 pts).
    public static Completeness Evaluate(ProfileData d)
    {
        var required = new (string Key, bool Ok)[]
        {
            ("goal", d.Goal != GoalType.None && Has(d.Kpi)),
            ("products", d.Products.Count(p => Has(p.Name)) >= MinProducts),
            ("competitors", d.Competitors.Count(c => Has(c.Domain)) >= MinCompetitors),
            ("redlines", d.NoRedLines || Has(d.BannedClaims) || Has(d.BannedCompetitorMentions) || Has(d.BannedTopics) || Has(d.ProtectedUrls)),
            ("tone", d.ToneApproved),
        };
        var optional = new (string Key, bool Ok)[]
        {
            ("audience", Has(d.Personas)),
            ("geo", Has(d.Cities)),
            ("advantage", Has(d.Advantage)),
            ("knowledge", Has(d.KnowledgeSources) || Has(d.Authors)),
            ("calendar", Has(d.BusinessCalendar)),
            ("tech", Has(d.Theme) || Has(d.SeoPlugin)),
            ("reports", Has(d.ReportEmails)),
            ("target", Has(d.Target)),
        };

        int score = (int)Math.Round(60.0 * required.Count(r => r.Ok) / required.Length
                                  + 40.0 * optional.Count(o => o.Ok) / optional.Length);
        return new Completeness(score,
            required.All(r => r.Ok),
            required.Where(r => !r.Ok).Select(r => r.Key).ToList(),
            optional.Where(o => !o.Ok).Select(o => o.Key).ToList());
    }

    static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);
}
