using HelixToolkit.Maths;
using Microsoft.Win32;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
using PDBLibrary.Sevices;
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
        // Każdy łańcuch ma osobny wiersz w panelu sekwencji.
        private readonly ObservableCollection<ChainSequenceItem> _chainSequenceItems = new();

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
            SequenceItemsControl.ItemsSource = _chainSequenceItems;
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

                RenderProtein(resetCamera: true);
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

        private void RenderProtein(bool resetCamera = false)
        {
            Point3D? savedPosition = null;
            Vector3D? savedLookDirection = null;
            Vector3D? savedUpDirection = null;

            if (!resetCamera &&
                Viewport.Camera is ProjectionCamera camera)
            {
                savedPosition = camera.Position;
                savedLookDirection = camera.LookDirection;
                savedUpDirection = camera.UpDirection;
            }
            if (_protein == null)
                return;

            var style = StyleCombo.SelectedIndex switch
            {
                0 => VisualizationStyle.BallAndStick,
                1 => VisualizationStyle.SpheresOnly,
                2 => VisualizationStyle.Wireframe,
                3 => VisualizationStyle.Cartoon,
                _ => VisualizationStyle.BallAndStick
            };

            VisualizationResult result = _visualizer.BuildWithMapping(_protein, style);

            ProteinModelVisual.Content = result.Model; // scalony, zamrozony model
            _residueInfo = result.ResidueInfo;

            RebuildHitTestCaches();

            HighlightModelVisual.Content = null;
            _highlightedResidue = null;

            // Czerwony overlay kolizji zawsze czyścimy przy przebudowie geometrii -
            // dotyczył konkretnych atomów z POPRZEDNIEGO stanu struktury; jeśli
            // nadal jest aktualny, wywołujący (MutateButton_Click) ustawi go na
            // nowo po tym wywołaniu, korzystając z atomów z nowej geometrii.
            ClashHighlightModelVisual.Content = null;

            // jesli jakas reszta byla zaznaczona, po przebudowie geometrii
            // pokaz jej podswietlenie ponownie (ta sama instancja Residues przetrwala)
            if (_selectedResidue != null && _residueInfo.TryGetValue(_selectedResidue, out var info))
                HighlightModelVisual.Content = _visualizer.BuildHighlightModel(info);

            if (resetCamera)
            {
                Viewport.ZoomExtents();
            }
            else if (Viewport.Camera is ProjectionCamera currentCamera &&
                     savedPosition.HasValue &&
                     savedLookDirection.HasValue &&
                     savedUpDirection.HasValue)
            {
                currentCamera.Position = savedPosition.Value;
                currentCamera.LookDirection = savedLookDirection.Value;
                currentCamera.UpDirection = savedUpDirection.Value;
            }
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
            _chainSequenceItems.Clear();
            _sequenceItemByResidue.Clear();
            _highlightedResidue = null;

            if (_protein == null)
            {
                SequenceHeaderText.Text = "Sekwencja: -";
                return;
            }

            int proteinChainCount = 0;

            foreach (var chain in _protein.Chains)
            {
                var chainItem = new ChainSequenceItem(chain.Id);

                foreach (var residue in chain.Residues
                             .OrderBy(r => r.Id)
                             .ThenBy(r => r.InsertionCode))
                {
                    // Panel pokazuje tylko standardowe aminokwasy.
                    //
                    // DNA/RNA:
                    // DA, DC, DG, DT itd.
                    //
                    // oraz:
                    // HOH, FAD, GOL, NA, XE itd.
                    //
                    // pozostają w strukturze i wizualizacji 3D,
                    // ale nie są częścią sekwencji białkowej.
                    if (!AminoAcidCodes.IsStandardAminoAcid(residue.Name))
                        continue;

                    var item = new ResidueSequenceItem(chain, residue);

                    chainItem.Residues.Add(item);

                    _sequenceItemByResidue[residue] = item;
                }

                // Nie pokazujemy pustych łańcuchów w panelu sekwencji.
                if (chainItem.Residues.Count > 0)
                {
                    _chainSequenceItems.Add(chainItem);
                    proteinChainCount++;
                }
            }

            SequenceHeaderText.Text =
                proteinChainCount == 1
                    ? "Sekwencja (1 łańcuch białkowy):"
                    : $"Sekwencja ({proteinChainCount} łańcuchy białkowe):";
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
            // Do mutacji można wybrać wyłącznie jeden
            // z 20 standardowych aminokwasów.
            //
            // Woda, jony, ligandy i inne HETATM
            // (HOH, XE, NA, GOL, FAD, 4MV itd.)
            // mogą być widoczne w modelu 3D, ale nie można
            // ich zamienić na aminokwas.
            if (!AminoAcidCodes.IsStandardAminoAcid(residue.Name))
            {
                ClearSelection();

                SelectedResidueText.Text =
                    $"{residue.Name} {residue.Id} - nie jest aminokwasem";

                return;
            }

            ClashHighlightModelVisual.Content = null;

            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }

            _selectedItem =
                _sequenceItemByResidue.TryGetValue(residue, out var item)
                    ? item
                    : null;

            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = true;
            }

            _selectedResidue = residue;

            SelectedResidueText.Text =
                $"{residue.Name} {residue.Id}";

            MutateButton.IsEnabled = true;

            // Podświetlenie zaznaczonego aminokwasu w 3D.
            if (_residueInfo.TryGetValue(residue, out var info))
            {
                HighlightModelVisual.Content =
                    _visualizer.BuildHighlightModel(info);
            }
            FocusCameraOnResidue(residue);
        }

        private void ClearSelection()
        {
            if (_selectedItem != null)
                _selectedItem.IsSelected = false;

            _selectedItem = null;
            _selectedResidue = null;
            SelectedResidueText.Text = "brak (kliknij literę w sekwencji lub model 3D)";
            MutateButton.IsEnabled = false;
            ClashHighlightModelVisual.Content = null;
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
        private void FocusCameraOnResidue(Residues residue, double distance = 25.0)
        {
            if (residue.Atoms.Count == 0)
                return;

            if (Viewport.Camera is not ProjectionCamera camera)
                return;

            double centerX = residue.Atoms.Average(a => a.X);

            double centerY = residue.Atoms.Average(a => a.Y);

            double centerZ = residue.Atoms.Average(a => a.Z);

            var target = new Point3D(centerX, centerY, centerZ);

            Vector3D direction = camera.LookDirection;

            if (direction.Length < 0.0001)
                direction = new Vector3D(0, 0, -1);

            direction.Normalize();

            camera.Position = target - direction * distance;

            camera.LookDirection = target - camera.Position;
        }

        private void MutateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_protein == null || _selectedResidue == null || _selectedItem == null)
                return;

            if (!AminoAcidCodes.IsStandardAminoAcid(_selectedResidue.Name))
            {
                MessageBox.Show(
                    $"Reszta {_selectedResidue.Name} {_selectedResidue.Id} " +
                    $"nie jest standardowym aminokwasem i nie może zostać podmieniona.",
                    "Nie można wykonać mutacji",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
            if (MutateCombo.SelectedItem is not ComboBoxItem selected || selected.Tag is not string newCode)
                return;

            string oldName = _selectedResidue.Name;
            SideChainRotationResult rotationResult;

            try
            {
                // ta metoda sama usuwa stary lancuch boczny, zmienia nazwe reszty
                // i dobudowuje nowy lancuch boczny z szablonu geometrii (CCD)
                // nalozonego na szkielet. Na koniec probuje go obrocic wokol
                // wiazania CA-CB (SideChainOrientationResolver), jesli nowo
                // zbudowany lancuch boczny koliduje przestrzennie z otoczeniem -
                // dlatego potrzebuje teraz calego _protein, nie tylko reszty.
                // Zwraca pelny log prob (nie tylko finalny wynik).
                rotationResult = ResidueMutator.Mutate(_protein, _selectedResidue, newCode);
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

            SelectedResidueText.Text = rotationResult.FoundBetter
                ? $"{_selectedResidue.Name} {_selectedResidue.Id} " +
                  $"(wykryto kolizję przestrzenną - obrócono łańcuch boczny o {rotationResult.FinalAngleDegrees:F0}°)"
                : rotationResult.HadInitialClash
                    ? $"{_selectedResidue.Name} {_selectedResidue.Id} " +
                      $"(wykryto kolizję przestrzenną - żaden obrót jej nie usunął)"
                    : $"{_selectedResidue.Name} {_selectedResidue.Id}";

            RunMutationValidation(_selectedResidue);

            // Jeżeli konformacja z szablonu kolidowała, dopisujemy do LISTY
            // walidacji (ValidationIssuesList - ta sama, przewijana, co prawdziwe
            // błędy/ostrzeżenia) log KOLEJNYCH prób obrotu: najpierw błąd przy
            // oryginalnej konformacji, potem kolejne błędy przy kolejnych obrotach.
            // Celowo NIE dopisujemy tego do ValidationSummaryText (zwykły TextBlock
            // bez przewijania) - przy wielu próbach rozciągałby panel w nieskończoność.
            if (rotationResult.Attempted && rotationResult.HadInitialClash)
            {
                AddInfoLine("— Log prób obrotu łańcucha bocznego (CA-CB), by usunąć kolizję: —");

                foreach (var attempt in rotationResult.Attempts)
                    AddInfoLine("  • " + attempt);

                AddInfoLine(rotationResult.FoundBetter
                    ? $"→ wybrano obrót {rotationResult.FinalAngleDegrees:F0}°."
                    : "→ żaden obrót nie poprawił sytuacji - zostawiono konformację z szablonu.");

                // Jeśli przy WYBRANYM obrocie coś jednak zostało (np. mniej błędów,
                // ale nie zero), pokazujemy dokładną treść tego, co zostało - a nie
                // tylko samą liczbę w logu powyżej. To jest dokładnie to ostrzeżenie,
                // przy którym walidacja scoped (RunMutationValidation) się zatrzymała.
                var finalAttempt = rotationResult.Attempts
                    .FirstOrDefault(a => a.AngleDegrees == rotationResult.FinalAngleDegrees);

                if (finalAttempt.Issues != null && finalAttempt.Issues.Count > 0)
                {
                    AddInfoLine("Pozostałe problemy przy wybranym obrocie:");

                    foreach (var issue in finalAttempt.Issues)
                        _validationIssues.Add(new ValidationIssueViewModel(issue));
                    // Podświetlamy na czerwono DOKŁADNIE te atomy, które nadal
                    // kolidują przy ostatecznie wybranym obrocie (geometria w
                    // _residueInfo już odpowiada tej konformacji, bo RenderProtein()
                    // przebudowała ją po tym, jak ResidueMutator.Mutate zostawił
                    // atomy reszty w najlepszej znalezionej pozycji).
                    var clashingAtoms = finalAttempt.Issues
                        .SelectMany(i => new[] { i.Atom, i.AtomB })
                        .Where(a => a != null)
                        .Select(a => a!)
                        .Distinct();

                    ClashHighlightModelVisual.Content = _visualizer.BuildClashHighlightModel(clashingAtoms);
                }
            }
        }

        // Dopisuje jedną linię "informacyjną" (nie błąd/ostrzeżenie ze struktury)
        // na KONIEC listy walidacji - ValidationIssuesList jest już przewijana
        // (ScrollViewer wewnątrz), więc dowolnie długi log nie rozsadza panelu.
        private void AddInfoLine(string message)
        {
            _validationIssues.Add(new ValidationIssueViewModel(new ValidationIssue
            {
                Severity = ValidationSeverity.Info,
                Message = message,
                Residue = _selectedResidue
            }));
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
