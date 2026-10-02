using HelixToolkit.Maths;
using Microsoft.Win32;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
using PDBLibrary.Srevices;
using PDBViewer.Vizualization;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PDBViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Protein? _protein;
        private readonly ProteinVisualizer _visualizer = new();

        // dane (nie modele!) potrzebne do zbudowania overlayu podswietlenia danej reszty
        private Dictionary<Residues, ResidueGeometryInfo> _residueInfo = new();

        private Residues? _highlightedResidue;

        // reszta i element sekwencji aktualnie ZAZNACZONE klikniectem (do podmiany)
        private Residues? _selectedResidue;
        private ResidueSequenceItem? _selectedItem;

        // dane dla wirtualizowanego ItemsControl (sekwencja aminokwasow)
        private readonly ObservableCollection<ResidueSequenceItem> _sequenceItems = new();

        public MainWindow()
        {
            InitializeComponent();
            SequenceItemsControl.ItemsSource = _sequenceItems;
            PopulateMutateCombo();
        }

        private void PopulateMutateCombo()
        {
            foreach (var (code, name) in AminoAcidCodes.StandardAminoAcids)
            {
                MutateCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{code} - {name}",
                    Tag = code
                });
            }
            MutateCombo.SelectedIndex = 0;
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Pliki PDB (*.pdb)|*.pdb|Wszystkie pliki (*.*)|*.*",
                Title = "Wybierz plik PDB"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var parser = new PdbParsers();
                _protein = parser.Load(dialog.FileName);

                int atomCount = _protein.Chains.Sum(c => c.Residues.Sum(r => r.Atoms.Count));
                int residueCount = _protein.Chains.Sum(c => c.Residues.Count);

                InfoText.Text = $"Łańcuchów: {_protein.Chains.Count}, " +
                                 $"reszt: {residueCount}, atomów: {atomCount}";

                SaveButton.IsEnabled = true;
                ClearSelection();

                RenderProtein();
                BuildSequencePanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd wczytywania pliku PDB:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_protein == null)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "Pliki PDB (*.pdb)|*.pdb|Wszystkie pliki (*.*)|*.*",
                Title = "Zapisz plik PDB",
                FileName = "structure.pdb"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                PdbWriter.Save(_protein, dialog.FileName);
                MessageBox.Show("Plik PDB zapisany pomyślnie.", "Zapisano",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd zapisu pliku PDB:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_protein != null)
                RenderProtein();
        }

        private void RenderProtein()
        {
            if (_protein == null)
                return;

            var style = StyleCombo.SelectedIndex switch
            {
                0 => VisualizationStyle.BallAndStick,
                1 => VisualizationStyle.SpheresOnly,
                _ => VisualizationStyle.Wireframe
            };

            VisualizationResult result = _visualizer.BuildWithMapping(_protein, style);

            ProteinModelVisual.Content = result.Model; // scalony, zamrozony model
            _residueInfo = result.ResidueInfo;

            HighlightModelVisual.Content = null;
            _highlightedResidue = null;

            // jesli jakas reszta byla zaznaczona, po przebudowie geometrii
            // pokaz jej podswietlenie ponownie (ta sama instancja Residues przetrwala)
            if (_selectedResidue != null && _residueInfo.TryGetValue(_selectedResidue, out var info))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);

            Viewport.ZoomExtents();
        }

        private void BuildSequencePanel()
        {
            _sequenceItems.Clear();
            _highlightedResidue = null;

            if (_protein == null)
            {
                SequenceHeaderText.Text = "Sekwencja: -";
                return;
            }

            SequenceHeaderText.Text = $"Sekwencja ({_protein.Chains.Count} łańcuch/y):";

            foreach (var chain in _protein.Chains)
            {
                _sequenceItems.Add(new ResidueSequenceItem($" Łańcuch {chain.Id}: "));

                foreach (var residue in chain.Residues.OrderBy(r => r.Id))
                {
                    _sequenceItems.Add(new ResidueSequenceItem(chain, residue));
                }
            }
        }

        // Odczytujemy DataContext z kontenera przy kazdym wywolaniu, wiec dziala
        // poprawnie takze po recyklingu kontenera przez wirtualizacje.
        private void SequenceItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (TryGetResidueItem(sender, out var item))
                HighlightResidue(item);
        }

        private void SequenceItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (TryGetResidueItem(sender, out var item))
                ClearHoverHighlight(item);
        }

        private void SequenceItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryGetResidueItem(sender, out var item))
                SelectResidue(item);
        }

        private static bool TryGetResidueItem(object sender, out ResidueSequenceItem item)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ResidueSequenceItem seqItem
                && !seqItem.IsLabel && seqItem.Residue != null)
            {
                item = seqItem;
                return true;
            }

            item = null!;
            return false;
        }

        private void SelectResidue(ResidueSequenceItem item)
        {
            if (_selectedItem != null)
                _selectedItem.IsSelected = false;

            _selectedItem = item;
            _selectedResidue = item.Residue;
            item.IsSelected = true;

            SelectedResidueText.Text = $"{item.Residue!.Name} {item.Residue.Id}";
            MutateButton.IsEnabled = true;

            // od razu pokaz podswietlenie w 3D (niezaleznie od hover)
            if (_residueInfo.TryGetValue(item.Residue, out var info))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);
        }

        private void ClearSelection()
        {
            if (_selectedItem != null)
                _selectedItem.IsSelected = false;

            _selectedItem = null;
            _selectedResidue = null;
            SelectedResidueText.Text = "brak (kliknij literę w sekwencji)";
            MutateButton.IsEnabled = false;
        }

        private void HighlightResidue(ResidueSequenceItem item)
        {
            if (item.Residue == null)
                return;

            _highlightedResidue = item.Residue;
            item.IsHighlighted = true;

            HoverInfoText.Text = $"{item.Residue.Name} {item.Residue.Id}";

            if (_residueInfo.TryGetValue(item.Residue, out var info))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);
        }

        // Po zjechaniu mysza z litery: wracamy do podswietlenia ZAZNACZONEJ reszty
        // (jesli jakas jest zaznaczona), albo czyscimy overlay calkowicie.
        private void ClearHoverHighlight(ResidueSequenceItem item)
        {
            item.IsHighlighted = false;

            if (_highlightedResidue == item.Residue)
            {
                HoverInfoText.Text = " ";
                _highlightedResidue = null;
            }

            if (_selectedResidue != null && _residueInfo.TryGetValue(_selectedResidue, out var info))
            {
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);
            }
            else
            {
                HighlightModelVisual.Content = null;
            }
        }

        private void MutateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedResidue == null || _selectedItem == null)
                return;

            if (MutateCombo.SelectedItem is not ComboBoxItem selected || selected.Tag is not string newCode)
                return;

            string oldName = _selectedResidue.Name;

            ResidueMutator.Mutate(_selectedResidue, newCode);

            // odswiez litere/tooltip w panelu sekwencji bez przebudowy calej listy
            _selectedItem.RefreshFromResidue();

            // szkielet atomow sie zmienil (usuniete atomy lancucha bocznego) ->
            // trzeba przebudowac geometrie 3D (wiazania, grupowanie po pierwiastku itd.)
            RenderProtein();

            SelectedResidueText.Text = $"{_selectedResidue.Name} {_selectedResidue.Id}";

            MessageBox.Show(
                $"Zamieniono {oldName} → {_selectedResidue.Name} (reszta {_selectedResidue.Id}).\n\n" +
                "Uwaga: na tym etapie łańcuch boczny nowego aminokwasu nie jest " +
                "dobudowywany geometrycznie - zachowany został tylko szkielet " +
                "(i Cβ, jeśli występuje). Pełna rekonstrukcja łańcucha bocznego " +
                "to kolejny krok.",
                "Podmieniono aminokwas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
