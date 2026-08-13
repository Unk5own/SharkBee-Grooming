using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PetGrooming.Controllers;

// Management reporting. Every figure comes from AppointmentItem.UnitPrice, the
// price snapshotted at booking time, so changing a service price today never
// rewrites last month's revenue.
[Authorize(Roles = "Admin")]
public class ReportController(DB db) : Controller
{
    private const int MonthsBack = 12;

    // GET: Report/Dashboard
    public IActionResult Dashboard()
    {
        ViewBag.Title = "Reports";
        return View(BuildDashboard());
    }

    // GET: Report/Data
    // The charts fetch their numbers from here, which keeps the view free of
    // embedded data and makes the figures inspectable on their own.
    public IActionResult Data()
    {
        return Json(BuildDashboard());
    }



    // ------------------------------------------------------------------------
    // Aggregation
    // ------------------------------------------------------------------------

    private DashboardVM BuildDashboard()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var windowStart = monthStart.AddMonths(-(MonthsBack - 1));

        var vm = new DashboardVM
        {
            RevenueThisMonth = db.AppointmentItems
                                 .Where(i => i.ItemStatus == AppointmentStatus.Completed
                                          && i.SlotStart >= monthStart)
                                 .Sum(i => (decimal?)i.UnitPrice) ?? 0m,

            AppointmentsThisMonth = db.AppointmentItems
                                      .Count(i => i.SlotStart >= monthStart
                                               && i.SlotStart < monthStart.AddMonths(1)),

            CompletedThisMonth = db.AppointmentItems
                                   .Count(i => i.ItemStatus == AppointmentStatus.Completed
                                            && i.SlotStart >= monthStart
                                            && i.SlotStart < monthStart.AddMonths(1)),
        };

        var settled = db.AppointmentItems.Count(i => i.SlotStart >= windowStart
                                                  && i.ItemStatus != AppointmentStatus.Pending
                                                  && i.ItemStatus != AppointmentStatus.Confirmed);

        var lost = db.AppointmentItems.Count(i => i.SlotStart >= windowStart
                                               && (i.ItemStatus == AppointmentStatus.Cancelled
                                                || i.ItemStatus == AppointmentStatus.NoShow));

        vm.CancellationRate = settled == 0 ? 0m : Math.Round(lost * 100m / settled, 1);

        vm.MonthlyRevenue = MonthlyRevenue(windowStart);
        vm.PopularServices = PopularServices(windowStart);
        vm.GroomerUtilisation = GroomerUtilisation(today);
        vm.PeakHours = PeakHours(windowStart);

        return vm;
    }

    private ChartSeriesVM MonthlyRevenue(DateTime from)
    {
        var rows = db.AppointmentItems
                     .Where(i => i.ItemStatus == AppointmentStatus.Completed && i.SlotStart >= from)
                     .GroupBy(i => new { i.SlotStart.Year, i.SlotStart.Month })
                     .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.UnitPrice) })
                     .ToList();

        var series = new ChartSeriesVM();

        // Walk every month in the window so a month with no revenue shows as a
        // gap in the trend rather than silently disappearing.
        for (int n = 0; n < MonthsBack; n++)
        {
            var month = from.AddMonths(n);
            var hit = rows.FirstOrDefault(r => r.Year == month.Year && r.Month == month.Month);

            series.Labels.Add(month.ToString("MMM yy"));
            series.Values.Add(hit?.Total ?? 0m);
        }

        return series;
    }

    private ChartSeriesVM PopularServices(DateTime from)
    {
        var rows = db.AppointmentItems
                     .Where(i => i.SlotStart >= from && i.ItemStatus == AppointmentStatus.Completed)
                     .GroupBy(i => i.Service.Name)
                     .Select(g => new { Name = g.Key, Count = g.Count() })
                     .OrderByDescending(x => x.Count)
                     .Take(8)
                     .ToList();

        var series = new ChartSeriesVM();

        foreach (var r in rows)
        {
            series.Labels.Add(r.Name);
            series.Values.Add(r.Count);
        }

        return series;
    }

    // Booked minutes as a percentage of rostered minutes over the last 30 days.
    private ChartSeriesVM GroomerUtilisation(DateTime today)
    {
        var from = today.AddDays(-30);

        var groomers = db.Staffs.Where(s => s.Active).OrderBy(s => s.Name).ToList();
        var schedules = db.StaffSchedules.ToList();
        var timeOffs = db.StaffTimeOffs.ToList();

        var booked = db.AppointmentItems
                       .Where(i => i.SlotStart >= from && i.SlotStart < today
                                && i.ItemStatus == AppointmentStatus.Completed)
                       .GroupBy(i => i.StaffEmail)
                       .Select(g => new
                       {
                           Email = g.Key,
                           Minutes = g.Sum(x => EF.Functions.DateDiffMinute(x.SlotStart, x.SlotEnd)),
                       })
                       .ToList();

        var series = new ChartSeriesVM();

        foreach (var g in groomers)
        {
            double rostered = 0;

            for (var day = from; day < today; day = day.AddDays(1))
            {
                var date = DateOnly.FromDateTime(day);

                if (timeOffs.Any(t => t.StaffEmail == g.Email
                                   && date >= t.StartDate && date <= t.EndDate))
                {
                    continue;
                }

                var shift = schedules.FirstOrDefault(s => s.StaffEmail == g.Email
                                                       && s.Day == day.DayOfWeek);

                if (shift != null) rostered += (shift.EndTime - shift.StartTime).TotalMinutes;
            }

            var used = booked.FirstOrDefault(b => b.Email == g.Email)?.Minutes ?? 0;

            series.Labels.Add(g.Name);
            series.Values.Add(rostered <= 0 ? 0m : Math.Round((decimal)(used / rostered * 100), 1));
        }

        return series;
    }

    private ChartSeriesVM PeakHours(DateTime from)
    {
        var rows = db.AppointmentItems
                     .Where(i => i.SlotStart >= from && i.ItemStatus != AppointmentStatus.Cancelled)
                     .GroupBy(i => i.SlotStart.Hour)
                     .Select(g => new { Hour = g.Key, Count = g.Count() })
                     .ToList();

        var series = new ChartSeriesVM();

        var first = rows.Count > 0 ? rows.Min(r => r.Hour) : 9;
        var last = rows.Count > 0 ? rows.Max(r => r.Hour) : 18;

        for (int h = first; h <= last; h++)
        {
            series.Labels.Add(new DateTime(1, 1, 1, h, 0, 0).ToString("h tt"));
            series.Values.Add(rows.FirstOrDefault(r => r.Hour == h)?.Count ?? 0);
        }

        return series;
    }
}
