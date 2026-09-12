"use client";

import { useRouter } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { jobsApi } from "@/lib/api-client";
import { JobForm } from "@/components/JobForm";
import type { CreateJobRequest } from "@/lib/types";

export default function NewJobPage() {
  const router = useRouter();
  const queryClient = useQueryClient();

  const createMutation = useMutation({
    mutationFn: (request: CreateJobRequest) => jobsApi.create(request),
    onSuccess: (job) => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      router.push(`/jobs/${job.id}`);
    },
  });

  return (
    <div className="max-w-3xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-900">New job</h1>
        <p className="text-sm text-slate-500">Define an HTTP request to automate.</p>
      </div>
      <JobForm
        onSubmit={async (request) => {
          await createMutation.mutateAsync(request);
        }}
        isSubmitting={createMutation.isPending}
        submitLabel="Create job"
      />
    </div>
  );
}
