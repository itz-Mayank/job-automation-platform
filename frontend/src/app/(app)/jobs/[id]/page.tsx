"use client";

import { use, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { jobsApi } from "@/lib/api-client";
import { ExecutionStatusBadge, JobStatusBadge } from "@/components/StatusBadge";
import { Button, Card, ConfirmButton, EmptyState, ErrorBanner, Spinner } from "@/components/ui";
import { formatDateTime, formatDuration, formatIntervalLabel } from "@/lib/format";
import type { ExecutionStatus } from "@/lib/types";

const STATUS_FILTERS: (ExecutionStatus | "All")[] = ["All", "Pending", "Running", "Succeeded", "Failed", "Retrying", "Cancelled"];

export default function JobDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const queryClient = useQueryClient();
  const [statusFilter, setStatusFilter] = useState<ExecutionStatus | "All">("All");

  const { data: job, isLoading: jobLoading, error: jobError } = useQuery({
    queryKey: ["job", id],
    queryFn: () => jobsApi.get(id),
  });

  const { data: executions, isLoading: executionsLoading } = useQuery({
    queryKey: ["job-executions", id, statusFilter],
    queryFn: () => jobsApi.executions(id, { status: statusFilter === "All" ? undefined : statusFilter, pageSize: 50 }),
    refetchInterval: 4000,
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["job", id] });
    queryClient.invalidateQueries({ queryKey: ["job-executions", id] });
  };

  const runMutation = useMutation({ mutationFn: () => jobsApi.run(id), onSuccess: invalidate });
  const pauseMutation = useMutation({ mutationFn: () => jobsApi.pause(id), onSuccess: invalidate });
  const resumeMutation = useMutation({ mutationFn: () => jobsApi.resume(id), onSuccess: invalidate });
  const deleteMutation = useMutation({
    mutationFn: () => jobsApi.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      router.push("/jobs");
    },
  });

  if (jobLoading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-6 w-6 text-slate-400" />
      </div>
    );
  }

  if (jobError || !job) {
    return <EmptyState title="Job not found" description="It may have been deleted, or belong to another account." />;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-semibold text-slate-900">{job.name}</h1>
            <JobStatusBadge status={job.status} />
          </div>
          {job.description && <p className="mt-1 text-sm text-slate-500">{job.description}</p>}
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="secondary"
            onClick={() => runMutation.mutate()}
            isLoading={runMutation.isPending}
            disabled={job.status === "Paused"}
          >
            Run Now
          </Button>
          {job.status === "Active" ? (
            <Button variant="secondary" onClick={() => pauseMutation.mutate()} isLoading={pauseMutation.isPending}>
              Pause
            </Button>
          ) : (
            <Button variant="secondary" onClick={() => resumeMutation.mutate()} isLoading={resumeMutation.isPending}>
              Resume
            </Button>
          )}
          <Link href={`/jobs/${id}/edit`}>
            <Button variant="secondary">Edit</Button>
          </Link>
          <ConfirmButton onConfirm={() => deleteMutation.mutate()} isLoading={deleteMutation.isPending}>
            Delete
          </ConfirmButton>
        </div>
      </div>

      {runMutation.error && <ErrorBanner message="Couldn't start the job. Please try again." />}

      <Card className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-4">
        <Field label="Method & URL">
          <span className="font-mono text-xs break-all">
            {job.httpMethod} {job.url}
          </span>
        </Field>
        <Field label="Schedule">{formatIntervalLabel(job.scheduleValueSeconds)}</Field>
        <Field label="Timeout">{job.timeoutSeconds}s</Field>
        <Field label="Max retries">{job.maxRetries} (base delay {job.retryDelaySeconds}s)</Field>
        <Field label="Last run">{formatDateTime(job.lastRunAt)}</Field>
        <Field label="Next run">{formatDateTime(job.nextRunAt)}</Field>
        <Field label="Created">{formatDateTime(job.createdAt)}</Field>
        <Field label="Updated">{formatDateTime(job.updatedAt)}</Field>
      </Card>

      <div>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-slate-900">Execution history</h2>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as ExecutionStatus | "All")}
            className="rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-slate-500 focus:outline-none"
          >
            {STATUS_FILTERS.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>

        {executionsLoading ? (
          <div className="flex justify-center py-10">
            <Spinner className="h-5 w-5 text-slate-400" />
          </div>
        ) : !executions || executions.items.length === 0 ? (
          <EmptyState title="No executions" description="Run this job to see execution history here." />
        ) : (
          <Card className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-left text-sm">
              <thead className="border-b border-slate-200 text-xs uppercase text-slate-500">
                <tr>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Attempt</th>
                  <th className="px-4 py-3">Trigger</th>
                  <th className="px-4 py-3">Scheduled at</th>
                  <th className="px-4 py-3">Duration</th>
                  <th className="px-4 py-3">HTTP</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {executions.items.map((execution) => (
                  <tr
                    key={execution.id}
                    className="cursor-pointer hover:bg-slate-50"
                    onClick={() => router.push(`/executions/${execution.id}`)}
                  >
                    <td className="px-4 py-3">
                      <ExecutionStatusBadge status={execution.status} />
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {execution.attempt}/{execution.maxAttempts}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{execution.trigger}</td>
                    <td className="px-4 py-3 text-slate-600">{formatDateTime(execution.scheduledAt)}</td>
                    <td className="px-4 py-3 text-slate-600">{formatDuration(execution.durationMs)}</td>
                    <td className="px-4 py-3 text-slate-600">{execution.httpStatusCode ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="text-xs font-medium uppercase text-slate-400">{label}</p>
      <p className="mt-0.5 text-sm text-slate-800">{children}</p>
    </div>
  );
}
