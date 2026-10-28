namespace HealthCare.Services;
public sealed class ClinicClock(IConfiguration configuration)
{
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(configuration["Clinic:TimeZone"] ?? "Asia/Shanghai");
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
    public DateTime Today => Now.Date;
}
