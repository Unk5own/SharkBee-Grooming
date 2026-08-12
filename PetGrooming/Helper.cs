using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
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

    private readonly PasswordHasher<object> ph = new();

    public string HashPassword(string password)
    {
        return ph.HashPassword(0, password);
    }

    public bool VerifyPassword(string hash, string password)
    {
        return ph.VerifyHashedPassword(0, hash, password)
               == PasswordVerificationResult.Success;
    }

    public void SignIn(string email, string role, bool rememberMe)
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

        ct.HttpContext!.SignInAsync(principal, properties);
    }

    public void SignOut()
    {
        ct.HttpContext!.SignOutAsync();
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
}
