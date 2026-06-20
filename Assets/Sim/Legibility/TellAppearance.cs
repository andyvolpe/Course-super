// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Legibility
{
    /// <summary>Plain RGB triple (0..1) so the Sim can describe colour without UnityEngine.Color.</summary>
    public struct Rgb
    {
        public double R, G, B;
        public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
        public override string ToString() => $"({R:F2},{G:F2},{B:F2})";
    }

    /// <summary>
    /// The honest, always-visible visual tell for one green sub-cell (TDD §6 legibility mapping).
    /// The Unity green shader consumes exactly these channels — it renders what the sim permits and
    /// computes nothing about hidden state itself. Per the fairness contract these tells NARROW the
    /// possibilities; they do not pinpoint a cause (e.g. dark green reads the same for vigorous and
    /// N-pushed turf), and turf debt is never encoded here — it only shows THROUGH thinning.
    /// </summary>
    public struct TellAppearance
    {
        public Rgb BaseColor;   // canopy colour (pale=starved, mid/dark=adequate..pushed — ambiguous on purpose)
        public double Lesions;  // 0..1 dollar-spot spotting on this cell
        public double Thinning; // 0..1 bare/thin canopy on this cell
        public double WetSheen; // 0..1 dark wet look (over-watered)
        public double WiltTint; // 0..1 blue/grey wilt (droughted)
    }
}
