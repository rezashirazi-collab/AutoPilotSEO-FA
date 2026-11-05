using System.Globalization;
using AutopilotSeo.Core;

namespace AutopilotSeo.Web;

/// <summary>Persian display helpers: digits, Shamsi dates and labels for core enums/keys.</summary>
public static class Fa
{
    public const string ProductName = "سئو خودران";

    static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    static readonly PersianCalendar Pc = new();

    public static string Num(object? value) =>
        string.Concat((value?.ToString() ?? "").Select(c => c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c == '.' ? '٫' : c == ',' ? '٬' : c));

    public static string N(long value) => Num(value.ToString("#,0", CultureInfo.InvariantCulture));

    public static string Dec(double value, string format = "0.#") => Num(value.ToString(format, CultureInfo.InvariantCulture));

    public static string Pct(double ratio) => Dec(ratio * 100, ratio < 0.1 ? "0.0#" : "0.#") + "٪";

    public static string ShortDate(DateOnly d) => Num($"{Pc.GetMonth(d.ToDateTime(TimeOnly.MinValue))}/{Pc.GetDayOfMonth(d.ToDateTime(TimeOnly.MinValue))}");

    public static string Date(DateOnly d)
    {
        var t = d.ToDateTime(TimeOnly.MinValue);
        return Num($"{Pc.GetYear(t)}/{Pc.GetMonth(t):00}/{Pc.GetDayOfMonth(t):00}");
    }

    public static string Date(DateTime utc, bool withTime = false)
    {
        var t = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tehran);
        var d = $"{Pc.GetYear(t)}/{Pc.GetMonth(t):00}/{Pc.GetDayOfMonth(t):00}";
        return Num(withTime ? $"{d} {t:HH:mm}" : d);
    }

    public static string State(ProjectState s) => s switch
    {
        ProjectState.Onboarding => "آنبوردینگ",
        ProjectState.Start => "شروع",
        ProjectState.Routine => "روتین",
        ProjectState.Growth => "رشد",
        ProjectState.Crisis => "بحران",
        _ => s.ToString(),
    };

    public static string Goal(GoalType g) => g switch
    {
        GoalType.Sales => "فروش آنلاین",
        GoalType.Leads => "جذب لید (فرم)",
        GoalType.Calls => "تماس تلفنی",
        GoalType.Bookings => "رزرو و نوبت",
        GoalType.Awareness => "آگاهی از برند",
        _ => "انتخاب کنید",
    };

    public static string Section(string key) => key switch
    {
        "goal" => "هدف و شاخص اصلی",
        "products" => $"حداقل {Num(ProfileRules.MinProducts)} محصول یا خدمت",
        "competitors" => $"حداقل {Num(ProfileRules.MinCompetitors)} رقیب",
        "redlines" => "خط قرمزها (یا «ندارد» صریح)",
        "tone" => "تأیید لحن برند",
        "audience" => "مخاطب",
        "geo" => "جغرافیا",
        "advantage" => "مزیت رقابتی",
        "knowledge" => "منابع دانش و نویسندگان",
        "calendar" => "تقویم کسب‌وکار",
        "tech" => "محدودیت فنی",
        "reports" => "گیرندگان گزارش",
        "target" => "هدف عددی",
        _ => key,
    };

    public static string Severity(Severity s) => s switch
    {
        Core.Severity.Critical => "بحرانی",
        Core.Severity.Warning => "هشدار",
        _ => "اطلاع",
    };

    public static string IssueType(string type) => type switch
    {
        "http_error" => "خطای HTTP در صفحه‌های لینک‌شده",
        "robots_blocked" => "صفحهٔ مهم بسته‌شده با robots.txt",
        "redirect_chain" => "زنجیرهٔ ریدایرکت",
        "internal_redirect" => "لینک داخلی به آدرس ریدایرکت‌شده",
        "noindex_valuable" => "noindex روی صفحهٔ مهم",
        "canonical_broken" => "canonical به صفحهٔ خراب",
        "sitemap_not_indexable" => "آدرس غیرقابل ایندکس در نقشهٔ سایت",
        "missing_title" => "بدون عنوان",
        "duplicate_title" => "عنوان تکراری",
        "missing_description" => "بدون توضیح متا",
        "duplicate_description" => "توضیح متای تکراری",
        "missing_h1" => "بدون H1",
        "orphan" => "صفحهٔ یتیم (بدون لینک داخلی)",
        "deep_page" => "صفحهٔ عمیق (کلیک زیاد از صفحهٔ اصلی)",
        _ => type,
    };

    public static string Action(string action) => action switch
    {
        "site.created" => "سایت ثبت شد",
        "site.paused" => "همهٔ اقدامات متوقف شد",
        "site.resumed" => "اقدامات از سر گرفته شد",
        "profile.saved" => "پروندهٔ کسب‌وکار ذخیره شد",
        "profile.approved" => "پروندهٔ کسب‌وکار تأیید شد",
        "crawl.queued" => "کراول دستی در صف قرار گرفت",
        "gsc.connected" => "سرچ کنسول وصل شد",
        "gsc.property_selected" => "property سرچ کنسول انتخاب شد",
        "gsc.disconnected" => "اتصال سرچ کنسول قطع شد",
        "gsc.reconnect_needed" => "دسترسی سرچ کنسول باطل شد",
        _ => action,
    };
}
