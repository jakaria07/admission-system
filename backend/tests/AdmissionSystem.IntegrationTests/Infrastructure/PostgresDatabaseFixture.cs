using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AdmissionSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AdmissionSystem.IntegrationTests.Infrastructure;

public sealed class ExternalApiProcessHandle : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdoutBuffer = new();
    private readonly StringBuilder _stderrBuffer = new();
    private readonly object _outputLock = new();

    public Uri BaseUri { get; }
    public int ProcessId => _process.Id;
    public int Port => BaseUri.Port;

    private ExternalApiProcessHandle(Process process, Uri baseUri)
    {
        _process = process;
        BaseUri = baseUri;

        // Safely consume redirected standard output and error asynchronously to prevent OS pipe buffer deadlocks.
        _process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (_outputLock)
                {
                    _stdoutBuffer.AppendLine(args.Data);
                }
            }
        };

        _process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (_outputLock)
                {
                    _stderrBuffer.AppendLine(args.Data);
                }
            }
        };

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public static async Task<ExternalApiProcessHandle> StartAsync(
        string connectionString,
        int holdDurationSeconds = 600)
    {
        var port = GetFreeTcpPort();
        var baseUri = new Uri($"http://127.0.0.1:{port}");
        var apiDllPath = Path.Combine(AppContext.BaseDirectory, "AdmissionSystem.Api.dll");

        if (!File.Exists(apiDllPath))
        {
            throw new FileNotFoundException($"Could not locate '{apiDllPath}' to start external API process.");
        }

        var psi = new ProcessStartInfo("dotnet", $"\"{apiDllPath}\" --urls {baseUri}")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["ConnectionStrings__Default"] = connectionString;
        psi.Environment["HOLD_DURATION_SECONDS"] = holdDurationSeconds.ToString();
        psi.Environment["SKIP_MIGRATIONS"] = "true";
        // Budget 40 connections per API process to keep 2 processes + runner within PostgreSQL 100 max_connections
        psi.Environment["MAX_POOL_SIZE"] = "40";

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start external dotnet API process.");

        var handle = new ExternalApiProcessHandle(process, baseUri);
        try
        {
            await handle.WaitForHealthyAsync(TimeSpan.FromSeconds(20));
            return handle;
        }
        catch
        {
            await handle.DisposeAsync();
            throw;
        }
    }

    private async Task WaitForHealthyAsync(TimeSpan timeout)
    {
        using var httpClient = new HttpClient { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            if (_process.HasExited)
            {
                string stderr, stdout;
                lock (_outputLock)
                {
                    stderr = _stderrBuffer.ToString();
                    stdout = _stdoutBuffer.ToString();
                }
                throw new InvalidOperationException(
                    $"External API process (PID {ProcessId}) exited prematurely with code {_process.ExitCode}. StdErr: {stderr} StdOut: {stdout}");
            }

            try
            {
                var response = await httpClient.GetAsync("/health");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch
            {
                // Still starting up
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"External API process at {BaseUri} did not become healthy within {timeout.TotalSeconds}s.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            ConnectionString = externalConnectionString;
        }
        else
        {
            // Aligns with the planned Docker environment by using PostgreSQL 17 as the default image.
            var postgresImage = Environment.GetEnvironmentVariable("TESTCONTAINERS_POSTGRES_IMAGE") ?? "postgres:17-alpine";
            _postgresContainer = new PostgreSqlBuilder(postgresImage)
                .WithDatabase("admission_test_db")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _postgresContainer.StartAsync();
            ConnectionString = _postgresContainer.GetConnectionString();
        }

        await EnsureDatabaseMigratedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    public WebApplicationFactory<Program> CreateAppFactory(int holdDurationSeconds = 600)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:Default", ConnectionString);
                builder.UseSetting("HOLD_DURATION_SECONDS", holdDurationSeconds.ToString());
                builder.UseSetting("SKIP_MIGRATIONS", "true");
                builder.UseSetting("MAX_POOL_SIZE", "40");
            });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE TABLE admission_holds, admission_classes RESTART IDENTITY CASCADE;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static HttpClient CreateStudentClient(WebApplicationFactory<Program> factory, string studentId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Student-Id");
        client.DefaultRequestHeaders.Add("X-Student-Id", studentId);
        return client;
    }

    private async Task EnsureDatabaseMigratedAsync()
    {
        var options = new DbContextOptionsBuilder<AdmissionDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var dbContext = new AdmissionDbContext(options);
        await dbContext.Database.MigrateAsync();
    }
}

[CollectionDefinition("PostgresIntegration")]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresDatabaseFixture>
{
}

