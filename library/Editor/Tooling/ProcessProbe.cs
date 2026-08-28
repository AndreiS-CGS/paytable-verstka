using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// Runs an external command without blocking the editor, streaming its output back.
    ///
    /// There is no pattern to copy in this project: the only ProcessStartInfo in Konami-Slots
    /// (Assets/PlayStudios/Build/Editor/AutoBuilder.cs) uses UseShellExecute, captures nothing, and
    /// calls WaitForExit() on the main thread, freezing the editor for the duration.
    ///
    /// Rules that are not negotiable here:
    ///   * argv as a list, never a shell command string. The paths involved contain spaces
    ///     ("Application Support") and quoting bugs would be the dominant failure mode.
    ///   * The output handlers run on threadpool threads and touch no Unity API — they only
    ///     enqueue. <see cref="Pump"/> drains on the main thread.
    ///   * No WaitForExit() on the main thread, ever.
    /// </summary>
    public sealed class ProcessProbe
    {
        public enum State { Idle, Running, Exited, TimedOut, Failed, Cancelled }

        public State Status { get; private set; } = State.Idle;
        public int ExitCode { get; private set; } = -1;
        public string CommandLine { get; private set; } = "";
        public double DurationSeconds { get; private set; }
        public string StartError { get; private set; }

        /// <summary>Everything the process wrote, stderr lines prefixed. Capped, oldest dropped.</summary>
        public IReadOnlyList<string> Output => _lines;

        public string OutputText => string.Join("\n", _lines);
        public string StdOutText => _stdout.ToString();

        public bool IsRunning => Status == State.Running;

        const int MaxLines = 2000;
        const int MaxLinesPerPump = 200;

        readonly List<string> _lines = new List<string>();
        readonly StringBuilder _stdout = new StringBuilder();
        readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> _pendingRaw = new ConcurrentQueue<string>();

        Process _proc;
        double _startedAt;
        double _timeout;
        Action<ProcessProbe> _onFinished;
        volatile bool _exited;

        /// <summary>
        /// Starts the process. Returns false and sets <see cref="StartError"/> if it could not be
        /// launched at all — a missing executable is a normal outcome here, not an exception to
        /// let escape.
        /// </summary>
        public bool Start(string exe, IList<string> args, string workingDir = null,
                          IDictionary<string, string> env = null, double timeoutSeconds = 120,
                          Action<ProcessProbe> onFinished = null)
        {
            if (Status == State.Running) throw new InvalidOperationException("already running");

            _lines.Clear();
            _stdout.Length = 0;
            while (_pending.TryDequeue(out _)) { }
            while (_pendingRaw.TryDequeue(out _)) { }
            _exited = false;
            ExitCode = -1;
            StartError = null;
            _timeout = timeoutSeconds;
            _onFinished = onFinished;

            var argString = BuildArguments(args);
            CommandLine = Quote(exe) + (string.IsNullOrEmpty(argString) ? "" : " " + argString);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = argString,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;

            // Without PYTHONUNBUFFERED, Python block-buffers into a pipe and nothing appears until
            // the process exits — the streaming this class exists for silently does not happen.
            // Without PYTHONIOENCODING, non-ASCII output (half the SKILL.md text is Russian) throws
            // UnicodeEncodeError on Windows.
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            // Stops git from blocking forever on a hidden credential prompt with the window
            // looking simply frozen.
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
            if (env != null)
                foreach (var kv in env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value ?? "";

            try
            {
                _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    _pending.Enqueue(e.Data);
                    _pendingRaw.Enqueue(e.Data);
                };
                _proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) _pending.Enqueue("stderr: " + e.Data);
                };
                _proc.Exited += (_, __) => _exited = true;

                if (!_proc.Start())
                {
                    StartError = "Process.Start returned false";
                    Status = State.Failed;
                    return false;
                }
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                StartError = e.GetType().Name + ": " + e.Message;
                Status = State.Failed;
                return false;
            }

            _startedAt = EditorApplication.timeSinceStartup;
            Status = State.Running;
            return true;
        }

        /// <summary>Call from EditorApplication.update. Returns true if anything changed.</summary>
        public bool Pump()
        {
            if (Status != State.Running) return false;
            var changed = false;

            var drained = 0;
            while (drained++ < MaxLinesPerPump && _pending.TryDequeue(out var line))
            {
                _lines.Add(line);
                if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
                changed = true;
            }
            while (_pendingRaw.TryDequeue(out var raw)) _stdout.AppendLine(raw);

            DurationSeconds = EditorApplication.timeSinceStartup - _startedAt;

            if (_exited)
            {
                // Only now, and only because the process has already signalled Exited: this flushes
                // the async read buffers and returns immediately.
                try { _proc.WaitForExit(); } catch { /* already gone */ }
                try { ExitCode = _proc.ExitCode; } catch { ExitCode = -1; }
                Finish(State.Exited);
                return true;
            }

            if (_timeout > 0 && DurationSeconds > _timeout)
            {
                Kill();
                Finish(State.TimedOut);
                return true;
            }
            return changed;
        }

        public void Cancel()
        {
            if (Status != State.Running) return;
            Kill();
            Finish(State.Cancelled);
        }

        void Kill()
        {
            try { _proc?.Kill(); }
            catch (Exception e) { Debug.LogWarning("[PaytableTools] could not kill child: " + e.Message); }
        }

        void Finish(State s)
        {
            Status = s;
            DurationSeconds = EditorApplication.timeSinceStartup - _startedAt;
            try { _proc?.Dispose(); } catch { }
            _proc = null;
            var cb = _onFinished;
            _onFinished = null;
            cb?.Invoke(this);
        }

        // ── argv quoting ────────────────────────────────────────────────────

        static string BuildArguments(IList<string> args)
        {
            if (args == null || args.Count == 0) return "";
            var sb = new StringBuilder();
            for (var i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Quote(args[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The Windows command-line quoting rule, which Mono also parses compatibly. Backslashes
        /// immediately before a quote have to be doubled; elsewhere they are literal.
        /// </summary>
        internal static string Quote(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"', '\\', '\n' }) < 0)
                return arg;

            var sb = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }
                sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
