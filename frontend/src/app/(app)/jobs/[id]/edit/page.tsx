"use client";

import { use } from "react";
import { useRouter } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { jobsApi } from "@/lib/api-client";
import { JobForm } from "@/components/JobForm";
import { EmptyState, Spinner } from "@/components/ui";
import type { CreateJobRequest } from "@/lib/types";

export default function EditJobPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const queryClient = useQueryClient();

  const { data: job, isLoading, error } = useQuery({
    queryKey: ["job", id],
    queryFn: () => jobsApi.get(id),
  });

  const updateMutation = useMutation({
    mutationFn: (request: CreateJobRequest) => jobsApi.update(id, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["job", id] });
      router.push(`/jobs/${id}`);
    },
  });

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-6 w-6 text-slate-400" />
      </div>
    );
  }

  if (error || !job) {
    return <EmptyState title="Job not found" description="It may have been deleted." />;
  }

  return (
    <div className="max-w-3xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-900">Edit job</h1>
        <p className="text-sm text-slate-500">{job.name}</p>
      </div>
      <JobForm
        initialJob={job}
        onSubmit={async (request) => {
          await updateMutation.mutateAsync(request);
        }}
        isSubmitting={updateMutation.isPending}
        submitLabel="Save changes"
      />
    </div>
  );
}
