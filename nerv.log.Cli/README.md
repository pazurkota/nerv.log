# nerv.log CLI

`nerv.log.Cli` is a command-line tool for developers and DevOps engineers to diagnose errors, analyze anomalies, and stress test the `nerv.log` gRPC log ingestion server. It streams synthetic `LogRequest` messages over the `LogIngestion/StreamLogs` RPC to reproduce real-world log traffic patterns, from steady streams to sudden bursts.

## Usage

```
nerv <command> [options]
```

Running the tool without any arguments prints the logo and usage help.

## Commands

### `flood`

Continuously streams a single log entry at a fixed interval until the process is stopped (`Ctrl+C`). Useful for simulating a steady, low-volume stream of logs.

```
nerv flood [-a|--address <ADDRESS>] [-d|--delay <MS>]
```

| Option | Default | Description |
|---|---|---|
| `-a`, `--address` | `http://localhost:8080` | URL address of the target gRPC service |
| `-d`, `--delay` | `1000` | Delay (in milliseconds) between each log entry |

Example — stream a log every 250ms to a custom server:

```
nerv flood --address http://localhost:5000 --delay 250
```

### `spike`

Simulates traffic spikes: it bursts a large number of logs as fast as possible for a short "spike" window, then rests before starting the next wave. This repeats indefinitely until the process is stopped (`Ctrl+C`). Useful for stress-testing how the server handles sudden bursts of load.

Each wave sends logs until either the log amount or the spike duration is reached, whichever comes first.

```
nerv spike [--address <ADDRESS>] [-a|--amount <COUNT>] [-s|--spike <SECONDS>] [-r|--rest <SECONDS>]
```

| Option | Default | Description |
|---|---|---|
| `--address` | `http://localhost:8080` | URL address of the target gRPC service |
| `-a`, `--amount` | `20000` | Maximum number of logs sent during a single spike |
| `-s`, `--spike` | `2` | Spike duration (in seconds) |
| `-r`, `--rest` | `10` | Rest duration between spikes (in seconds) |

Example — burst 50,000 logs over 2 seconds, resting 15 seconds between waves:

```
nerv spike --amount 50000 --spike 2 --rest 15
```

### `analyze`

Analyzes logs already stored on the server for statistics and common anomalies, over a configurable time window.

```
nerv analyze [--address <ADDRESS>] [-c|--check <CHECK>] [-s|--service <SERVICE>] [-w|--window <MINUTES>] [-t|--threshold <COUNT>]
```

| Option | Default | Description |
|---|---|---|
| `--address` | `http://localhost:8080` | URL address of the target gRPC service |
| `-c`, `--check` | `all` | Analysis to run: `stats`, `brute-force`, `error-spike` or `all` |
| `-s`, `--service` | *(none)* | Limit the analysis to a single service name |
| `-w`, `--window` | `60` | Time window to analyze, in minutes counting back from now |
| `-t`, `--threshold` | `10` | Number of suspicious events within the window that raises a finding |

Available checks:

- **`stats`** — overall log counts per level and per service, with error rates.
- **`brute-force`** — flags services with a burst of `≥ threshold` failed (Error/Critical) logs within a short sliding window, indicative of brute-force attempts.
- **`error-spike`** — flags services whose error count in a time bucket both meets `threshold` and significantly exceeds their own baseline error rate, indicative of a sudden spike.

Example — check for brute-force bursts on a single service over the last 24 hours:

```
nerv analyze --check brute-force --service Auth.API --window 1440 --threshold 5
```

### `db vacuum`

Deletes old logs from the server's database to keep its size in check.

```
nerv db vacuum [-a|--address <ADDRESS>] [--older-than <DAYS>] [--keep-errors]
```

| Option | Default | Description |
|---|---|---|
| `-a`, `--address` | `http://localhost:8080` | URL address of the target gRPC service |
| `--older-than` | `7` | Deletes logs older than the provided time (in days) |
| `--keep-errors` | `false` | Keep Error/Critical logs regardless of age |

Example — delete logs older than 30 days, keeping errors for auditing:

```
nerv db vacuum --older-than 30 --keep-errors
```

## Getting help

Every command supports `--help` for a full list of options:

```
nerv flood --help
nerv spike --help
nerv analyze --help
nerv db vacuum --help
```
