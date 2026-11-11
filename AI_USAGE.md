# AI Usage Disclosure (`AI_USAGE.md`)

In accordance with the assignment guidelines, this document provides a transparent account of how AI tools were used during the development of the **Online School Admission System**, where they accelerated implementation, and—most importantly—the real mistakes and edge-case gaps identified during verification and code review and how they were corrected.

---

## 1. AI Tools Used

- **Antigravity (Google DeepMind Agentic Coding Assistant):** Used inside the IDE for scaffolding Clean Architecture project layers, drafting EF Core entities/migrations, writing xUnit integration tests, configuring Dockerfiles, and building the React + TypeScript UI.
- **ChatGPT:** Used as an independent technical reviewer across Pull Requests (`PR #1`, `PR #2`, `PR #3`) to audit concurrency edge cases, connection pool sizing, Docker networking, and specification compliance.

---

## 2. How AI Helped

1. **Boilerplate & Clean Architecture Scaffolding:**
   - Generated initial DTO records, custom domain exceptions (`ConflictException`, `HoldConflictException`, `NotFoundException`), and the global exception-handling middleware that maps domain errors to `{ code, message }` JSON responses.
2. **SQL & EF Core Configuration:**
   - Assisted with setting up the PostgreSQL partial unique index (`ux_admission_holds_active_student_per_class`), the `pg_advisory_lock` startup migration guard, and parameterized `FromSqlInterpolated` queries for `SELECT ... FOR UPDATE`.
3. **Frontend Component Structure & Bengali Copy:**
   - Accelerated building the React 19 + TanStack Query v5 polling hooks (`refetchInterval: 3000`), the 1-second countdown hook, and natural Bengali status/error messages for guardians and school admins.

---

## 3. Genuine Mistakes & Gaps Identified and Corrected

Every issue listed below was caught during testing or PR review and fixed in our repository's commit history:

### 1. Two-Process Concurrency Test Request Routing Bug (`PR #1`)
- **What went wrong:** In the initial version of the two-instance concurrency test (`TwoInstanceConcurrency_100RequestsAcross2ProcessesFor10Seats_Exactly10SucceedAnd90Return409`), the target `instanceUrl` was selected outside `Task.Run` using the loop variable `index % 2`, while the `HttpClient` was selected inside the lambda using a separately incremented counter (`Interlocked.Increment`). Under parallel scheduling, a request could use `Client[0]` with `InstanceUrl[1]` or route unevenly.
- **How it was verified & fixed:** Caught during PR #1 review. Fixed by computing `var targetIndex = index % 2;` once per iteration before `Task.Run` and selecting both `clients[targetIndex]` and `instanceUrls[targetIndex]` with the exact same index, ensuring a deterministic 50/50 split across both backend OS processes.

### 2. Database Credential Leakage in Test Output Logs (`PR #1`)
- **What went wrong:** When adding diagnostic logging to prove that the two spawned backend processes shared the same PostgreSQL database, the test initially logged `_fixture.ConnectionString` directly to `ITestOutputHelper`, which printed `Username=postgres;Password=postgres` in the test console output.
- **How it was verified & fixed:** Identified during PR #1 security review. Replaced raw string logging with `new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)` and logged only `csb.Host`, `csb.Port`, and `csb.Database`, ensuring no credentials ever appear in CI or console logs.

### 3. Npgsql Connection Pool Contention & Raw String Parsing (`PR #1` & `PR #2`)
- **What went wrong:**
  1. `DependencyInjection.cs` initially checked for custom pool settings using raw string `.Contains("Maximum Pool Size")` / `.Contains("MaxPoolSize")` and hardcoded `MaxPoolSize = 40` for all environments.
  2. Later, when running `docker compose --profile test run --rm tests` in Milestone 5, three processes (`api`, `api-2`, and the `tests` runner spawning `WebApplicationFactory` hosts) connected to the same PostgreSQL container simultaneously, hitting PostgreSQL's default `max_connections=100` limit (`53300: sorry, too many clients already`).
- **How it was verified & fixed:**
  1. Replaced raw string checks with `NpgsqlConnectionStringBuilder` in `DependencyInjection.cs`, preserving any explicit pool size in `ConnectionStrings__Default` and supporting an optional `MAX_POOL_SIZE` environment variable.
  2. Configured `command: ["postgres", "-c", "max_connections=200"]` on the `postgres` service, `MAX_POOL_SIZE: 35` on `api` and `api-2`, and `MAX_POOL_SIZE: 25` on the `tests` container in `docker-compose.yml`. Verified by running all 6 integration tests end-to-end inside Docker Compose (`Passed: 6, Failed: 0`).

### 4. Ephemeral Port Race (`GetFreeTcpPort`) When Spawning Local API Processes (`PR #1`)
- **What went wrong:** In local mode, `ExternalApiProcessHandle` found an available TCP port by binding a temporary `TcpListener(IPAddress.Loopback, 0)`, reading the port, stopping the listener, and starting `dotnet AdmissionSystem.Api.dll`. Occasionally, another OS socket could claim that port in the brief window before Kestrel bound to it.
- **How it was verified & fixed:** Added a startup retry loop (up to 3 attempts) in `ExternalApiProcessHandle.StartAsync` that selects a fresh port not used by the peer instance if the child process exits early due to a port-binding conflict, and asynchronously drains redirected `StandardOutput` / `StandardError` streams so child processes never block on full pipe buffers.

### 5. Fixed `Task.Delay` in Hold Expiry Test (`PR #1`)
- **What went wrong:** The hold expiry integration test (`HoldExpiry_WaitPastExpiry_HoldBecomesExpired_SeatReturns_ConfirmFails409_StudentCanReapply`) initially used a fixed `await Task.Delay(2600)` for a 2-second hold.
- **How it was verified & fixed:** Replaced the fixed delay with a deadline-based polling helper (`WaitForHoldExpirationAsync`) that polls `GET /holds/{id}` every `150ms` up to a 10-second timeout until `status == "EXPIRED"`, making the test both faster and resilient to CI scheduling jitter.

### 6. Missing `curl` in `.NET 10` Runtime Image & Container Hostname Assertion (`PR #2`)
- **What went wrong:**
  1. The `api` healthcheck in `docker-compose.yml` initially used `curl -f http://localhost:8080/health`, which failed because `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian slim/Ubuntu noble) does not ship with `curl` pre-installed.
  2. In Docker Compose, `E2E_API_URLS` was set to `http://api:8080,http://api-2:8080`. The two-instance test initially asserted `uri0.Port != uri1.Port`, which worked for local processes (`localhost:port1` vs `localhost:port2`) but failed when two distinct containers used the same internal container port `8080`.
- **How it was verified & fixed:**
  1. Switched the runtime base image in `backend/Dockerfile` to `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (which includes BusyBox `wget` out of the box) and updated the healthcheck to `wget -q --spider http://localhost:8080/health || exit 1`.
  2. Updated the endpoint distinctness assertion in `AdmissionSystemIntegrationTests.cs` to require different ports when both URLs share the same host, and allow the same internal port when the URLs target different container hostnames (`api` vs `api-2`).

