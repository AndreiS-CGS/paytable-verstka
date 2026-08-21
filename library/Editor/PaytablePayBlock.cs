using System.Collections.Generic;
using System.Linq;

namespace CGS.PaytableLibrary
{
    /// <summary>
    /// Text-formatting rules for the PayBlock.prefab's Count/Pay pair — see BLOCKS.md. Pure string
    /// logic, no Unity API; callers assign the results to the two TMP_Text components themselves.
    /// </summary>
    public static class PaytablePayBlock
    {
        /// <summary>
        /// Count column: first line always BLANK (aligns with Pay's "1 credit" label line), then
        /// one count number per line, the whole run wrapped in a single color tag.
        /// </summary>
        public static string FormatCount(IEnumerable<string> counts, string color = "green")
        {
            return $"<color={color}>\n{string.Join("\n", counts)}</color>";
        }

        /// <summary>
        /// Pay column: first line is ALWAYS the literal "1 credit" (added by convention — do not
        /// read this from the reference screenshot, it may not show it there), left uncolored;
        /// remaining lines are the pay values (keep any bonus suffix like "(+10 FREE GAMES)" on
        /// the SAME line as its number) wrapped in a single color tag spanning all of them.
        /// </summary>
        public static string FormatPay(IEnumerable<string> values, string color = "yellow")
        {
            return $"1 credit\n<color={color}>{string.Join("\n", values)}</color>\n";
        }

        /// <summary>Convenience overload for plain integer pay values with no bonus suffix.</summary>
        public static string FormatPay(IEnumerable<int> values, string color = "yellow")
        {
            return FormatPay(values.Select(v => v.ToString()), color);
        }
    }
}
