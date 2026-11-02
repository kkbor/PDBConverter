using PDBLibrary.Srevices;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PDBViewer
{
    /// <summary>
    /// Osobne okno informujące o próbach obrotu łańcucha bocznego (CA-CB)
    /// wykonanych po podmianie aminokwasu, żeby ta informacja NIE trafiała
    /// do listy błędów/ostrzeżeń walidacji.
    ///
    /// Jeżeli znaleziono więcej niż jeden wariant bez żadnych błędów (różniący
    /// się np. liczbą ostrzeżeń), okno pozwala użytkownikowi wybrać, który
    /// zastosować - w przeciwnym razie pełni wyłącznie funkcję informacyjną,
    /// a zastosowany zostaje wariant dobrany automatycznie.
    /// </summary>
    public partial class RotationChoiceWindow : Window
    {
        private sealed class CandidateItem
        {
            public double AngleDegrees { get; init; }
            public string Label { get; init; } = "";
            public string Detail { get; init; } = "";
        }

        /// <summary>
        /// Kąt obrotu, który należy ostatecznie zastosować - domyślnie wariant
        /// zalecany (automatyczny), a po zamknięciu okna przyciskiem OK -
        /// to, co użytkownik wybrał na liście (jeśli lista wyboru była pokazana).
        /// </summary>
        public double ChosenAngleDegrees { get; private set; }

        /// <param name="attempts">Pełny log wszystkich sprawdzonych kątów (do wglądu).</param>
        /// <param name="zeroErrorCandidates">Podzbiór attempts bez żadnych błędów (może być pusty, jeden, albo kilka).</param>
        /// <param name="recommendedAngle">
        /// Kąt proponowany jako domyślny wybór: najlepszy spośród
        /// zeroErrorCandidates, jeśli taki istnieje, inaczej globalnie
        /// najmniej "dotkliwy" z całej puli.
        /// </param>
        /// <param name="foundBetter">Czy jakikolwiek obrót poprawił sytuację względem konformacji z szablonu.</param>
        public RotationChoiceWindow(
            IReadOnlyList<RotationAttempt> attempts,
            IReadOnlyList<RotationAttempt> zeroErrorCandidates,
            double recommendedAngle,
            bool foundBetter)
        {
            InitializeComponent();

            ChosenAngleDegrees = recommendedAngle;

            AttemptsLogList.ItemsSource = attempts.Select(a => a.ToString()).ToList();

            if (zeroErrorCandidates.Count > 1)
            {
                HeaderText.Text =
                    "Nowy łańcuch boczny kolidował przestrzennie z otoczeniem. " +
                    "Znaleziono kilka obrotów bez żadnych błędów (różniących się " +
                    "np. liczbą ostrzeżeń) - wybierz, który zastosować.";

                ChoiceHeaderText.Visibility = Visibility.Visible;
                CandidatesBorder.Visibility = Visibility.Visible;

                var items = zeroErrorCandidates
                    .Select(a =>
                    {
                        string baseLabel = a.AngleDegrees == 0
                            ? "Konformacja z szablonu (bez obrotu)"
                            : $"Obrót o {a.AngleDegrees:F0}°";

                        string label = a.AngleDegrees == recommendedAngle
                            ? $"{baseLabel}  (zalecany)"
                            : baseLabel;

                        string detail = a.WarningCount == 0
                            ? "Brak ostrzeżeń."
                            : $"{a.WarningCount} ostrzeżeń.";

                        return new CandidateItem
                        {
                            AngleDegrees = a.AngleDegrees,
                            Label = label,
                            Detail = detail
                        };
                    })
                    .ToList();

                CandidatesList.ItemsSource = items;
                CandidatesList.SelectedItem =
                    items.FirstOrDefault(i => i.AngleDegrees == recommendedAngle)
                    ?? items.FirstOrDefault();
            }
            else if (zeroErrorCandidates.Count == 1)
            {
                HeaderText.Text =
                    $"Nowy łańcuch boczny kolidował przestrzennie z otoczeniem - " +
                    $"zastosowano obrót o {recommendedAngle:F0}°, jedyny znaleziony " +
                    $"wariant bez żadnych błędów.";
            }
            else
            {
                HeaderText.Text = foundBetter
                    ? $"Nowy łańcuch boczny kolidował przestrzennie z otoczeniem. " +
                      $"Żaden sprawdzony obrót nie wyeliminował błędów całkowicie - " +
                      $"zastosowano wariant o najmniejszej liczbie problemów " +
                      $"(obrót {recommendedAngle:F0}°)."
                    : "Nowy łańcuch boczny kolidował przestrzennie z otoczeniem. " +
                      "Żaden sprawdzony obrót nie poprawił sytuacji - zostawiono " +
                      "konformację z szablonu.";
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (CandidatesBorder.Visibility == Visibility.Visible &&
                CandidatesList.SelectedItem is CandidateItem item)
            {
                ChosenAngleDegrees = item.AngleDegrees;
            }

            DialogResult = true;
            Close();
        }
    }
}
