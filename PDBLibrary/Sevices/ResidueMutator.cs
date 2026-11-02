using PDBLibrary.Models;
using PDBLibrary.Sevices;
using PDBLibrary.Sevices.PDBLibrary.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDBLibrary.Srevices
{
    /// <summary>
    /// Podmienia aminokwas w strukturze i buduje jego nowy
    /// łańcuch boczny na podstawie kanonicznego szablonu geometrii.
    ///
    /// Szkielet N-CA-C-O-OXT pozostaje na swoim miejscu.
    /// Łańcuch boczny starego aminokwasu jest usuwany,
    /// a następnie tworzony jest łańcuch boczny nowego aminokwasu.
    ///
    /// Jeżeli nowo zbudowany łańcuch boczny koliduje przestrzennie z otoczeniem,
    /// próbujemy go naprawić obracając go wokół wiązania CA-CB (patrz
    /// SideChainOrientationResolver), zanim oddamy sterowanie dalej - dlatego
    /// ta metoda potrzebuje teraz całego Protein, nie tylko podmienianej reszty.
    /// </summary>
    public static class ResidueMutator
    {
        private static readonly HashSet<string> BackboneAtomNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "N", "CA", "C", "O", "OXT"
            };

        /// <summary>
        /// Podmienia aminokwas i (w miarę możliwości) eliminuje kolizje steryczne
        /// nowego łańcucha bocznego poprzez obrót wokół CA-CB.
        ///
        /// Zwraca pełny log prób (SideChainRotationResult) - nie tylko to, czy
        /// finalnie się udało, ale i to, ile błędów/ostrzeżeń dawała konformacja
        /// z szablonu oraz każdy kolejny sprawdzony kąt obrotu, żeby dało się to
        /// pokazać w UI zamiast samego "tak/nie". To tylko informacja pomocnicza -
        /// ostateczny stan reszty i tak trzeba sprawdzić pełną walidacją po
        /// mutacji (StructureValidator.ValidateAfterMutation).
        /// </summary>
        public static SideChainRotationResult Mutate(
            Protein protein,
            Residues residue,
            string newThreeLetterCode)
        {
            ValidateArgsAndBuildTemplate(protein, residue, newThreeLetterCode);

            // Szablon CCD to jedna, uśredniona konformacja - w realnej
            // strukturze nowy łańcuch boczny może kolidować przestrzennie
            // z sąsiadami. Zanim oddamy sterowanie dalej, próbujemy to
            // naprawić czysto algebraicznie: obracając cały łańcuch boczny
            // wokół wiązania CA-CB, aż przestanie kolidować (albo skończą
            // się próby kątów) - i od razu stosujemy najlepszy znaleziony wariant.
            return SideChainOrientationResolver.TryResolveClashes(protein, residue);
        }

        /// <summary>
        /// Jak <see cref="Mutate"/>, ale NIE rozwiązuje automatycznie kolizji
        /// obrotem - zamiast tego sprawdza WSZYSTKIE możliwe obroty i zwraca
        /// pełną "sondę" (<see cref="SideChainRotationProbe"/>), żeby wywołujący
        /// (UI) mógł samodzielnie zdecydować, który wariant zastosować -
        /// automatycznie najlepszy, albo wybrany interaktywnie przez użytkownika,
        /// gdy jest kilka sensownych (bezbłędnych) opcji do wyboru.
        ///
        /// Zaraz po tym wywołaniu reszta jest w konformacji 0° (prosto z
        /// szablonu CCD, jeszcze nieobrócona) - żeby faktycznie zastosować
        /// wybrany wariant, trzeba potem wywołać
        /// <see cref="SideChainOrientationResolver.ApplyRotation"/>.
        /// </summary>
        public static SideChainRotationProbe MutateAndProbeRotation(
            Protein protein,
            Residues residue,
            string newThreeLetterCode)
        {
            ValidateArgsAndBuildTemplate(protein, residue, newThreeLetterCode);

            return SideChainOrientationResolver.ProbeRotations(protein, residue);
        }

        /// <summary>
        /// Wspólna część obu metod: walidacja argumentów, usunięcie starego
        /// łańcucha bocznego i zbudowanie nowego z szablonu CCD (bez żadnej
        /// próby rozwiązania kolizji - to robi wywołujący, osobno).
        /// </summary>
        private static void ValidateArgsAndBuildTemplate(
            Protein protein,
            Residues residue,
            string newThreeLetterCode)
        {
            if (protein == null)
                throw new ArgumentNullException(nameof(protein));

            if (residue == null)
                throw new ArgumentNullException(nameof(residue));

            if (string.IsNullOrWhiteSpace(newThreeLetterCode))
                throw new ArgumentException(
                    "Nazwa nowego aminokwasu nie może być pusta.",
                    nameof(newThreeLetterCode));

            string newName =
                newThreeLetterCode.Trim().ToUpperInvariant();

            // Pobranie geometrii nowego aminokwasu
            if (!AminoAcidTemplateLibrary.TryGet(
                    newName,
                    out AminoAcidTemplate? template))
            {
                throw new InvalidOperationException(
                    $"Brak szablonu geometrii dla aminokwasu {newName}.");
            }

            // Najpierw usuwamy stary łańcuch boczny.
            // Pozostawiamy wyłącznie szkielet.
            residue.Atoms.RemoveAll(atom =>
                !BackboneAtomNames.Contains(atom.Name));

            // Zmieniamy nazwę reszty.
            residue.Name = newName;

            // Aktualizujemy nazwę reszty w atomach szkieletu.
            foreach (var atom in residue.Atoms)
            {
                atom.ResidueName = newName;
            }

            // Zbudowanie nowego łańcucha bocznego
            bool success =
                SideChainBuilder.TryBuildSideChain(
                    residue,
                    template!);

            if (!success)
            {
                throw new InvalidOperationException(
                    $"Nie można zbudować łańcucha bocznego " +
                    $"dla reszty {residue.Id}, ponieważ brakuje " +
                    $"atomów szkieletu N, CA lub C.");
            }
        }
    }
}
