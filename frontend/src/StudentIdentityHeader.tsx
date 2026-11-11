import type { FormEvent } from 'react';

export const STUDENT_ID_STORAGE_KEY = 'admission_student_id';

export function isValidStudentId(value: string): boolean {
  return /^\d{17}$/.test(value.trim());
}

export interface StudentIdentityHeaderProps {
  savedStudentId: string;
  studentIdInput: string;
  onInputChange: (value: string) => void;
  onSubmit: (e?: FormEvent) => void;
  onClear: () => void;
}

export function StudentIdentityHeader({
  savedStudentId,
  studentIdInput,
  onInputChange,
  onSubmit,
  onClear
}: StudentIdentityHeaderProps) {
  const isValid = isValidStudentId(savedStudentId);

  return (
    <header className="top-header">
      <div className="container header-inner">
        <div className="brand-block">
          <h1>অনলাইন স্কুল ভর্তি সিস্টেম</h1>
          <p className="subtitle">
            আগে আবেদন করলে আগে পাবে (First-Come, First-Served) · কোনো লটারি নেই
          </p>
          <nav className="portal-nav" aria-label="Portal Navigation">
            <a href="#admin-section" className="portal-nav-link">
              ১. Class তৈরি (Admin)
            </a>
            <a href="#classes-section" className="portal-nav-link">
              ২. Class তালিকা
            </a>
            <a href="#active-hold-section" className="portal-nav-link">
              ৩. চলমান আবেদন ও পেমেন্ট
            </a>
            <a href="#my-holds-section" className="portal-nav-link">
              ৪. আমার আবেদন
            </a>
          </nav>
        </div>

        <form className="student-id-bar" onSubmit={onSubmit}>
          <label htmlFor="student-id-input" className="student-id-label">
            শিক্ষার্থীর ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর (Student ID):
          </label>
          <div className="student-id-controls">
            <input
              id="student-id-input"
              type="text"
              inputMode="numeric"
              maxLength={17}
              placeholder="যেমন: 20152691234567890"
              value={studentIdInput}
              onChange={(e) => onInputChange(e.target.value)}
              className={`input student-id-input ${
                isValidStudentId(studentIdInput) ? 'input-valid' : ''
              }`}
            />
            <button type="submit" className="btn btn-primary">
              নিশ্চিত করুন
            </button>
            {(studentIdInput.length > 0 || isValid) && (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onClear}
                title="অন্য শিক্ষার্থীর জন্ম নিবন্ধন নম্বর দিতে এটি পরিষ্কার করুন"
              >
                পরিবর্তন
              </button>
            )}
          </div>
          <div className="student-id-hint">
            {isValid ? (
              <span className="status-pill pill-confirmed">
                সক্রিয় জন্ম নিবন্ধন: {savedStudentId} ({studentIdInput.length}/17)
              </span>
            ) : (
              <span className="status-pill pill-warning">
                আবেদন করতে ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর দিন ({studentIdInput.length}/17 অঙ্ক)
              </span>
            )}
          </div>
        </form>
      </div>
    </header>
  );
}

