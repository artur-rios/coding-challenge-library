using System.Text.Json.Serialization;
using FluentValidation;
using Library.Cache.DependencyInjection;
using Library.Command.Auditing;
using Library.Command.Handlers;
using Library.Command.Input;
using Library.Command.Input.Validation;
using Library.Command.Output;
using Library.Data.Configuration;
using Library.Data.DependencyInjection;
using Library.Mediator.Command;
using Library.Mediator.Command.Interfaces;
using Library.Mediator.Query;
using Library.Mediator.Query.Interfaces;
using Library.Output;
using Library.Query.Handlers;
using Library.Query.Input;
using Library.Query.Input.Validation;
using Library.Query.Output;
using Library.Shared.Auditing;
using Library.Shared.Telemetry;
using Library.WebApi.Auditing;
using Library.WebApi.Core.AspNetCore;
using Library.WebApi.Core.Middleware;
using Library.WebApi.Health;
using Library.WebApi.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Library.WebApi;

/// <summary>
///     Builds and runs the API. One class, read top to bottom: configuration, services, pipeline.
/// </summary>
/// <param name="args">The command-line arguments the host was started with.</param>
public class Startup(string[] args)
{
    /// <summary>Environment variable naming the OTLP endpoint traces and metrics are exported to.</summary>
    public const string OtlpEndpointVariable = "LIBRARY_OTLP_ENDPOINT";

    /// <summary>Environment variable controlling whether pending migrations are applied at start-up.</summary>
    public const string RunMigrationsVariable = "LIBRARY_RUN_MIGRATIONS";

    private WebApplicationBuilder Builder { get; } = WebApplication.CreateBuilder(args);

    private WebApplication App { get; set; } = null!;

    /// <summary>Configures everything and runs the host.</summary>
    public async Task BuildAndRunAsync()
    {
        ConfigureLogging();

        Log.Information(
            "Starting the library API in the {Environment} environment",
            Builder.Environment.EnvironmentName);

        ConfigureServices();

        App = Builder.Build();

        ConfigurePipeline();

        await ApplyMigrationsAsync();

        Log.Information("Ready");

        await App.RunAsync();
    }

