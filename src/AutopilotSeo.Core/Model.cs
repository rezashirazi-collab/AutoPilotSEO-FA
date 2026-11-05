using Microsoft.AspNetCore.Identity;

namespace AutopilotSeo.Core;

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = "";
}

/// <summary>Every site is always in exactly one state; the state decides what the system may do.</summary>
public enum ProjectState { Onboarding, Start, Routine, Growth, Crisis }

public class Site
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Domain { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ProjectState State { get; set; } = ProjectState.Onboarding;
    /// <summary>Kill switch: when set, nothing is executed on this site.</summary>
    public bool IsPaused { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<BusinessProfile> Profiles { get; set; } = [];
}

/// <summary>
/// One version of the business profile. Approved versions are frozen; editing one starts a new draft
/// version, so every recommendation can point at the exact profile it was made with.
/// </summary>
public class BusinessProfile
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public int Version { get; set; } = 1;
    public ProfileData Data { get; set; } = new();
    public int Score { get; set; }
    public bool GatePassed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedById { get; set; }

    public Site Site { get; set; } = null!;
}

/// <summary>Append-only ledger: who did what, when, with which evidence. A DB trigger blocks UPDATE/DELETE.</summary>
public class AuditEvent
{
    public long Id { get; set; }
    public int? SiteId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public enum AnnotationKind { GoogleUpdate, DataAnomaly, SiteChange }

/// <summary>Things every time-based analysis must see: Google updates, GSC data anomalies, our own changes. SiteId null = global.</summary>
public class Annotation
{
    public int Id { get; set; }
    public int? SiteId { get; set; }
    public AnnotationKind Kind { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Title { get; set; } = "";
    public string? Note { get; set; }
}
