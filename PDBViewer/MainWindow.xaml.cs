using HelixToolkit.Maths;
using Microsoft.Win32;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
using PDBLibrary.Srevices;
using PDBLibrary.Validation;
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

        // szybki dostep reszta -> jej "literka" w panelu sekwencji (do synchronizacji 3D <-> sekwencja)
        private readonly Dictionary<Residues, ResidueSequenceItem> _sequenceItemByResidue = new();

        // cache do hit-testingu w 3D: plaska lista atomow + atom -> jego reszta.
        // Odbudowywane przy kazdym RenderProtein() (geometria/atomy moga sie zmienic po mutacji).
        private List<Atoms> _allAtomsCache = new();
        private Dictionary<Atoms, Residues> _atomToResidue = new();

        // wyniki ostatniej walidacji struktury (do wyswietlenia w panelu po prawej)
        private readonly ObservableCollection<ValidationIssueViewModel> _validationIssues = new();

        public MainWindow()
        {
            InitializeComponent();
            SequenceItemsControl.ItemsSource = _sequenceItems;
            ValidationIssuesList.ItemsSource = _validationIssues;
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
                Filter = "Pliki struktur (*.pdb;*.ent;*.cif;*.mmcif)|*.pdb;*.ent;*.cif;*.mmcif|" +
                         "Pliki PDB (*.pdb;*.ent)|*.pdb;*.ent|" +
                         "Pliki mmCIF (*.cif;*.mmcif)|*.cif;*.mmcif|" +
                         "Wszystkie pliki (*.*)|*.*",
                Title = "Wybierz plik struktury"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                // wybor parsera (PDB vs mmCIF) nastepuje automatycznie wg rozszerzenia/tresci
                _protein = StructureFileLoader.Load(dialog.FileName);

                int atomCount = _protein.Chains.Sum(c => c.Residues.Sum(r => r.Atoms.Count));
                int residueCount = _protein.Chains.Sum(c => c.Residues.Count);

                InfoText.Text = $"Łańcuchów: {_protein.Chains.Count}, " +
                                 $"reszt: {residueCount}, atomów: {atomCount}";

                SaveButton.IsEnabled = true;
                ValidateButton.IsEnabled = true;
                ClearSelection();
                _validationIssues.Clear();
                ValidationSummaryText.Text = "Brak uruchomionej walidacji.";

                // Resetuj liczniki do zera po wczytaniu nowego pliku
                ErrorCountText.Text = "0";
                WarningCountText.Text = "0";

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
                Filter = "Plik PDB (*.pdb)|*.pdb|Plik mmCIF (*.cif)|*.cif",
                Title = "Zapisz plik struktury",
                FileName = "structure.pdb"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();

                if (ext == ".cif" || ext == ".mmcif")
                    MmcifWriter.Save(_protein, dialog.FileName);
                else
                    PdbWriter.Save(_protein, dialog.FileName);

                MessageBox.Show("Plik zapisany pomyślnie.", "Zapisano",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd zapisu pliku PDB:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e) => RunValidation();

        // Uruchamia walidacje (spojnosc lancucha + konflikty przestrzenne) i
        // wypelnia panel po prawej. Wywolywane recznie (przycisk) oraz
        // automatycznie po kazdej udanej podmianie aminokwasu.
        private void RunValidation()
        {
            if (_protein == null)
                return;

            var report = StructureValidator.Validate(_protein);

            _validationIssues.Clear();
            foreach (var issue in report.Issues.OrderByDescending(i => i.Severity))
                _validationIssues.Add(new ValidationIssueViewModel(issue));

            ErrorCountText.Text = report.ErrorCount.ToString();
            WarningCountText.Text = report.WarningCount.ToString();

            ValidationSummaryText.Text = report.Issues.Count == 0
                ? "Brak wykrytych problemów."
                : $"Znaleziono {report.Issues.Count} problem(ów): " +
                  $"{report.ErrorCount} błędów, {report.WarningCount} ostrzeżeń.";
        }

        // Klikniecie w problem na liscie -> zaznacz (i podswietl w 3D) powiazana reszte
        private void ValidationIssuesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ValidationIssuesList.SelectedItem is ValidationIssueViewModel vm && vm.Issue.Residue != null)
                SelectResidue(vm.Issue.Residue);
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

            RebuildHitTestCaches();

            HighlightModelVisual.Content = null;
            _highlightedResidue = null;

            // jesli jakas reszta byla zaznaczona, po przebudowie geometrii
            // pokaz jej podswietlenie ponownie (ta sama instancja Residues przetrwala)
            if (_selectedResidue != null && _residueInfo.TryGetValue(_selectedResidue, out var info))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);

            Viewport.ZoomExtents();
        }

        // Plaska lista atomow + atom->reszta, uzywane przy klikaniu/najezdzaniu na model 3D
        // (znajdujemy najblizszy atom do punktu trafienia promienia, a potem jego reszte).
        private void RebuildHitTestCaches()
        {
            _allAtomsCache = new List<Atoms>();
            _atomToResidue = new Dictionary<Atoms, Residues>();

            foreach (var pair in _residueInfo)
            {
                foreach (var atom in pair.Value.Atoms)
                {
                    _allAtomsCache.Add(atom);
                    _atomToResidue[atom] = pair.Key;
                }
            }
        }

        private void BuildSequencePanel()
        {
            _sequenceItems.Clear();
            _sequenceItemByResidue.Clear();
            _highlightedResidue = null;

            if (_protein == null)
            {
                SequenceHeaderText.Text = "Sekwencja: -";
                return;
            }

            SequenceHeaderText.Text = $"Sekwencja ({_protein.Chains.Count} łańcuch/y):";

            foreach (var chain in _protein.Chains)
            {
                _sequenceItems.Add(
                    new ResidueSequenceItem($" Łańcuch {chain.Id}: ")
                );

                foreach (var residue in chain.Residues.OrderBy(r => r.Id))
                {
                    // Do panelu sekwencji dodajemy tylko 20 standardowych
                    // aminokwasów. Woda, jony, ligandy i kofaktory
                    // (np. HOH, ZN, NA, PLP, FAD, GOL, XE) pozostają
                    // w modelu białka, ale nie są częścią sekwencji.
                    if (!AminoAcidCodes.IsStandardAminoAcid(residue.Name))
                        continue;

                    var item = new ResidueSequenceItem(chain, residue);

                    _sequenceItems.Add(item);
                    _sequenceItemByResidue[residue] = item;
                }
            }
        }

        // Odczytujemy DataContext z kontenera przy kazdym wywolaniu, wiec dziala
        // poprawnie takze po recyklingu kontenera przez wirtualizacje.
        private void SequenceItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (TryGetResidueItem(sender, out var item))
                SetHoveredResidue(item.Residue);
        }

        private void SequenceItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (TryGetResidueItem(sender, out var item) && _highlightedResidue == item.Residue)
                SetHoveredResidue(null);
        }

        private void SequenceItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryGetResidueItem(sender, out var item) && item.Residue != null)
                SelectResidue(item.Residue);
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

        // --- Najechanie/klikniecie bezposrednio na model 3D ---

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_protein == null)
                return;

            var residue = HitTestResidue(e.GetPosition(Viewport));
            Viewport.Cursor = residue != null ? Cursors.Hand : Cursors.Arrow;
            SetHoveredResidue(residue);
        }

        private void Viewport_MouseLeave(object sender, MouseEventArgs e)
        {
            Viewport.Cursor = Cursors.Arrow;
            SetHoveredResidue(null);
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_protein == null)
                return;

            var residue = HitTestResidue(e.GetPosition(Viewport));
            if (residue != null)
                SelectResidue(residue);
        }

        // Standardowy WPF 3D hit-test: VisualTreeHelper.HitTest wykonuje raycasting
        // przez zawartosc Viewport3D i zwraca RayMeshGeometry3DHitTestResult z
        // dokladnym punktem trafienia (PointHit) na powierzchni mesha. Poniewaz
        // nie stosujemy zadnego Transform3D przy budowie geometrii (wspolrzedne
        // kul/walcow sa budowane bezposrednio w ukladzie bialka), PointHit od razu
        // odpowiada rzeczywistym wspolrzednym w strukturze - wystarczy znalezc
        // najblizszy atom.
        private Residues? HitTestResidue(Point mousePosition)
        {
            var hitResult = VisualTreeHelper.HitTest(Viewport, mousePosition);

            if (hitResult is not RayMeshGeometry3DHitTestResult meshHit)
                return null;

            return FindNearestResidue(meshHit.PointHit);
        }

        // Poniewaz geometria jest scalona (jeden mesh na pierwiastek/wiazania dla
        // wydajnosci), nie da sie bezposrednio odczytac "ktory to byl atom" z
        // wyniku hit-testu - zamiast tego szukamy atomu najblizszego punktowi
        // trafienia. Prosty skan liniowy - dla pojedynczego klikniecia/ruchu myszy
        // to tanie, nawet przy duzych strukturach (porownanie odleglosci, bez
        // pierwiastkowania - uzywamy kwadratu odleglosci).
        private Residues? FindNearestResidue(Point3D hitPoint)
        {
            Atoms? nearest = null;
            double bestDistSq = double.MaxValue;

            foreach (var atom in _allAtomsCache)
            {
                double dx = atom.X - hitPoint.X;
                double dy = atom.Y - hitPoint.Y;
                double dz = atom.Z - hitPoint.Z;
                double distSq = dx * dx + dy * dy + dz * dz;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = atom;
                }
            }

            return nearest != null && _atomToResidue.TryGetValue(nearest, out var residue) ? residue : null;
        }

        // --- Wspolna logika zaznaczania/podswietlania, uzywana zarowno przez
        //     panel sekwencji, jak i przez model 3D ---

        private void SelectResidue(Residues residue)
        {
            if (_selectedItem != null)
                _selectedItem.IsSelected = false;

            _selectedItem = _sequenceItemByResidue.TryGetValue(residue, out var item) ? item : null;
            if (_selectedItem != null)
                _selectedItem.IsSelected = true;

            _selectedResidue = residue;
            SelectedResidueText.Text = $"{residue.Name} {residue.Id}";
            MutateButton.IsEnabled = true;

            // od razu pokaz podswietlenie w 3D (niezaleznie od hover)
            if (_residueInfo.TryGetValue(residue, out var info))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);
        }

        private void ClearSelection()
        {
            if (_selectedItem != null)
                _selectedItem.IsSelected = false;

            _selectedItem = null;
            _selectedResidue = null;
            SelectedResidueText.Text = "brak (kliknij literę w sekwencji lub model 3D)";
            MutateButton.IsEnabled = false;
        }

        // Centralny punkt stanu "najechania" - dziala identycznie niezaleznie od
        // tego, czy zrodlem jest hover nad litera w sekwencji, czy nad atomem w 3D.
        // Synchronizuje oba widoki: podswietla literke ORAZ model 3D jednoczesnie.
        private void SetHoveredResidue(Residues? residue)
        {
            if (_highlightedResidue == residue)
                return;

            if (_highlightedResidue != null && _sequenceItemByResidue.TryGetValue(_highlightedResidue, out var prevItem))
                prevItem.IsHighlighted = false;

            _highlightedResidue = residue;

            if (residue != null)
            {
                if (_sequenceItemByResidue.TryGetValue(residue, out var item))
                    item.IsHighlighted = true;

                HoverInfoText.Text = $"{residue.Name} {residue.Id}";

                if (_residueInfo.TryGetValue(residue, out var info))
                    HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);

                return;
            }

            HoverInfoText.Text = " ";

            // po zjechaniu wracamy do podswietlenia ZAZNACZONEJ reszty, jesli jakas jest
            if (_selectedResidue != null && _residueInfo.TryGetValue(_selectedResidue, out var selInfo))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(selInfo);
            else
                HighlightModelVisual.Content = null;
        }

        private void MutateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedResidue == null || _selectedItem == null)
                return;

            if (MutateCombo.SelectedItem is not ComboBoxItem selected || selected.Tag is not string newCode)
                return;

            string oldName = _selectedResidue.Name;

            try
            {
                // ta metoda (Twoja wersja) sama usuwa stary lancuch boczny,
                // zmienia nazwe reszty i dobudowuje nowy lancuch boczny
                // z szablonu geometrii (CCD) nalozonego na szkielet
                ResidueMutator.Mutate(_selectedResidue, newCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się podmienić aminokwasu:\n{ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // odswiez litere/tooltip w panelu sekwencji bez przebudowy calej listy
            _selectedItem.RefreshFromResidue();

            // atomy sie zmienily -> trzeba przebudowac geometrie 3D
            RenderProtein();

            SelectedResidueText.Text = $"{_selectedResidue.Name} {_selectedResidue.Id}";

            RunMutationValidation(_selectedResidue);
        }
        private void RunMutationValidation(Residues changedResidue)
        {
            if (_protein == null)
                return;

            var report = StructureValidator.ValidateAfterMutation(
                _protein,
                changedResidue);

            _validationIssues.Clear();

            foreach (var issue in report.Issues.OrderByDescending(i => i.Severity))
                _validationIssues.Add(new ValidationIssueViewModel(issue));

            ErrorCountText.Text = report.ErrorCount.ToString();
            WarningCountText.Text = report.WarningCount.ToString();

            ValidationSummaryText.Text =
                report.Issues.Count == 0
                    ? $"Podmiana {changedResidue.Name} {changedResidue.Id}: brak problemów."
                    : $"Podmiana {changedResidue.Name} {changedResidue.Id}: " +
                      $"znaleziono {report.ErrorCount} błędów i " +
                      $"{report.WarningCount} ostrzeżeń.";

            // Jeżeli są problemy, można automatycznie otworzyć panel
            ValidationExpander.IsExpanded = report.Issues.Count > 0;
        }
    }
}
