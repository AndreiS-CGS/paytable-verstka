using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CGS.PaytableLibrary
{
    /// <summary>
    /// Freezes a built paytable's text layout into concrete numbers, so it cannot collapse at
    /// runtime.
    ///
    /// THE BUG THIS EXISTS FOR. The text blocks size themselves with <c>ContentSizeFitter</c> =
    /// PreferredSize, which asks <c>TMP_Text</c> for <c>preferredHeight</c>. TMP returns <b>0</b>
    /// until it has generated its text mesh. The first layout pass after a page is enabled
    /// therefore measures 0, the parent <c>VerticalLayoutGroup</c> parks the block against its top
    /// padding, and nothing schedules a second pass. Symptom: a TextBlock sits at
    /// <c>Pos Y = -25</c> in Game Mode against <c>-336.995</c> in the prefab, and toggling the
    /// object off and on "fixes" it for that session. The prefab value was never wrong — the
    /// runtime measurement was.
    ///
    /// This is inherent to the fitter-driven block design, so every game built from this library
    /// hits it. Baking is the fix: write the resolved sizes into the prefab and stop anything from
    /// asking TMP at runtime. Nothing ships into the player — this assembly is Editor-only.
    ///
    /// WHY THIS IS CODE AND NOT A PARAGRAPH IN THE SKILL. The procedure has three steps and two
    /// traps, and each trap produces a plausible-looking wrong answer rather than an error:
    /// baking while <c>childForceExpandHeight</c> is on diverges (measured 130 -> 264 -> 309 on
    /// one label across three runs), and clearing that flag is not enough because the panel also
    /// hands out spare height through <c>flexibleHeight</c>. A prose instruction gets one of those
    /// wrong eventually. <see cref="Bake"/> is idempotent by construction and
    /// <see cref="Verify"/> refuses to call a prefab finished while anything can still re-measure.
    ///
    /// RE-RUN THE BAKE whenever what the height is derived FROM changes: the text, the font asset
    /// or its metrics, <c>fontSize</c>/<c>lineSpacing</c>/<c>paragraphSpacing</c>, or a sprite
    /// asset swapped for one with different glyph heights (inline sprites drive line height). All
    /// of those are assembly-time events, so re-baking is part of finishing the prefab.
    ///
    /// WHERE BAKING IS THE WRONG ANSWER. A baked height is a height for one specific string. Two
    /// things would invalidate it, and both were checked against this project before choosing to
    /// bake at all:
    /// <list type="bullet">
    /// <item>Localisation — a height baked for English clips German or Russian. Paytable art
    /// exists only in <c>_en_</c> variants across every bundle and no paytable code calls the
    /// localisation layer, so paytables here are English-only.</item>
    /// <item>Text rewritten at runtime, e.g.
    /// <c>paragraph.text = paragraph.text.Replace("$$$", value)</c> in
    /// <c>RepeatedCharmsPaytableDynamicValueStrategy</c>. One game does this and no current
    /// <c>_gel/_games</c> prefab carries such a placeholder. If a game needs it, that page cannot
    /// be baked — leave its fitter live and force a second layout pass at runtime instead.</item>
    /// </list>
    ///
    /// Call it on objects that are LIVE IN A SCENE — the skill's scratch scene during assembly, or
    /// an open prefab stage. Do not run it against <c>PrefabUtility.LoadPrefabContents</c> output:
    /// TMP mesh generation there is not the same thing being measured.
    /// </summary>
    public static class PaytableLayoutBake
    {
        /// <summary>Cap on measurement passes. Fitter and group settle in 2-3; more means trouble.</summary>
        public const int MaxPasses = 8;

        /// <summary>
        /// Height change below which a PASS counts as settled, in world units. This is an
        /// intra-run convergence threshold only — it is not a measure of idempotence, see
        /// <see cref="Bake"/>.
        /// </summary>
        public const float Epsilon = 0.05f;

        /// <summary>
        /// Must be &gt; 0 to win. <c>TMP_Text</c> implements <c>ILayoutElement</c> at priority 0,
        /// so a <c>LayoutElement</c> at 1 is what makes <c>LayoutUtility</c> take the baked number
        /// and never consult the mesh. This is the whole mechanism — do not lower it.
        /// </summary>
        public const int BakedLayoutPriority = 1;

        public sealed class Report
        {
            public int Texts;
            public int HeightsBaked;
            public int WidthsBaked;
            public int FittersDisabled;
            public int FlexiblesCleared;
            public int ForceExpandCleared;
            public int LayoutElementsAdded;
            public int SkippedInactive;
            public int Passes;
            public bool Converged;
            public float MaxDelta;
            public readonly List<string> Warnings = new List<string>();

            public bool Ok => Converged && Warnings.Count == 0;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Ok ? "BAKE OK" : "BAKE INCOMPLETE").Append("  ");
                sb.Append($"texts {Texts}, heights {HeightsBaked}, widths {WidthsBaked}, ");
                sb.Append($"fitters disabled {FittersDisabled}, flexible cleared {FlexiblesCleared}, ");
                sb.Append($"forceExpand cleared {ForceExpandCleared}, ");
                sb.Append($"LayoutElements added {LayoutElementsAdded}, ");
                sb.Append($"skipped (switched off) {SkippedInactive}, ");
                sb.Append($"passes {Passes}, max delta {MaxDelta:F3}");
                foreach (var w in Warnings) sb.Append("\n  ! ").Append(w);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Resets, measures to a fixed point, then freezes. Safe to run repeatedly: the reset
        /// puts the hierarchy into a state that does not depend on whether a bake ran before, so
        /// pass 2 of run 2 measures what pass 2 of run 1 measured.
        ///
        /// Verify by running it twice and comparing the BAKED NUMBERS between runs — not
        /// <see cref="Report.MaxDelta"/>, which measures how far the layout moved between passes
        /// WITHIN one run and therefore settles to ~0 on any successful run, first or second. It
        /// says nothing about whether two runs agree, so it would not have caught the
        /// 130 -> 264 -> 309 divergence this class exists to prevent. Snapshot every owned text's
        /// rect size and preferredWidth/Height, run again, and diff.
        /// </summary>
        public static Report Bake(GameObject root)
        {
            var report = new Report();
            if (root == null)
            {
                report.Warnings.Add("root is null");
                return report;
            }

            var owned = OwnedTexts(root, report);
            report.Texts = owned.Count;
            if (owned.Count == 0)
            {
                report.Warnings.Add("no TMP_Text with a ContentSizeFitter found under " + root.name +
                                    " — nothing to bake, which is itself suspicious for a paytable page");
                return report;
            }

            ResetToMeasurableState(owned, report);
            report.Converged = Converge(root, owned, report);
            if (!report.Converged)
                report.Warnings.Add($"layout did not settle within {MaxPasses} passes " +
                                    $"(last delta {report.MaxDelta:F3}) — the numbers below are a " +
                                    "snapshot of an unstable layout, not a measurement");
            Freeze(owned, report);
            return report;
        }

        /// <summary>Bakes every page under a dialog root, one report per page, plus a combined one.</summary>
        public static Report BakeAll(GameObject dialogRoot, out List<Report> perPage)
        {
            perPage = new List<Report>();
            var combined = new Report { Converged = true };
            if (dialogRoot == null)
            {
                combined.Converged = false;
                combined.Warnings.Add("dialogRoot is null");
                return combined;
            }

            // Pages are the dialog's registered cards in the shells this library ships; falling
            // back to direct children keeps this usable mid-assembly, before cards[] is filled.
            foreach (var page in Pages(dialogRoot))
            {
                var r = Bake(page);
                perPage.Add(r);
                combined.Texts += r.Texts;
                combined.HeightsBaked += r.HeightsBaked;
                combined.WidthsBaked += r.WidthsBaked;
                combined.FittersDisabled += r.FittersDisabled;
                combined.FlexiblesCleared += r.FlexiblesCleared;
                combined.ForceExpandCleared += r.ForceExpandCleared;
                combined.LayoutElementsAdded += r.LayoutElementsAdded;
                combined.Passes = Mathf.Max(combined.Passes, r.Passes);
                combined.MaxDelta = Mathf.Max(combined.MaxDelta, r.MaxDelta);
                combined.Converged &= r.Converged;
                foreach (var w in r.Warnings) combined.Warnings.Add(page.name + ": " + w);
            }
            if (perPage.Count == 0)
            {
                // A bake that baked nothing must never read as success — that is the same false
                // green this whole library keeps having to design against.
                combined.Converged = false;
                combined.Warnings.Add("found no pages under " + dialogRoot.name +
                                      " — neither a cards[] array nor any child carrying text");
            }
            return combined;
        }

        /// <summary>
        /// Returns "" when nothing under <paramref name="root"/> can re-measure itself at runtime,
        /// and a description of every offender otherwise.
        ///
        /// This is the gate, not a courtesy: a prefab that fails it looks completely correct in
        /// the editor and collapses the first time it is shown. Treat a non-empty result as a
        /// blocking failure of the run.
        /// </summary>
        public static string Verify(GameObject root)
        {
            if (root == null) return "root is null";
            var problems = new List<string>();

            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // A switched-off block is a layout decision, not a defect — see OwnedTexts.
                if (!t.gameObject.activeInHierarchy) continue;
                var path = Path(t.transform, root.transform);
                var fitter = t.GetComponent<ContentSizeFitter>();
                if (fitter != null && fitter.enabled)
                    problems.Add(path + ": ContentSizeFitter still enabled — it will ask TMP for " +
                                 "preferredHeight on the first frame and get 0");

                if (fitter == null) continue;   // never fitter-driven, so never at risk

                // A LayoutElement is only needed where a parent group actually asks about this
                // axis. PayRows' HorizontalLayoutGroup ships childControlHeight = false on
                // purpose — the children's height is the fitter's business alone — so demanding
                // one there reports 16 non-problems next to the real ones, and a checker that
                // cries wolf gets skimmed. Measured on a real prefab, which is how this rule got
                // narrowed.
                var parentControlsHeight = ParentControlsHeight(t.transform);

                var le = t.GetComponent<LayoutElement>();
                if (le == null)
                {
                    if (parentControlsHeight)
                        problems.Add(path + ": no LayoutElement, so the parent group falls through " +
                                     "to TMP_Text's own ILayoutElement and measures the mesh");
                    continue;
                }
                if (parentControlsHeight && le.layoutPriority < BakedLayoutPriority)
                    problems.Add($"{path}: LayoutElement.layoutPriority is {le.layoutPriority}; " +
                                 $"TMP_Text is 0, so it needs at least {BakedLayoutPriority} to win");
                if (parentControlsHeight &&
                    fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize &&
                    le.preferredHeight <= 0f)
                    problems.Add(path + ": vertical fit was PreferredSize but no height is baked");
                if (le.flexibleHeight > 0f)
                    problems.Add($"{path}: flexibleHeight is {le.flexibleHeight} — the parent group " +
                                 "will hand it spare height and undo the baked size");
            }

            foreach (var g in OwnedGroups(OwnedTexts(root)))
                if (g.childForceExpandHeight)
                    problems.Add(Path(g.transform, root.transform) +
                                 ": childForceExpandHeight is on, which treats every child as " +
                                 "flexible regardless of its LayoutElement");

            return problems.Count == 0 ? "" : string.Join("\n", problems);
        }

        // ── internals ───────────────────────────────────────────────────────

        /// <summary>
        /// The texts this bake owns: a <c>TMP_Text</c> that a <c>ContentSizeFitter</c> sizes.
        /// Everything else is left strictly alone.
        ///
        /// Switched-off subtrees are skipped, and that is not an optimisation. The library's whole
        /// layout vocabulary is "switch off what you don't need" — unused <c>GridCell_3</c>,
        /// <c>SpecialPanel_2</c>/<c>_3</c>, <c>ImageContainer_2..4</c>, <c>PayRows</c> on a trigger
        /// panel, <c>Title</c> on a full-page image. Nothing under them is ever laid out, so their
        /// measured height is 0, and baking that 0 while disabling the fitter is strictly worse
        /// than leaving them alone: the page renders identically today, and the moment anyone
        /// enables that panel its text is pinned to zero height with nothing left to re-measure it.
        /// The first live run on a real prefab did exactly this to 24 texts before this filter
        /// existed.
        ///
        /// The narrowness is deliberate. A blanket pass over every <c>LayoutElement</c> would wipe
        /// values the library sets on purpose — <c>PanelRow</c>'s own root carries
        /// <c>flexibleHeight = 1</c> precisely so it absorbs whatever height the panel has left
        /// after the labels take theirs, and grid cell sizes are baked into
        /// <c>GridPage</c>/<c>GridCell</c>. Clearing those would not collapse anything at runtime;
        /// it would quietly relayout the whole panel at bake time instead.
        /// </summary>
        static List<TMP_Text> OwnedTexts(GameObject root, Report report = null)
        {
            var all = root.GetComponentsInChildren<TMP_Text>(true)
                          .Where(t => t.GetComponent<ContentSizeFitter>() != null)
                          .ToList();
            var live = all.Where(t => t.gameObject.activeInHierarchy).ToList();
            if (report != null) report.SkippedInactive = all.Count - live.Count;
            return live;
        }

        /// <summary>The layout groups that directly parent an owned text.</summary>
        static List<HorizontalOrVerticalLayoutGroup> OwnedGroups(List<TMP_Text> owned)
        {
            var groups = new List<HorizontalOrVerticalLayoutGroup>();
            foreach (var t in owned)
            {
                var parent = t.transform.parent;
                if (parent == null) continue;
                var g = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if (g != null && !groups.Contains(g)) groups.Add(g);
            }
            return groups;
        }

        /// <summary>
        /// Puts the hierarchy into the one state a measurement is valid from. Every value written
        /// here is absolute, never relative to what a previous bake left behind — that is what
        /// makes <see cref="Bake"/> idempotent, and it is the fix for the divergence trap.
        /// </summary>
        static void ResetToMeasurableState(List<TMP_Text> owned, Report report)
        {
            // Groups first: while childForceExpandHeight is on, HorizontalOrVerticalLayoutGroup
            // treats every child's flexible size as at least 1, so the group inflates the child no
            // matter what its LayoutElement says. Measure under that and the bake freezes the
            // inflated value; the next bake inflates THAT. Turning it off is not a tuning choice,
            // it is a precondition for the measurement meaning anything.
            foreach (var g in OwnedGroups(owned))
            {
                if (!g.childForceExpandHeight) continue;
                g.childForceExpandHeight = false;
                report.ForceExpandCleared++;
                EditorUtility.SetDirty(g);
            }

            foreach (var t in owned)
            {
                var le = t.GetComponent<LayoutElement>();
                if (le != null)
                {
                    // Clearing the flag above is not sufficient on its own: SpecialPanel ships
                    // Label and OptionalTextBlock with flexibleHeight = 1, so the group keeps
                    // distributing spare panel height to them through the LayoutElement itself.
                    if (le.flexibleHeight > 0f)
                    {
                        le.flexibleHeight = -1f;
                        report.FlexiblesCleared++;
                    }
                    // -1 means "no opinion", which hands the question back to the fitter for the
                    // duration of the measurement.
                    le.preferredHeight = -1f;
                    le.preferredWidth = -1f;
                    EditorUtility.SetDirty(le);
                }

                var fitter = t.GetComponent<ContentSizeFitter>();
                if (fitter != null && !fitter.enabled)
                {
                    fitter.enabled = true;
                    EditorUtility.SetDirty(fitter);
                }
            }
        }

        /// <summary>
        /// Measures to a fixed point. Two things matter here and both are the bug in miniature.
        ///
        /// <c>ForceMeshUpdate</c> comes FIRST, on every text, before any layout work: it is the
        /// step the runtime never gets to do in time, and without it every <c>preferredHeight</c>
        /// read below is 0.
        ///
        /// Layout roots are rebuilt PARENTS-FIRST. <c>Body</c> has <c>sizeDelta.x = 0</c> with
        /// anchors (0,0)/(0,0) — its width is assigned by <c>Inner_Group</c>'s layout group, so
        /// rebuilding from <c>Body</c> downward measures at width 0, wraps every word onto its own
        /// line, and returns heights roughly 10x too large. That reads exactly like catastrophic
        /// overflow and is pure artefact.
        /// </summary>
        static bool Converge(GameObject root, List<TMP_Text> owned, Report report)
        {
            var groups = LayoutRootsParentsFirst(root);
            var heights = new float[owned.Count];
            for (var i = 0; i < heights.Length; i++) heights[i] = float.NaN;

            for (var pass = 0; pass < MaxPasses; pass++)
            {
                foreach (var t in owned) t.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                foreach (var g in groups) LayoutRebuilder.ForceRebuildLayoutImmediate(g);

                report.Passes = pass + 1;
                var maxDelta = 0f;
                for (var i = 0; i < owned.Count; i++)
                {
                    var h = ((RectTransform)owned[i].transform).rect.height;
                    if (!float.IsNaN(heights[i])) maxDelta = Mathf.Max(maxDelta, Mathf.Abs(h - heights[i]));
                    heights[i] = h;
                }
                report.MaxDelta = maxDelta;
                if (pass > 0 && maxDelta <= Epsilon) return true;
            }
            return false;
        }

        /// <summary>Every layout-controlled RectTransform under root, shallowest first.</summary>
        static List<RectTransform> LayoutRootsParentsFirst(GameObject root)
        {
            return root.GetComponentsInChildren<LayoutGroup>(true)
                       .Select(g => (RectTransform)g.transform)
                       .OrderBy(rt => Depth(rt, root.transform))
                       .ToList();
        }

        static int Depth(Transform t, Transform root)
        {
            var d = 0;
            for (var p = t; p != null && p != root; p = p.parent) d++;
            return d;
        }

        /// <summary>
        /// Writes the measured sizes down and takes TMP out of the loop, in that order.
        ///
        /// Both consumers have to be answered or the bake is decorative. The fitter is disabled so
        /// it stops re-deriving the size; the parent group is answered with
        /// <c>LayoutElement.preferredHeight</c>, whose priority outranks <c>TMP_Text</c>'s own.
        /// Disabling only the fitter leaves the group measuring the mesh; setting only the
        /// LayoutElement leaves the fitter overwriting the RectTransform on the first frame.
        /// </summary>
        static void Freeze(List<TMP_Text> owned, Report report)
        {
            foreach (var t in owned)
            {
                var rt = (RectTransform)t.transform;
                var fitter = t.GetComponent<ContentSizeFitter>();
                var width = rt.rect.width;
                var height = rt.rect.height;

                var le = t.GetComponent<LayoutElement>();
                if (le == null)
                {
                    le = Undo.AddComponent<LayoutElement>(t.gameObject);
                    report.LayoutElementsAdded++;
                }
                if (le.layoutPriority < BakedLayoutPriority) le.layoutPriority = BakedLayoutPriority;

                // Only the axes the fitter was actually driving. Baking an axis the fitter left
                // alone would pin a size the layout was deliberately controlling from elsewhere.
                if (fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize)
                {
                    le.preferredHeight = height;
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                    report.HeightsBaked++;
                }
                if (fitter.horizontalFit == ContentSizeFitter.FitMode.PreferredSize)
                {
                    le.preferredWidth = width;
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    report.WidthsBaked++;
                }

                fitter.enabled = false;
                report.FittersDisabled++;

                EditorUtility.SetDirty(le);
                EditorUtility.SetDirty(fitter);
                EditorUtility.SetDirty(rt);
            }
        }

        /// <summary>
        /// The dialog's pages. Prefers the slider's registered <c>cards</c> so the bake covers
        /// exactly what will be shown; falls back to direct children, which is what exists while
        /// assembly is still in progress.
        /// </summary>
        static List<GameObject> Pages(GameObject dialogRoot)
        {
            var pages = new List<GameObject>();
            foreach (var c in dialogRoot.GetComponents<MonoBehaviour>())
            {
                if (c == null) continue;
                var so = new SerializedObject(c);
                var cards = so.FindProperty("cards");
                if (cards == null || !cards.isArray) continue;
                for (var i = 0; i < cards.arraySize; i++)
                {
                    var go = cards.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                    if (go != null && !pages.Contains(go)) pages.Add(go);
                }
                if (pages.Count > 0) return pages;
            }

            foreach (Transform child in dialogRoot.transform)
                if (child.GetComponentInChildren<TMP_Text>(true) != null)
                    pages.Add(child.gameObject);
            return pages;
        }

        /// <summary>
        /// Whether this object's parent layout group controls its height. When it does not, the
        /// height is the fitter's business alone and no LayoutElement is required — but the fitter
        /// still has to be disabled, because it is the thing that measures 0 on the first frame.
        /// </summary>
        static bool ParentControlsHeight(Transform t)
        {
            var parent = t.parent;
            if (parent == null) return false;
            var g = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            return g != null && g.childControlHeight;
        }

        static string Path(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var p = t; p != null && p != root; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return parts.Count == 0 ? root.name : string.Join("/", parts);
        }
    }
}
