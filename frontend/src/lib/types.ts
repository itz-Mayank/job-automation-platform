export type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
export type ScheduleType = "Manual" | "Interval";
export type JobStatus = "Active" | "Paused";
export type ExecutionStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "Retrying" | "Cancelled";
export type ExecutionTrigger = "Manual" | "Scheduled" | "Retry";

export interface UserDto {
  id: string;
  email: string;
  createdAt: string;
}

export interface AuthResponse {
  token: string;
  user: UserDto;
}

export interface JobDto {
  id: string;
  name: string;
  description: string | null;
  jobType: string;
  status: JobStatus;
  httpMethod: HttpMethod;
  url: string;
  headers: Record<string, string> | null;
  body: string | null;
  scheduleType: ScheduleType;
  scheduleValueSeconds: number | null;
  timeoutSeconds: number;
  maxRetries: number;
  retryDelaySeconds: number;
  lastRunAt: string | null;
  nextRunAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ExecutionDto {
  id: string;
  jobId: string;
  jobName: string;
  status: ExecutionStatus;
  attempt: number;
  maxAttempts: number;
  workerId: string | null;
  trigger: ExecutionTrigger;
  retryOfExecutionId: string | null;
  scheduledAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  durationMs: number | null;
  httpStatusCode: number | null;
  responseBody: string | null;
  errorMessage: string | null;
  nextAttemptAt: string | null;
  willRetry: boolean;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface DashboardSummaryDto {
  totalJobs: number;
  activeJobs: number;
  pausedJobs: number;
  runningExecutions: number;
  successfulExecutions: number;
  failedExecutions: number;
  recentExecutions: ExecutionDto[];
}

export interface CreateJobRequest {
  name: string;
  description?: string | null;
  httpMethod: HttpMethod;
  url: string;
  headers?: Record<string, string> | null;
  body?: string | null;
  scheduleType: ScheduleType;
  scheduleValueSeconds?: number | null;
  timeoutSeconds: number;
  maxRetries: number;
  retryDelaySeconds: number;
}

export type UpdateJobRequest = CreateJobRequest;

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
}

export const INTERVAL_OPTIONS: { label: string; seconds: number }[] = [
  { label: "Every 1 minute", seconds: 60 },
  { label: "Every 5 minutes", seconds: 300 },
  { label: "Every 15 minutes", seconds: 900 },
  { label: "Every hour", seconds: 3600 },
  { label: "Every day", seconds: 86400 },
];
