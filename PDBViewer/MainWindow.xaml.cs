using HelixToolkit.Maths;
using Microsoft.Win32;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
using PDBViewer.Vizualization;
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

        public MainWindow()
        {
            InitializeComponent();
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd wczytywania pliku PDB:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StyleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

            Model3DGroup model = _visualizer.Build(_protein, style);

            ProteinModelVisual.Content = model;

            Viewport.ZoomExtents();
        }
    }
}