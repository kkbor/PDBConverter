using PDBLibrary.Geometry;
using PDBLibrary.Models;
using PDBLibrary.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Sevices
{
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
    /// "dotkliwość" kolizji sterycznych z resztą struktury (ClashDetector) i
    /// zostajemy przy obrocie, dla którego jest ona najmniejsza.
    ///
    /// Świadome ograniczenie: to wciąż JEDNA, kanoniczna konformacja z szablonu
    /// CCD, tylko obrócona jako całość wokół CA-CB - nie dostrajamy osobno
    /// kątów chi2/chi3 w głębi łańcucha (to już realna domena biblioteki
    /// rotamerów, np. Dunbrack). Dla GLY (brak CB) i ALA (jedyny atom boczny to
    /// sam CB, leżący NA osi obrotu - obrót nic by nie zmienił) nie ma czego
    /// obracać, więc od razu zwracamy false.
    /// </summary>
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

    /// <summary>Wynik próby naprawienia kolizji łańcucha bocznego obrotem wokół CA-CB.</summary>
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
        /// wyjściowa (0°), kolejne to kolejne kąty co 360/StepsPerFullTurn stopni -
        /// aż do znalezienia zera kolizji albo wyczerpania prób.
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
    /// "dotkliwość" kolizji sterycznych z resztą struktury (ClashDetector) i
    /// zostajemy przy obrocie, dla którego jest ona najmniejsza.
    ///
    /// Świadome ograniczenie: to wciąż JEDNA, kanoniczna konformacja z szablonu
    /// CCD, tylko obrócona jako całość wokół CA-CB - nie dostrajamy osobno
    /// kątów chi2/chi3 w głębi łańcucha (to już realna domena biblioteki
    /// rotamerów, np. Dunbrack). Dla GLY (brak CB) i ALA (jedyny atom boczny to
    /// sam CB, leżący NA osi obrotu - obrót nic by nie zmienił) nie ma czego
    /// obracać, więc od razu zwracamy NotAttempted.
    /// </summary>
    public static class SideChainOrientationResolver
    {
        private static readonly HashSet<string> BackboneAtomNames =
            new(StringComparer.OrdinalIgnoreCase) { "N", "CA", "C", "O", "OXT" };

        // Co ile stopni próbujemy kolejny obrót (360 / 24 = 15 stopni/krok).
        // Mniejszy krok = dokładniejsze poszukiwanie, ale każdy krok to pełne
        // sprawdzenie kolizji reszty względem całej struktury (ClashDetector),
        // więc nie warto przesadzać z gęstością siatki kątów.
        private const int StepsPerFullTurn = 24;

        /// <summary>
        /// Próbuje znaleźć obrót łańcucha bocznego wokół wiązania CA-CB, który
        /// eliminuje albo przynajmniej zmniejsza konflikty przestrzenne reszty
        /// z resztą struktury. Atomy reszty są modyfikowane w miejscu.
        ///
        /// Zwraca pełny log prób (SideChainRotationResult.Attempts) - pierwsza
        /// pozycja to zawsze konformacja wyjściowa z szablonu (0°, bez obrotu),
        /// razem z jej błędami/ostrzeżeniami, a kolejne to kolejne sprawdzone
        /// kąty - dzięki temu wywołujący widzi nie tylko finalny wynik, ale i
        /// to, co po drodze nie zadziałało.
        /// </summary>
        public static SideChainRotationResult TryResolveClashes(Protein protein, Residues residue)
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
                return SideChainRotationResult.NotAttempted;
            }

            var pivot = new Vec3(ca.X, ca.Y, ca.Z);
            var axis = (new Vec3(cb.X, cb.Y, cb.Z) - pivot).Normalized();

            // Oryginalne pozycje - potrzebne, żeby wrócić do najlepszego
            // znalezionego wariantu (albo do oryginału, jeśli żaden obrót nie
            // okazał się lepszy niż konformacja z szablonu).
            var originalPositions = sideChainAtoms
                .Select(a => new Vec3(a.X, a.Y, a.Z))
                .ToList();

            var attempts = new List<RotationAttempt>();

            var (initialErrors, initialWarnings, initialIssues) = ProbeClashes(protein, residue);
            attempts.Add(new RotationAttempt(0, initialErrors, initialWarnings, initialIssues));

            double bestOverlap = initialErrors * 2 + initialWarnings;

            if (bestOverlap <= 0)
            {
                // już teraz nie koliduje - nie ma czego naprawiać
                return new SideChainRotationResult
                {
                    Attempted = true,
                    HadInitialClash = false,
                    FoundBetter = false,
                    FinalAngleDegrees = 0,
                    Attempts = attempts
                };
            }

            var bestPositions = originalPositions;
            double bestAngleDegrees = 0;
            bool foundBetter = false;

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
                    bestPositions = rotated;
                    bestAngleDegrees = angleDeg;
                    foundBetter = true;

                    if (overlap <= 0)
                        break; // znaleziono orientację bez konfliktów - koniec poszukiwań
                }
            }

            ApplyPositions(sideChainAtoms, bestPositions);

            return new SideChainRotationResult
            {
                Attempted = true,
                HadInitialClash = true,
                FoundBetter = foundBetter,
                FinalAngleDegrees = bestAngleDegrees,
                Attempts = attempts
            };
        }

        /// <summary>
        /// Błędy i ostrzeżenia sterczne reszty względem reszty struktury - zarówno
        /// policzone, jak i w pełnej treści (do pokazania w logu/UI).
        /// Uwaga wydajnościowa: ClashDetector.ValidateResidue przelicza siatkę
        /// przestrzenną i wiązania całej struktury przy każdym wywołaniu - dla
        /// bardzo dużych struktur ta pętla (do 24 prób) będzie to odczuwać.
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
        /// wyliczona wprost ze wzoru Rodriguesa (R = I + sin(θ)K + (1-cos θ)K²),
        /// rozpisanego na 9 skalarnych współczynników i złożona jako trzy
        /// kolumny Mat3 - bez zależności od tego, czy Vec3 udostępnia mnożenie
        /// przez skalar albo iloczyn skalarny.
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
