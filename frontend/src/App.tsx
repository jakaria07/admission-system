import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  admissionApi,
  ApiRequestError,
  type ClassSummary,
  type HoldStatus,
  type MyHoldItem
} from './api';
import {
  StudentIdentityHeader,
  STUDENT_ID_STORAGE_KEY,
  isValidStudentId
} from './StudentIdentityHeader';
import './App.css';

function getRemainingSeconds(expiresAtIso: string, nowMs: number): number {
  const targetMs = Date.parse(expiresAtIso);
  if (Number.isNaN(targetMs)) {
    return 0;
  }
  return Math.max(0, Math.ceil((targetMs - nowMs) / 1000));
}

function formatCountdown(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
}

function resolveEffectiveStatus(item: MyHoldItem, nowMs: number): HoldStatus {
  if (item.status === 'HELD' && getRemainingSeconds(item.expiresAt, nowMs) <= 0) {
    return 'EXPIRED';
  }
  return item.status;
}

function getStatusLabel(status: HoldStatus): string {
  switch (status) {
    case 'HELD':
      return 'HELD (সাময়িক সংরক্ষিত)';
    case 'CONFIRMED':
      return 'CONFIRMED (ভর্তি নিশ্চিত)';
    case 'EXPIRED':
      return 'EXPIRED (সময় শেষ)';
    case 'CANCELLED':
      return 'CANCELLED (বাতিল)';
  }
}

