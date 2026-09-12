"use client";

import { useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { jobsApi } from "@/lib/api-client";
import { JobStatusBadge } from "@/components/StatusBadge";
import { Button, Card, ConfirmButton, EmptyState, ErrorBanner, Spinner } from "@/components/ui";
import { formatDateTime, formatIntervalLabel } from "@/lib/format";
import type { JobDto } from "@/lib/types";

export default function JobsPage() {
  const [search, setSearch] = useState("");
  const queryClient = useQueryClient();

  const { data, isLoading, error } = useQuery({
    queryKey: ["jobs", search],
    queryFn: () => jobsApi.list({ search: search || undefined, pageSize: 50 }),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["jobs"] });

  const runMutation = useMutation({
    mutationFn: (id: string) => jobsApi.run(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["dashboard-summary"] }),
  });
  const pauseMutation = useMutation({ mutationFn: (id: string) => jobsApi.pause(id), onSuccess: invalidate });
  const resumeMutation = useMutation({ mutationFn: (id: string) => jobsApi.resume(id), onSuccess: invalidate });
  const deleteMutation = useMutation({ mutationFn: (id: string) => jobsApi.remove(id), onSuccess: invalidate });

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-900">Jobs</h1>
          <p className="text-sm text-slate-500">Manage your automated HTTP jobs.</p>
        </div>
        <Link href="/jobs/new">
          <Button>+ New job</Button>
        </Link>
      </div>

      <input
        type="search"
        placeholder="Search jobs by name…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="w-full max-w-sm rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
      />

      {runMutation.error && <ErrorBanner message="Couldn't start the job. Please try again." />}

      {isLoading ? (
        <div className="flex justify-center py-16">
          <Spinner className="h-6 w-6 text-slate-400" />
        </div>
      ) : error ? (
        <ErrorBanner message="Couldn't load jobs." />
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          title={search ? "No jobs match your search" : "No jobs yet"}
          description={search ? undefined : "Create your first automated HTTP job to get started."}
          action={
            !search && (
              <Link href="/jobs/new">
                <Button>+ New job</Button>
              </Link>
            )
          }
        />
      ) : (
        <Card className="overflow-x-auto">
          <table className="w-full min-w-[720px] text-left text-sm">
            <thead className="border-b border-slate-200 text-xs uppercase text-slate-500">
              <tr>
                <th className="px-4 py-3">Name</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Schedule</th>
                <th className="px-4 py-3">Last run</th>
                <th className="px-4 py-3">Next run</th>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.items.map((job: JobDto) => (
                <tr key={job.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3">
                    <Link href={`/jobs/${job.id}`} className="font-medium text-slate-900 hover:underline">
                      {job.name}
                    </Link>
                    <div className="text-xs text-slate-500">
                      {job.httpMethod} {job.url}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <JobStatusBadge status={job.status} />
                  </td>
                  <td className="px-4 py-3 text-slate-600">{formatIntervalLabel(job.scheduleValueSeconds)}</td>
                  <td className="px-4 py-3 text-slate-600">{formatDateTime(job.lastRunAt)}</td>
                  <td className="px-4 py-3 text-slate-600">{formatDateTime(job.nextRunAt)}</td>
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-2">
                      <Button
                        variant="secondary"
                        onClick={() => runMutation.mutate(job.id)}
                        isLoading={runMutation.isPending && runMutation.variables === job.id}
                        disabled={job.status === "Paused"}
                        title={job.status === "Paused" ? "Resume the job before running it" : "Run now"}
                      >
                        Run Now
                      </Button>
                      {job.status === "Active" ? (
                        <Button
                          variant="secondary"
                          onClick={() => pauseMutation.mutate(job.id)}
                          isLoading={pauseMutation.isPending && pauseMutation.variables === job.id}
                        >
                          Pause
                        </Button>
                      ) : (
                        <Button
                          variant="secondary"
                          onClick={() => resumeMutation.mutate(job.id)}
                          isLoading={resumeMutation.isPending && resumeMutation.variables === job.id}
                        >
                          Resume
                        </Button>
                      )}
                      <Link href={`/jobs/${job.id}/edit`}>
                        <Button variant="secondary">Edit</Button>
                      </Link>
                      <ConfirmButton
                        onConfirm={() => deleteMutation.mutate(job.id)}
                        isLoading={deleteMutation.isPending && deleteMutation.variables === job.id}
                      >
                        Delete
                      </ConfirmButton>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </div>
  );
}
