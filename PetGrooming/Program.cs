global using PetGrooming.Models;
global using PetGrooming;
global using Microsoft.EntityFrameworkCore;

// QuestPDF Community licence: free for individuals and for companies under
// USD 1M revenue, which covers this project. Must be set before any PDF is made.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
// NOTE: The practicals omit "Initial Catalog". Without it, EF cannot create the
// .mdf on first run -- SQL Server reports error 15350 (auto-named attach failed)
// instead of "database does not exist", so the migration aborts. Naming the
// catalog lets EF create the file. It is still a file-based SQL Server Express
// database as the assignment requires.
builder.Services.AddSqlServer<DB>($@"
    Data Source=(LocalDB)\MSSQLLocalDB;
    AttachDbFilename={builder.Environment.ContentRootPath}\DB.mdf;
    Initial Catalog=PetGroomingDB;
    Integrated Security=True;
    MultipleActiveResultSets=True;
");
builder.Services.AddScoped<Helper>();
builder.Services.AddAuthentication().AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession();

var app = builder.Build();

// Apply pending migrations and seed demo data on startup, so a fresh clone runs
// with a populated database without any manual steps.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DB>();
    var hp = scope.ServiceProvider.GetRequiredService<Helper>();
    db.Database.Migrate();
    Seeder.Seed(db, hp);
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization("en-MY");
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();
app.Run();
