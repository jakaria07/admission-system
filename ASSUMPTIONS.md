# System Design Decisions & Assumptions (`ASSUMPTIONS.md`)

This document explains the architectural choices, domain assumptions, and concurrency mechanisms implemented in the **Online School Admission System**. Every detail below corresponds directly to the production implementation in `backend/` and `frontend/`.

---

## 1. Student Identity & Authentication

1. **No Authentication / Login System:**
   - Per the specification, neither the School Admin view nor the Guardian view requires user registration, passwords, or JWT/session authentication.
2. **17-Digit Birth Registration Number (`X-Student-Id`):**
   - A student is uniquely identified by a **17-digit numeric Birth Registration Number** matching the strict regular expression `^\d{17}$` (ASCII digits `0–9` only, enforced by `StudentId.TryCreate` in `AdmissionSystem.Domain`).
   - Any missing or malformed `X-Student-Id` on a student-scoped endpoint immediately returns `400 Bad Request` with `MISSING_STUDENT_ID` or `INVALID_STUDENT_ID`.
   - The `X-Student-Id` header is required **only** on the four student-scoped endpoints:
     - `POST /classes/{id}/holds`
     - `POST /holds/{id}/confirm`
     - `DELETE /holds/{id}`
     - `GET /me/holds`
   - Public/admin endpoints (`POST /classes`, `GET /classes`, `GET /classes/{id}`, `GET /holds/{id}`) do not require `X-Student-Id`.
3. **Frontend Identity Persistence:**
   - The React frontend stores the validated 17-digit Student ID in `localStorage` under `admission_student_id` so guardians do not lose their context across page refreshes or browser restarts. Guardians can switch to another student ID at any time using the **পরিবর্তন** (Change) button.

---

## 2. Admission Classes & Shifts

1. **One Class Entity per Shift:**
   - Schools with multiple shifts (e.g., *Class 6 - Morning Shift* and *Class 6 - Day Shift*) create each shift as a separate class record with its own independent seat pool.
2. **Case-Insensitive Name Uniqueness:**
   - Class names are trimmed and normalized (`name_normalized = name.Trim().ToUpperInvariant()`), backed by a unique database index (`ix_admission_classes_name_normalized`). Creating a duplicate class name returns `409 Conflict` (`DUPLICATE_CLASS_NAME`).
3. **Seat Capacity Bounds:**
   - `totalSeats` must be an integer between `1` and `10,000` (enforced both in `AdmissionClass.Create` and via the PostgreSQL check constraint `ck_admission_classes_total_seats_positive`), and `name` must be `1–150` characters after trimming. Invalid values return `400 Bad Request` (`INVALID_TOTAL_SEATS` or `INVALID_CLASS_NAME`).
4. **No Class Deletion or Seat Reduction:**
   - Classes are immutable once created. No delete endpoint is exposed or faked in the UI.

---

## 3. Seat Hold, Expiry, Confirmation & Cancellation Rules

1. **Seat Accounting Invariant:**
   - At all times, for every class:
     - `heldSeats` = count of holds with `status = 'HELD'` and `expires_at > now()`
     - `confirmedSeats` = count of holds with `status = 'CONFIRMED'`
     - `availableSeats` = `totalSeats - heldSeats - confirmedSeats`
     - `heldSeats + confirmedSeats <= totalSeats` (never negative, never overbooked).
2. **Configurable Hold Duration:**
   - Controlled by the `HOLD_DURATION_SECONDS` environment variable (defaults to `600` seconds / 10 minutes in `docker-compose.yml` and `appsettings.json`; overridden to short durations like `2` seconds during automated expiry tests).
3. **Three-Layer Expiry Enforcement:**
   - **Read-Time Effective Status:** Read queries (`GET /classes`, `GET /classes/{id}`, `GET /holds/{id}`, `GET /me/holds`) compare `expires_at` against PostgreSQL `now()`. If a row is still marked `HELD` in storage but `expires_at <= now()`, it is immediately reported as `EXPIRED` and excluded from `heldSeats` without waiting for a background tick.
   - **Write-Time Inline Expiry:** Before checking seat availability or duplicate holds inside `CreateHoldUnderClassLockAsync`, all overdue `HELD` rows for that class (`expires_at <= clock_timestamp()`) are atomically updated to `EXPIRED` under the class row lock. Similarly, if `ConfirmHold` or `CancelHold` is called on a hold whose `expires_at <= clock_timestamp()`, the transaction persists `status = 'EXPIRED'` and commits before returning `409 HOLD_EXPIRED`.
   - **Background Sweeper (`HoldExpiryWorker`):** A hosted background service runs every 2 seconds executing `UPDATE admission_holds SET status = 'EXPIRED' WHERE status = 'HELD' AND expires_at <= now()` to keep persisted statuses clean even for idle classes.
