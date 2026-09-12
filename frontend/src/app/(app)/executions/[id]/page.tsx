"use client";

import { use } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { executionsApi, extractErrorMessage } from "@/lib/api-client";
import { ExecutionStatusBadge } from "@/components/StatusBadge";
import { Button, Card, EmptyState, ErrorBanner, Spinner } from "@/components/ui";
import { formatDateTime, formatDuration, formatRelative } from "@/lib/format";

export default function ExecutionDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const queryClient = useQueryClient();

  const { data: execution, isLoading, error } = useQuery({
    queryKey: ["execution", id],
    queryFn: () => executionsApi.get(id),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "Pending" || status === "Running" || status === "Retrying" ? 3000 : false;
    },
  });

  const retryMutation = useMutation({
    mutationFn: () => executionsApi.retry(id),
    onSuccess: (newExecution) => {
      queryClient.invalidateQueries({ queryKey: ["job-executions"] });
      router.push(`/executions/${newExecution.id}`);
    },
  });

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-6 w-6 text-slate-400" />
      </div>
    );
  }

  if (error || !execution) {
    return <EmptyState title="Execution not found" description="It may belong to another account." />;
  }

  const isTerminal = ["Succeeded", "Failed", "Cancelled"].includes(execution.status);

  return (
    <div className="max-w-3xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href={`/jobs/${execution.jobId}`} className="text-sm text-slate-500 hover:underline">
            ← {execution.jobName}
          </Link>
          <div className="mt-1 flex items-center gap-2">
            <h1 className="text-xl font-semibold text-slate-900">Execution details</h1>
            <ExecutionStatusBadge status={execution.status} />
          </div>
          <p className="mt-1 font-mono text-xs text-slate-400">{execution.id}</p>
        </div>
        {execution.status === "Failed" && (
          <div className="text-right">
            <Button onClick={() => retryMutation.mutate()} isLoading={retryMutation.isPending}>
              Retry execution
            </Button>
            {retryMutation.error && (
              <p className="mt-1 text-xs text-red-600">{extractErrorMessage(retryMutation.error)}</p>
            )}
          </div>
        )}
      </div>

      {/* WHAT failed / WHY, front and center for a failing or retrying execution */}
      {(execution.status === "Failed" || execution.status === "Retrying") && execution.errorMessage && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-4">
          <p className="text-sm font-semibold text-red-800">
            {execution.status === "Failed" ? "This execution failed" : "This attempt failed — a retry is scheduled"}
          </p>
          <p className="mt-1 text-sm text-red-700">{execution.errorMessage}</p>
          {execution.status === "Retrying" && execution.nextAttemptAt && (
            <p className="mt-2 text-sm text-red-700">
              Will retry attempt {execution.attempt + 1}/{execution.maxAttempts}{" "}
              <span className="font-medium">{formatRelative(execution.nextAttemptAt)}</span> (
              {formatDateTime(execution.nextAttemptAt)}).
            </p>
          )}
          {execution.status === "Failed" && (
            <p className="mt-2 text-sm text-red-700">
              {execution.attempt >= execution.maxAttempts
                ? `All ${execution.maxAttempts} attempts were used — this execution will not retry automatically.`
                : "This error was not retryable."}
            </p>
          )}
        </div>
      )}

      {execution.status === "Succeeded" && (
        <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800">
          Succeeded on attempt {execution.attempt}/{execution.maxAttempts} with HTTP {execution.httpStatusCode}.
        </div>
      )}

      <Card className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
        <Field label="Attempt">
          {execution.attempt} / {execution.maxAttempts}
        </Field>
        <Field label="Trigger">{execution.trigger}</Field>
        <Field label="Worker">
          <span className="font-mono text-xs">{execution.workerId ?? "not yet claimed"}</span>
        </Field>
        <Field label="Scheduled at">{formatDateTime(execution.scheduledAt)}</Field>
        <Field label="Started at">{formatDateTime(execution.startedAt)}</Field>
        <Field label="Finished at">{formatDateTime(execution.finishedAt)}</Field>
        <Field label="Duration">{formatDuration(execution.durationMs)}</Field>
        <Field label="HTTP status">{execution.httpStatusCode ?? "—"}</Field>
        <Field label="Will retry?">
          {execution.status === "Retrying" ? "Yes" : execution.status === "Pending" && !isTerminal ? "Pending first attempt" : "No"}
        </Field>
      </Card>

      {execution.responseBody && (
        <div>
          <p className="mb-2 text-sm font-semibold text-slate-900">Response preview</p>
          <Card className="p-4">
            <pre className="max-h-64 overflow-auto whitespace-pre-wrap text-xs text-slate-700">{execution.responseBody}</pre>
          </Card>
        </div>
      )}

      {execution.retryOfExecutionId && (
        <p className="text-sm text-slate-500">
          This is a manual retry of{" "}
          <Link href={`/executions/${execution.retryOfExecutionId}`} className="font-medium text-slate-900 hover:underline">
            a previous execution
          </Link>
          .
        </p>
      )}

      {retryMutation.isError && <ErrorBanner message={extractErrorMessage(retryMutation.error)} />}
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
