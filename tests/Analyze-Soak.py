"""Summarize bounded JSONL telemetry without retaining any worker output in RAM."""
import argparse
import datetime as dt
import json
import re
from pathlib import Path

MIB = 1024 * 1024


def load_samples(path):
    result = []
    if path.exists():
        with path.open(encoding="utf-8-sig") as stream:
            for line in stream:
                try:
                    result.append(json.loads(line))
                except json.JSONDecodeError:
                    # A live writer may still be completing its final line.
                    continue
    return result


def slope(samples, key):
    if len(samples) < 10:
        return None
    mean_x = sum(s["elapsedSeconds"] for s in samples) / len(samples)
    mean_y = sum(s[key] for s in samples) / len(samples)
    denominator = sum((s["elapsedSeconds"] - mean_x) ** 2 for s in samples)
    if not denominator:
        return None
    return round(sum((s["elapsedSeconds"] - mean_x) * (s[key] - mean_y) for s in samples) / denominator * 3600 / MIB, 3)


def analyze(run_root):
    manifest = json.loads((run_root / "manifest.json").read_text(encoding="utf-8-sig"))
    workers = []
    for worker in manifest["workers"]:
        directory = Path(worker["directory"])
        samples = load_samples(directory / "samples.jsonl")
        summary_path = directory / "summary.json"
        summary = json.loads(summary_path.read_text(encoding="utf-8-sig")) if summary_path.exists() else None
        row = {"variant": worker["variant"], "workload": worker["workload"], "pid": worker["pid"],
               "state": summary["state"] if summary else "running", "summary": summary, "directory": str(directory)}
        if samples:
            first, last = samples[0], samples[-1]
            measured = [s for s in samples if s["phase"] in ("running", "end-before-gc")]
            latter = [s for s in measured if s["elapsedSeconds"] >= max(300, last["elapsedSeconds"] / 2)]
            recent = [s for s in measured if s["elapsedSeconds"] >= last["elapsedSeconds"] - 180]
            timestamps = [dt.datetime.fromisoformat(re.sub(r"(\.\d{6})\d+", r"\1", s["utc"]).replace("Z", "+00:00")) for s in samples]
            maximum_gap = max(((b - a).total_seconds() for a, b in zip(timestamps, timestamps[1:])), default=0)
            row.update({
                "cycles": last["cycles"], "commands": last["commands"], "reads": last["reads"],
                "elapsedHours": round(last["elapsedSeconds"] / 3600, 4),
                "latestUtc": last["utc"], "samples": len(samples),
                "initialPrivateMiB": round(first["privateBytes"] / MIB, 3),
                "latestPrivateMiB": round(last["privateBytes"] / MIB, 3),
                "peakPrivateMiB": round(max(s["privateBytes"] for s in samples) / MIB, 3),
                "privateGrowthMiB": round((last["privateBytes"] - first["privateBytes"]) / MIB, 3),
                "managedGrowthMiB": round((last["managedBytes"] - first["managedBytes"]) / MIB, 3),
                "handleGrowth": last["handles"] - first["handles"],
                "latestHandles": last["handles"],
                "gen0CollectionsDuringMeasuredLoop": max((s["gen0"] for s in measured), default=first["gen0"]) - first["gen0"],
                "gen2CollectionsDuringMeasuredLoop": max((s["gen2"] for s in measured), default=first["gen2"]) - first["gen2"],
                "recentWindowSeconds": round(recent[-1]["elapsedSeconds"] - recent[0]["elapsedSeconds"], 3) if recent else 0,
                "recentPrivateRangeMiB": round((max(s["privateBytes"] for s in recent) - min(s["privateBytes"] for s in recent)) / MIB, 3) if recent else None,
                "recentPrivateChangeMiB": round((recent[-1]["privateBytes"] - recent[0]["privateBytes"]) / MIB, 3) if recent else None,
                "latterHalfPrivateSlopeMiBPerHour": slope(latter, "privateBytes"),
                "latterHalfManagedSlopeMiBPerHour": slope(latter, "managedBytes"),
                "maximumSampleGapSeconds": round(maximum_gap, 3),
                "lastSampleIsAfterForcedGc": last["phase"] == "end-after-gc",
            })
        workers.append(row)
    report = {"runRoot": str(run_root), "updatedUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
              "workers": workers,
              "interpretation": "Running values include normal allocation/GC fluctuations. Trend slopes are descriptive, not proof of no leak. Only completed 24-hour paced runs with continuous samples provide day-long host coverage. The historic silica service revision and its job/output buffering are not reproduced."}
    host = load_samples(run_root / "host-samples.jsonl")
    if host:
        initial_processes = {p["pid"]: p for p in host[0]["audioProcesses"]}
        report["host"] = {
            "samples": len(host), "latestUtc": host[-1]["utc"],
            "latestFreePhysicalGiB": round(host[-1]["freePhysicalBytes"] / MIB / 1024, 3),
            "audioProcesses": [dict(p, privateGrowthMiB=round((p.get("privateBytes", 0) - initial_processes.get(p["pid"], p).get("privateBytes", 0)) / MIB, 3))
                               for p in host[-1]["audioProcesses"]],
            "notes": "Host sampling starts after worker launch. Shared audio-service growth cannot be assigned to one worker from these samples alone.",
        }
    (run_root / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("run_directory", type=Path)
    options = parser.parse_args()
    print(json.dumps(analyze(options.run_directory), indent=2))
