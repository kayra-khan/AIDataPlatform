using Azure.Identity;
using AIDataPlatform.Client.Pages;
using AIDataPlatform.Components;
using AIDataPlatform.Components.Account;
using AIDataPlatform.Data;
using AIDataPlatform.Plugins;
using AIDataPlatform.Services;
using AIDataPlatform.Services.AI;
using AIDataPlatform.Services.Communication;
using AIDataPlatform.Services.Database;
using AIDataPlatform.Services.DocumentIntelligence;
using AIDataPlatform.Services.File;
using AIDataPlatform.Services.Process;
using AIDataPlatform.Services.Search;
using AIDataPlatform.Services.Storage;
using AIDataPlatform.Services.Tenant;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Blazor;
using MudBlazor.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

// centralized logging.
// it uses asp.net core built-in dependency injection (DI) and logging framework across the application
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var clientId = builder.Configuration["Azure:ClientId"];
var clientSecret = builder.Configuration["Azure:ClientSecret"];
var tenantId = builder.Configuration["Azure:TenantId"];

var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

var keyVaultEndpoint = new Uri("https://blazorserverocrvault.vault.azure.net/");
builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, credential);
//builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, PersistingRevalidatingAuthenticationStateProvider>();

// Scoped Services (one instance per request)
builder.Services.AddScoped<BlobStorageService>();
builder.Services.AddScoped<CosmosDbService>();
builder.Services.AddScoped<DocumentIntelligenceService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<KernelMemoryService>();
builder.Services.AddScoped<AIService>();
builder.Services.AddScoped<PdfService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<TenantProvider>();
builder.Services.AddScoped<EmailSenderService>();
builder.Services.AddScoped<AutoGenMultiAgentSystem>();
builder.Services.AddScoped<PerplexityResearchPlugin>();
builder.Services.AddScoped<IDockerService, DockerService>();
builder.Services.AddScoped<AIDataPlatform.Services.DocumentIngestion.PythonIngestionService>();
builder.Services.AddHttpClient<IAutoGenStreamingService, AutoGenStreamingService>();

// Transient Services (short-lived objects)
builder.Services.AddTransient<IEmailSender<ApplicationUser>, EmailSenderService>();
builder.Services.AddTransient<InvitationService>();

// Singleton Services (one instance per application)
builder.Services.AddSingleton<DocumentProcessingService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Configure Azure SQL for user information
var connectionString = builder.Configuration.GetValue<string>("PostgreSql:ConnectionString")
                    ?? throw new InvalidOperationException("Connection string for PostgreSQL not found.");


builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, providerOptions => 
    {
        providerOptions.EnableRetryOnFailure(5);
    }),
    ServiceLifetime.Scoped);

// Configure Cosmos DB for document OCR metadata
var cosmosDbSettings = builder.Configuration.GetSection("AzureCosmosDb");
var cosmosEndpointUri = cosmosDbSettings.GetValue<string>("EndpointUri");
var cosmosPrimaryKey = cosmosDbSettings.GetValue<string>("PrimaryKey");
var cosmosDatabaseName = cosmosDbSettings.GetValue<string>("DatabaseName");

builder.Services.AddDbContextFactory<CosmosDbContext>(options =>
    options.UseCosmos(cosmosEndpointUri, cosmosPrimaryKey, cosmosDatabaseName), 
    ServiceLifetime.Scoped);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>() // custom add roles for authorization
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

//  During development or testing, you might not want to send real emails. Using a no-op email sender allows you to bypass actual email sending while still satisfying the dependency requirements. 2.Placeholder: It can act as a placeholder until you implement a real email sender, such as one using SendGrid or SMTP.
//builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Custom call AddHubOptions method to server side project only
builder.Services.AddServerSideBlazor().AddHubOptions(o => { o.MaximumReceiveMessageSize = 102400000; });
builder.Services.AddSyncfusionBlazor();

// For Mudblazor
builder.Services.AddMudServices();

// For Radzen
builder.Services.AddRadzenComponents();

// custom add memorycache
builder.Services.AddMemoryCache();

// custom add hosted service
builder.Services.AddHostedService<DocumentProcessingService>();

// custom Register Syncfusion license
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JFaF5cXGRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXZceHVSRGNfVUxyXEVWYEg=");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Counter).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
