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

## Getting help

Every command supports `--help` for a full list of options:

```
nerv flood --help
nerv spike --help
```
