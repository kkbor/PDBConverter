using PDBLibrary.Models;
using PDBLibrary.Sevices;
using PDBLibrary.Sevices.PDBLibrary.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Srevices
{
    /// <summary>
    /// Podmienia aminokwas w strukturze i buduje jego nowy
    /// łańcuch boczny na podstawie kanonicznego szablonu geometrii.
    ///
    /// Szkielet N-CA-C-O-OXT pozostaje na swoim miejscu.
    /// Łańcuch boczny starego aminokwasu jest usuwany,
    /// a następnie tworzony jest łańcuch boczny nowego aminokwasu.
    /// </summary>
    public static class ResidueMutator
    {
        private static readonly HashSet<string> BackboneAtomNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "N", "CA", "C", "O", "OXT"
            };

        public static void Mutate(
            Residues residue,
            string newThreeLetterCode)
        {
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
                    template);

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