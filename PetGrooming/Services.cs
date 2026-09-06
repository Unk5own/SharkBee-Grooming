using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PetGrooming;

// Student 1 support services. These use application memory so no extra database
// migration is required for the optional security features.
public class LoginSecurityService
{
    private sealed class AttemptState
    {
        public int FailedCount { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
    }

    private readonly ConcurrentDictionary<string, AttemptState> attempts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 3;

    public bool IsTemporarilyBlocked(string email, out DateTimeOffset until)
    {
        until = default;
        if (!attempts.TryGetValue(email, out var state)) return false;

        if (state.BlockedUntil is { } blockedUntil)
        {
            if (blockedUntil > DateTimeOffset.UtcNow)
            {
                until = blockedUntil;
                return true;
            }

            attempts.TryRemove(email, out _);
        }

        return false;
    }

    public int RecordFailure(string email, out DateTimeOffset? blockedUntil)
    {
        var state = attempts.GetOrAdd(email, _ => new AttemptState());
        state.FailedCount++;

        if (state.FailedCount >= MaxAttempts)
        {
            state.BlockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
            blockedUntil = state.BlockedUntil;
        }
        else
        {
            blockedUntil = null;
        }

        return state.FailedCount;
    }

    public void Clear(string email) => attempts.TryRemove(email, out _);
}

public class CaptchaService
{
    public const string SessionKey = "Student1.LoginCaptcha";

    public string Generate(HttpContext context)
    {
        int a = RandomNumberGenerator.GetInt32(1, 10);
        int b = RandomNumberGenerator.GetInt32(1, 10);
        context.Session.SetInt32(SessionKey, a + b);
        return $"{a} + {b} = ?";
    }

    public bool Validate(HttpContext context, int? answer)
    {
        int? expected = context.Session.GetInt32(SessionKey);
        context.Session.Remove(SessionKey);
        return expected.HasValue && answer.HasValue && expected.Value == answer.Value;
    }
}

public record EmailVerificationTicket(string Email, DateTimeOffset ExpiresAt);

// One-time password-reset token. Consumed on first use so a leaked/forwarded
// link cannot be replayed after the password has already been changed.
public record PasswordResetTicket(string Email, DateTimeOffset ExpiresAt);
