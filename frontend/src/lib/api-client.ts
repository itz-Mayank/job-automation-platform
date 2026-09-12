import axios, { AxiosError } from "axios";
import type {
  AuthResponse,
  CreateJobRequest,
  DashboardSummaryDto,
  ExecutionDto,
  ExecutionStatus,
  JobDto,
  PagedResult,
  ProblemDetails,
  UpdateJobRequest,
  UserDto,
} from "./types";

const API_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080";

export const apiClient = axios.create({
  baseURL: API_URL,
});

const TOKEN_KEY = "jobforge_token";

export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string) {
  window.localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken() {
  window.localStorage.removeItem(TOKEN_KEY);
}

apiClient.interceptors.request.use((config) => {
  const token = getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

let onUnauthorized: (() => void) | null = null;
export function registerUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError) => {
    if (error.response?.status === 401) {
      clearToken();
      onUnauthorized?.();
    }
    return Promise.reject(error);
  }
);

export function extractErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const problem = error.response?.data as ProblemDetails | undefined;
    if (problem?.detail) return problem.detail;
    if (problem?.title) return problem.title;
    if (error.message) return error.message;
  }
  return "Something went wrong. Please try again.";
}

// ---- Auth ----

export const authApi = {
  register: (email: string, password: string) =>
    apiClient.post<AuthResponse>("/api/auth/register", { email, password }).then((r) => r.data),
  login: (email: string, password: string) =>
    apiClient.post<AuthResponse>("/api/auth/login", { email, password }).then((r) => r.data),
  me: () => apiClient.get<UserDto>("/api/auth/me").then((r) => r.data),
};

// ---- Jobs ----

export const jobsApi = {
  list: (params?: { search?: string; page?: number; pageSize?: number }) =>
    apiClient.get<PagedResult<JobDto>>("/api/jobs", { params }).then((r) => r.data),
  get: (id: string) => apiClient.get<JobDto>(`/api/jobs/${id}`).then((r) => r.data),
  create: (body: CreateJobRequest) => apiClient.post<JobDto>("/api/jobs", body).then((r) => r.data),
  update: (id: string, body: UpdateJobRequest) =>
    apiClient.put<JobDto>(`/api/jobs/${id}`, body).then((r) => r.data),
  remove: (id: string) => apiClient.delete(`/api/jobs/${id}`),
  pause: (id: string) => apiClient.post<JobDto>(`/api/jobs/${id}/pause`).then((r) => r.data),
  resume: (id: string) => apiClient.post<JobDto>(`/api/jobs/${id}/resume`).then((r) => r.data),
  run: (id: string, idempotencyKey?: string) =>
    apiClient
      .post<ExecutionDto>(
        `/api/jobs/${id}/run`,
        null,
        idempotencyKey ? { headers: { "Idempotency-Key": idempotencyKey } } : undefined
      )
      .then((r) => r.data),
  executions: (id: string, params?: { status?: ExecutionStatus; page?: number; pageSize?: number }) =>
    apiClient.get<PagedResult<ExecutionDto>>(`/api/jobs/${id}/executions`, { params }).then((r) => r.data),
};

// ---- Executions ----

export const executionsApi = {
  get: (id: string) => apiClient.get<ExecutionDto>(`/api/executions/${id}`).then((r) => r.data),
  retry: (id: string) => apiClient.post<ExecutionDto>(`/api/executions/${id}/retry`).then((r) => r.data),
};

// ---- Dashboard ----

export const dashboardApi = {
  summary: () => apiClient.get<DashboardSummaryDto>("/api/dashboard/summary").then((r) => r.data),
};
