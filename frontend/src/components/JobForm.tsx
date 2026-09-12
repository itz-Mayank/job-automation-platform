"use client";

import { useState } from "react";
import { Button, ErrorBanner } from "@/components/ui";
import { INTERVAL_OPTIONS, type CreateJobRequest, type HttpMethod, type JobDto, type ScheduleType } from "@/lib/types";

const HTTP_METHODS: HttpMethod[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

interface HeaderRow {
  key: string;
  value: string;
}

function headersToRows(headers: Record<string, string> | null | undefined): HeaderRow[] {
  if (!headers || Object.keys(headers).length === 0) return [{ key: "", value: "" }];
  return Object.entries(headers).map(([key, value]) => ({ key, value }));
}

export function JobForm({
  initialJob,
  onSubmit,
  isSubmitting,
  submitLabel,
}: {
  initialJob?: JobDto;
  onSubmit: (request: CreateJobRequest) => Promise<void>;
  isSubmitting: boolean;
  submitLabel: string;
}) {
  const [name, setName] = useState(initialJob?.name ?? "");
  const [description, setDescription] = useState(initialJob?.description ?? "");
  const [httpMethod, setHttpMethod] = useState<HttpMethod>(initialJob?.httpMethod ?? "GET");
  const [url, setUrl] = useState(initialJob?.url ?? "");
  const [headerRows, setHeaderRows] = useState<HeaderRow[]>(headersToRows(initialJob?.headers));
  const [body, setBody] = useState(initialJob?.body ?? "");
  const [scheduleType, setScheduleType] = useState<ScheduleType>(initialJob?.scheduleType ?? "Manual");
  const [scheduleValueSeconds, setScheduleValueSeconds] = useState<number>(
    initialJob?.scheduleValueSeconds ?? INTERVAL_OPTIONS[1].seconds
  );
  const [timeoutSeconds, setTimeoutSeconds] = useState(initialJob?.timeoutSeconds ?? 30);
  const [maxRetries, setMaxRetries] = useState(initialJob?.maxRetries ?? 3);
  const [retryDelaySeconds, setRetryDelaySeconds] = useState(initialJob?.retryDelaySeconds ?? 30);
  const [error, setError] = useState<string | null>(null);

  const canHaveBody = httpMethod === "POST" || httpMethod === "PUT" || httpMethod === "PATCH";

  function updateHeaderRow(index: number, field: "key" | "value", value: string) {
    setHeaderRows((rows) => rows.map((row, i) => (i === index ? { ...row, [field]: value } : row)));
  }

  function addHeaderRow() {
    setHeaderRows((rows) => [...rows, { key: "", value: "" }]);
  }

  function removeHeaderRow(index: number) {
    setHeaderRows((rows) => rows.filter((_, i) => i !== index));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    let parsedBody: string | undefined;
    if (canHaveBody && body.trim()) {
      try {
        JSON.parse(body);
      } catch {
        setError("Body must be valid JSON.");
        return;
      }
      parsedBody = body;
    }

    const headers: Record<string, string> = {};
    for (const row of headerRows) {
      if (row.key.trim()) headers[row.key.trim()] = row.value;
    }

    try {
      await onSubmit({
        name,
        description: description || null,
        httpMethod,
        url,
        headers: Object.keys(headers).length > 0 ? headers : null,
        body: parsedBody ?? null,
        scheduleType,
        scheduleValueSeconds: scheduleType === "Interval" ? scheduleValueSeconds : null,
        timeoutSeconds,
        maxRetries,
        retryDelaySeconds,
      });
    } catch {
      setError("Couldn't save the job. Check your inputs and try again.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-6 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      {error && <ErrorBanner message={error} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label className="block text-sm font-medium text-slate-700">Name</label>
          <input
            required
            maxLength={200}
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
            placeholder="Sync inventory levels"
          />
        </div>
        <div className="sm:col-span-2">
          <label className="block text-sm font-medium text-slate-700">Description (optional)</label>
          <input
            maxLength={1000}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700">Method</label>
          <select
            value={httpMethod}
            onChange={(e) => setHttpMethod(e.target.value as HttpMethod)}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
          >
            {HTTP_METHODS.map((m) => (
              <option key={m} value={m}>
                {m}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700">URL</label>
          <input
            required
            type="url"
            maxLength={2048}
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
            placeholder="https://api.example.com/endpoint"
          />
        </div>
      </div>

      <div>
        <div className="flex items-center justify-between">
          <label className="block text-sm font-medium text-slate-700">Headers (optional)</label>
          <button type="button" onClick={addHeaderRow} className="text-xs font-medium text-slate-600 hover:text-slate-900">
            + Add header
          </button>
        </div>
        <div className="mt-2 space-y-2">
          {headerRows.map((row, i) => (
            <div key={i} className="flex gap-2">
              <input
                placeholder="Header-Name"
                value={row.key}
                onChange={(e) => updateHeaderRow(i, "key", e.target.value)}
                className="w-1/2 rounded-md border border-slate-300 px-3 py-1.5 text-sm focus:border-slate-500 focus:outline-none"
              />
              <input
                placeholder="value"
                value={row.value}
                onChange={(e) => updateHeaderRow(i, "value", e.target.value)}
                className="w-1/2 rounded-md border border-slate-300 px-3 py-1.5 text-sm focus:border-slate-500 focus:outline-none"
              />
              <button
                type="button"
                onClick={() => removeHeaderRow(i)}
                className="px-2 text-sm text-slate-400 hover:text-red-600"
                aria-label="Remove header"
              >
                ✕
              </button>
            </div>
          ))}
        </div>
        <p className="mt-1 text-xs text-slate-400">
          Values you enter here (including Authorization tokens) are sent with every request but never shown back
          in execution history.
        </p>
      </div>

      {canHaveBody && (
        <div>
          <label className="block text-sm font-medium text-slate-700">JSON body (optional)</label>
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={4}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 font-mono text-sm focus:border-slate-500 focus:outline-none"
            placeholder='{"key": "value"}'
          />
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className="block text-sm font-medium text-slate-700">Schedule</label>
          <select
            value={scheduleType}
            onChange={(e) => setScheduleType(e.target.value as ScheduleType)}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
          >
            <option value="Manual">Manual only (Run Now)</option>
            <option value="Interval">Recurring interval</option>
          </select>
        </div>
        {scheduleType === "Interval" && (
          <div>
            <label className="block text-sm font-medium text-slate-700">Interval</label>
            <select
              value={scheduleValueSeconds}
              onChange={(e) => setScheduleValueSeconds(Number(e.target.value))}
              className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
            >
              {INTERVAL_OPTIONS.map((opt) => (
                <option key={opt.seconds} value={opt.seconds}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <div>
          <label className="block text-sm font-medium text-slate-700">Timeout (seconds)</label>
          <input
            type="number"
            min={1}
            max={120}
            required
            value={timeoutSeconds}
            onChange={(e) => setTimeoutSeconds(Number(e.target.value))}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700">Max retries</label>
          <input
            type="number"
            min={0}
            max={10}
            required
            value={maxRetries}
            onChange={(e) => setMaxRetries(Number(e.target.value))}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-700">Retry base delay (seconds)</label>
          <input
            type="number"
            min={1}
            max={3600}
            required
            value={retryDelaySeconds}
            onChange={(e) => setRetryDelaySeconds(Number(e.target.value))}
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
          />
        </div>
      </div>
      <p className="text-xs text-slate-400">
        Retries use exponential backoff: delay = base × 2^(attempt-1). With a {retryDelaySeconds}s base delay,
        retries fire at ~{retryDelaySeconds}s, {retryDelaySeconds * 2}s, {retryDelaySeconds * 4}s, …
      </p>

      <div className="flex justify-end gap-3 border-t border-slate-100 pt-4">
        <Button type="submit" isLoading={isSubmitting}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
