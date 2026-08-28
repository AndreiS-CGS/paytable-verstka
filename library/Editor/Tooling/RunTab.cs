using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CGS.PaytableLibrary.Tooling
{
    public enum SlotSystem { GEL, MCF }

    [Serializable]
    public sealed class RunRequest
    {
        public string GameName = "";
        public string SlotName = "";
        public string ConfluenceUrl = "";
        public SlotSystem System = SlotSystem.GEL;
        public string BundlePath = "";
        public string ArtPathOverride = "";
        public string SpritePrefix = "";

        /// <summary>The two canonical layouts. Older games use other shapes; those are legacy.</summary>
        public string DefaultBundlePath =>
            string.IsNullOrEmpty(SlotName)
                ? ""
                : System == SlotSystem.GEL
                    ? $"Assets/Bundles/_gel/_games/{SlotName}/"
                    : $"Assets/Bundles/_games/{SlotName}/";
    }

    /// <summary>
    /// Composes the invocation; the human runs it.
    ///
    /// Deliberately does not launch anything. The skill has human gates by design — the art gate,
    /// the review steps — so a headless run stalls at the first one. Firing it from here would also
    /// need a TTY, which on macOS means an automation prompt asking to control Terminal, a strange
    /// thing to meet from a paytable tool.
    /// </summary>
    public sealed class RunTab
    {
        readonly PaytableToolsWindow _w;
        string _prompt = "";
        Vector2 _promptScroll;

        // The extraction script's own rule: parse_url() needs /pages/<digits> and gives up
        // otherwise. Validating with the same pattern means the message can name the real problem
        // instead of saying "invalid URL".
        static readonly Regex PageIdRe = new Regex(@"/pages/(\d+)", RegexOptions.Compiled);

        const string PrefsFile = "ProjectSettings/CGSPaytableTools.json";

        public RunTab(PaytableToolsWindow w)
        {
            _w = w;
            LoadFields();
        }

        public void Draw()
        {
            var r = _w.Run;

            EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                r.GameName = EditorGUILayout.TextField(
                    new GUIContent("Game name", "As it should read on the paytable pages"), r.GameName);
                r.SlotName = EditorGUILayout.TextField(
                    new GUIContent("Slot id", "The bundle folder name, lowercase"), r.SlotName);
                r.ConfluenceUrl = EditorGUILayout.TextField("Confluence GDD URL", r.ConfluenceUrl);
                r.SpritePrefix = EditorGUILayout.TextField(
                    new GUIContent("Sprite/asset prefix", "e.g. S_Symbol_ or HP_"), r.SpritePrefix);

                var newSystem = (SlotSystem)EditorGUILayout.EnumPopup("Slot system", r.System);
                if (newSystem != r.System)
                {
                    r.System = newSystem;
                    r.BundlePath = "";       // re-derive from the new convention
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var shown = string.IsNullOrEmpty(r.BundlePath) ? r.DefaultBundlePath : r.BundlePath;
                    var edited = EditorGUILayout.TextField("Bundle path", shown);
                    if (edited != shown) r.BundlePath = edited;
                    if (GUILayout.Button("Reset", GUILayout.Width(56))) r.BundlePath = "";
                }

                r.ArtPathOverride = EditorGUILayout.TextField(
                    new GUIContent("Art path (optional)",
                        "Leave empty — Phase 4 searches the bundle for symbol art itself"),
                    r.ArtPathOverride);
            }

            var issues = Validate();
            var blocking = issues.FindAll(i => i.Blocking);
            foreach (var i in issues)
                EditorGUILayout.HelpBox(i.Message, i.Blocking ? MessageType.Error : MessageType.Warning);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(blocking.Count > 0))
                {
                    if (GUILayout.Button("Compose prompt", GUILayout.Height(24)))
                    {
                        _prompt = Compose();
                        SaveFields();
                    }
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_prompt)))
                {
                    if (GUILayout.Button("Copy", GUILayout.Width(80), GUILayout.Height(24)))
                    {
                        EditorGUIUtility.systemCopyBuffer = _prompt;
                        Debug.Log("[Paytable Tool] prompt copied to the clipboard.");
                    }
                    if (GUILayout.Button("Save to _verstka", GUILayout.Width(130), GUILayout.Height(24)))
                        SavePrompt();
                }
            }

            if (string.IsNullOrEmpty(_prompt)) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Paste this into Claude Code", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Contains absolute paths from this machine.", EditorStyles.miniLabel);
            using (var s = new EditorGUILayout.ScrollViewScope(_promptScroll, GUILayout.Height(220)))
            {
                _promptScroll = s.scrollPosition;
                EditorGUILayout.SelectableLabel(_prompt, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            }
        }

        struct Issue
        {
            public string Message;
            public bool Blocking;
        }

        List<Issue> Validate()
        {
            var r = _w.Run;
            var issues = new List<Issue>();

            if (string.IsNullOrWhiteSpace(r.GameName))
                issues.Add(new Issue { Message = "Game name is required.", Blocking = true });

            if (string.IsNullOrWhiteSpace(r.ConfluenceUrl))
                issues.Add(new Issue { Message = "Confluence GDD URL is required.", Blocking = true });
            else if (!PageIdRe.IsMatch(r.ConfluenceUrl))
                issues.Add(new Issue
                {
                    Message = "The extraction script cannot get a page ID from this URL — it needs a " +
                              "/pages/<number>/ segment. Copy the link from Confluence's own share " +
                              "button rather than the browser address bar.",
                    Blocking = true
                });

            if (string.IsNullOrWhiteSpace(r.SlotName))
                issues.Add(new Issue { Message = "Slot id is required to derive the bundle path.", Blocking = true });

            var bundle = string.IsNullOrEmpty(r.BundlePath) ? r.DefaultBundlePath : r.BundlePath;
            if (!string.IsNullOrEmpty(bundle))
            {
                var abs = Path.Combine(PaytablePaths.UnityProjectRoot ?? "", bundle);
                if (!Directory.Exists(abs))
                    issues.Add(new Issue
                    {
                        Message = $"Bundle path does not exist:\n{abs}\n\nCheck the slot id and the " +
                                  "GEL/MCF choice.",
                        Blocking = true
                    });
            }

            if (string.IsNullOrWhiteSpace(r.SpritePrefix))
                issues.Add(new Issue
                {
                    Message = "No sprite prefix given. The skill can usually work it out, but naming " +
                              "conventions vary per game, so supplying it saves a round trip.",
                    Blocking = false
                });

            var py = _w.Setup.Get(CheckId.Python);
            if (py != null && (py.Status == CheckStatus.Missing || py.Status == CheckStatus.Wrong))
                issues.Add(new Issue
                {
                    Message = "The Python environment is not ready — see the Setup tab. Extraction " +
                              "will fail immediately.",
                    Blocking = true
                });

            var skills = _w.Setup.Get(CheckId.Skills);
            if (skills != null && (skills.Status == CheckStatus.Missing || skills.Status == CheckStatus.Wrong))
                issues.Add(new Issue
                {
                    Message = "The skills are not installed — see the Setup tab. Claude Code will not " +
                              "find paytable-verstka.",
                    Blocking = true
                });

            return issues;
        }

        string Compose()
        {
            var r = _w.Run;
            var bundle = string.IsNullOrEmpty(r.BundlePath) ? r.DefaultBundlePath : r.BundlePath;
            var work = Path.Combine(PaytablePaths.UnityProjectRoot ?? "", "_verstka", r.GameName);

            var sb = new StringBuilder();
            sb.AppendLine("Use the paytable-verstka skill.");
            sb.AppendLine();
            sb.AppendLine("Game name: " + r.GameName);
            sb.AppendLine("Confluence GDD URL: " + r.ConfluenceUrl);
            sb.AppendLine("Sprite/asset prefix: " +
                          (string.IsNullOrWhiteSpace(r.SpritePrefix) ? "(derive it)" : r.SpritePrefix));
            sb.AppendLine();
            // Everything below is what the skill's Portability section asks to be supplied at run
            // time rather than hardcoded. Handing it over saves the run several turns of discovery.
            sb.AppendLine("Context (already resolved by the Unity Paytable Tool — do not re-derive):");
            sb.AppendLine("- Unity project root: " + PaytablePaths.UnityProjectRoot);
            sb.AppendLine("- Slot system: " + r.System + " (slot id " + r.SlotName + ")");
            sb.AppendLine("- Bundle path: " + bundle);
            if (!string.IsNullOrWhiteSpace(r.ArtPathOverride))
                sb.AppendLine("- Art path: " + r.ArtPathOverride);
            sb.AppendLine("- Working dir: " + work);
            sb.AppendLine("- Block library: " + PaytablePaths.PackageName +
                          " resolved at " + PaytablePaths.PackageRoot);
            sb.AppendLine("- Python: the scripts re-exec themselves into " + PaytablePaths.VenvPython +
                          "; invoke them with plain `python3` and let them switch.");

            var profile = EditorPrefs.GetString("CGS.Paytable.Setup.ChromeProfile", "");
            if (!string.IsNullOrEmpty(profile))
                sb.AppendLine("- Confluence auth: browser cookies, Chrome profile \"" + profile + "\".");

            var mcp = _w.Setup.Get(CheckId.UnityMcp);
            sb.AppendLine("- unityMCP: " +
                          (mcp != null && mcp.Status == CheckStatus.Ok
                              ? "present, so the assembly phase can run."
                              : "NOT detected — run phases 1-4 and stop before assembly."));
            return sb.ToString();
        }

        void SavePrompt()
        {
            var dir = Path.Combine(PaytablePaths.UnityProjectRoot ?? "", "_verstka", _w.Run.GameName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "run-prompt.md");
            File.WriteAllText(path, _prompt);
            Debug.Log("[Paytable Tool] wrote " + path);
            EditorUtility.RevealInFinder(path);
        }

        // ── persistence ─────────────────────────────────────────────────────
        // Per project, not EditorPrefs: EditorPrefs is machine-global, so two Unity projects open
        // at once would bleed one project's bundle path into the other.

        public void SaveFields()
        {
            try
            {
                var path = Path.Combine(PaytablePaths.UnityProjectRoot ?? ".", PrefsFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(_w.Run, true));
            }
            catch (Exception e) { Debug.LogWarning("[Paytable Tool] could not save fields: " + e.Message); }
        }

        void LoadFields()
        {
            try
            {
                var path = Path.Combine(PaytablePaths.UnityProjectRoot ?? ".", PrefsFile);
                if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), _w.Run);
            }
            catch { /* a corrupt prefs file must not stop the window opening */ }
        }
    }
}
