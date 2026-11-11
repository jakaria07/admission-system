export type HoldStatus = 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED';

export interface ClassSummary {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface CreateClassRequest {
  name: string;
  totalSeats: number;
}

export interface HoldCreatedResponse {
  holdId: string;
  classId: string;
  studentId: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface HoldDetailsResponse {
  holdId: string;
  classId: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface HoldConfirmedResponse {
  holdId: string;
  status: HoldStatus;
}

export interface MyHoldItem {
  holdId: string;
  classId: string;
  className: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface ApiProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
}

declare global {
  interface Window {
    __ENV__?: {
      VITE_API_BASE_URL?: string;
    };
  }
}

export function getApiBaseUrl(): string {
  const runtimeUrl = window.__ENV__?.VITE_API_BASE_URL?.trim();
  if (runtimeUrl) {
    return runtimeUrl.replace(/\/+$/, '');
  }

  const buildUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.trim();
  if (buildUrl) {
    return buildUrl.replace(/\/+$/, '');
  }

  return 'http://localhost:8080';
}

export class ApiRequestError extends Error {
  public readonly status: number;
  public readonly code: string;
  public readonly userMessage: string;

  constructor(status: number, code: string, userMessage: string) {
    super(userMessage);
    this.name = 'ApiRequestError';
    this.status = status;
    this.code = code;
    this.userMessage = userMessage;
  }
}

const FRIENDLY_ERROR_MESSAGES: Record<string, string> = {
  NO_SEATS: 'দুঃখিত, এই ক্লাসে বর্তমানে কোনো আসন খালি নেই (No seats available)।',
  DUPLICATE_APPLICATION: 'এই ক্লাসে আপনার ইতিমধ্যে একটি চলমান (HELD) অথবা নিশ্চিত (CONFIRMED) আবেদন রয়েছে।',
  HOLD_EXPIRED: 'আপনার আবেদনের নির্ধারিত সময় শেষ হয়ে গেছে (EXPIRED)। অনুগ্রহ করে আবার নতুন করে আবেদন করুন।',
  HOLD_CANCELLED: 'এই আবেদনটি ইতিমধ্যে বাতিল করা হয়েছে।',
  HOLD_ALREADY_CONFIRMED: 'আপনার ভর্তি ইতিমধ্যে নিশ্চিত (CONFIRMED) করা হয়েছে, এটি বাতিল করা যাবে না।',
  FORBIDDEN_NOT_OWNER: 'আপনি শুধুমাত্র নিজের জন্ম নিবন্ধন নম্বরের আবেদন পরিচালনা করতে পারবেন।',
  INVALID_STUDENT_ID: 'সঠিক ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর (Student ID) প্রদান করুন।',
  MISSING_STUDENT_ID: 'আবেদন করার আগে উপরে আপনার ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর দিন।',
  INVALID_CLASS_NAME: 'ক্লাসের নাম সঠিক নয় (১ থেকে ১৫০ অক্ষরের মধ্যে হতে হবে)।',
  INVALID_TOTAL_SEATS: 'আসন সংখ্যা ১ থেকে ১০,০০০-এর মধ্যে পূর্ণসংখ্যা হতে হবে।',
  DUPLICATE_CLASS_NAME: 'এই নামের একটি ক্লাস ইতিমধ্যে তৈরি করা হয়েছে।',
  CLASS_NOT_FOUND: 'কাঙ্ক্ষিত ক্লাসটি খুঁজে পাওয়া যায়নি।',
  HOLD_NOT_FOUND: 'কাঙ্ক্ষিত আবেদনটি খুঁজে পাওয়া যায়নি।',
  NETWORK_ERROR: 'সার্ভারের সাথে যোগাযোগ করা যাচ্ছে না। ব্যাকএন্ড সার্ভার চালু আছে কি না যাচাই করুন।'
};

export function resolveFriendlyMessage(code?: string, fallbackDetail?: string, status?: number): string {
  if (code && FRIENDLY_ERROR_MESSAGES[code]) {
    return FRIENDLY_ERROR_MESSAGES[code];
  }

  if (status === 409) {
    return fallbackDetail || 'আসন খালি নেই অথবা আবেদনটি ইতিমধ্যে পরিবর্তিত হয়েছে (409 Conflict)।';
  }

  if (status === 400) {
    return fallbackDetail || 'প্রদত্ত তথ্য সঠিক নয়। অনুগ্রহ করে ইনপুট যাচাই করুন।';
  }

  if (status === 403) {
    return FRIENDLY_ERROR_MESSAGES.FORBIDDEN_NOT_OWNER;
  }

  if (status === 404) {
    return fallbackDetail || 'তথ্যটি খুঁজে পাওয়া যায়নি (404 Not Found)।';
  }

  return fallbackDetail || 'একটি অপ্রত্যাশিত সমস্যা হয়েছে। অনুগ্রহ করে কিছুক্ষণ পর আবার চেষ্টা করুন।';
}

async function requestJson<T>(
  path: string,
  options: RequestInit = {},
  studentId?: string
): Promise<T> {
  const baseUrl = getApiBaseUrl();
  const headers = new Headers(options.headers);

  if (options.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (studentId) {
    headers.set('X-Student-Id', studentId);
  }

  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, {
      ...options,
      headers
    });
  } catch {
    throw new ApiRequestError(
      0,
      'NETWORK_ERROR',
      FRIENDLY_ERROR_MESSAGES.NETWORK_ERROR
    );
  }

  if (!response.ok) {
    let problem: ApiProblemDetails | null = null;
    try {
      problem = (await response.json()) as ApiProblemDetails;
    } catch {
      // Response had no JSON body
    }

    const code = problem?.code || `HTTP_${response.status}`;
    const friendly = resolveFriendlyMessage(problem?.code, problem?.detail, response.status);
    throw new ApiRequestError(response.status, code, friendly);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const admissionApi = {
  getClasses: () => requestJson<ClassSummary[]>('/classes'),

  getClassById: (classId: string) =>
    requestJson<ClassSummary>(`/classes/${encodeURIComponent(classId)}`),

  createClass: (payload: CreateClassRequest) =>
    requestJson<ClassSummary>('/classes', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),

  createHold: (classId: string, studentId: string) =>
    requestJson<HoldCreatedResponse>(
      `/classes/${encodeURIComponent(classId)}/holds`,
      { method: 'POST' },
      studentId
    ),

  getHoldById: (holdId: string) =>
    requestJson<HoldDetailsResponse>(`/holds/${encodeURIComponent(holdId)}`),

  confirmHold: (holdId: string, studentId: string) =>
    requestJson<HoldConfirmedResponse>(
      `/holds/${encodeURIComponent(holdId)}/confirm`,
      { method: 'POST' },
      studentId
    ),

  cancelHold: (holdId: string, studentId: string) =>
    requestJson<void>(
      `/holds/${encodeURIComponent(holdId)}`,
      { method: 'DELETE' },
      studentId
    ),

  getMyHolds: (studentId: string) =>
    requestJson<MyHoldItem[]>('/me/holds', { method: 'GET' }, studentId)
};

