export function formatDateTime(value: string | null | undefined): string {
  if (!value) return "—";
  return new Date(value).toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

export function formatRelative(value: string | null | undefined): string {
  if (!value) return "—";
  const date = new Date(value);
  const diffMs = date.getTime() - Date.now();
  const diffSec = Math.round(diffMs / 1000);
  const abs = Math.abs(diffSec);

  const units: [string, number][] = [
    ["second", 1],
    ["minute", 60],
    ["hour", 3600],
    ["day", 86400],
  ];

  let unit = "second";
  let value2 = diffSec;
  for (const [name, secondsPer] of units) {
    if (abs < secondsPer * 60 || name === "day") {
      unit = name;
      value2 = Math.round(diffSec / secondsPer);
      break;
    }
  }

  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  return rtf.format(value2, unit as Intl.RelativeTimeFormatUnit);
}

export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return "—";
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

export function formatIntervalLabel(seconds: number | null | undefined): string {
  if (!seconds) return "Manual";
  const presets: Record<number, string> = {
    60: "Every 1 minute",
    300: "Every 5 minutes",
    900: "Every 15 minutes",
    3600: "Every hour",
    86400: "Every day",
  };
  return presets[seconds] ?? `Every ${seconds}s`;
}
