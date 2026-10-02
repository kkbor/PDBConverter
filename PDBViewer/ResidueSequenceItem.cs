using PDBLibrary.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDBViewer
{
    /// <summary>
    /// Lekki element danych dla jednego "kafelka" sekwencji. ItemsControl tworzy
    /// kontenery wizualne na podstawie tych obiektow, co pozwala trzymac stan
    /// (IsHighlighted - hover, IsSelected - kliknięcie) w danych, a nie w
    /// konkretnym, moze-juz-recyklingowanym elemencie UI.
    /// </summary>
    public class ResidueSequenceItem : INotifyPropertyChanged
    {
        public Residues? Residue { get; }
        public Chains? Chain { get; }
        public bool IsLabel { get; }

        private string _letter;
        public string Letter
        {
            get => _letter;
            private set { if (_letter != value) { _letter = value; OnPropertyChanged(); } }
        }

        private string _tooltip;
        public string Tooltip
        {
            get => _tooltip;
            private set { if (_tooltip != value) { _tooltip = value; OnPropertyChanged(); } }
        }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChanged(); } }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        // konstruktor dla reszty aminokwasowej
        public ResidueSequenceItem(Chains chain, Residues residue)
        {
            Chain = chain;
            Residue = residue;
            IsLabel = false;
            _letter = string.Empty;
            _tooltip = string.Empty;
            RefreshFromResidue();
        }

        // konstruktor dla etykiety lancucha ("Łańcuch A:")
        public ResidueSequenceItem(string labelText)
        {
            IsLabel = true;
            _letter = labelText;
            _tooltip = string.Empty;
        }

        /// <summary>
        /// Przelicza litere i tooltip na podstawie aktualnego stanu Residue.
        /// Wywolac po mutacji aminokwasu, zeby panel sekwencji pokazal nowa
        /// litere bez konieczności przebudowy calego panelu od zera.
        /// </summary>
        public void RefreshFromResidue()
        {
            if (Residue == null || Chain == null)
                return;

            Letter = AminoAcidCodes.GetOneLetterCode(Residue.Name).ToString();
            Tooltip = $"{Residue.Name} {Residue.Id} (łańcuch {Chain.Id})";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
