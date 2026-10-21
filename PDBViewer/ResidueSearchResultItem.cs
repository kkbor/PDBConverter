using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBViewer
{
    /// <summary>
    /// Pojedynczy wynik na liście wyszukiwarki aminokwasów/reszt (panel
    /// "Szukaj aminokwasu/reszty" nad panelem sekwencji).
    ///
    /// Lekki obiekt wyłącznie do wyświetlenia - kliknięcie wyniku na liście
    /// powoduje zaznaczenie wskazywanej reszty dokładnie tak samo, jak
    /// kliknięcie jej litery w panelu sekwencji (SelectResidue), plus
    /// przewinięcie do niej w panelu sekwencji.
    /// </summary>
    public class ResidueSearchResultItem
    {
        public Chains Chain { get; }
        public Residues Residue { get; }

        /// <summary>Tekst pokazywany na liście wyników, np. "Łańcuch A: ALA 25".</summary>
        public string DisplayText { get; }

        public ResidueSearchResultItem(Chains chain, Residues residue)
        {
            Chain = chain;
            Residue = residue;
            DisplayText = $"Łańcuch {chain.Id}: {residue.Name} {residue.Id}";
        }
    }
}
