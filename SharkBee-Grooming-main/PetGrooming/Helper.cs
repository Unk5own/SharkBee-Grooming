using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Rendering;
using QuestPDF.Fluent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PetGrooming;

public class Helper(IWebHostEnvironment en,
                    IHttpContextAccessor ct,
                    IConfiguration cf)
{
    // ------------------------------------------------------------------------
    // Photo Upload Helper Functions
    // ------------------------------------------------------------------------

    public string ValidatePhoto(IFormFile f)
    {
        var reType = new Regex(@"^image\/(jpeg|png)$", RegexOptions.IgnoreCase);
        var reName = new Regex(@"^.+\.(jpeg|jpg|png)$", RegexOptions.IgnoreCase);

        if (!reType.IsMatch(f.ContentType) || !reName.IsMatch(f.FileName))
        {
            return "Only JPG and PNG photo is allowed.";
        }
        else if (f.Length > 1 * 1024 * 1024)
        {
            return "Photo size cannot more than 1MB.";
        }

        return "";
    }

    public string SavePhoto(IFormFile f, string folder)
    {
        return SavePhoto(f, folder, 200, 200);
    }

    // Overload: some photos (pet gallery, grooming before/after) need a larger size
    // than the 200x200 avatar default.
    public string SavePhoto(IFormFile f, string folder, int width, int height)
    {
        var file = Guid.NewGuid().ToString("n") + ".jpg";
        var dir = Path.Combine(en.WebRootPath, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, file);

        var options = new ResizeOptions
        {
            Size = new(width, height),
            Mode = ResizeMode.Crop,
        };

        using var stream = f.OpenReadStream();
        using var img = Image.Load(stream);
        img.Mutate(x => x.Resize(options));
        img.Save(path);

        return file;
    }

    public void DeletePhoto(string file, string folder)
    {
        file = Path.GetFileName(file);
        var path = Path.Combine(en.WebRootPath, folder, file);
        if (File.Exists(path)) File.Delete(path);
    }



    // ------------------------------------------------------------------------
    // Security Helper Functions
    // ------------------------------------------------------------------------

    // Password hashing is implemented directly with PBKDF2. This keeps the project
    // independent of ASP.NET Core Identity, as required by the assignment.
    private const int PasswordIterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, PasswordIterations, HashAlgorithmName.SHA256, HashSize);

        return $"PBKDF2-SHA256${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string storedHash, string password)
    {
        try
        {
            string[] parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256") return false;

            int iterations = int.Parse(parts[1]);
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public async Task SignInAsync(string email, string role, bool rememberMe)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Role, role),
        ];

        ClaimsIdentity identity = new(claims, "Cookies");

        ClaimsPrincipal principal = new(identity);

        AuthenticationProperties properties = new()
        {
            IsPersistent = rememberMe,
        };

        await ct.HttpContext!.SignInAsync("Cookies", principal, properties);
    }

    public async Task SignOutAsync()
    {
        await ct.HttpContext!.SignOutAsync("Cookies");
    }

    public string RandomPassword()
    {
        string s = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string password = "";

        Random r = new();

        for (int i = 1; i <= 10; i++)
        {
            password += s[r.Next(s.Length)];
        }

        return password;
    }



    // ------------------------------------------------------------------------
    // Email Helper Functions
    // ------------------------------------------------------------------------

    public void SendEmail(MailMessage mail)
    {
        string user = cf["Smtp:User"] ?? "";
        string pass = cf["Smtp:Pass"] ?? "";
        string name = cf["Smtp:Name"] ?? "";
        string host = cf["Smtp:Host"] ?? "";
        int port = cf.GetValue<int>("Smtp:Port");

        mail.From = new MailAddress(user, name);

        using var smtp = new SmtpClient
        {
            Host = host,
            Port = port,
            EnableSsl = true,
            Credentials = new NetworkCredential(user, pass),
        };

        smtp.Send(mail);
    }



    // ------------------------------------------------------------------------
    // DateTime Helper Functions
    // ------------------------------------------------------------------------

    // Return January (1) to December (12)
    public SelectList GetMonthList()
    {
        var list = new List<object>();

        for (int n = 1; n <= 12; n++)
        {
            list.Add(new
            {
                Id = n,
                Name = new DateTime(1, n, 1).ToString("MMMM"),
            });
        }

        return new SelectList(list, "Id", "Name");
    }

    // Return min to max years
    public SelectList GetYearList(int min, int max, bool reverse = false)
    {
        var list = new List<int>();

        for (int n = min; n <= max; n++)
        {
            list.Add(n);
        }

        if (reverse) list.Reverse();

        return new SelectList(list);
    }



    // ------------------------------------------------------------------------
    // Booking Cart Helper Functions
    // ------------------------------------------------------------------------
    // Unlike the shopping cart in the practicals (a Dictionary of product id to
    // quantity), an appointment cart holds a list of distinct bookings, because
    // the same service can be booked twice for two pets at two different times.

    public List<BookingCartItem> GetCart()
    {
        return ct.HttpContext!.Session.Get<List<BookingCartItem>>("Cart") ?? [];
    }

    public void SetCart(List<BookingCartItem>? list = null)
    {
        if (list == null || list.Count == 0)
        {
            ct.HttpContext!.Session.Remove("Cart");
        }
        else
        {
            ct.HttpContext!.Session.Set("Cart", list);
        }
    }



    // ------------------------------------------------------------------------
    // Appointment Helper Functions
    // ------------------------------------------------------------------------

    // Human-readable booking reference, e.g. PG-20260812-4F7A
    public string NextBookingRef()
    {
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpper();
        return $"PG-{DateTime.Today:yyyyMMdd}-{suffix}";
    }



    // ------------------------------------------------------------------------
    // E-Receipt Helper Functions
    // ------------------------------------------------------------------------

    // The appointment must already have Items (with Pet, Service and Staff),
    // Payments and Member loaded.
    public byte[] GenerateReceipt(Appointment appointment)
    {
        return new ReceiptDocument(appointment).GeneratePdf();
    }

    public string ReceiptFileName(Appointment appointment)
    {
        return $"Receipt-{appointment.BookingRef}.pdf";
    }

    // True when real SMTP credentials have been configured. The committed
    // appsettings.json ships placeholders, so a developer who has not set up a
    // Gmail app password does not get a failed send on every booking.
    public bool IsEmailConfigured()
    {
        var user = cf["Smtp:User"] ?? "";
        var pass = cf["Smtp:Pass"] ?? "";

        return user.Contains('@')
            && !user.StartsWith("your.account")
            && !pass.StartsWith("xxxx");
    }

    // Emails the e-receipt as a PDF attachment. Returns an empty string on
    // success, otherwise the reason it did not send -- a booking must never fail
    // just because the mail server is unreachable.
    public string EmailReceipt(Appointment appointment, byte[] pdf)
    {
        if (!IsEmailConfigured())
        {
            return "Email is not configured, so the receipt was not sent.";
        }

        try
        {
            var mail = new MailMessage
            {
                Subject = $"Your SharkBee Grooming booking {appointment.BookingRef}",
                IsBodyHtml = true,
                Body = $@"
                    <p>Hi {appointment.Member?.Name ?? "there"},</p>
                    <p>
                        Your booking <b>{appointment.BookingRef}</b> is confirmed.
                        The e-receipt is attached to this email.
                    </p>
                    <p>
                        Total: <b>RM {appointment.Total:N2}</b><br>
                        First appointment:
                        <b>{appointment.Items.Min(i => i.SlotStart):ddd, d MMM yyyy h:mm tt}</b>
                    </p>
                    <p>Please arrive about ten minutes early so we can check your pet in.</p>
                    <p>&mdash; SharkBee Grooming</p>",
            };

            mail.To.Add(new MailAddress(appointment.MemberEmail));

            var stream = new MemoryStream(pdf);
            mail.Attachments.Add(new Attachment(stream, ReceiptFileName(appointment),
                                                "application/pdf"));

            SendEmail(mail);
            return "";
        }
        catch (Exception ex)
        {
            return $"The receipt email could not be sent ({ex.Message}).";
        }
    }
}
