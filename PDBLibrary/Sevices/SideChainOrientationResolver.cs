using PDBLibrary.Geometry;
using PDBLibrary.Models;
using PDBLibrary.Validation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDBLibrary.Srevices
{
    /// <summary>
    /// Jedna próba obrotu łańcucha bocznego wokół CA-CB - do pokazania w UI/logu,
    /// żeby było widać NIE TYLKO finalny wynik, ale i to, co po drodze nie zadziałało
    /// (np. "oryginalna konformacja: 2 błędy" -> "obrót o 15°: 2 błędy" -> ... ->
    /// "obrót o 90°: 0 błędów - wybrano").
    /// </summary>
    public readonly struct RotationAttempt
    {
        /// <summary>0 = konformacja z szablonu, bez obrotu.</summary>
        public double AngleDegrees { get; }
        public int ErrorCount { get; }
        public int WarningCount { get; }

        /// <summary>
        /// Rzeczywiste problemy (treść komunikatów, nie tylko ich liczba) znalezione
        /// PRZY TEJ próbie - dzięki temu, jeśli wybrany obrót dalej zostawia jakieś
        /// ostrzeżenie (np. mniej błędów, ale nie zero), da się pokazać dokładnie,
        /// co to za ostrzeżenie, a nie tylko samą liczbę.
        /// </summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        public RotationAttempt(double angleDegrees, int errorCount, int warningCount, IReadOnlyList<ValidationIssue> issues)
        {
            AngleDegrees = angleDegrees;
            ErrorCount = errorCount;
            WarningCount = warningCount;
            Issues = issues;
        }

        internal double Severity => ErrorCount * 2 + WarningCount;

        public override string ToString() =>
            AngleDegrees == 0
                ? $"konformacja z szablonu (bez obrotu): {ErrorCount} błędów, {WarningCount} ostrzeżeń"
                : $"obrót o {AngleDegrees:F0}°: {ErrorCount} błędów, {WarningCount} ostrzeżeń";
    }

    /// <summary>
    /// Pełny wynik SPRAWDZENIA (probingu) wszystkich obrotów łańcucha bocznego -
    /// BEZ automatycznego zastosowania żadnego z nich. Reszta po wywołaniu
    /// <see cref="SideChainOrientationResolver.ProbeRotations"/> jest zawsze z
    /// powrotem w konformacji 0° (prosto z szablonu CCD) - to, który wariant
    /// faktycznie zastosować (automatycznie najlepszy, albo wybrany przez
    /// użytkownika), ustala wywołujący poprzez
    /// <see cref="SideChainOrientationResolver.ApplyRotation"/>.
    /// </summary>
    public sealed class SideChainRotationProbe
    {
        /// <summary>False dla GLY/ALA/niekompletnego szkieletu - nie było czego próbować.</summary>
        public bool Attempted { get; init; }

        /// <summary>Czy konformacja z szablonu (przed jakimkolwiek obrotem) w ogóle kolidowała.</summary>
        public bool HadInitialClash { get; init; }

        /// <summary>Czy znaleziono obrót lepszy (niższa "dotkliwość" błędów/ostrzeżeń) niż konformacja wyjściowa.</summary>
        public bool FoundBetter { get; init; }

        /// <summary>
        /// Kąt o NAJNIŻSZEJ "dotkliwości" (błędy liczą się podwójnie względem
        /// ostrzeżeń) spośród wszystkich sprawdzonych - domyślna propozycja,
        /// gdy wywołujący nie chce (albo nie musi) pytać użytkownika o wybór.
        /// Uwaga: to niekoniecznie wariant BEZ błędów - jeśli żaden taki nie
        /// istnieje, to po prostu "najmniej zły" z całej puli.
        /// </summary>
        public double BestAngleDegrees { get; init; }

        /// <summary>
        /// Log KAŻDEJ sprawdzonej próby, w kolejności: [0] to zawsze konformacja
        /// wyjściowa (0°), kolejne to kolejne kąty co 360/StepsPerFullTurn stopni.
        /// Sprawdzane są WSZYSTKIE kąty (bez przerywania na pierwszym bezbłędnym) -
        /// dzięki temu, jeśli istnieje więcej niż jeden wariant bez błędów,
        /// wywołujący (UI) może dać użytkownikowi wybór między nimi.
        /// </summary>
        public IReadOnlyList<RotationAttempt> Attempts { get; init; } = Array.Empty<RotationAttempt>();

        public static readonly SideChainRotationProbe NotAttempted = new() { Attempted = false };
    }

    /// <summary>Wynik próby naprawienia kolizji łańcucha bocznego obrotem wokół CA-CB (ścieżka automatyczna, bez wyboru użytkownika).</summary>
    public sealed class SideChainRotationResult
    {
        /// <summary>False dla GLY/ALA/niekompletnego szkieletu - nie było czego próbować.</summary>
        public bool Attempted { get; init; }

        /// <summary>Czy konformacja z szablonu (przed jakimkolwiek obrotem) w ogóle kolidowała.</summary>
        public bool HadInitialClash { get; init; }

        /// <summary>Czy znaleziono obrót lepszy niż konformacja wyjściowa.</summary>
        public bool FoundBetter { get; init; }

        /// <summary>Kąt, przy którym ostatecznie zostawiono resztę (0, jeśli nie obracano/nic nie pomogło).</summary>
        public double FinalAngleDegrees { get; init; }

        /// <summary>
        /// Log KAŻDEJ sprawdzonej próby, w kolejności: [0] to zawsze konformacja
        /// wyjściowa (0°), kolejne to kolejne kąty co 360/StepsPerFullTurn stopni.
        /// </summary>
        public IReadOnlyList<RotationAttempt> Attempts { get; init; } = Array.Empty<RotationAttempt>();

        public static readonly SideChainRotationResult NotAttempted = new() { Attempted = false };
    }

    /// <summary>
    /// Jeżeli nowo zbudowany łańcuch boczny (SideChainBuilder) koliduje
    /// przestrzennie z otoczeniem, próbujemy go "obrócić", zanim zgłosimy to
    /// jako gotowy konflikt.
    ///
    /// Łańcuch boczny ma względem szkieletu tak naprawdę jeden sensowny stopień
    /// swobody, który możemy ruszyć bez łamania geometrii szablonu: obrót CAŁEGO
    /// łańcucha bocznego jako sztywnej bryły wokół osi wiązania CA-CB (odpowiednik
    /// kąta torsyjnego chi1). To czysto algebraiczna operacja - macierz obrotu
    /// wokół dowolnej osi (wzór Rodriguesa), bez żadnej biblioteki rotamerów:
    /// próbujemy kolejne kąty co 360/StepsPerFullTurn stopni, dla każdego liczymy
    /// "dotkliwość" kolizji sterycznych z resztą struktury (ClashDetector).
    ///
    /// Dwie warstwy API:
    ///   - ProbeRotations + ApplyRotation - rozdzielone "sprawdź wszystko" i
    ///     "zastosuj wybrany wariant", żeby UI mogło wstawić między nie okno
    ///     wyboru, gdy jest kilka sensownych (bezbłędnych) opcji.
    ///   - TryResolveClashes - wygodny, w pełni automatyczny wrapper na tych
    ///     dwóch (bez pytania o cokolwiek) - zachowany dla wywołujących, którym
    ///     wystarczy samo-automatyczne zachowanie (np. testy).
    ///
    /// Świadome ograniczenie: to wciąż JEDNA, kanoniczna konformacja z szablonu
    /// CCD, tylko obrócona jako całość wokół CA-CB - nie dostrajamy osobno
    /// kątów chi2/chi3 w głębi łańcucha. Dla GLY (brak CB) i ALA (jedyny atom
    /// boczny to sam CB, leżący NA osi obrotu) nie ma czego obracać.
    /// </summary>
    public static class SideChainOrientationResolver
    {
        private static readonly HashSet<string> BackboneAtomNames =
            new(StringComparer.OrdinalIgnoreCase) { "N", "CA", "C", "O", "OXT" };

        // Co ile stopni próbujemy kolejny obrót (360 / 24 = 15 stopni/krok).
        private const int StepsPerFullTurn = 24;

        /// <summary>
        /// Sprawdza WSZYSTKIE kąty obrotu łańcucha bocznego wokół CA-CB (bez
        /// przerywania na pierwszym bezbłędnym wariancie) i zwraca pełny log.
        ///
        /// WAŻNE: ta metoda NIE zostawia reszty obróconej - niezależnie od
        /// wyniku, na końcu atomy są z powrotem w konformacji 0° (prosto z
        /// szablonu). Żeby faktycznie zastosować któryś wariant, wywołaj potem
        /// <see cref="ApplyRotation"/> z wybranym kątem.
        /// </summary>
        public static SideChainRotationProbe ProbeRotations(Protein protein, Residues residue)
        {
            if (protein == null) throw new ArgumentNullException(nameof(protein));
            if (residue == null) throw new ArgumentNullException(nameof(residue));

            var ca = FindAtom(residue, "CA");
            var cb = FindAtom(residue, "CB");

            var sideChainAtoms = residue.Atoms
                .Where(a => !BackboneAtomNames.Contains(a.Name))
                .ToList();

            // GLY: brak CB - nie ma łańcucha bocznego.
            // ALA: jedyny atom boczny to CB, który leży NA osi obrotu CA-CB,
            // więc dowolny obrót wokół tej osi go nie rusza - nie ma sensu próbować.
            if (ca == null || cb == null || sideChainAtoms.Count == 0 ||
                (sideChainAtoms.Count == 1 && ReferenceEquals(sideChainAtoms[0], cb)))
            {
                return SideChainRotationProbe.NotAttempted;
            }

            var pivot = new Vec3(ca.X, ca.Y, ca.Z);
            var axis = (new Vec3(cb.X, cb.Y, cb.Z) - pivot).Normalized();

            // Oryginalne pozycje (konformacja z szablonu, 0°) - do przywrócenia
            // na koniec i jako baza dla każdej próbowanej rotacji.
            var originalPositions = sideChainAtoms
                .Select(a => new Vec3(a.X, a.Y, a.Z))
                .ToList();

            var attempts = new List<RotationAttempt>();

            var (initialErrors, initialWarnings, initialIssues) = ProbeClashes(protein, residue);
            attempts.Add(new RotationAttempt(0, initialErrors, initialWarnings, initialIssues));

            bool hadInitialClash = initialErrors > 0 || initialWarnings > 0;

            double bestOverlap = initialErrors * 2 + initialWarnings;
            double bestAngleDegrees = 0;
            bool foundBetter = false;

            if (hadInitialClash)
            {
                for (int step = 1; step < StepsPerFullTurn; step++)
                {
                    double angleRad = step * (2.0 * Math.PI / StepsPerFullTurn);
                    double angleDeg = step * (360.0 / StepsPerFullTurn);

                    var rotation = RotationMatrixAroundAxis(axis, angleRad);

                    var rotated = originalPositions
                        .Select(p => (rotation * (p - pivot)) + pivot)
                        .ToList();

                    ApplyPositions(sideChainAtoms, rotated);

                    var (errors, warnings, issues) = ProbeClashes(protein, residue);
                    attempts.Add(new RotationAttempt(angleDeg, errors, warnings, issues));

                    double overlap = errors * 2 + warnings;

                    if (overlap < bestOverlap)
                    {
                        bestOverlap = overlap;
                        bestAngleDegrees = angleDeg;
                        foundBetter = true;
                    }

                    // Celowo NIE przerywamy na pierwszym bezbłędnym wariancie -
                    // chcemy pełny obraz wszystkich opcji, żeby dało się wybrać
                    // między kilkoma wariantami bez błędów (różniącymi się np.
                    // liczbą ostrzeżeń).
                }
            }

            // Reszta ZAWSZE wraca do konformacji wyjściowej (0°) - decyzja,
            // który wariant faktycznie zastosować, zapada POZA tą metodą.
            ApplyPositions(sideChainAtoms, originalPositions);

            return new SideChainRotationProbe
            {
                Attempted = true,
                HadInitialClash = hadInitialClash,
                FoundBetter = foundBetter,
                BestAngleDegrees = bestAngleDegrees,
                Attempts = attempts
            };
        }

        /// <summary>
        /// Stosuje wybrany kąt obrotu łańcucha bocznego wokół CA-CB. Zakłada, że
        /// reszta jest AKTUALNIE w konformacji 0° (tak jak zostawia ją
        /// <see cref="ProbeRotations"/>) - wywołanie z kątem 0 jest nieszkodliwym
        /// no-opem (reszta już tam jest).
        /// </summary>
        public static void ApplyRotation(Residues residue, double angleDegrees)
        {
            if (residue == null) throw new ArgumentNullException(nameof(residue));

            if (angleDegrees == 0)
                return;

            var ca = FindAtom(residue, "CA");
            var cb = FindAtom(residue, "CB");

            var sideChainAtoms = residue.Atoms
                .Where(a => !BackboneAtomNames.Contains(a.Name))
                .ToList();

            if (ca == null || cb == null || sideChainAtoms.Count == 0)
                return;

            var pivot = new Vec3(ca.X, ca.Y, ca.Z);
            var axis = (new Vec3(cb.X, cb.Y, cb.Z) - pivot).Normalized();

            var basePositions = sideChainAtoms
                .Select(a => new Vec3(a.X, a.Y, a.Z))
                .ToList();

            double angleRad = angleDegrees * Math.PI / 180.0;
            var rotation = RotationMatrixAroundAxis(axis, angleRad);

            var rotated = basePositions
                .Select(p => (rotation * (p - pivot)) + pivot)
                .ToList();

            ApplyPositions(sideChainAtoms, rotated);
        }

        /// <summary>
        /// Wygodny, w pełni automatyczny wrapper: sprawdza wszystkie obroty i od
        /// razu stosuje ten o najniższej "dotkliwości" (bez pytania o cokolwiek).
        /// Zachowany dla wywołujących, którzy nie chcą interaktywnego wyboru
        /// (np. testy jednostkowe).
        /// </summary>
        public static SideChainRotationResult TryResolveClashes(Protein protein, Residues residue)
        {
            var probe = ProbeRotations(protein, residue);

            if (!probe.Attempted)
                return SideChainRotationResult.NotAttempted;

            if (probe.HadInitialClash)
                ApplyRotation(residue, probe.BestAngleDegrees);

            return new SideChainRotationResult
            {
                Attempted = true,
                HadInitialClash = probe.HadInitialClash,
                FoundBetter = probe.FoundBetter,
                FinalAngleDegrees = probe.HadInitialClash ? probe.BestAngleDegrees : 0,
                Attempts = probe.Attempts
            };
        }

        /// <summary>
        /// Błędy i ostrzeżenia steryczne reszty względem reszty struktury - zarówno
        /// policzone, jak i w pełnej treści (do pokazania w logu/UI).
        /// </summary>
        private static (int errors, int warnings, List<ValidationIssue> issues) ProbeClashes(Protein protein, Residues residue)
        {
            var probeReport = new ValidationReport();
            ClashDetector.ValidateResidue(protein, residue, probeReport);

            int errors = probeReport.Issues.Count(i => i.Severity == ValidationSeverity.Error);
            int warnings = probeReport.Issues.Count(i => i.Severity == ValidationSeverity.Warning);
            return (errors, warnings, probeReport.Issues);
        }

        private static void ApplyPositions(List<Atoms> atoms, List<Vec3> positions)
        {
            for (int i = 0; i < atoms.Count; i++)
            {
                atoms[i].X = positions[i].X;
                atoms[i].Y = positions[i].Y;
                atoms[i].Z = positions[i].Z;
            }
        }

        /// <summary>
        /// Macierz obrotu o dany kąt (radiany) wokół znormalizowanej osi k,
        /// wyliczona wprost ze wzoru Rodriguesa (R = I + sin(θ)K + (1-cos θ)K²).
        /// </summary>
        private static Mat3 RotationMatrixAroundAxis(Vec3 k, double angle)
        {
            double kx = k.X, ky = k.Y, kz = k.Z;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double t = 1 - cos;

            var col0 = new Vec3(
                cos + kx * kx * t,
                ky * kx * t + kz * sin,
                kz * kx * t - ky * sin);

            var col1 = new Vec3(
                kx * ky * t - kz * sin,
                cos + ky * ky * t,
                kz * ky * t + kx * sin);

            var col2 = new Vec3(
                kx * kz * t + ky * sin,
                ky * kz * t - kx * sin,
                cos + kz * kz * t);

            return new Mat3(col0, col1, col2);
        }

        private static Atoms? FindAtom(Residues residue, string name) =>
            residue.Atoms.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
