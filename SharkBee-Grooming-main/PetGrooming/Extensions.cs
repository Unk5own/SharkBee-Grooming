using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using PetGrooming.Models;
using System.Text.Json;

namespace PetGrooming;

public static class Extensions
{
    // ------------------------------------------------------------------------
    // Slot Locking
    // ------------------------------------------------------------------------

    // Exclusive, transaction-scoped lock on an arbitrary key, released when the
    // transaction ends. Used by checkout and by rescheduling so that two writers
    // cannot both decide the same groomer slot is free.
    //
    // A SERIALIZABLE transaction is not enough on its own: both sessions take
    // shared range locks while reading, then both try to write, and SQL Server
    // resolves the deadlock by killing one (error 1205). An application lock
    // makes the second session wait instead.
    public static bool TryLockSlot(this DB db, string resource)
    {
        const string sql = @"
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {0},
                                         @LockMode = 'Exclusive',
                                         @LockOwner = 'Transaction',
                                         @LockTimeout = 5000;
            SELECT @result AS Value;";

        return db.Database.SqlQueryRaw<int>(sql, resource).AsEnumerable().First() >= 0;
    }

    // The lock key for one groomer at one moment.
    public static string SlotKey(string staffEmail, DateTime slotStart)
    {
        return $"slot:{staffEmail}:{slotStart:yyyyMMddHHmm}";
    }

    public static bool IsAjax(this HttpRequest request)
    {
        return request.Headers.XRequestedWith == "XMLHttpRequest";
    }

    public static bool IsValid(this ModelStateDictionary ms, string key)
    {
        return ms.GetFieldValidationState(key) == ModelValidationState.Valid;
    }



    // ------------------------------------------------------------------------
    // Date and Time Extension Methods
    // ------------------------------------------------------------------------

    public static DateOnly ToDateOnly(this DateTime dt)
    {
        return DateOnly.FromDateTime(dt);
    }

    public static TimeOnly ToTimeOnly(this DateTime dt)
    {
        return TimeOnly.FromDateTime(dt);
    }

    public static DateOnly Today(this DateOnly date)
    {
        return DateOnly.FromDateTime(DateTime.Today);
    }

    public static TimeOnly Now(this TimeOnly date)
    {
        return TimeOnly.FromDateTime(DateTime.Now);
    }

    // Round a time down to the nearest slot boundary (e.g. 09:47 -> 09:30 for 30 min slots)
    public static DateTime FloorToSlot(this DateTime dt, int minutes)
    {
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTime(dt.Ticks - (dt.Ticks % ticks), dt.Kind);
    }



    // ------------------------------------------------------------------------
    // Session Extension Methods
    // ------------------------------------------------------------------------

    public static void Set<T>(this ISession session, string key, T value)
    {
        session.SetString(key, JsonSerializer.Serialize(value));
    }

    public static T? Get<T>(this ISession session, string key)
    {
        var value = session.GetString(key);
        return value == null ? default : JsonSerializer.Deserialize<T>(value);
    }
}
