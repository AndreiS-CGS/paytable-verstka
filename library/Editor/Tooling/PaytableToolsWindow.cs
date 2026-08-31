using System;
using UnityEditor;
using UnityEngine;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// Setup and run entry point for the paytable pipeline.
    ///
    /// IMGUI on purpose: Konami-Slots has zero .uxml/.uss under Assets/ and all ~64 of its editor
    /// windows are OnGUI, so this looks like every other tool a Konami dev has opened. It also
    /// avoids loading UXML through a package path, which needs PackageInfo resolution and breaks
    /// differently depending on how the package was installed.
    /// </summary>
    public sealed class PaytableToolsWindow : EditorWindow
    {
        const string MenuPath = "PlayStudios/Slot Tools/Paytable Tool";
        const string PrefTab = "CGS.Paytable.Window.ActiveTab";
        const string PrefConsole = "CGS.Paytable.Window.ShowConsole";
        const string PrefConsoleHeight = "CGS.Paytable.Window.ConsoleHeight";
        const float MinConsoleHeight = 60f;

        enum Tab { Setup, Run }

        static readonly string[] TabLabels = { "Setup", "Run" };

        [SerializeField] Tab _tab = Tab.Setup;
        [SerializeField] SetupState _setup = new SetupState();
        [SerializeField] RunRequest _run = new RunRequest();
        [SerializeField] bool _showConsole = true;
        [SerializeField] Vector2 _scroll;
        [SerializeField] Vector2 _consoleScroll;
        [SerializeField] float _consoleHeight = 150f;

        SetupTab _setupTab;
        RunTab _runTab;

        /// <summary>One at a time, deliberately: two concurrent pip installs into one venv fight.</summary>
        public ProcessProbe Probe { get; private set; }

        public SetupState Setup => _setup;
        public RunRequest Run => _run;
        public bool Busy => Probe != null && Probe.IsRunning;

        double _lastRepaint;

        [MenuItem(MenuPath, false, 40)]
        public static void Open()
        {
            var w = GetWindow<PaytableToolsWindow>("Paytable Tool");
            w.minSize = new Vector2(640, 420);
            w.Show();
        }

        void OnEnable()
        {
            Probe = new ProcessProbe();
            _setupTab = new SetupTab(this);
            _runTab = new RunTab(this);
            _tab = (Tab)EditorPrefs.GetInt(PrefTab, (int)Tab.Setup);
            _showConsole = EditorPrefs.GetBool(PrefConsole, true);
            _consoleHeight = Mathf.Clamp(EditorPrefs.GetFloat(PrefConsoleHeight, 150f),
                                         MinConsoleHeight, 2000f);
            _setupTab.EnsureChecks();
            EditorApplication.update += OnUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            SavePrefs();
            Probe?.Cancel();
        }

        void OnLostFocus() => SavePrefs();

        void SavePrefs()
        {
            EditorPrefs.SetInt(PrefTab, (int)_tab);
            EditorPrefs.SetBool(PrefConsole, _showConsole);
            EditorPrefs.SetFloat(PrefConsoleHeight, _consoleHeight);
            _runTab?.SaveFields();
        }

        /// <summary>
        /// A domain reload orphans any child process — the handle is gone and we would be lying
        /// about what is still running. Kill it and say so rather than pretending.
        /// LockReloadAssemblies() would be worse than the problem it solves.
        /// </summary>
        void OnBeforeReload()
        {
            if (Probe != null && Probe.IsRunning)
            {
                Debug.LogWarning("[Paytable Tool] a script reload cancelled the running command. " +
                                 "Avoid editing scripts while an install is in progress.");
                Probe.Cancel();
            }
        }

        void OnUpdate()
        {
            if (Probe == null) return;
            if (Probe.Pump() && EditorApplication.timeSinceStartup - _lastRepaint > 0.1)
            {
                _lastRepaint = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        void OnGUI()
        {
            DrawHeader();

            using (var s = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = s.scrollPosition;
                switch (_tab)
                {
                    case Tab.Setup: _setupTab.Draw(); break;
                    case Tab.Run: _runTab.Draw(); break;
                }
            }

            DrawConsole();
        }

        void DrawHeader()
        {
            EditorGUILayout.Space(4);
            var newTab = (Tab)GUILayout.Toolbar((int)_tab, TabLabels, GUILayout.Height(22));
            if (newTab != _tab)
            {
                _tab = newTab;
                GUI.FocusControl(null);
            }

            // The package source drives what is writable and how long it lives, and most confused
            // reports about this tool trace back to not knowing which one is active. Always visible.
            var src = PaytablePaths.Source;
            var msg = "Package: " + src;
            if (src == UnityEditor.PackageManager.PackageSource.Git)
                msg += " — read-only, re-fetched on every resolve. Skills are installed as copies.";
            else
                msg += " — writable, edits are live.";
            EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            EditorGUILayout.Space(2);
        }

        void DrawConsole()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _showConsole = GUILayout.Toggle(_showConsole, "Output", EditorStyles.toolbarButton,
                    GUILayout.Width(70));
                GUILayout.FlexibleSpace();
                if (Busy)
                {
                    GUILayout.Label($"running… {Probe.DurationSeconds:F0}s", EditorStyles.miniLabel);
                    if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(60)))
                        Probe.Cancel();
                }
                else if (Probe != null && Probe.Status != ProcessProbe.State.Idle)
                {
                    GUILayout.Label($"{Probe.Status} (exit {Probe.ExitCode}) in {Probe.DurationSeconds:F1}s",
                        EditorStyles.miniLabel);
                }
                using (new EditorGUI.DisabledScope(Probe == null || Probe.Output.Count == 0))
                {
                    if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(48)))
                        EditorGUIUtility.systemCopyBuffer =
                            Probe.CommandLine + "\n\n" + Probe.OutputText;
                }
            }

            if (!_showConsole || Probe == null) return;

            DrawConsoleResizeHandle();

            using (var s = new EditorGUILayout.ScrollViewScope(_consoleScroll,
                                                              GUILayout.Height(_consoleHeight)))
            {
                _consoleScroll = s.scrollPosition;
                if (!string.IsNullOrEmpty(Probe.CommandLine))
                    EditorGUILayout.LabelField(Probe.CommandLine, EditorStyles.miniBoldLabel);
                if (!string.IsNullOrEmpty(Probe.StartError))
                    EditorGUILayout.HelpBox(Probe.StartError, MessageType.Error);
                var text = Probe.OutputText;
                if (!string.IsNullOrEmpty(text))
                {
                    // An explicit content height is what makes this scroll. With
                    // GUILayout.ExpandHeight the label fills the visible area instead of the
                    // content, so anything past the fold was simply clipped and unreachable —
                    // which is no good for the one panel whose whole job is showing full output.
                    var style = EditorStyles.wordWrappedMiniLabel;
                    var width = Mathf.Max(60f, EditorGUIUtility.currentViewWidth - 24f);
                    var h = style.CalcHeight(new GUIContent(text), width);
                    EditorGUILayout.SelectableLabel(text, style, GUILayout.Height(h));
                }
            }
        }

        /// <summary>
        /// A drag strip above the output pane. IMGUI has no splitter, so this is the usual
        /// three-part idiom: reserve a thin rect, advertise the cursor over it, and move the
        /// stored height on drag.
        /// </summary>
        void DrawConsoleResizeHandle()
        {
            var r = GUILayoutUtility.GetRect(0f, 5f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            // Widen the grab area beyond the drawn line — 5px is accurate but unpleasant to hit.
            var grab = new Rect(r.x, r.y - 2f, r.width, r.height + 4f);
            EditorGUIUtility.AddCursorRect(grab, MouseCursor.ResizeVertical);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && grab.Contains(e.mousePosition))
            {
                _resizingConsole = true;
                e.Use();
            }
            else if (_resizingConsole && e.type == EventType.MouseDrag)
            {
                // Dragging up grows the pane, which is why delta is subtracted.
                _consoleHeight = Mathf.Clamp(_consoleHeight - e.delta.y,
                                             MinConsoleHeight, position.height - 120f);
                e.Use();
                Repaint();
            }
            else if (_resizingConsole && (e.type == EventType.MouseUp || e.type == EventType.Ignore))
            {
                _resizingConsole = false;
                EditorPrefs.SetFloat(PrefConsoleHeight, _consoleHeight);
                e.Use();
            }
        }

        bool _resizingConsole;

        // ── shared helpers for the tabs ─────────────────────────────────────

        public bool StartProcess(string exe, System.Collections.Generic.IList<string> args,
                                 string workingDir = null, double timeout = 300,
                                 Action<ProcessProbe> onFinished = null)
        {
            if (Busy)
            {
                Debug.LogWarning("[Paytable Tool] a command is already running.");
                return false;
            }
            _showConsole = true;
            var ok = Probe.Start(exe, args, workingDir, null, timeout, p =>
            {
                onFinished?.Invoke(p);
                Repaint();
            });
            if (!ok) Debug.LogError("[Paytable Tool] could not start: " + Probe.StartError);
            Repaint();
            return ok;
        }

        public static GUIContent StatusIcon(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Ok: return EditorGUIUtility.IconContent("TestPassed");
                case CheckStatus.Missing:
                case CheckStatus.Wrong: return EditorGUIUtility.IconContent("TestFailed");
                case CheckStatus.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml");
                case CheckStatus.Checking: return EditorGUIUtility.IconContent("WaitSpin00");
                default: return EditorGUIUtility.IconContent("TestInconclusive");
            }
        }
    }
}
