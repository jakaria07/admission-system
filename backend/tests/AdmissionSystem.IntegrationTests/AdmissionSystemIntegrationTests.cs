using System.Net;
using System.Net.Http.Json;
using System.Text;
using AdmissionSystem.Application.Classes;
using AdmissionSystem.Application.Holds;
using AdmissionSystem.Infrastructure;
using AdmissionSystem.IntegrationTests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace AdmissionSystem.IntegrationTests;

[Collection("PostgresIntegration")]
public sealed class AdmissionSystemIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public AdmissionSystemIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lifecycle_CreateClass_Hold_Confirm_And_Cancel_UpdatesSeatCountsAccurately()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateAppFactory(holdDurationSeconds: 600);
        using var adminClient = factory.CreateClient();

        // 1. Create Class
        var createResp = await adminClient.PostAsJsonAsync("/classes", new
        {
            name = "Class 6 - Morning Shift",
            totalSeats = 5
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdClass = await createResp.Content.ReadFromJsonAsync<ClassSummaryResponseDto>();
        createdClass.Should().NotBeNull();
        createdClass!.Name.Should().Be("Class 6 - Morning Shift");
        createdClass.TotalSeats.Should().Be(5);
        createdClass.AvailableSeats.Should().Be(5);

        var classId = createdClass.Id;

        // 2. Student 1 holds a seat
        const string student1 = "20152691234567890";
        using var student1Client = PostgresDatabaseFixture.CreateStudentClient(factory, student1);

        var hold1Resp = await student1Client.PostAsync($"/classes/{classId}/holds", content: null);
        hold1Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var hold1 = await hold1Resp.Content.ReadFromJsonAsync<HoldCreatedResponseDto>();
        hold1.Should().NotBeNull();
        hold1!.ClassId.Should().Be(classId);
        hold1.StudentId.Should().Be(student1);
        hold1.Status.Should().Be("HELD");
        hold1.ExpiresAt.Should().EndWith("Z");

        // 3. Student 2 holds a seat
        const string student2 = "20152691234567891";
        using var student2Client = PostgresDatabaseFixture.CreateStudentClient(factory, student2);

        var hold2Resp = await student2Client.PostAsync($"/classes/{classId}/holds", content: null);
        hold2Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var hold2 = await hold2Resp.Content.ReadFromJsonAsync<HoldCreatedResponseDto>();

        // Verify counts: 2 held, 0 confirmed, 3 available
        var classAfterHolds = await adminClient.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
        classAfterHolds!.HeldSeats.Should().Be(2);
        classAfterHolds.ConfirmedSeats.Should().Be(0);
        classAfterHolds.AvailableSeats.Should().Be(3);

        // 4. Student 1 confirms their hold (idempotent even when called twice)
        var confirm1Resp = await student1Client.PostAsync($"/holds/{hold1.HoldId}/confirm", content: null);
        confirm1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed1 = await confirm1Resp.Content.ReadFromJsonAsync<HoldConfirmedResponseDto>();
        confirmed1!.Status.Should().Be("CONFIRMED");

        var confirm1RepeatResp = await student1Client.PostAsync($"/holds/{hold1.HoldId}/confirm", content: null);
        confirm1RepeatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify counts: 1 held, 1 confirmed, 3 available
        var classAfterConfirm = await adminClient.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
        classAfterConfirm!.HeldSeats.Should().Be(1);
        classAfterConfirm.ConfirmedSeats.Should().Be(1);
        classAfterConfirm.AvailableSeats.Should().Be(3);

        // 5. Student 2 cancels their hold
        var cancel2Resp = await student2Client.DeleteAsync($"/holds/{hold2!.HoldId}");
        cancel2Resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify counts: 0 held, 1 confirmed, 4 available
        var classAfterCancel = await adminClient.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
        classAfterCancel!.HeldSeats.Should().Be(0);
        classAfterCancel.ConfirmedSeats.Should().Be(1);
        classAfterCancel.AvailableSeats.Should().Be(4);

        // Verify GET /me/holds for Student 1
        var student1Holds = await student1Client.GetFromJsonAsync<List<MyHoldItemResponseDto>>("/me/holds");
        student1Holds.Should().ContainSingle();
        student1Holds![0].Status.Should().Be("CONFIRMED");
        student1Holds[0].ClassName.Should().Be("Class 6 - Morning Shift");
    }

    [Fact]
    public async Task Expiry_HoldExpiresAutomatically_ReclaimsSeat_RejectsConfirm_AndAllowsReapply()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateAppFactory(holdDurationSeconds: 2);
        using var adminClient = factory.CreateClient();

        var createResp = await adminClient.PostAsJsonAsync("/classes", new
        {
            name = "Class 7 - Day Shift",
            totalSeats = 1
        });
        var createdClass = await createResp.Content.ReadFromJsonAsync<ClassSummaryResponseDto>();
        var classId = createdClass!.Id;

        const string studentId = "20152691234567892";
        using var studentClient = PostgresDatabaseFixture.CreateStudentClient(factory, studentId);

        // Hold the single seat
        var holdResp = await studentClient.PostAsync($"/classes/{classId}/holds", content: null);
        holdResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var hold = await holdResp.Content.ReadFromJsonAsync<HoldCreatedResponseDto>();

        // Immediately after hold: 0 available
        var immediateClass = await adminClient.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
        immediateClass!.AvailableSeats.Should().Be(0);
        immediateClass.HeldSeats.Should().Be(1);

        // Wait for hold duration (2s) to elapse
        await Task.Delay(TimeSpan.FromMilliseconds(2600));

        // Without any write API call, GET /classes/{classId} must show seat returned (Available = 1, Held = 0)
        var afterExpiryClass = await adminClient.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
        afterExpiryClass!.HeldSeats.Should().Be(0);
        afterExpiryClass.AvailableSeats.Should().Be(1);

        // GET /holds/{holdId} must show EXPIRED
        var holdDetails = await adminClient.GetFromJsonAsync<HoldDetailsResponseDto>($"/holds/{hold!.HoldId}");
        holdDetails!.Status.Should().Be("EXPIRED");

        // Attempting to confirm expired hold must return 409 Conflict
        var confirmExpiredResp = await studentClient.PostAsync($"/holds/{hold.HoldId}/confirm", content: null);
        confirmExpiredResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Same student can re-apply now that previous hold expired
        var reapplyResp = await studentClient.PostAsync($"/classes/{classId}/holds", content: null);
        reapplyResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrency_100ParallelHoldRequests_For10SeatClass_NeverExceedsTotalSeats()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateAppFactory(holdDurationSeconds: 600);
        using var adminClient = factory.CreateClient();

        const int totalSeats = 10;
        const int concurrentRequests = 100;

        var createResp = await adminClient.PostAsJsonAsync("/classes", new
        {
            name = "Class 8 - Concurrency Shift",
            totalSeats
        });
        var createdClass = await createResp.Content.ReadFromJsonAsync<ClassSummaryResponseDto>();
        var classId = createdClass!.Id;

        using var barrier = new SemaphoreSlim(0, concurrentRequests);

        var tasks = Enumerable.Range(1, concurrentRequests)
            .Select(async index =>
            {
                var studentId = (20150000000000000L + index).ToString();
                using var client = PostgresDatabaseFixture.CreateStudentClient(factory, studentId);
                await barrier.WaitAsync();
                return await client.PostAsync($"/classes/{classId}/holds", content: null);
            })
            .ToArray();

        barrier.Release(concurrentRequests);
        var responses = await Task.WhenAll(tasks);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        createdCount.Should().Be(totalSeats);
        conflictCount.Should().Be(concurrentRequests - totalSeats);

        var finalClass = await adminClient.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
        finalClass!.HeldSeats.Should().Be(totalSeats);
        finalClass.ConfirmedSeats.Should().Be(0);
        finalClass.AvailableSeats.Should().Be(0);
        (finalClass.HeldSeats + finalClass.ConfirmedSeats).Should().BeLessThanOrEqualTo(finalClass.TotalSeats);
    }

    [Fact]
    public async Task RaceCondition_ConcurrentConfirmAndCancel_OnSameHold_EndsInSingleConsistentTerminalState()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateAppFactory(holdDurationSeconds: 600);
        using var adminClient = factory.CreateClient();

        var createResp = await adminClient.PostAsJsonAsync("/classes", new
        {
            name = "Class 9 - Race Test Shift",
            totalSeats = 5
        });
        var createdClass = await createResp.Content.ReadFromJsonAsync<ClassSummaryResponseDto>();
        var classId = createdClass!.Id;

        const string studentId = "20159999999999999";
        using var studentClient1 = PostgresDatabaseFixture.CreateStudentClient(factory, studentId);
        using var studentClient2 = PostgresDatabaseFixture.CreateStudentClient(factory, studentId);

        var holdResp = await studentClient1.PostAsync($"/classes/{classId}/holds", content: null);
        var hold = await holdResp.Content.ReadFromJsonAsync<HoldCreatedResponseDto>();

        using var gate = new SemaphoreSlim(0, 2);

        var confirmTask = Task.Run(async () =>
        {
            await gate.WaitAsync();
            return await studentClient1.PostAsync($"/holds/{hold!.HoldId}/confirm", content: null);
        });

        var cancelTask = Task.Run(async () =>
        {
            await gate.WaitAsync();
            return await studentClient2.DeleteAsync($"/holds/{hold!.HoldId}");
        });

        gate.Release(2);
        var results = await Task.WhenAll(confirmTask, cancelTask);

        var confirmStatus = results[0].StatusCode;
        var cancelStatus = results[1].StatusCode;

        // Because both lock the class row and re-read the hold FOR UPDATE:
        // Either Confirm wins (200 OK) and Cancel gets 409 Conflict,
        // OR Cancel wins (204 NoContent) and Confirm gets 409 Conflict.
        var confirmWon = confirmStatus == HttpStatusCode.OK && cancelStatus == HttpStatusCode.Conflict;
        var cancelWon = cancelStatus == HttpStatusCode.NoContent && confirmStatus == HttpStatusCode.Conflict;

        (confirmWon || cancelWon).Should().BeTrue(
            $"Expected exactly one operation to succeed and the other to return 409 Conflict, but got Confirm={confirmStatus} and Cancel={cancelStatus}");

        var finalHold = await adminClient.GetFromJsonAsync<HoldDetailsResponseDto>($"/holds/{hold!.HoldId}");
        finalHold!.Status.Should().Be(confirmWon ? "CONFIRMED" : "CANCELLED");
    }

    [Fact]
    public async Task Validation_And_BusinessRules_Return4xx_Never500()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateAppFactory(holdDurationSeconds: 600);
        using var client = factory.CreateClient();

        // 1. Invalid class name or totalSeats -> 400
        var emptyNameResp = await client.PostAsJsonAsync("/classes", new { name = "   ", totalSeats = 50 });
        emptyNameResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var zeroSeatsResp = await client.PostAsJsonAsync("/classes", new { name = "Valid Name", totalSeats = 0 });
        zeroSeatsResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Malformed JSON -> 400
        using var malformedContent = new StringContent("{ invalid json ", Encoding.UTF8, "application/json");
        var malformedResp = await client.PostAsync("/classes", malformedContent);
        malformedResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Create valid class, duplicate class name -> 409
        var createResp = await client.PostAsJsonAsync("/classes", new { name = "Class 10 - Science", totalSeats = 30 });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdClass = await createResp.Content.ReadFromJsonAsync<ClassSummaryResponseDto>();

        var dupClassResp = await client.PostAsJsonAsync("/classes", new { name = "  class 10 - science ", totalSeats = 40 });
        dupClassResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Missing or invalid X-Student-Id -> 400
        var missingHeaderResp = await client.PostAsync($"/classes/{createdClass!.Id}/holds", content: null);
        missingHeaderResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var shortIdClient = PostgresDatabaseFixture.CreateStudentClient(factory, "12345");
        var shortIdResp = await shortIdClient.PostAsync($"/classes/{createdClass.Id}/holds", content: null);
        shortIdResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. Duplicate hold by same student in same class -> 409
        const string studentA = "20151111111111111";
        using var studentAClient = PostgresDatabaseFixture.CreateStudentClient(factory, studentA);
        var firstHoldResp = await studentAClient.PostAsync($"/classes/{createdClass.Id}/holds", content: null);
        firstHoldResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstHold = await firstHoldResp.Content.ReadFromJsonAsync<HoldCreatedResponseDto>();

        var duplicateHoldResp = await studentAClient.PostAsync($"/classes/{createdClass.Id}/holds", content: null);
        duplicateHoldResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 6. Another student trying to confirm or cancel Student A's hold -> 403 Forbidden
        const string studentB = "20152222222222222";
        using var studentBClient = PostgresDatabaseFixture.CreateStudentClient(factory, studentB);
        var wrongOwnerConfirm = await studentBClient.PostAsync($"/holds/{firstHold!.HoldId}/confirm", content: null);
        wrongOwnerConfirm.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var wrongOwnerCancel = await studentBClient.DeleteAsync($"/holds/{firstHold.HoldId}");
        wrongOwnerCancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 7. Unknown class / hold ID -> 404
        var unknownClassResp = await client.GetAsync($"/classes/{Guid.NewGuid()}");
        unknownClassResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var nonGuidClassResp = await client.GetAsync("/classes/not-a-guid");
        nonGuidClassResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // 8. Verify DATABASE_URL URI parsing (postgres:// and postgresql://) into Npgsql connection string
        var parsedUriConn = DependencyInjection.NormalizeConnectionString(
            "postgresql://postgres:secret%40123@db.internal:5433/admission_db?sslmode=Prefer");
        var csb = new NpgsqlConnectionStringBuilder(parsedUriConn);
        csb.Host.Should().Be("db.internal");
        csb.Port.Should().Be(5433);
        csb.Database.Should().Be("admission_db");
        csb.Username.Should().Be("postgres");
        csb.Password.Should().Be("secret@123");
    }

    [Fact]
    public async Task TwoInstanceConcurrency_AcrossSeparateProcesses_PreservesSeatLimitWhenConfigured()
    {
        await _fixture.ResetDatabaseAsync();

        var rawUrls = Environment.GetEnvironmentVariable("E2E_API_URLS");
        ExternalApiProcessHandle? spawnedProcess1 = null;
        ExternalApiProcessHandle? spawnedProcess2 = null;

        try
        {
            Uri[] urls;
            if (!string.IsNullOrWhiteSpace(rawUrls))
            {
                urls = rawUrls
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(u => new Uri(u))
                    .ToArray();
            }
            else
            {
                // Spawn two genuinely separate OS processes (dotnet AdmissionSystem.Api.dll) on distinct ports
                // sharing the exact same PostgreSQL database connection string.
                spawnedProcess1 = await ExternalApiProcessHandle.StartAsync(_fixture.ConnectionString, holdDurationSeconds: 600);
                spawnedProcess2 = await ExternalApiProcessHandle.StartAsync(_fixture.ConnectionString, holdDurationSeconds: 600);
                urls = [spawnedProcess1.BaseUri, spawnedProcess2.BaseUri];
            }

            urls.Length.Should().BeGreaterThanOrEqualTo(2, "Two separate backend process URLs are required.");
            urls[0].Port.Should().NotBe(urls[1].Port);

            using var client1 = new HttpClient { BaseAddress = urls[0] };
            using var client2 = new HttpClient { BaseAddress = urls[1] };

            const int totalSeats = 10;
            const int concurrentRequests = 100;
            var uniqueClassName = $"MultiProcess-Class-{Guid.NewGuid():N}";

            var createResp = await client1.PostAsJsonAsync("/classes", new
            {
                name = uniqueClassName,
                totalSeats
            });
            createResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var createdClass = await createResp.Content.ReadFromJsonAsync<ClassSummaryResponseDto>();
            var classId = createdClass!.Id;

            using var barrier = new SemaphoreSlim(0, concurrentRequests);

            var tasks = Enumerable.Range(1, concurrentRequests)
                .Select(async index =>
                {
                    var targetUrl = urls[index % urls.Length];
                    var studentId = (20160000000000000L + index).ToString();

                    using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(targetUrl, $"/classes/{classId}/holds"));
                    req.Headers.Add("X-Student-Id", studentId);

                    var targetClient = index % 2 == 0 ? client1 : client2;
                    await barrier.WaitAsync();
                    return await targetClient.SendAsync(req);
                })
                .ToArray();

            barrier.Release(concurrentRequests);
            var responses = await Task.WhenAll(tasks);

            var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
            var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

            createdCount.Should().Be(totalSeats);
            conflictCount.Should().Be(concurrentRequests - totalSeats);

            var summaryFromInstance2 = await client2.GetFromJsonAsync<ClassSummaryResponseDto>($"/classes/{classId}");
            summaryFromInstance2!.HeldSeats.Should().Be(totalSeats);
            summaryFromInstance2.AvailableSeats.Should().Be(0);
            (summaryFromInstance2.HeldSeats + summaryFromInstance2.ConfirmedSeats).Should().BeLessThanOrEqualTo(totalSeats);
        }
        finally
        {
            if (spawnedProcess1 is not null)
            {
                await spawnedProcess1.DisposeAsync();
            }

            if (spawnedProcess2 is not null)
            {
                await spawnedProcess2.DisposeAsync();
            }
        }
    }
}

