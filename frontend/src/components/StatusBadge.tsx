import clsx from "clsx";
import type { ExecutionStatus, JobStatus } from "@/lib/types";

const EXECUTION_STYLES: Record<ExecutionStatus, string> = {
  Pending: "bg-slate-100 text-slate-700 ring-slate-200",
  Running: "bg-blue-100 text-blue-700 ring-blue-200",
  Succeeded: "bg-emerald-100 text-emerald-700 ring-emerald-200",
  Failed: "bg-red-100 text-red-700 ring-red-200",
  Retrying: "bg-amber-100 text-amber-700 ring-amber-200",
  Cancelled: "bg-slate-100 text-slate-500 ring-slate-200",
};

const JOB_STYLES: Record<JobStatus, string> = {
  Active: "bg-emerald-100 text-emerald-700 ring-emerald-200",
  Paused: "bg-slate-100 text-slate-600 ring-slate-200",
};

function Badge({ label, className }: { label: string; className: string }) {
  return (
    <span
      className={clsx(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset",
        className
      )}
    >
      {label}
    </span>
  );
}

export function ExecutionStatusBadge({ status }: { status: ExecutionStatus }) {
  return <Badge label={status} className={EXECUTION_STYLES[status]} />;
}

export function JobStatusBadge({ status }: { status: JobStatus }) {
  return <Badge label={status} className={JOB_STYLES[status]} />;
}
