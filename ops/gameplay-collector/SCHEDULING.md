# Automatic hourly gameplay feedback job (Windows)

**Superseded: do not register this task.** Production aggregation now runs entirely
inside the Cloudflare Worker scheduled handler. These instructions describe the
unexecuted earlier design only. See `../clear-dashboard/worker/README-v3.md`.
No Windows task was registered.

Nothing in this change registers a task or contacts a deployed service. Deployment,
credential provisioning, and task registration need the parent's final approval.
No existing scheduling convention was found in this checkout; use Windows Task Scheduler.
The Worker continues serving events/aggregates only: the existing Python learner runs here.

## What runs unattended

Task Scheduler starts `run-hourly-job.ps1` hourly. It loads an account/machine-bound DPAPI
credential from an owned private CLIXML file, passes it only through the child environment,
and invokes `run_hourly_job.py --run`. That command always invokes the existing
`collect_telemetry.py --collect --publish`: new raw pages are fetched, validated sessions
are aggregated, and the cohort datasets are PUT automatically. No operator command is
needed each hour after registration. Clients consume those datasets through their
consent/capability-gated v3 refresh path.

The job has a **900-second collector deadline**, kills/waits for the collector on timeout,
and holds a Windows process lock to prevent overlap. Task Scheduler additionally uses
`IgnoreNew` and a 16-minute execution limit. Cursor/raw commits already completed survive
an interrupted run; the next hourly run resumes safely. If collection fails, publication
is not started. Individual cohort PUTs are replace/idempotent; a failure between cohorts
is recovered by publishing the complete derived set on the next successful run.

The private `job-status.json` is atomically replaced with a bounded latest-result record;
there is no growing log. It contains a timestamp, status, exit code, and aggregate counts,
never a token, packet body, URL, child stdout, or child stderr. Errors return nonzero rather
than being silently treated as success. A deadline cannot guarantee freshness while the
service is unavailable or backlog exceeds capacity: inspect failures before increasing
capacity. Existing 30-day raw retention and minimum-sample/Wilson gates remain unchanged.

## Exact one-time setup AFTER approval

Use Windows PowerShell 5.1 or later **as the account that will run the task**, on the target
machine, from the reviewed repository root. Python 3.12+ must be installed for that account.
Use a dedicated least-privilege account with a real Windows logon password if unattended
operation after logout is required. DPAPI config cannot be moved to another account or
machine. The account needs outbound HTTPS and write access only to its private state.

First provision a new private directory, a pinned copy of the reviewed collector, and an
encrypted config. These commands prompt for the approved origin and collector credential;
the token is never a command-line argument, literal in shell history, or printed value.
Do not use a public repository, shared directory, or synchronized folder for private state.

```powershell
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path .).Path
$python = (& python -c 'import sys; print(sys.executable)').Trim()
if ($LASTEXITCODE -ne 0 -or -not [IO.Path]::IsPathRooted($python)) { throw 'Python unavailable' }
$account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$private = Join-Path $env:LOCALAPPDATA 'OrandGameplayFeedback'
if (Test-Path -LiteralPath $private) { throw 'Use a new private directory or review its existing ACL/config first' }
New-Item -ItemType Directory -Path $private | Out-Null
icacls.exe $private /inheritance:r /grant:r "${account}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Private directory ACL setup failed' }
$program = Join-Path $private 'program'
Copy-Item -LiteralPath (Join-Path $repo 'ops\gameplay-collector') -Destination $program -Recurse
$state = Join-Path $private '.gameplay-learning-artifacts'
$configPath = Join-Path $private 'job.clixml'
$endpoint = Read-Host 'Approved deployed Worker HTTPS origin (no path/query)'
$token = Read-Host 'Collector bearer credential' -AsSecureString
$credential = [pscredential]::new('collector', $token)
[pscustomobject]@{
    Version = 1
    Endpoint = $endpoint
    StateDirectory = $state
    Credential = $credential
} | Export-Clixml -LiteralPath $configPath
Remove-Variable token, credential
```

Then register exactly one hourly task. Registration is the approval-gated step; **these
commands were not executed during implementation**. The second credential prompt is for
the Windows task account, NOT the collector token. This permits execution when logged off.
Use elevation only if required by local task-registration policy; runtime stays limited.

```powershell
$launcher = Join-Path $program 'run-hourly-job.ps1'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "{0}" -PythonPath "{1}" -ConfigPath "{2}"' -f $launcher, $python, $configPath
$action = New-ScheduledTaskAction -Execute $powershell -Argument $arguments -WorkingDirectory $private
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Hours 1)
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 16)
$windowsCredential = Get-Credential -UserName $account -Message 'Windows task account logon password (not collector token)'
if ($windowsCredential.UserName -ne $account) { throw 'Task account must match the account that encrypted job.clixml' }
Register-ScheduledTask -TaskName 'OrandGameplayFeedbackHourly' -Action $action -Trigger $trigger -Settings $settings -RunLevel Limited -User $account -Password $windowsCredential.GetNetworkCredential().Password
Remove-Variable windowsCredential
```

With no repetition duration supplied, the hourly trigger repeats indefinitely. The first
run starts five minutes after registration. No automatic task replacement (`-Force`),
remote registration, git commit/push, or Worker deployment is performed by these scripts.
If enterprise policy requires signing, sign the reviewed PowerShell launcher; do not
lower machine-wide execution policy. Update the pinned program copy only after review.

## Monitoring and credential rotation

After approved registration, inspect `Get-ScheduledTaskInfo -TaskName
'OrandGameplayFeedbackHourly'` and `Get-Content -LiteralPath (Join-Path $state
'job-status.json')`. Expected completed runs report `exitCode:0`; `already-running` is a
harmless skipped overlap. Failures use 1 (collector), 65 (invalid child summary),
71 (collector launch), 78 (configuration/status storage), or 124 (deadline). The absence
of a recent completion or repeated failures requires operator investigation. No credentials
or raw payloads are forwarded into scheduler logs; detailed child errors are intentionally
not echoed because they can include untrusted server content.

To rotate a token, stop/wait for the current task, recreate the CLIXML config with a secure
prompt under the same account, and restart scheduling after approval. Do not put the token
in the scheduled action or persistent machine-wide environment. Existing cache/state can
remain; changing accounts/machines requires a newly encrypted credential.

## Environment-only provisioning alternative

If an approved secret manager supplies per-process environment variables, omit `-ConfigPath`
and provide `ORAND_GAMEPLAY_ENDPOINT`, an absolute `ORAND_GAMEPLAY_STATE_DIRECTORY`, and
`ORAND_COLLECTOR_TOKEN` to `run-hourly-job.ps1 -PythonPath <absolute interpreter path>`.
The Python job rejects origins with userinfo, paths, queries, fragments, or non-HTTPS
transport (loopback HTTP is allowed solely for local tests). The launcher restores its
previous environment when it finishes. The recommended Task Scheduler setup above uses
DPAPI rather than relying on a logged-in user's interactive shell environment.

## Local verification

```text
python -m unittest discover -s ops/gameplay-collector -p "test_*.py"
```

The scheduling test creates a temporary DPAPI config containing only a synthetic token,
executes the real PowerShell launcher, and verifies collection/automatic publication
against a loopback HTTP server. It never registers a task. Separate tests cover deadline
propagation, nonzero failures without secret output, missing/unsafe configuration, and
exclusive-job locking. No sleeps or task-registration side effects are used.
