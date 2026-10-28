using System.Security.Claims;
using HealthCare.Models;
using HealthCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HealthCare.Controllers;

[ApiController, Authorize, Route("api")]
public class ClinicController(ClinicDb db, ClinicClock clock, IConfiguration config, IPasswordHasher<Staff> hasher) : ControllerBase
{
    int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    bool IsDoctor => User.IsInRole("Doctor");
    bool Clinical => IsDoctor || User.IsInRole("Admin");
    void Audit(string action, string target) => db.Audit.Add(new AuditEntry { Actor = User.Identity!.Name!, Action = action, Target = target });
    IActionResult Error(string message, int status = 400) => StatusCode(status, new { message });
    static object PatientView(Patient p) => new { p.Id, p.Name, p.Gender, p.BirthDate, p.Phone, p.Address, p.Allergies, p.Version, p.CreatedUtc };
    static object AppointmentView(Appointment a) => new { a.Id, a.PatientId, patientName = a.Patient.Name, a.Patient.Gender, a.Patient.Phone, a.DoctorId, doctorName = a.Doctor.Name, a.Doctor.Department, a.Slot, a.Status, a.Reason, a.Version };

    [HttpGet("dashboard")]
    public async Task<object> Dashboard()
    {
        var today = clock.Today; var tomorrow = today.AddDays(1); var first = today.AddDays(-6);
        var query = db.Appointments.AsNoTracking().Where(a => !IsDoctor || a.DoctorId == UserId);
        var week = await query.Where(a => a.Slot >= first && a.Slot < tomorrow && a.Status != "Cancelled").Select(a => new { a.Slot, a.Status }).ToListAsync();
        var current = week.Where(a => a.Slot.Date == today).ToList();
        var pending = await query.Include(a => a.Patient).Include(a => a.Doctor).Where(a => a.Slot >= today && a.Slot < tomorrow && a.Status != "Completed" && a.Status != "Cancelled").OrderBy(a => a.Slot).Take(6).ToListAsync();
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(today, clock.Zone); var endUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrow, clock.Zone);
        int? revenue = IsDoctor ? null : await db.Invoices.Where(i => i.PaidUtc >= startUtc && i.PaidUtc < endUtc).SumAsync(i => (int?)i.AmountCents) ?? 0;
        return new { patients = await db.Patients.CountAsync(), total = current.Count, waiting = current.Count(a => a.Status is "Scheduled" or "CheckedIn"), completed = current.Count(a => a.Status == "Completed"), revenueCents = revenue,
            trend = Enumerable.Range(0,7).Select(i => new { date = first.AddDays(i).ToString("MM-dd"), total = week.Count(a => a.Slot.Date == first.AddDays(i)), completed = week.Count(a => a.Slot.Date == first.AddDays(i) && a.Status == "Completed") }), queue = pending.Select(AppointmentView),
            doctors = await db.Staff.CountAsync(s => s.Role == "Doctor"), unpaid = IsDoctor ? (int?)null : await db.Invoices.CountAsync(i => i.Status == "Unpaid") };
    }
    [HttpGet("patients")]
    public async Task<object> Patients(string? q, int page = 1)
    {
        page = Math.Max(1,page); q = q?.Trim();
        var query = db.Patients.AsNoTracking().Where(p => string.IsNullOrEmpty(q) || p.Name.Contains(q) || p.Phone.Contains(q) || p.Id.ToString() == q);
        return new { total = await query.CountAsync(), page, pageSize = 10, items = (await query.OrderByDescending(p => p.Id).Skip((page-1)*10).Take(10).ToListAsync()).Select(PatientView) };
    }
    [HttpGet("patients/{id:int}")]
    public async Task<IActionResult> PatientDetail(int id)
    {
        var p = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id); if (p is null) return NotFound();
        object[] history = [];
        if (Clinical)
        {
            history = (await db.Encounters.AsNoTracking().Include(e => e.Appointment).ThenInclude(a => a.Doctor).Where(e => e.Appointment.PatientId == id && e.FinalizedUtc != null).OrderByDescending(e => e.Appointment.Slot).Take(30).ToListAsync()).Select(e => (object)new { e.Id, e.Complaint, e.Assessment, e.Plan, e.FollowUpDate, slot = e.Appointment.Slot, doctorName = e.Appointment.Doctor.Name }).ToArray();
        }
        Audit("查看患者档案", $"Patient/{id}"); await db.SaveChangesAsync();
        return Ok(new { patient = PatientView(p), history });
    }
    bool ValidBirth(DateTime date) => date.Date <= clock.Today && date.Date >= clock.Today.AddYears(-130);
    [HttpPost("patients"), Authorize(Roles = "Admin,Reception")]
    public async Task<IActionResult> CreatePatient(PatientRequest r)
    {
        if (!ValidBirth(r.BirthDate)) return Error("出生日期必须在过去 130 年内。");
        var p = new Patient { Name = r.Name.Trim(), Gender = r.Gender, BirthDate = r.BirthDate.Date, Phone = r.Phone.Trim(), Address = r.Address?.Trim() ?? "", Allergies = string.IsNullOrWhiteSpace(r.Allergies) ? "未提供" : r.Allergies.Trim() };
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Patients.Add(p); await db.SaveChangesAsync(); Audit("创建患者档案", $"Patient/{p.Id}"); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Ok(PatientView(p));
    }
    [HttpPut("patients/{id:int}"), Authorize(Roles = "Admin,Reception")]
    public async Task<IActionResult> EditPatient(int id, PatientRequest r)
    {
        var p = await db.Patients.FindAsync(id); if (p is null) return NotFound();
        if (p.Version != r.Version) return Error("档案已更新，请刷新后修改。",409);
        if (!ValidBirth(r.BirthDate)) return Error("出生日期不正确。");
        p.Name = r.Name.Trim(); p.Gender = r.Gender; p.BirthDate = r.BirthDate.Date; p.Phone = r.Phone.Trim(); p.Address = r.Address?.Trim() ?? ""; p.Allergies = string.IsNullOrWhiteSpace(r.Allergies) ? "未提供" : r.Allergies.Trim(); p.Version++;
        Audit("更新患者档案", $"Patient/{id}"); await db.SaveChangesAsync(); return Ok(PatientView(p));
    }
    [HttpGet("doctors")]
    public async Task<object> Doctors() => await db.Staff.Where(s => s.Role == "Doctor").Select(s => new { s.Id, s.Name, s.Department }).ToListAsync();
    [HttpGet("slots")]
    public async Task<object> Slots(int doctorId, DateTime date)
    {
        var booked = await db.Appointments.Where(a => a.DoctorId == doctorId && a.Slot >= date.Date && a.Slot < date.Date.AddDays(1) && a.Status != "Cancelled").Select(a => a.Slot).ToListAsync();
        return Enumerable.Range(0,20).Select(i => date.Date.AddHours(8+i*0.5)).Select(s => new { slot = s.ToString("yyyy-MM-ddTHH:mm:ss"), available = s > clock.Now && s.Date <= clock.Today.AddDays(90) && !booked.Contains(s) });
    }
    [HttpGet("appointments")]
    public async Task<object> Appointments(DateTime date, string? status, int page = 1)
    {
        page = Math.Max(1,page);
        var query = db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Doctor).Where(a => a.Slot >= date.Date && a.Slot < date.Date.AddDays(1) && (!IsDoctor || a.DoctorId == UserId) && (string.IsNullOrEmpty(status) || a.Status == status));
        return new { total = await query.CountAsync(), page, pageSize = 12, items = (await query.OrderBy(a => a.Slot).Skip((page-1)*12).Take(12).ToListAsync()).Select(AppointmentView) };
    }
    [HttpPost("appointments"), Authorize(Roles = "Admin,Reception")]
    public async Task<IActionResult> Book(BookingRequest r)
    {
        var slot = DateTime.SpecifyKind(r.Slot, DateTimeKind.Unspecified);
        if (slot <= clock.Now || slot.Date > clock.Today.AddDays(90) || slot.Hour < 8 || slot.Hour >= 18 || slot.Minute % 30 != 0 || slot.Second != 0 || slot.Ticks % TimeSpan.TicksPerSecond != 0) return Error("请选择未来 90 天内 08:00–17:30 的半小时时段。");
        if (!await db.Patients.AnyAsync(p => p.Id == r.PatientId) || !await db.Staff.AnyAsync(s => s.Id == r.DoctorId && s.Role == "Doctor")) return Error("患者或医生不存在。");
        var a = new Appointment { PatientId = r.PatientId, DoctorId = r.DoctorId, Slot = slot, Reason = r.Reason?.Trim() ?? "" };
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Appointments.Add(a); await db.SaveChangesAsync(); Audit("预约挂号", $"Appointment/{a.Id}"); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Ok(new { a.Id, a.Version });
    }
    [HttpPost("appointments/{id:int}/{actionName}"), Authorize(Roles = "Admin,Doctor,Reception")]
    public async Task<IActionResult> Transition(int id, string actionName, VersionRequest r)
    {
        var a = await db.Appointments.Include(a => a.Encounter).SingleOrDefaultAsync(a => a.Id == id); if (a is null) return NotFound();
        if (a.Version != r.Version) return Error("预约状态已改变，请刷新。",409);
        if (IsDoctor && a.DoctorId != UserId) return Forbid();
        switch (actionName)
        {
            case "checkin":
                if (IsDoctor) return Forbid();
                if (a.Status != "Scheduled" || a.Slot.Date != clock.Today) return Error("仅当天待报到预约可以报到。",409);
                a.Status = "CheckedIn"; break;
            case "cancel":
                if (IsDoctor) return Forbid();
                if (a.Status is not ("Scheduled" or "CheckedIn")) return Error("当前预约不能取消。",409);
                a.Status = "Cancelled"; break;
            case "start":
                if (!Clinical) return Forbid();
                if (a.Status != "CheckedIn" || a.Slot.Date != clock.Today) return Error("请先完成当天报到。",409);
                a.Status = "InProgress"; a.Encounter = new Encounter(); break;
            default: return NotFound();
        }
        a.Version++; Audit(new Dictionary<string,string> { ["checkin"] = "患者报到", ["cancel"] = "取消预约", ["start"] = "开始接诊" }[actionName], $"Appointment/{id}");
        await db.SaveChangesAsync(); return Ok(new { a.Id, a.Status, a.Version });
    }
    [HttpGet("appointments/{id:int}/encounter"), Authorize(Roles = "Admin,Doctor")]
    public async Task<IActionResult> GetEncounter(int id)
    {
        var a = await db.Appointments.Include(a => a.Patient).Include(a => a.Doctor).Include(a => a.Encounter).SingleOrDefaultAsync(a => a.Id == id);
        if (a is null || a.Encounter is null) return NotFound(); if (IsDoctor && a.DoctorId != UserId) return Forbid();
        Audit("查看就诊记录", $"Appointment/{id}"); await db.SaveChangesAsync();
        var e = a.Encounter;
        return Ok(new { appointment = AppointmentView(a), patient = PatientView(a.Patient), encounter = new { e.Id, e.Complaint, e.Assessment, e.Plan, e.FollowUpDate, e.Version, e.FinalizedUtc } });
    }
    [HttpPut("appointments/{id:int}/encounter"), Authorize(Roles = "Admin,Doctor")]
    public async Task<IActionResult> SaveEncounter(int id, EncounterRequest r, bool finalize = false)
    {
        var a = await db.Appointments.Include(a => a.Encounter).SingleOrDefaultAsync(a => a.Id == id); if (a?.Encounter is null) return NotFound();
        if (IsDoctor && a.DoctorId != UserId) return Forbid();
        var e = a.Encounter;
        if (a.Status != "InProgress" || e.FinalizedUtc != null || e.Version != r.Version) return Error("记录已完成或已被更新，请刷新。",409);
        if (r.FollowUpDate is not null && r.FollowUpDate.Value.Date <= clock.Today) return Error("随访日期应晚于今天。");
        e.Complaint = r.Complaint.Trim(); e.Assessment = r.Assessment.Trim(); e.Plan = r.Plan.Trim(); e.FollowUpDate = r.FollowUpDate?.Date; e.UpdatedUtc = DateTime.UtcNow; e.Version++;
        if (finalize) { e.FinalizedUtc = DateTime.UtcNow; a.Status = "Completed"; a.Version++; a.Invoice = new Invoice { AmountCents = config.GetValue<int>("Clinic:ConsultationFeeCents") }; }
        Audit(finalize ? "完成就诊并生成账单" : "保存病历草稿", $"Appointment/{id}"); await db.SaveChangesAsync();
        return Ok(new { e.Version, finalized = finalize });
    }
    [HttpGet("invoices"), Authorize(Roles = "Admin,Reception")]
    public async Task<object> Invoices(string? status, int page = 1)
    {
        page = Math.Max(1,page);
        var query = db.Invoices.AsNoTracking().Where(i => string.IsNullOrEmpty(status) || i.Status == status);
        return new { total = await query.CountAsync(), page, pageSize = 10, items = await query.OrderByDescending(i => i.Id).Skip((page-1)*10).Take(10).Select(i => new { i.Id, i.AppointmentId, patientName = i.Appointment.Patient.Name, doctorName = i.Appointment.Doctor.Name, i.AmountCents, i.Status, i.PaymentMethod, i.PaidUtc, i.CreatedUtc, i.Version }).ToListAsync() };
    }
    [HttpPost("invoices/{id:int}/pay"), Authorize(Roles = "Admin,Reception")]
    public async Task<IActionResult> Pay(int id, PaymentRequest r)
    {
        var invoice = await db.Invoices.FindAsync(id); if (invoice is null) return NotFound();
        if (invoice.Status != "Unpaid" || invoice.Version != r.Version) return Error("账单已收款或已更新，请刷新。",409);
        invoice.Status = "Paid"; invoice.PaymentMethod = r.Method; invoice.PaidUtc = DateTime.UtcNow; invoice.Version++;
        Audit("登记收款", $"Invoice/{id}"); await db.SaveChangesAsync(); return Ok(new { invoice.Id, invoice.Status });
    }
    [HttpGet("followups"), Authorize(Roles = "Admin,Doctor")]
    public async Task<object> Followups() => await db.Encounters.AsNoTracking().Where(e => e.FinalizedUtc != null && e.FollowUpDate >= clock.Today && e.FollowUpDate <= clock.Today.AddDays(30) && (!IsDoctor || e.Appointment.DoctorId == UserId)).OrderBy(e => e.FollowUpDate).Take(100).Select(e => new { e.AppointmentId, e.Appointment.PatientId, patientName = e.Appointment.Patient.Name, e.Appointment.Patient.Phone, doctorName = e.Appointment.Doctor.Name, e.FollowUpDate }).ToListAsync();
    [HttpGet("staff"), Authorize(Roles = "Admin")]
    public async Task<object> StaffList() => await db.Staff.Select(s => new { s.Id, s.Username, s.Name, s.Role, s.Department }).ToListAsync();
    [HttpPost("staff"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateStaff(StaffRequest r)
    {
        var s = new Staff { Username = r.Username.ToLower(), Name = r.Name.Trim(), Role = r.Role, Department = r.Department.Trim() }; s.PasswordHash = hasher.HashPassword(s, r.Password);
        db.Staff.Add(s); Audit("创建员工账号", $"Staff/{s.Username}"); await db.SaveChangesAsync(); return Ok(new { s.Id });
    }
    [HttpGet("audit"), Authorize(Roles = "Admin")]
    public async Task<object> AuditList(int page = 1)
    {
        page = Math.Max(1,page); return new { total = await db.Audit.CountAsync(), page, pageSize = 15, items = await db.Audit.AsNoTracking().OrderByDescending(a => a.Id).Skip((page-1)*15).Take(15).ToListAsync() };
    }
}
