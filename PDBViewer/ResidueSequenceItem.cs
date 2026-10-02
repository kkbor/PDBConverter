using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PDBViewer
{
     /// <summary>
     /// Lekki element danych dla jednego "kafelka" sekwencji. ItemsControl tworzy
     /// kontenery wizualne na podstawie TAKICH obiektow (nie na podstawie Residues
     /// bezposrednio), co pozwala trzymac stan podswietlenia (IsHighlighted) w danych,
     /// a nie w konkretnym, moze-juz-recyklingowanym elemencie UI.
     /// </summary>
    public class ResidueSequenceItem : INotifyPropertyChanged
    {
        public Residues? Residue { get; }
        public Chains? Chain { get; }
        public bool IsLabel { get; }
        public string Letter { get; }
        public string Tooltip { get; }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged();
                }
            }
        }

        // konstruktor dla reszty aminokwasowej
        public ResidueSequenceItem(Chains chain, Residues residue)
        {
            Chain = chain;
            Residue = residue;
            IsLabel = false;
            Letter = AminoAcidCodes.GetOneLetterCode(residue.Name).ToString();
            Tooltip = $"{residue.Name} {residue.Id} (łańcuch {chain.Id})";
        }

        // konstruktor dla etykiety lancucha ("Łańcuch A:")
        public ResidueSequenceItem(string labelText)
        {
            IsLabel = true;
            Letter = labelText;
            Tooltip = string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
