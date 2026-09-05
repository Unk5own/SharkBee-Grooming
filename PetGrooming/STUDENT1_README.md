# Student 1 — Access & User Management

This is the integrated Student 1 implementation for the existing SharkBee Grooming project.
The original project structure and the other students' controllers/models are preserved.

## Core requirements implemented

### 1. Manual cookie authentication
- Admin, Staff (Groomer), and Member (Pet Owner) accounts.
- ASP.NET Core Identity is NOT used.
- Login uses the existing ASP.NET Core cookie authentication middleware.
- Passwords are stored as PBKDF2-SHA256 salted hashes using `System.Security.Cryptography`.
- `Remember me` creates a persistent authentication cookie.
- `returnUrl` is restricted to local URLs to avoid open redirects.
- Blocked accounts cannot log in.
- Logout is implemented through `/Account/Logout`.

### 2. User Maintenance
Admin-only route:
- `/Admin/Users`

Functions:
- Read/list users
- Search by email/name
- Filter by Admin/Staff/Member
- Create Admin, Staff, or Member
- Edit account details
- Reset password
- Block/unblock accounts

The project's database intentionally preserves account records because they can be referenced by appointment history. Therefore the assignment's Delete operation is implemented as a safe soft-delete (Block) rather than physical deletion.

## Optional features included

### Temporary login blocking
- 3 failed password attempts trigger a 10-minute temporary lockout.
- The counter is cleared after a successful login.
- This state is intentionally kept in application memory, so no database migration is needed.

### CAPTCHA
- Login and registration use a simple server-generated arithmetic CAPTCHA.
- The answer is stored in Session and consumed after one attempt.

### Email verification
- Database field `EmailVerified` and migration are included.
- Set `Security:RequireEmailVerification` to `true` to require verification.
- Configure SMTP credentials in `appsettings.Development.json` (never commit real credentials).
- Verification tokens expire after 24 hours.
- For local demo, the setting is `false` by default so the project works without SMTP.

### Webcam profile photo
- Member registration supports either JPG/PNG upload or browser webcam capture.
- Camera access requires browser permission and normally HTTPS/localhost.
- Captured JPEG is saved using the existing image helper.

## Demo accounts
All seeded accounts use:

`abc123`

Admin:
- `admin@pawfect.com`
- `manager@pawfect.com`

Staff/Groomer:
- `aisyah@pawfect.com`
- `kumar@pawfect.com`
- `meiling@pawfect.com`
- `daniel@pawfect.com`

Member:
- `chloe@gmail.com`
- `arif@gmail.com`
- `priya@gmail.com`
- `jason@gmail.com`
- `sarah@gmail.com`
- `wei@gmail.com`

## How to run

1. Open `PetGrooming.slnx` in Visual Studio 2026 or another .NET 10 compatible IDE.
2. Make sure SQL Server LocalDB is installed and running.
3. Build the solution.
4. Run the project with HTTPS.
5. On first run, the existing EF Core migrations create the database and seed demo users.
6. Test `/Account/Login`.
7. Log in as Admin and open `/Admin/Users`.
8. Test Create, Edit, Block, and Unblock.

## Important integration note

Do not remove the existing Student 2/Student 3 controllers or their database entities. Student 1 uses the shared `User`, `Admin`, `Staff`, and `Member` model and the existing cookie authorization roles.

The development-only `DevSeedController` is retained because other team members may use it for local testing. It is not required by the Student 1 end-user flow and is not shown in the normal navigation.
