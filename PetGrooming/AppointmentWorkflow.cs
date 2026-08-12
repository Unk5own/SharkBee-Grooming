namespace PetGrooming;

// The appointment status rules, in one place.
//
// Both the staff board and the member's cancel action go through here, so the
// rules cannot drift apart between controllers. Every transition attempt is
// checked server side -- the buttons a view happens to render are never the
// only thing standing between a member and an illegal state change.
public static class AppointmentWorkflow
{
    // What each status is allowed to become.
    private static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> Allowed = new()
    {
        [AppointmentStatus.Pending] =
        [
            AppointmentStatus.Confirmed,
            AppointmentStatus.Cancelled,
        ],
        [AppointmentStatus.Confirmed] =
        [
            AppointmentStatus.CheckedIn,
            AppointmentStatus.Cancelled,
            AppointmentStatus.NoShow,
        ],
        [AppointmentStatus.CheckedIn] =
        [
            AppointmentStatus.InProgress,
            AppointmentStatus.Cancelled,
            AppointmentStatus.NoShow,
        ],
        [AppointmentStatus.InProgress] =
        [
            AppointmentStatus.Completed,
        ],

        // Terminal states.
        [AppointmentStatus.Completed] = [],
        [AppointmentStatus.Cancelled] = [],
        [AppointmentStatus.NoShow] = [],
    };

    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to)
    {
        return Allowed.TryGetValue(from, out var next) && next.Contains(to);
    }

    public static AppointmentStatus[] NextStatuses(AppointmentStatus from)
    {
        return Allowed.TryGetValue(from, out var next) ? next : [];
    }

    public static bool IsTerminal(AppointmentStatus status)
    {
        return NextStatuses(status).Length == 0;
    }

    // A slot is only genuinely free when nothing but a cancelled or no-show
    // booking sits on it. Used by availability checks.
    public static bool ReleasesSlot(AppointmentStatus status)
    {
        return status is AppointmentStatus.Cancelled;
    }

    // The wording staff see on the action button.
    public static string ActionLabel(AppointmentStatus to) => to switch
    {
        AppointmentStatus.Confirmed => "Confirm",
        AppointmentStatus.CheckedIn => "Check In",
        AppointmentStatus.InProgress => "Start Grooming",
        AppointmentStatus.Completed => "Mark Completed",
        AppointmentStatus.Cancelled => "Cancel",
        AppointmentStatus.NoShow => "Mark No-Show",
        _ => to.ToString(),
    };

    // The audit trail remark recorded for each transition.
    public static string DefaultRemark(AppointmentStatus to) => to switch
    {
        AppointmentStatus.Confirmed => "Booking confirmed",
        AppointmentStatus.CheckedIn => "Pet checked in",
        AppointmentStatus.InProgress => "Grooming started",
        AppointmentStatus.Completed => "Grooming completed",
        AppointmentStatus.Cancelled => "Cancelled by staff",
        AppointmentStatus.NoShow => "Member did not arrive",
        _ => "",
    };
}
