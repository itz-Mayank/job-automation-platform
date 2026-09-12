"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { dashboardApi } from "@/lib/api-client";
import { ExecutionStatusBadge } from "@/components/StatusBadge";
import { Card, EmptyState, Spinner } from "@/components/ui";
import { formatDateTime } from "@/lib/format";

function StatCard({ label, value, tone }: { label: string; value: number; tone?: "success" | "danger" | "info" }) {
  const toneClass =
    tone === "success" ? "text-emerald-600" : tone === "danger" ? "text-red-600" : tone === "info" ? "text-blue-600" : "text-slate-900";
  return (
    <Card className="p-4">
      <p className="text-sm text-slate-500">{label}</p>
      <p className={`mt-1 text-2xl font-semibold ${toneClass}`}>{value}</p>
    </Card>
  );
}

export default function DashboardPage() {
  const { data, isLoading, error } = useQuery({
    queryKey: ["dashboard-summary"],
    queryFn: dashboardApi.summary,
    refetchInterval: 5000,
  });

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-6 w-6 text-slate-400" />
      </div>
    );
  }

  if (error || !data) {
    return <EmptyState title="Couldn't load dashboard" description="Try refreshing the page." />;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-900">Dashboard</h1>
        <p className="text-sm text-slate-500">Overview of your automated jobs and recent executions.</p>
      </div>

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <StatCard label="Total jobs" value={data.totalJobs} />
        <StatCard label="Active jobs" value={data.activeJobs} tone="info" />
        <StatCard label="Paused jobs" value={data.pausedJobs} />
        <StatCard label="Running" value={data.runningExecutions} tone="info" />
        <StatCard label="Succeeded" value={data.successfulExecutions} tone="success" />
        <StatCard label="Failed" value={data.failedExecutions} tone="danger" />
      </div>

      <div>
        <h2 className="mb-3 text-sm font-semibold text-slate-900">Recent executions</h2>
        {data.recentExecutions.length === 0 ? (
          <EmptyState
            title="No executions yet"
            description="Create a job and run it to see execution history here."
            action={
              <Link href="/jobs/new" className="text-sm font-medium text-slate-900 hover:underline">
                Create your first job →
              </Link>
            }
          />
        ) : (
          <Card>
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-200 text-xs uppercase text-slate-500">
                <tr>
                  <th className="px-4 py-3">Job</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Scheduled at</th>
                  <th className="px-4 py-3">Attempt</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.recentExecutions.map((execution) => (
                  <tr key={execution.id} className="hover:bg-slate-50">
                    <td className="px-4 py-3">
                      <Link href={`/executions/${execution.id}`} className="font-medium text-slate-900 hover:underline">
                        {execution.jobName}
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <ExecutionStatusBadge status={execution.status} />
                    </td>
                    <td className="px-4 py-3 text-slate-600">{formatDateTime(execution.scheduledAt)}</td>
                    <td className="px-4 py-3 text-slate-600">
                      {execution.attempt}/{execution.maxAttempts}
                    </td>
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
