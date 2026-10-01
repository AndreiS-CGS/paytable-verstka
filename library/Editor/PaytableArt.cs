using UnityEngine;
using UnityEngine.UI;

namespace CGS.PaytableLibrary
{
    /// <summary>
    /// The one way art goes into a block's <c>Image</c>.
    ///
    /// WHY THIS EXISTS. Every placeholder <c>Image</c> in the library (<c>IconSlot</c>,
    /// <c>ImageContainer_1..4</c>, <c>ManualSlot</c>) ships with a magenta TINT on the component,
    /// not just a magenta placeholder sprite — that is what makes an unfilled slot impossible to
    /// miss. Assigning <c>Image.sprite</c> alone keeps the tint, so the real art renders multiplied
    /// by magenta: a green trolley comes out black, a gold ingot red. Every automated check still
    /// passes — the sprite is right, the name is right, nothing overflows — and only a render shows
    /// it. Assign through here and the tint is cleared in the same step.
    /// </summary>
    public static class PaytableArt
    {
        public static void Assign(Image target, Sprite sprite)
        {
            target.sprite = sprite;
            target.color = Color.white;
            target.preserveAspect = true;
        }

        /// <summary>Copies art from an existing filled Image, keeping its tint (normally white).</summary>
        public static void CopyFrom(Image target, Image source)
        {
            target.sprite = source.sprite;
            target.color = source.color;
            target.preserveAspect = source.preserveAspect;
        }

        /// <summary>
        /// True when an active Image still carries the placeholder tint.
        ///
        /// NOT CALLED BY ANYTHING YET. It exists so the magenta-tint failure can be checked
        /// mechanically instead of only by looking at a render, but the checker that would walk a
        /// finished prefab and call it has not been written. Until it is, a wrong tint reaches the
        /// final report only if a human sees the picture — and the whole reason this class exists
        /// is that every other automated check passes while the art renders the wrong colour.
        ///
        /// When that checker is written: a slot left magenta ON PURPOSE (art missing, agreed at the
        /// art gate) must be reported too, not filtered out. It belongs in the run's final report
        /// either way.
        /// </summary>
        public static bool IsPlaceholderTint(Image image)
        {
            var c = image.color;
            return c.r > 0.9f && c.g < 0.1f && c.b > 0.75f;
        }
    }
}
