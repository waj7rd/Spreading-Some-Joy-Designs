using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SpreadingJoy;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Imaging;
using SpreadingJoy.DAL.Repositories;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Licensing;
using SpreadingJoy.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// The designer is anonymous, so an in-progress design has nowhere to live but
// the session. Deliberately short-lived and not persisted: a visitor who
// wanders off shouldn't leave a half-made design in the database.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.Name = "SpreadingJoy.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    // Marked essential so it survives a visitor declining non-essential
    // cookies — without it the designer silently forgets every image.
    options.Cookie.IsEssential = true;
});

// EF Core DbContext, pointed at the SpreadingJoy database using the connection
// string named "SpreadingJoyContext" in appsettings.json.
builder.Services.AddDbContext<SpreadingJoyContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SpreadingJoyContext")));

// Cookie auth for staff sign-in. No ASP.NET Core Identity — just the built-in
// cookie handler plus our own Users table and PasswordHasher.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";

        // Signed in but not permitted is a different situation from not signed
        // in — bouncing to the login form for it just confuses people.
        options.AccessDeniedPath = "/Account/Denied";

        // Roughly a shift, sliding while somebody is working. The studio machine
        // is shared, so this is the backstop for "nobody clicked Log Out" rather
        // than the primary protection.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        options.Cookie.Name = "SpreadingJoy.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

// How the studio runs, and what it has paid for — both read from the Studios
// row rather than from configuration, so the studio can change its own hours and
// capacity from a screen. Cached; the settings screen calls Reload after saving.
builder.Services.AddSingleton<IStudioContext, StudioContextProvider>();
builder.Services.AddSingleton<IStudioSettings, StudioSettingsFromContext>();
builder.Services.AddSingleton<IFeatureFlags, FeatureFlagsFromContext>();

builder.Services.AddSingleton<IAuthorizationHandler, FeatureAuthorizationHandler>();

// Who may do what. Declared as policies so the rule lives here rather than as
// role-name strings sprinkled through the controllers.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.ManageStaff, p => p.RequireRole(Roles.Admin));
    options.AddPolicy(Policies.ManageCustomers, p => p.RequireRole(Roles.Admin, Roles.Manager));
    options.AddPolicy(Policies.ViewCustomers, p => p.RequireRole(Roles.Admin, Roles.Manager, Roles.Associate));
    options.AddPolicy(Policies.ManageOrders, p => p.RequireRole(Roles.Admin, Roles.Manager, Roles.Associate));
    options.AddPolicy(Policies.ManageCatalog, p => p.RequireRole(Roles.Admin, Roles.Manager));
    options.AddPolicy(Policies.ManageStudioSettings, p => p.RequireRole(Roles.Admin, Roles.Manager));

    // Narrower than ManageOrders on purpose: an associate can print a job
    // without being the person who decides a picture is legal to print.
    options.AddPolicy(Policies.ModerateArtwork, p => p.RequireRole(Roles.Admin, Roles.Manager));

    // Tier-gated areas. These combine a role check with a licensing check, so a
    // feature the studio hasn't bought is unreachable rather than merely hidden.
    // Nothing uses these yet — they're the socket the paid tiers plug into.
    options.AddPolicy(FeaturePolicies.BulkDiscounts, p => p
        .RequireRole(Roles.Admin, Roles.Manager)
        .AddRequirements(new FeatureRequirement(Feature.BulkDiscounts)));

    options.AddPolicy(FeaturePolicies.OrderExports, p => p
        .RequireRole(Roles.Admin, Roles.Manager)
        .AddRequirements(new FeatureRequirement(Feature.OrderExports)));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // The public order form is an anonymous POST that writes to the database,
    // so it needs a ceiling. Per-IP: a real person orders once, thinks about it,
    // maybe orders again.
    options.AddPolicy(RateLimitPolicies.PublicOrdering, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    // Sign-in gets one too. UserLogic locks a known account after five bad
    // passwords, but that does nothing about someone spraying many different
    // addresses — each one is a first failure. This covers that.
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    // Tightest of the three. Every call here makes our server fetch a URL the
    // caller chose — bandwidth we pay for, and the exact operation someone
    // wants to run in a loop while mapping an internal network. Twenty images
    // is a generous design session; a thousand is a scan.
    options.AddPolicy(RateLimitPolicies.ArtworkFetch, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));
});

// Repositories: bind each interface to its implementation.
// Scoped = one instance per web request.
builder.Services.AddScoped<IStudioRepository, StudioRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IArtworkRepository, ArtworkRepository>();
builder.Services.AddScoped<IDesignRepository, DesignRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderRequestRepository, OrderRequestRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();

// One transaction per business operation. See IUnitOfWork.
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Studio clock. Due dates are wall-clock dates where the press is, so the logic
// layer needs the studio's timezone rather than the server's — hosted on a UTC
// machine, DateTime.Now would reject same-day orders as being in the past. A bad
// timezone id fails loudly at startup, which beats silently wrong dates.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStudioClock, StudioClockFromContext>();

// Artwork pipeline.
//
// The HttpClient is configured to NOT follow redirects: HttpImageFetcher walks
// them by hand so it can re-check the destination of every hop. Left to the
// handler, a public URL that 302s to an internal address would sail straight
// through the address check.
builder.Services.AddHttpClient(HttpImageFetcher.HttpClientName, client =>
    {
        // Identify ourselves honestly. HttpClient sends no User-Agent at all by
        // default, and a good number of image hosts — Wikimedia among them —
        // answer 403 to anything anonymous. Customers would read that as "your
        // site is broken", because from where they're standing it is.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SpreadingSomeJoyDesigns/1.0 (+artwork fetch on behalf of a customer)");

        // Ask for images. Servers that content-negotiate will hand back the
        // picture rather than an HTML wrapper around it.
        client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Redirects are followed by hand in HttpImageFetcher so the destination
        // of every hop is re-checked. Left to the handler, a public URL that
        // 302s to an internal address sails straight through the address check.
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

builder.Services.AddScoped<IImageFetcher, HttpImageFetcher>();
builder.Services.AddSingleton<IImageInspector, ImageSharpInspector>();

builder.Services.AddSingleton<IImageStore>(sp =>
{
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var configured = builder.Configuration["Artwork:StoragePath"] ?? "App_Data/artwork";

    var root = Path.IsPathRooted(configured)
        ? configured
        : Path.Combine(environment.ContentRootPath, configured);

    return new FileSystemImageStore(root);
});

// Logic layer (business rules) — bind interface to implementation.
builder.Services.AddScoped<IStudioLogic, StudioLogic>();
builder.Services.AddScoped<IProductLogic, ProductLogic>();
builder.Services.AddScoped<IArtworkLogic, ArtworkLogic>();
builder.Services.AddScoped<IDesignLogic, DesignLogic>();
builder.Services.AddScoped<IOrderLogic, OrderLogic>();
builder.Services.AddScoped<IOrderRequestLogic, OrderRequestLogic>();
builder.Services.AddScoped<IUserLogic, UserLogic>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

// Before authentication so the designer's session is available to anonymous
// visitors, which is the only kind it has.
app.UseSession();

// Must come before UseAuthorization — authorization checks read the identity
// authentication sets up here.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();


// Top-level statements compile to an internal Program class. Test projects that
// spin the real application up through WebApplicationFactory<Program> need to
// see it.
public partial class Program { }