    /// <summary>
    ///     Structured logs to the console as JSON.
    /// </summary>
    /// <remarks>
    ///     The console and nothing else, deliberately: in a container the log stream is the log, and
    ///     writing files inside the image only creates something to rotate and lose. JSON because the
    ///     collector that ships these lines parses them, and a correlation identifier is only useful if
    ///     it arrives as a field rather than as part of a sentence.
    /// </remarks>
    private void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(Builder.Configuration)
            // The correlation middleware opens a logging scope; this is what turns that scope's
            // properties into fields on every line the request produces.
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service.name", TelemetryConfiguration.ServiceName)
            .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter())
            .CreateLogger();

        Builder.Host.UseSerilog();
    }

    private void ConfigureServices()
    {
        Builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                // Enums as names on the wire. LoanStatus.Returned reads as "Returned" rather than as
                // 2, which matters for an API whose history endpoints exist to be read by people.
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
            });

        // ASP.NET Core's own model-binding failures - a malformed GUID in the body, a string where an
        // int belongs - are rewritten into the same problem-details shape as every other refusal, so a
        // client has one error format to parse rather than two.
        Builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .SelectMany(entry => entry.Value?.Errors ?? [])
                    .Select(error => error.ErrorMessage)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToList();

                var output = ProcessOutput.New.WithErrors(
                    errors.Count > 0 ? errors : ["The request body could not be read."]);

                return ResponseResolver.Problem(
                    output, context.HttpContext, StatusCodes.Status400BadRequest);
            });

        Builder.Services.AddEndpointsApiExplorer();
        Builder.Services.AddSwaggerGen(SwaggerConfiguration.Configure);

        AddData();
        AddTelemetry();
        AddHealthChecks();
        AddApplicationServices();
    }

    private void AddData()
    {
        // EF diagnostics expose parameter values - reader names and e-mail addresses on every query -
        // so they stay off in production.
        var diagnosticsEnabled = !Builder.Environment.IsProduction();

        Builder.Services.AddLibraryData(
            diagnostics: new DbContextDiagnosticsOptions
            {
                SensitiveDataLogging = diagnosticsEnabled,
                DetailedErrors = diagnosticsEnabled
            });

        Builder.Services.AddLibraryCache();
    }

    private void AddTelemetry()
    {
        Builder.Services.AddSingleton<LibraryMetrics>();

        Builder.Services.AddLibraryTelemetry(
            Environment.GetEnvironmentVariable(OtlpEndpointVariable));
    }

    private void AddHealthChecks() =>
        Builder.Services.AddHealthChecks()
            // Tagged ready, so it gates readiness but never liveness.
            .AddCheck<DatabaseHealthCheck>(
                "postgresql", tags: [HealthEndpoints.ReadyTag])
            .AddCheck<RedisHealthCheck>(
                "redis", tags: [HealthEndpoints.ReadyTag]);

    private void AddApplicationServices()
    {
        Builder.Services.AddHttpContextAccessor();
        Builder.Services.AddSingleton(TimeProvider.System);

        Builder.Services.AddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();
        Builder.Services.AddScoped<IAuditEventWriter, AuditEventWriter>();

        Builder.Services.AddScoped<CommandMediator>();
        Builder.Services.AddScoped<QueryMediator>();

        Builder.Services.AddScoped<IValidator<CreateBookCommand>, CreateBookCommandValidator>();
        Builder.Services
            .AddScoped<ICommandHandlerAsync<CreateBookCommand, BookCommandOutput>, CreateBookCommandHandler>();

        Builder.Services.AddScoped<IValidator<UpdateBookCommand>, UpdateBookCommandValidator>();
        Builder.Services
            .AddScoped<ICommandHandlerAsync<UpdateBookCommand, BookCommandOutput>, UpdateBookCommandHandler>();

        // No validator: the only input is a route GUID, which the route constraint has already
        // refused if it is not one.
        Builder.Services
            .AddScoped<ICommandHandlerAsync<DeactivateBookCommand, DeactivateBookCommandOutput>,
                DeactivateBookCommandHandler>();

        Builder.Services.AddScoped<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
        Builder.Services
            .AddScoped<ICommandHandlerAsync<CreateUserCommand, CreateUserCommandOutput>,
                CreateUserCommandHandler>();

        Builder.Services.AddScoped<IValidator<CreateLoanCommand>, CreateLoanCommandValidator>();
        Builder.Services
            .AddScoped<ICommandHandlerAsync<CreateLoanCommand, LoanCommandOutput>, CreateLoanCommandHandler>();

        // Likewise no validators for the two transitions: a route GUID and, for cancellation, an
        // optional free-text reason the handler truncates.
        Builder.Services
            .AddScoped<ICommandHandlerAsync<ReturnLoanCommand, LoanCommandOutput>, ReturnLoanCommandHandler>();
        Builder.Services
            .AddScoped<ICommandHandlerAsync<CancelLoanCommand, LoanCommandOutput>, CancelLoanCommandHandler>();

        Builder.Services
            .AddScoped<IQueryHandlerAsync<GetBookByIdQuery, BookOutput>, GetBookByIdQueryHandler>();
        Builder.Services
            .AddScoped<IQueryHandlerAsync<GetBookAvailabilityQuery, BookAvailabilityOutput>,
                GetBookAvailabilityQueryHandler>();
        Builder.Services
            .AddScoped<IPaginatedQueryHandlerAsync<ListBooksQuery, BookOutput>, ListBooksQueryHandler>();
        Builder.Services
            .AddScoped<IPaginatedQueryHandlerAsync<GetBookHistoryQuery, LoanOutput>,
                GetBookHistoryQueryHandler>();
        Builder.Services
            .AddScoped<IPaginatedQueryHandlerAsync<GetUserLoansQuery, LoanOutput>, GetUserLoansQueryHandler>();
        Builder.Services
            .AddScoped<IPaginatedQueryHandlerAsync<ListAuditEventsQuery, AuditEventOutput>,
                ListAuditEventsQueryHandler>();

        Builder.Services.AddScoped<IValidator<ListBooksQuery>, ListBooksQueryValidator>();
        Builder.Services.AddScoped<IValidator<GetBookHistoryQuery>, GetBookHistoryQueryValidator>();
        Builder.Services.AddScoped<IValidator<GetUserLoansQuery>, GetUserLoansQueryValidator>();
        Builder.Services.AddScoped<IValidator<ListAuditEventsQuery>, ListAuditEventsQueryValidator>();
    }

    private void ConfigurePipeline()
    {
        // Order matters, and this is the whole of it.
        //
        // Exception handling is outermost, so a failure anywhere below it - including inside the
        // correlation middleware - still answers problem details rather than a bare 500.
        //
        // Correlation comes next, before anything that logs or writes a response, so every log line
        // and every response body carries the identifier. It sits above routing because a 404 from
        // routing is still a request somebody may need to correlate.
        App.UseMiddleware<ExceptionMiddleware>();
        App.UseMiddleware<CorrelationIdMiddleware>();

        // Serilog's request log replaces the framework's several lines per request with one, and it
        // is registered after correlation so that line carries the identifier too.
        App.UseSerilogRequestLogging();

        if (!App.Environment.IsProduction())
        {
            App.UseSwagger();
            App.UseSwaggerUI(options =>
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Library API v1"));
        }

        App.UseRouting();
        App.MapControllers();
        App.MapHealthEndpoints();
    }

    /// <summary>
    ///     Applies pending EF Core migrations when asked to.
    /// </summary>
    /// <remarks>
    ///     Off by default, and that is the setting that matters between 2 and 11 replicas: every
    ///     replica starting at once would run the migrator at once, and while EF takes an advisory lock
    ///     that makes this safe rather than corrupting, it also makes every replica wait on the first.
    ///     In Compose one process applies them; in Kubernetes an init container or a Job does. The
    ///     switch exists so the same image can do either.
    /// </remarks>
    private async Task ApplyMigrationsAsync()
    {
        var requested = Environment.GetEnvironmentVariable(RunMigrationsVariable);

        if (!string.Equals(requested, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Log.Information("Applying pending migrations");

        using var scope = App.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.MigrateAsync();

        Log.Information("Migrations up to date");
    }
}
