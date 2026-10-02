using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Srevices
{
    /// <summary>
    /// Podmienia tozsamosc reszty aminokwasowej na inna.
    ///
    /// WAZNE - swiadome uproszczenie na tym etapie projektu (brak walidacji,
    /// brak biblioteki rotamerow):
    ///   - Szkielet (N, CA, C, O, ewentualnie OXT) ZOSTAJE dokladnie w miejscu,
    ///     w ktorym byl - to on definiuje przebieg lancucha polipeptydowego
    ///     i NIE powinien sie zmieniac przy samej podmianie tozsamosci reszty.
    ///   - Atomy lancucha bocznego (wszystko poza szkieletem i CB) sa USUWANE,
    ///     bo nie mamy jak poprawnie policzyc ich nowego polozenia bez modelu
    ///     geometrii aminokwasow / rotameru (to osobna, duza funkcjonalnosc).
    ///   - CB (pierwszy atom lancucha bocznego) jest zachowywany, jesli nowy
    ///     aminokwas rowniez go posiada (wszystkie poza glicyna) - to tylko
    ///     PRZYBLIZENIE pozycji, nie prawdziwy CB nowego aminokwasu.
    ///   - Jesli mutujemy DO glicyny, CB jest usuwany (glicyna go nie ma).
    ///
    /// Efekt: po podmianie struktura ma poprawny, ciagly szkielet, ale
    /// niekompletny/brakujacy lancuch boczny nowego aminokwasu - to celowe
    /// i bedzie uzupelnione w kolejnym etapie (np. przez prosty model
    /// geometryczny lub walidacje + alert dla uzytkownika).
    /// </summary>
    public static class ResidueMutator
    {
        private static readonly HashSet<string> BackboneAtomNames =
            new(StringComparer.OrdinalIgnoreCase) { "N", "CA", "C", "O", "OXT" };

        public static void Mutate(Residues residue, string newThreeLetterCode)
        {
            if (residue == null)
                throw new ArgumentNullException(nameof(residue));

            if (string.IsNullOrWhiteSpace(newThreeLetterCode))
                throw new ArgumentException("Nazwa nowego aminokwasu nie moze byc pusta.", nameof(newThreeLetterCode));

            string newName = newThreeLetterCode.Trim().ToUpperInvariant();
            bool newResidueHasNoCBeta = newName == "GLY";

            residue.Name = newName;

            residue.Atoms.RemoveAll(atom =>
            {
                if (BackboneAtomNames.Contains(atom.Name))
                    return false; // szkielet - zawsze zostaje

                bool isCBeta = atom.Name.Equals("CB", StringComparison.OrdinalIgnoreCase);
                if (isCBeta && !newResidueHasNoCBeta)
                    return false; // CB zostaje, chyba ze mutujemy do glicyny

                return true; // reszta lancucha bocznego - usuwamy
            });

            // synchronizacja metadanych na atomach (uzywane m.in. przy ewentualnym
            // debugowaniu/eksporcie bezposrednio z atomu, writer i tak czyta z Residue)
            foreach (var atom in residue.Atoms)
                atom.ResidueName = newName;
        }
    }
}
