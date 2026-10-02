using HelixToolkit.Maths;
using Microsoft.Win32;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
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

        // dane dla wirtualizowanego ItemsControl (sekwencja aminokwasow)
        private readonly ObservableCollection<ResidueSequenceItem> _sequenceItems = new();

        public MainWindow()
        {
            InitializeComponent();
            SequenceItemsControl.ItemsSource = _sequenceItems;
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

                RenderProtein();
                BuildSequencePanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd wczytywania pliku PDB:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_protein != null)
            {
                RenderProtein();
            }
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

            ProteinModelVisual.Content = result.Model; // scalony, zamrozony model - budowany raz
            _residueInfo = result.ResidueInfo;

            HighlightModelVisual.Content = null;
            _highlightedResidue = null;

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
            if (sender is FrameworkElement fe && fe.DataContext is ResidueSequenceItem item
                && !item.IsLabel && item.Residue != null)
            {
                HighlightResidue(item);
            }
        }

        private void SequenceItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ResidueSequenceItem item
                && !item.IsLabel && item.Residue != null)
            {
                ClearHighlight(item);
            }
        }

        private void HighlightResidue(ResidueSequenceItem item)
        {
            if (item.Residue == null)
                return;

            _highlightedResidue = item.Residue;
            item.IsHighlighted = true;

            HoverInfoText.Text = $"{item.Residue.Name} {item.Residue.Id}";

            // Nie dotykamy glownego (zamrozonego) modelu. Budujemy osobny,
            // bardzo maly model tylko dla atomow/wiazan tej jednej reszty.
            if (_residueInfo.TryGetValue(item.Residue, out var info))
            {
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);
            }
        }

        private void ClearHighlight(ResidueSequenceItem item)
        {
            item.IsHighlighted = false;

            if (_highlightedResidue == item.Residue)
            {
                HoverInfoText.Text = " ";
                _highlightedResidue = null;
                HighlightModelVisual.Content = null;
            }
        }
    }
}
