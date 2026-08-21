using System;

namespace CGS.PaytableLibrary
{
    /// <summary>
    /// Grid-layout math for PIC/Card pay pages, and for GoldBox-as-panel sizing (see the
    /// paytable-verstka skill, Phase 5b/5c). Body is fixed at 1410x1440 in this library's shells.
    /// This class holds only the arithmetic — assembly (instantiating prefabs, filling content)
    /// is done by the skill's own execute_code calls, which call into this class.
    /// </summary>
    public static class PaytableGridMath
    {
        public const float BodyWidth = 1410f;
        public const float BodyHeight = 1440f;
        public const int MaxCellsPerRow = 3;
        /// <summary>Uniform edge/gap unit used everywhere in this library's grid and panel sizing.</summary>
        public const float Gap = 50f;

        /// <summary>
        /// Distributes n symbols across ceil(n / MaxCellsPerRow) rows as evenly as possible, with
        /// the larger remainder going to the earlier rows, and never an orphan single-item last
        /// row. Examples: 4-&gt;[2,2], 5-&gt;[3,2], 6-&gt;[3,3], 7-&gt;[3,2,2], 8-&gt;[3,3,2], 9-&gt;[3,3,3].
        /// </summary>
        public static int[] DistributeRows(int n)
        {
            if (n <= 0) return Array.Empty<int>();
            int rows = (int)Math.Ceiling(n / (float)MaxCellsPerRow);
            int baseCount = n / rows;
            int remainder = n % rows;
            var dist = new int[rows];
            for (int i = 0; i < rows; i++)
                dist[i] = baseCount + (i < remainder ? 1 : 0);
            return dist;
        }

        public struct CellSize
        {
            public float widthCell;
            public float heightCell;
            /// <summary>What to set on the GoldBoxRow's own RectTransform — includes the row's
            /// built-in top/bottom padding so rows stack with a uniform 50-unit gap and edge.</summary>
            public float rowHeight;
        }

        /// <summary>
        /// Symmetric cell sizing for a PIC/Card grid: <see cref="Gap"/> at every edge and between
        /// every cell/row, both horizontally and vertically. `maxCols` = the widest row on this
        /// page; `rows` = DistributeRows(n).Length. Do NOT feed an asymmetric edge/gap budget in
        /// here — a uniform symmetric budget is what makes this formula correct in the first place
        /// (see paytable-verstka's SKILL.md "Known gotchas" for why an uneven split breaks under
        /// Body's own VerticalLayoutGroup alignment).
        /// </summary>
        public static CellSize ComputeCellSize(int rows, int maxCols)
        {
            float widthCell = (BodyWidth - 2 * Gap - Gap * (maxCols - 1)) / maxCols;
            float heightCell = (BodyHeight - 2 * Gap - Gap * (rows - 1)) / rows;
            return new CellSize
            {
                widthCell = widthCell,
                heightCell = heightCell,
                rowHeight = heightCell + Gap
            };
        }
    }
}