4. **Idempotent Confirmation (`POST /holds/{id}/confirm`):**
   - Confirming an active, unexpired `HELD` hold transitions it to `CONFIRMED` and returns `200 OK`.
   - Confirming a hold that is **already `CONFIRMED`** by the same student is idempotent and returns `200 OK` with `{ "holdId": "...", "status": "CONFIRMED" }` without double-counting the seat.
   - Confirming an `EXPIRED` or `CANCELLED` hold returns `409 Conflict` (`HOLD_EXPIRED` or `HOLD_CANCELLED`).
5. **Cancellation Rules (`DELETE /holds/{id}`):**
   - Only an active, unexpired `HELD` hold can be cancelled (`204 No Content`), immediately freeing the seat.
   - A `CONFIRMED` admission cannot be cancelled via this endpoint (`409 HOLD_ALREADY_CONFIRMED`), as post-payment cancellation would require a fee refund workflow outside the scope of this specification.
   - Cancelling an already `CANCELLED` or `EXPIRED` hold returns `409 Conflict` (`HOLD_CANCELLED` or `HOLD_EXPIRED`).
6. **Ownership Authorization (`403 Forbidden`):**
   - If Student B attempts to confirm or cancel a hold created by Student A, the API returns `403 Forbidden` (`FORBIDDEN_NOT_OWNER`).
7. **Re-Application After Expiry or Cancellation:**
   - A student cannot have more than one active (`HELD`) or `CONFIRMED` hold in the same class (`409 DUPLICATE_APPLICATION`).
   - However, once a previous hold becomes `EXPIRED` or `CANCELLED`, the student is permitted to apply again for the same class if seats are available.

---

## 4. Multi-Instance Concurrency & Database Synchronization

To support running multiple stateless backend instances (`api` on port `8080` and `api-2` on port `8081`) against a single PostgreSQL database, the system avoids `lock (...)`, `SemaphoreSlim`, or in-memory caches for admission state:

1. **Pessimistic Row-Level Locking (`SELECT ... FOR UPDATE`):**
   - Every write operation (`CreateHold`, `ConfirmHold`, `CancelHold`) opens a database transaction and locks the parent `admission_classes` row first (`SELECT ... FROM admission_classes WHERE id = @classId FOR UPDATE`).
   - For `ConfirmHold` and `CancelHold`, the parent class row is locked first, followed by locking the target `admission_holds` row (`FOR UPDATE`). Enforcing the global lock acquisition order (`Class -> Hold`) prevents deadlocks when concurrent requests create, confirm, and cancel holds in the same class.
2. **PostgreSQL `clock_timestamp()` After Lock Acquisition:**
   - In PostgreSQL, `now()` / `CURRENT_TIMESTAMP` returns the timestamp of the *start of the transaction*. If Transaction B waits 150ms behind Transaction A on a `FOR UPDATE` lock, `now()` would reflect the time *before* the wait.
   - Therefore, write transactions query `SELECT clock_timestamp()` **after** acquiring the `FOR UPDATE` locks (`DbClock.GetClockTimestampAsync`), ensuring expiry checks and new `expires_at` calculations use the true wall-clock instant inside PostgreSQL.
3. **Partial Unique Index:**
   - The migration creates a partial unique index in PostgreSQL:
     ```sql
     CREATE UNIQUE INDEX ux_admission_holds_active_student_per_class
     ON admission_holds (class_id, student_id)
     WHERE status IN ('HELD', 'CONFIRMED');
     ```
   - Even under extreme race conditions, PostgreSQL physically prevents two active/confirmed holds for the same `(class_id, student_id)` pair; any unique constraint violation (`SqlState 23505`) is translated into `409 DUPLICATE_APPLICATION`.
4. **Advisory Lock for Startup Migrations:**
   - When `api` and `api-2` start simultaneously in Docker Compose, both invoke `ApplyDatabaseMigrationsAsync` in `Program.cs`, which acquires `pg_advisory_lock(7283940129384712)` before running EF Core migrations and releases it with `pg_advisory_unlock(7283940129384712)` in a `finally` block.

