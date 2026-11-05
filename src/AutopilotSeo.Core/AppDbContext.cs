using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AutopilotSeo.Core;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Annotation> Annotations => Set<Annotation>();
    public DbSet<GscConnection> GscConnections => Set<GscConnection>();
    public DbSet<GscQuery> GscQueries => Set<GscQuery>();
    public DbSet<GscPage> GscPages => Set<GscPage>();
    public DbSet<GscDaily> GscDaily => Set<GscDaily>();
    public DbSet<GscPageDaily> GscPageDaily => Set<GscPageDaily>();
    public DbSet<GscQueryPageDaily> GscQueryPageDaily => Set<GscQueryPageDaily>();
    public DbSet<Crawl> Crawls => Set<Crawl>();
    public DbSet<CrawlPage> CrawlPages => Set<CrawlPage>();
    public DbSet<CrawlLink> CrawlLinks => Set<CrawlLink>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<UrlInspection> UrlInspections => Set<UrlInspection>();
    public DbSet<Alert> Alerts => Set<Alert>();

    /// <summary>Queues a ledger entry; it is written with the next SaveChanges, in the same transaction as the change.</summary>
    public void Log(int? siteId, string? userId, string action, object? detail = null) =>
        AuditEvents.Add(new AuditEvent
        {
            SiteId = siteId, UserId = userId, Action = action,
            Detail = detail is null ? null : JsonSerializer.Serialize(detail),
        });

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>().Property(u => u.FullName).HasMaxLength(100);

        b.Entity<Site>(e =>
        {
            e.Property(s => s.Domain).HasMaxLength(253);
            e.Property(s => s.DisplayName).HasMaxLength(100);
            e.HasIndex(s => new { s.OwnerId, s.Domain }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.OwnerId);
        });

        b.Entity<BusinessProfile>(e =>
        {
            e.HasIndex(p => new { p.SiteId, p.Version }).IsUnique();
            e.Property(p => p.Data).HasConversion(
                d => JsonSerializer.Serialize(d, JsonSerializerOptions.Default),
                s => JsonSerializer.Deserialize<ProfileData>(s, JsonSerializerOptions.Default) ?? new ProfileData(),
                new ValueComparer<ProfileData>(
                    (a, c) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(c, JsonSerializerOptions.Default),
                    d => JsonSerializer.Serialize(d, JsonSerializerOptions.Default).GetHashCode(),
                    d => JsonSerializer.Deserialize<ProfileData>(JsonSerializer.Serialize(d, JsonSerializerOptions.Default), JsonSerializerOptions.Default)!));
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(64);
            e.HasIndex(a => new { a.SiteId, a.At });
        });

        b.Entity<GscConnection>(e =>
        {
            e.HasIndex(c => c.SiteId).IsUnique();
            e.HasOne<Site>().WithOne().HasForeignKey<GscConnection>(c => c.SiteId).OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.GoogleEmail).HasMaxLength(256);
            e.Property(c => c.Property).HasMaxLength(300);
            e.Property(c => c.LastError).HasMaxLength(1000);
        });

        // Search Console facts. Lookups keep long strings out of the big tables; no FKs on fact tables
        // (bulk-copied, and SQL Server rejects multiple cascade paths). Lookup text is not unique-indexed:
        // a site is synced by one worker at a time, which dedupes in memory.
        b.Entity<GscQuery>(e => { e.Property(q => q.Text).HasMaxLength(1024); e.HasIndex(q => q.SiteId); });
        b.Entity<GscPage>(e => { e.Property(p => p.Url).HasMaxLength(2048); e.HasIndex(p => p.SiteId); });
        b.Entity<GscDaily>().HasKey(d => new { d.SiteId, d.Date });
        b.Entity<GscPageDaily>().HasKey(d => new { d.SiteId, d.Date, d.PageId });
        b.Entity<GscQueryPageDaily>(e =>
        {
            e.HasKey(d => new { d.SiteId, d.Date, d.QueryId, d.PageId, d.Device });
            e.Property(d => d.Device).HasConversion<byte>();
        });

        b.Entity<Crawl>(e =>
        {
            e.HasIndex(c => new { c.SiteId, c.Id });
            e.HasOne<Site>().WithMany().HasForeignKey(c => c.SiteId).OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Error).HasMaxLength(1000);
        });
        // Crawl facts are bulk-copied; like the GSC facts they carry no FKs.
        b.Entity<CrawlPage>(e =>
        {
            e.HasIndex(p => p.CrawlId);
            e.Property(p => p.Url).HasMaxLength(2048);
            e.Property(p => p.RedirectTo).HasMaxLength(2048);
            e.Property(p => p.Canonical).HasMaxLength(2048);
            e.Property(p => p.Title).HasMaxLength(512);
            e.Property(p => p.H1).HasMaxLength(512);
            e.Property(p => p.MetaDescription).HasMaxLength(1024);
        });
        b.Entity<CrawlLink>(e =>
        {
            e.HasKey(l => new { l.CrawlId, l.FromId, l.ToId });
            e.Property(l => l.Anchor).HasMaxLength(256);
        });
        b.Entity<Issue>(e =>
        {
            e.HasIndex(i => new { i.SiteId, i.CrawlId });
            e.Property(i => i.Type).HasMaxLength(40);
            e.Property(i => i.Url).HasMaxLength(2048);
            e.Property(i => i.Detail).HasMaxLength(1024);
        });
        b.Entity<UrlInspection>(e =>
        {
            e.HasIndex(i => i.SiteId);
            e.Property(i => i.Url).HasMaxLength(2048);
            e.Property(i => i.Verdict).HasMaxLength(40);
            e.Property(i => i.CoverageState).HasMaxLength(200);
            e.Property(i => i.IndexingState).HasMaxLength(60);
            e.Property(i => i.RobotsTxtState).HasMaxLength(60);
            e.Property(i => i.PageFetchState).HasMaxLength(60);
            e.Property(i => i.GoogleCanonical).HasMaxLength(2048);
            e.Property(i => i.UserCanonical).HasMaxLength(2048);
        });
        b.Entity<Alert>(e =>
        {
            e.HasIndex(a => new { a.SiteId, a.ResolvedAt });
            e.HasOne<Site>().WithMany().HasForeignKey(a => a.SiteId).OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.Kind).HasMaxLength(40);
            e.Property(a => a.Key).HasMaxLength(300);
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.Detail).HasMaxLength(1000);
        });

        b.Entity<Annotation>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(200);
            e.HasIndex(a => new { a.SiteId, a.StartDate });
            // Known GSC data facts from the design doc; every time-based analysis must account for them.
            e.HasData(
                new Annotation { Id = 1, Kind = AnnotationKind.DataAnomaly, StartDate = new(2025, 5, 13), EndDate = new(2026, 4, 27),
                    Title = "Impressions over-reported in Search Console", Note = "Clicks are correct; year-over-year comparisons use clicks only." },
                new Annotation { Id = 2, Kind = AnnotationKind.GoogleUpdate, StartDate = new(2026, 5, 7),
                    Title = "FAQ rich results removed", Note = "FAQ schema is no longer a CTR lever." });
        });
    }
}