export default function App() {
  const queryClient = useQueryClient();

  const [savedStudentId, setSavedStudentId] = useState<string>(() => {
    const initial = localStorage.getItem(STUDENT_ID_STORAGE_KEY)?.trim() ?? '';
    return isValidStudentId(initial) ? initial : '';
  });

  const [studentIdInput, setStudentIdInput] = useState<string>(() => {
    return localStorage.getItem(STUDENT_ID_STORAGE_KEY)?.trim() ?? '';
  });

  const [classNameInput, setClassNameInput] = useState('');
  const [totalSeatsInput, setTotalSeatsInput] = useState('120');

  const [feedbackBanner, setFeedbackBanner] = useState<{
    type: 'success' | 'error';
    message: string;
  } | null>(null);

  const [selectedHoldId, setSelectedHoldId] = useState<string | null>(null);
  const [nowMs, setNowMs] = useState<number>(() => Date.now());

  // 1-second ticker for live countdown and automatic HELD -> EXPIRED UI flip without reload (Rule 9)
  useEffect(() => {
    const timer = window.setInterval(() => {
      setNowMs(Date.now());
    }, 1000);
    return () => window.clearInterval(timer);
  }, []);

  // Poll classes every 3 seconds (Rule 10)
  const classesQuery = useQuery({
    queryKey: ['classes'],
    queryFn: () => admissionApi.getClasses(),
    refetchInterval: 3000,
    retry: 1
  });

  // Poll student's applications every 3 seconds when a valid 17-digit ID is active (Rule 11)
  const myHoldsQuery = useQuery({
    queryKey: ['myHolds', savedStudentId],
    queryFn: () => admissionApi.getMyHolds(savedStudentId),
    enabled: isValidStudentId(savedStudentId),
    refetchInterval: 3000,
    retry: 1
  });

  // Automatically sync active student ID as soon as user types 17 digits or submits
  const handleApplyStudentId = (e?: React.FormEvent) => {
    e?.preventDefault();
    const trimmed = studentIdInput.trim();
    if (!isValidStudentId(trimmed)) {
      setFeedbackBanner({
        type: 'error',
        message: 'সঠিক ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর (শুধুমাত্র ইংরেজি সংখ্যা 0-9) প্রদান করুন।'
      });
      return;
    }

    localStorage.setItem(STUDENT_ID_STORAGE_KEY, trimmed);
    setSavedStudentId(trimmed);
    setFeedbackBanner({
      type: 'success',
      message: `শিক্ষার্থীর জন্ম নিবন্ধন নম্বর (${trimmed}) সেট করা হয়েছে।`
    });
  };

  const handleStudentInputChange = (value: string) => {
    const digitsOnly = value.replace(/\D/g, '').slice(0, 17);
    setStudentIdInput(digitsOnly);
    if (isValidStudentId(digitsOnly)) {
      localStorage.setItem(STUDENT_ID_STORAGE_KEY, digitsOnly);
      setSavedStudentId(digitsOnly);
    } else if (savedStudentId) {
      localStorage.removeItem(STUDENT_ID_STORAGE_KEY);
      setSavedStudentId('');
    }
  };

  const handleClearStudentId = () => {
    localStorage.removeItem(STUDENT_ID_STORAGE_KEY);
    setStudentIdInput('');
    setSavedStudentId('');
    setSelectedHoldId(null);
    setFeedbackBanner({
      type: 'success',
      message: 'পূর্বের জন্ম নিবন্ধন নম্বর সরানো হয়েছে। নতুন ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর দিন।'
    });
  };

  const createClassMutation = useMutation({
    mutationFn: (payload: { name: string; totalSeats: number }) =>
      admissionApi.createClass(payload),
    onSuccess: (created) => {
      setClassNameInput('');
      setTotalSeatsInput('120');
      setFeedbackBanner({
        type: 'success',
        message: `"${created.name}" (${created.totalSeats} আসন) সফলভাবে তৈরি হয়েছে।`
      });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
    },
    onError: (err) => {
      const msg =
        err instanceof ApiRequestError
          ? err.userMessage
          : 'ক্লাস তৈরি করতে সমস্যা হয়েছে।';
      setFeedbackBanner({ type: 'error', message: msg });
    }
  });

  const createHoldMutation = useMutation({
    mutationFn: (classId: string) => {
      if (!isValidStudentId(savedStudentId)) {
        throw new ApiRequestError(
          400,
          'MISSING_STUDENT_ID',
          'আবেদন করার আগে উপরে আপনার ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর দিন।'
        );
      }
      return admissionApi.createHold(classId, savedStudentId);
    },
    onSuccess: (createdHold) => {
      setSelectedHoldId(createdHold.holdId);
      setFeedbackBanner({
        type: 'success',
        message:
          'আপনার আসন সাময়িকভাবে সংরক্ষিত (HELD) হয়েছে! নির্ধারিত সময়ের মধ্যে "ভর্তি ফি দিন" বাটনে ক্লিক করে ভর্তি নিশ্চিত করুন।'
      });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
    },
    onError: (err) => {
      const msg =
        err instanceof ApiRequestError
          ? err.userMessage
          : 'আবেদন সম্পন্ন করা যায়নি।';
      setFeedbackBanner({ type: 'error', message: msg });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
    }
  });

  const confirmHoldMutation = useMutation({
    mutationFn: (holdId: string) => admissionApi.confirmHold(holdId, savedStudentId),
    onSuccess: () => {
      setFeedbackBanner({
        type: 'success',
        message: 'ভর্তি ফি পরিশোধ সফল হয়েছে! আপনার ভর্তি এখন চূড়ান্তভাবে নিশ্চিত (CONFIRMED)।'
      });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
    },
    onError: (err) => {
      const msg =
        err instanceof ApiRequestError
          ? err.userMessage
          : 'ভর্তি নিশ্চিত করতে সমস্যা হয়েছে।';
      setFeedbackBanner({ type: 'error', message: msg });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
    }
  });

  const cancelHoldMutation = useMutation({
    mutationFn: (holdId: string) => admissionApi.cancelHold(holdId, savedStudentId),
    onSuccess: () => {
      setFeedbackBanner({
        type: 'success',
        message: 'আবেদনটি বাতিল (CANCELLED) করা হয়েছে এবং আসনটি উন্মুক্ত করা হয়েছে।'
      });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
    },
    onError: (err) => {
      const msg =
        err instanceof ApiRequestError
          ? err.userMessage
          : 'আবেদন বাতিল করতে সমস্যা হয়েছে।';
      setFeedbackBanner({ type: 'error', message: msg });
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
    }
  });

  const handleCreateClassSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmedName = classNameInput.trim();
    const parsedSeats = Number(totalSeatsInput);

    if (!trimmedName) {
      setFeedbackBanner({
        type: 'error',
        message: 'অনুগ্রহ করে ক্লাস ও শিফটের নাম লিখুন (যেমন: Class 6 - Morning Shift)।'
      });
      return;
    }

    if (!Number.isInteger(parsedSeats) || parsedSeats < 1 || parsedSeats > 10000) {
      setFeedbackBanner({
        type: 'error',
        message: 'মোট আসন সংখ্যা ১ থেকে ১০,০০০-এর মধ্যে পূর্ণসংখ্যা হতে হবে।'
      });
      return;
    }

    createClassMutation.mutate({
      name: trimmedName,
      totalSeats: parsedSeats
    });
  };

  const myHolds = useMemo(() => myHoldsQuery.data ?? [], [myHoldsQuery.data]);

  // Trigger a background refetch once when a HELD application crosses 0 seconds locally
  const hasJustExpiredHold = useMemo(() => {
    return myHolds.some(
      (h) => h.status === 'HELD' && getRemainingSeconds(h.expiresAt, nowMs) === 0
    );
  }, [myHolds, nowMs]);

  useEffect(() => {
    if (hasJustExpiredHold) {
      void queryClient.invalidateQueries({ queryKey: ['classes'] });
      void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
      const followUpTimer = window.setTimeout(() => {
        void queryClient.invalidateQueries({ queryKey: ['classes'] });
        void queryClient.invalidateQueries({ queryKey: ['myHolds', savedStudentId] });
      }, 1200);
      return () => window.clearTimeout(followUpTimer);
    }
  }, [hasJustExpiredHold, queryClient, savedStudentId]);

  // Featured application for Section 3 ("আবেদন"):
  // Prefer user-selected hold, or the latest active HELD hold, or the most recent hold
  const featuredHold: MyHoldItem | null = useMemo(() => {
    if (myHolds.length === 0) {
      return null;
    }
    if (selectedHoldId) {
      const found = myHolds.find((h) => h.holdId === selectedHoldId);
      if (found) return found;
    }
    const activeHold = myHolds.find(
      (h) => resolveEffectiveStatus(h, nowMs) === 'HELD'
    );
    return activeHold ?? myHolds[0];
  }, [myHolds, selectedHoldId, nowMs]);

  const activeOrConfirmedClassIds = useMemo(() => {
    const map = new Map<string, HoldStatus>();
    for (const h of myHolds) {
      const effective = resolveEffectiveStatus(h, nowMs);
      if (effective === 'HELD' || effective === 'CONFIRMED') {
        map.set(h.classId, effective);
      }
    }
    return map;
  }, [myHolds, nowMs]);

  const serverDownError =
    classesQuery.error instanceof ApiRequestError &&
    classesQuery.error.code === 'NETWORK_ERROR'
      ? classesQuery.error.userMessage
      : null;

  return (
    <div className="app-shell">
      {/* Top Header with 17-digit Birth Registration Number & Portal Navigation */}
      <StudentIdentityHeader
        savedStudentId={savedStudentId}
        studentIdInput={studentIdInput}
        onInputChange={handleStudentInputChange}
        onSubmit={handleApplyStudentId}
        onClear={handleClearStudentId}
      />

      <main className="container main-content">
        {/* Global Server Unreachable Warning (Rule 12) */}
        {serverDownError && (
          <div className="alert alert-error" role="alert">
            <strong>সার্ভার সংযোগ বিচ্ছিন্ন:</strong> {serverDownError}
          </div>
        )}

        {/* User Action Feedback Banner (Rule 12) */}
        {feedbackBanner && (
          <div
            className={`alert ${
              feedbackBanner.type === 'error' ? 'alert-error' : 'alert-success'
            }`}
            role="status"
          >
            <span>{feedbackBanner.message}</span>
            <button
              type="button"
              className="alert-close"
              onClick={() => setFeedbackBanner(null)}
              aria-label="বন্ধ করুন"
            >
              ✕
            </button>
          </div>
        )}

        <div className="grid-top">
          {/* 1. Class তৈরি (School Admin) */}
          <section id="admin-section" className="card">
            <div className="card-header">
              <h2>১. Class তৈরি (School Admin)</h2>
              <span className="badge-tag">প্রশাসক প্যানেল</span>
            </div>
            <p className="section-desc">
              প্রতিটি শিফটকে আলাদা ক্লাস হিসেবে নতুন আসন সংখ্যাসহ যুক্ত করুন।
            </p>

            <form onSubmit={handleCreateClassSubmit} className="admin-form">
              <div className="form-group">
                <label htmlFor="class-name-input">ক্লাস ও শিফটের নাম</label>
                <input
                  id="class-name-input"
                  type="text"
                  placeholder="যেমন: Class 6 - Morning Shift"
                  value={classNameInput}
                  onChange={(e) => setClassNameInput(e.target.value)}
                  className="input"
                  maxLength={150}
                />
              </div>

              <div className="form-group">
                <label htmlFor="total-seats-input">মোট আসন সংখ্যা (Total Seats)</label>
                <input
                  id="total-seats-input"
                  type="number"
                  min={1}
                  max={10000}
                  value={totalSeatsInput}
                  onChange={(e) => setTotalSeatsInput(e.target.value)}
                  className="input"
                />
              </div>

              <button
                type="submit"
                className="btn btn-primary"
                disabled={createClassMutation.isPending}
              >
                {createClassMutation.isPending ? 'তৈরি হচ্ছে...' : 'নতুন Class তৈরি করুন'}
              </button>
            </form>
          </section>

          {/* 3. আবেদন (Active Hold / Current Application Status, Countdown, Pay & Cancel) */}
          <section id="active-hold-section" className="card highlight-card">
            <div className="card-header">
              <h2>৩. আবেদন (চলমান আবেদনের অবস্থা ও ফি পরিশোধ)</h2>
              <span className="badge-tag">তাৎক্ষণিক পেমেন্ট সিমুলেশন</span>
            </div>

            {!isValidStudentId(savedStudentId) ? (
              <div className="empty-state">
                উপরে আপনার ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর দিলে আপনার চলমান আবেদন ও ফি পরিশোধের অপশন এখানে দেখা যাবে।
              </div>
            ) : !featuredHold ? (
              <div className="empty-state">
                আপনার এখনো কোনো আবেদন নেই। নিচের <strong>Class তালিকা</strong> থেকে পছন্দের ক্লাসে{' '}
                <strong>“আবেদন করুন”</strong> বাটনে ক্লিক করুন।
              </div>
            ) : (
              (() => {
                const effectiveStatus = resolveEffectiveStatus(featuredHold, nowMs);
                const remainingSecs = getRemainingSeconds(featuredHold.expiresAt, nowMs);
                const isHeldAndUnexpired = effectiveStatus === 'HELD' && remainingSecs > 0;

                return (
                  <div className="active-hold-panel">
                    <div className="active-hold-row">
                      <div>
                        <div className="active-hold-class">{featuredHold.className}</div>
                        <div className="active-hold-meta">
                          Application ID: <code>{featuredHold.holdId}</code>
                        </div>
                      </div>
                      <span className={`status-badge status-${effectiveStatus.toLowerCase()}`}>
                        {getStatusLabel(effectiveStatus)}
                      </span>
                    </div>

                    <div className="countdown-box">
                      {isHeldAndUnexpired ? (
                        <>
                          <span className="countdown-label">
                            আসন সংরক্ষণের বাকি সময় (Expiry Countdown):
                          </span>
                          <span className="countdown-timer">
                            {formatCountdown(remainingSecs)} ({remainingSecs} সেকেন্ড)
                          </span>
                        </>
                      ) : effectiveStatus === 'CONFIRMED' ? (
                        <span className="countdown-confirmed">
                          অভিনন্দন! আপনার ভর্তি চূড়ান্তভাবে নিশ্চিত (CONFIRMED) হয়েছে।
                        </span>
                      ) : effectiveStatus === 'EXPIRED' ? (
                        <span className="countdown-expired">
                          নির্ধারিত সময়ের মধ্যে ফি পরিশোধ না করায় আবেদনটি EXPIRED হয়ে গেছে এবং আসনটি অন্যদের জন্য খালি হয়ে গেছে।
                        </span>
                      ) : (
                        <span className="countdown-cancelled">
                          আবেদনটি বাতিল (CANCELLED) করা হয়েছে।
                        </span>
                      )}
                    </div>

                    <div className="action-buttons">
                      <button
                        type="button"
                        className="btn btn-success"
                        disabled={!isHeldAndUnexpired || confirmHoldMutation.isPending}
                        onClick={() => confirmHoldMutation.mutate(featuredHold.holdId)}
                      >
                        {confirmHoldMutation.isPending ? 'ফি জমা হচ্ছে...' : 'ভর্তি ফি দিন'}
                      </button>

                      <button
                        type="button"
                        className="btn btn-danger"
                        disabled={!isHeldAndUnexpired || cancelHoldMutation.isPending}
                        onClick={() => cancelHoldMutation.mutate(featuredHold.holdId)}
                      >
                        {cancelHoldMutation.isPending ? 'বাতিল হচ্ছে...' : 'বাতিল'}
                      </button>
                    </div>
                  </div>
                );
              })()
            )}
          </section>
        </div>

        {/* 2. Class তালিকা */}
        <section id="classes-section" className="card">
          <div className="card-header">
            <div>
              <h2>২. Class তালিকা (সকল ক্লাস ও আসন সংখ্যা)</h2>
              <p className="section-desc">
                প্রতি ৩ সেকেন্ড পর পর আসন সংখ্যা স্বয়ংক্রিয়ভাবে আপডেট হয় (পেইজ রিলোড ছাড়াই)।
              </p>
            </div>
            <span className="live-indicator">● Live Auto-Refresh (3s)</span>
          </div>

          {classesQuery.isLoading ? (
            <div className="empty-state">ক্লাস তালিকা লোড হচ্ছে...</div>
          ) : classesQuery.isError && !classesQuery.data ? (
            <div className="empty-state error-text">
              ক্লাস তালিকা লোড করা যায়নি। সার্ভার চালু আছে কি না যাচাই করুন।
            </div>
          ) : (classesQuery.data ?? []).length === 0 ? (
            <div className="empty-state">
              এখনো কোনো ক্লাস তৈরি করা হয়নি। উপরের “Class তৈরি” ফর্ম থেকে একটি নতুন ক্লাস তৈরি করুন।
            </div>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>ক্লাস ও শিফট (Class Name)</th>
                    <th className="num-col">মোট আসন (Total)</th>
                    <th className="num-col">আটকে আছে (Held)</th>
                    <th className="num-col">ভর্তি নিশ্চিত (Confirmed)</th>
                    <th className="num-col">খালি আসন (Available)</th>
                    <th className="action-col">আবেদন</th>
                  </tr>
                </thead>
                <tbody>
                  {(classesQuery.data ?? []).map((cls: ClassSummary) => {
                    const existingStatus = activeOrConfirmedClassIds.get(cls.id);
                    const noSeatsLeft = cls.availableSeats <= 0;
                    const canApply =
                      isValidStudentId(savedStudentId) &&
                      !noSeatsLeft &&
                      !existingStatus &&
                      !createHoldMutation.isPending;

                    return (
                      <tr key={cls.id}>
                        <td>
                          <div className="class-title">{cls.name}</div>
                          <div className="class-id-sub">ID: {cls.id}</div>
                        </td>
                        <td className="num-col">{cls.totalSeats}</td>
                        <td className="num-col held-num">{cls.heldSeats}</td>
                        <td className="num-col confirmed-num">{cls.confirmedSeats}</td>
                        <td className="num-col">
                          <span
                            className={`seat-pill ${
                              cls.availableSeats > 0 ? 'seat-available' : 'seat-full'
                            }`}
                          >
                            {cls.availableSeats}
                          </span>
                        </td>
                        <td className="action-col">
                          {existingStatus ? (
                            <span className={`status-badge status-${existingStatus.toLowerCase()}`}>
                              {existingStatus === 'CONFIRMED' ? 'ভর্তি নিশ্চিত' : 'আবেদন চলমান (HELD)'}
                            </span>
                          ) : (
                            <button
                              type="button"
                              className="btn btn-primary btn-sm"
                              disabled={!canApply}
                              title={
                                !isValidStudentId(savedStudentId)
                                  ? 'আগে উপরে ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর দিন'
                                  : noSeatsLeft
                                  ? 'এই ক্লাসে কোনো আসন খালি নেই'
                                  : 'আসন সংরক্ষণ করতে ক্লিক করুন'
                              }
                              onClick={() => createHoldMutation.mutate(cls.id)}
                            >
                              {noSeatsLeft ? 'আসন পূর্ণ' : 'আবেদন করুন'}
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {/* 4. আমার আবেদন */}
        <section id="my-holds-section" className="card">
          <div className="card-header">
            <div>
              <h2>৪. আমার আবেদন (শিক্ষার্থীর সকল আবেদনের তালিকা)</h2>
              <p className="section-desc">
                পেইজ রিফ্রেশ বা ব্রাউজার বন্ধ করে আবার খুললেও আপনার সকল আবেদনের সর্বশেষ অবস্থা এখানে দেখা যাবে।
              </p>
            </div>
            {isValidStudentId(savedStudentId) && (
              <span className="badge-tag">Student ID: {savedStudentId}</span>
            )}
          </div>

          {!isValidStudentId(savedStudentId) ? (
            <div className="empty-state">
              আপনার আবেদনের তালিকা দেখতে উপরে ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর প্রদান করুন।
            </div>
          ) : myHoldsQuery.isLoading ? (
            <div className="empty-state">আপনার আবেদনের তথ্য লোড হচ্ছে...</div>
          ) : myHolds.length === 0 ? (
            <div className="empty-state">
              এই জন্ম নিবন্ধন নম্বরে ({savedStudentId}) এখনো কোনো আবেদন পাওয়া যায়নি।
            </div>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>ক্লাসের নাম (Class)</th>
                    <th>অবস্থা (Status)</th>
                    <th>বাকি সময় / মেয়াদ (Expiry)</th>
                    <th className="action-col">পদক্ষেপ (Actions)</th>
                  </tr>
                </thead>
                <tbody>
                  {myHolds.map((item) => {
                    const effectiveStatus = resolveEffectiveStatus(item, nowMs);
                    const remainingSecs = getRemainingSeconds(item.expiresAt, nowMs);
                    const isHeldAndUnexpired = effectiveStatus === 'HELD' && remainingSecs > 0;

                    return (
                      <tr
                        key={item.holdId}
                        className={
                          featuredHold?.holdId === item.holdId ? 'selected-row' : ''
                        }
                        onClick={() => setSelectedHoldId(item.holdId)}
                      >
                        <td>
                          <div className="class-title">{item.className}</div>
                          <div className="class-id-sub">Hold ID: {item.holdId}</div>
                        </td>
                        <td>
                          <span
                            className={`status-badge status-${effectiveStatus.toLowerCase()}`}
                          >
                            {getStatusLabel(effectiveStatus)}
                          </span>
                        </td>
                        <td>
                          {isHeldAndUnexpired ? (
                            <span className="countdown-inline">
                              ⏱ {formatCountdown(remainingSecs)} বাকি
                            </span>
                          ) : (
                            <span className="muted-text">
                              {new Date(item.expiresAt).toLocaleTimeString()}
                            </span>
                          )}
                        </td>
                        <td className="action-col" onClick={(e) => e.stopPropagation()}>
                          <div className="table-actions">
                            <button
                              type="button"
                              className="btn btn-success btn-sm"
                              disabled={!isHeldAndUnexpired || confirmHoldMutation.isPending}
                              onClick={() => confirmHoldMutation.mutate(item.holdId)}
                            >
                              ভর্তি ফি দিন
                            </button>
                            <button
                              type="button"
                              className="btn btn-danger btn-sm"
                              disabled={!isHeldAndUnexpired || cancelHoldMutation.isPending}
                              onClick={() => cancelHoldMutation.mutate(item.holdId)}
                            >
                              বাতিল
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
