using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
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

// Data protection keys, persisted to disk.
//
// These keys encrypt the auth cookie, the session cookie, and every antiforgery
// token. Left unconfigured, ASP.NET Core keeps the keyring in the user profile —
// and under IIS shared hosting there frequently isn't one, so the ring is
// regenerated from scratch every time the app pool recycles.
//
// That failure does not announce itself at startup. It shows up later as staff
// being signed out at random and forms rejecting with "the antiforgery token
// could not be decrypted", which reads as the site being flaky rather than as a
// missing configuration line.
//
// SetApplicationName is the half people leave out. The application name is part
// of the key discriminator, so without it a redeploy to a different physical
// path ignores the keys sitting right there in the folder.
//
// Under App_Data for the same reason the artwork is: ContentRoot is not served,
// wwwroot is. Anyone who can read this folder can mint a staff auth cookie.
var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? "App_Data/keys";

var keyDirectory = new DirectoryInfo(Path.IsPathRooted(keyPath)
    ? keyPath
    : Path.Combine(builder.Environment.ContentRootPath, keyPath));

// Created here rather than lazily on first write, so a hosting account without
// write permission fails at startup where it is obvious, instead of at the first
// sign-in attempt where it looks like a broken password.
keyDirectory.Create();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keyDirectory)
    .SetApplicationName("SpreadingSomeJoyDesigns");

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

// EF Core DbContext.
//
// The connection string is deliberately absent from appsettings.json. That file
// is in source control, and a hosted database is reached with a SQL username and
// password rather than Windows auth — so the real one is a credential, and
// committing it publishes it to anyone who ever gets a copy of the repository.
//
// Local development reads it from appsettings.Development.json. The deployed
// site supplies it as an environment variable:
//
//     ConnectionStrings__SpreadingJoyContext
//
// Two underscores, not one — that is how ASP.NET maps a flat environment
// variable onto nested configuration. Most Windows hosts expose this in the
// control panel; failing that it goes in web.config under
// <aspNetCore><environmentVariables>.
//
// Checked here rather than left to EF, because a missing connection string
// otherwise surfaces as an exception page the first time somebody opens the
// shop — later than startup, and far less obvious about what's wrong.
var connectionString = builder.Configuration.GetConnectionString("SpreadingJoyContext");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No connection string named 'SpreadingJoyContext' was found. On the server, set the " +
        "environment variable ConnectionStrings__SpreadingJoyContext. When running locally, " +
        "add it to appsettings.Development.json.");
}

builder.Services.AddDbContext<SpreadingJoyContext>(options =>
    options.UseSqlServer(connectionString));

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

// Trusting X-Forwarded-* is opt-in, and off by default.
//
// It has to be on behind a reverse proxy or every request appears to come from
// the proxy — which puts every visitor in one rate-limit bucket and writes the
// proxy's address into every LoginAudit row, making the audit trail useless.
//
// It also must NOT be on when the app is directly reachable: the header is
// client-supplied, so trusting it lets anyone forge their own IP and walk
// straight past the login and artwork-fetch limits. Off is the safe default;
// turning it on is a deployment decision, and the known proxy addresses have to
// be listed or ASP.NET won't honour it anyway.
var forwardedHeaders = builder.Configuration.GetSection("ForwardedHeaders");

if (forwardedHeaders.GetValue<bool>("Enabled"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxy in forwardedHeaders.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var address))
                options.KnownProxies.Add(address);
        }

        // How many proxies sit in front. Default 1; a CDN in front of a load
        // balancer is 2. Too high and a client can inject extra hops.
        options.ForwardLimit = forwardedHeaders.GetValue<int?>("ForwardLimit") ?? 1;
    });
}

var app = builder.Build();

if (forwardedHeaders.GetValue<bool>("Enabled"))
    app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Security headers on every response, static files included.
//
// The one that earns its place here is nosniff. This application serves bytes
// supplied by strangers, from its own origin, at /Artwork/File. Images are
// decoded and re-encoded before storage so a polyglot doesn't survive — but
// without nosniff a browser is still free to ignore our Content-Type and sniff
// a crafted file as HTML, and that is stored XSS on the same origin as the
// staff session cookie.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;

    headers["X-Content-Type-Options"] = "nosniff";

    // The artwork queue's approve and reject buttons are worth clickjacking.
    headers["X-Frame-Options"] = "DENY";

    // Artwork URLs shouldn't travel to third parties in a Referer header.
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

    // style-src allows 'unsafe-inline' because artwork placement is expressed as
    // style attributes computed per request — the percentages that put an image
    // on the shirt. Scripts get no such allowance, which is the half that
    // matters; every inline handler was moved into site.js to keep it that way.
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    await next();
});

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
