using System;
using System.Collections.Generic;

namespace CGS.PaytableLibrary.Tooling
{
    /// <summary>
    /// One row in the Setup tab.
    ///
    /// The status is deliberately not a boolean. This whole tool exists because the old setup
    /// procedure reported success it had not verified — pip's exit code, File.Exists, one symlink
    /// level — so a probe that could not run must land on <see cref="CheckStatus.Unknown"/> and
    /// never quietly on Ok.
    /// </summary>
    public enum CheckStatus
    {
        /// <summary>Not probed yet.</summary>
        Unknown,
        Checking,
        Ok,
        /// <summary>Works, but something about it will bite later.</summary>
        Warning,
        /// <summary>Definitely not set up.</summary>
        Missing,
        /// <summary>Set up, but wrong.</summary>
        Wrong,
        /// <summary>The probe itself failed — timed out, tool absent. NOT the same as Ok.</summary>
        Blocked,
    }

    public enum CheckId
    {
        Package,
        Python,
        Skills,
        Confluence,
        UnityMcp,
    }

    [Serializable]
    public sealed class SetupCheck
    {
        public CheckId Id;
        public string Title;
        public CheckStatus Status = CheckStatus.Unknown;

        /// <summary>One line next to the status pill.</summary>
        public string Summary;

        /// <summary>Multi-line, behind a foldout. The exact command, its output, the reasoning.</summary>
        public string Detail;

        /// <summary>Null when there is nothing safe to automate — the row then explains what to do.</summary>
        public string FixLabel;

        /// <summary>True when the fix writes outside the Unity project and should confirm first.</summary>
        public bool FixWritesOutsideProject;

        public bool DetailExpanded;
        public double LastCheckedAt;

        [NonSerialized] public Action Fix;
        [NonSerialized] public Action Recheck;

        public bool HasFix => Fix != null && !string.IsNullOrEmpty(FixLabel);

        public void Set(CheckStatus status, string summary, string detail = null)
        {
            Status = status;
            Summary = summary;
            Detail = detail;
            LastCheckedAt = UnityEditor.EditorApplication.timeSinceStartup;
        }
    }

    [Serializable]
    public sealed class SetupState
    {
        public List<SetupCheck> Checks = new List<SetupCheck>();
        public double LastFullRunAt = -1;

        public SetupCheck Get(CheckId id) => Checks.Find(c => c.Id == id);
    }
}
