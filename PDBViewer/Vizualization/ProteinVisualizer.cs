using HelixToolkit.Wpf;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PDBViewer.Vizualization
{
    /// <summary>
    /// Buduje geometrię 3D białka zoptymalizowaną pod kątem
    /// dużych struktur PDB.
    ///
    /// Najważniejsze optymalizacje:
    ///
    /// 1. Wszystkie atomy tego samego pierwiastka są umieszczane
    ///    w jednym MeshGeometry3D.
    ///
    /// 2. Wszystkie wiązania są umieszczane w jednym MeshGeometry3D.
    ///
    /// 3. Dla dużych struktur zmniejszana jest liczba segmentów
    ///    sfer i cylindrów.
    ///
    /// 4. Główny Model3DGroup jest zamrażany przez Freeze().
    ///
    /// 5. Podświetlenie reszty nie modyfikuje głównego modelu.
    ///    Zamiast tego tworzony jest mały model overlay.
    /// </summary>
    public partial class ProteinVisualizer
    {

        private static readonly Dictionary<string, Color> CpkColors = new()
        {
            { "H", Colors.White },
            { "C", Color.FromRgb(60, 60, 60) },
            { "N", Colors.Blue },
            { "O", Colors.Red },
            { "S", Colors.Yellow },
            { "P", Colors.Orange },
            { "FE", Color.FromRgb(224, 102, 51) },
            { "ZN", Color.FromRgb(124, 128, 176) },
            { "CA", Color.FromRgb(60, 200, 60) },
            { "MG", Color.FromRgb(34, 139, 34) }
        };

        private static readonly Dictionary<string, double> AtomRadii = new()
        {
            { "H", 0.25 },
            { "C", 0.40 },
            { "N", 0.40 },
            { "O", 0.40 },
            { "S", 0.45 },
            { "P", 0.45 }
        };

        private const double DefaultAtomRadius = 0.40;

        private const double BondRadius = 0.12;

        /// <summary>
        /// Buduje model 3D bez dodatkowego mapowania.
        ///
        /// Metoda pozostawiona dla kompatybilności z wcześniejszym kodem.
        /// </summary>
        public Model3DGroup Build(
            Protein protein,
            VisualizationStyle style = VisualizationStyle.BallAndStick)
        {
            return BuildWithMapping(protein, style).Model;
        }

        /// <summary>
        /// Buduje zoptymalizowany model 3D wraz z informacjami
        /// potrzebnymi do późniejszego podświetlania reszt.
        /// </summary>
        public VisualizationResult BuildWithMapping(
            Protein protein,
            VisualizationStyle style = VisualizationStyle.BallAndStick)
        {
            var result = new VisualizationResult();

            if (protein == null)
                return result;

            var allResidues = protein.Chains
                .SelectMany(chain => chain.Residues)
                .ToList();

            var allAtoms = allResidues
                .SelectMany(residue => residue.Atoms)
                .ToList();

            int atomCount = allAtoms.Count;

            var (sphereSegments, sphereRings) =
                GetSphereResolution(atomCount);

            int cylinderSides =
                GetCylinderSides(atomCount);


            var atomToResidue =
                new Dictionary<Atoms, Residues>();

            foreach (var residue in allResidues)
            {
                var info =
                    new ResidueGeometryInfo();

                result.ResidueInfo[residue] = info;

                foreach (var atom in residue.Atoms)
                {
                    atomToResidue[atom] = residue;

                    info.Atoms.Add(atom);
                }
            }
            // Cartoon jest budowany niezależnie od modeli atomowych.
            // Używamy przebiegu atomów CA kolejnych aminokwasów.
            if (style == VisualizationStyle.Cartoon)
            {
                BuildCartoon(protein, result);

                result.Model.Freeze();

                return result;
            }


            if (style != VisualizationStyle.Wireframe)
            {
                /*
                 * Zamiast:
                 *
                 * atom -> GeometryModel3D
                 *
                 * tworzymy:
                 *
                 * C -> jeden MeshGeometry3D
                 * N -> jeden MeshGeometry3D
                 * O -> jeden MeshGeometry3D
                 * itd.
                 */

                var meshesByElement = new Dictionary<string, MeshGeometry3D>();

                foreach (var atom in allAtoms)
                {
                    string element = NormalizeElement(atom.Element);

                    if (!meshesByElement.TryGetValue( element, out var mesh))
                    {
                        mesh = new MeshGeometry3D();
                        meshesByElement[element] = mesh;
                    }

                    AddAtomSphere(mesh,atom,sphereSegments,sphereRings);
                }

                foreach (var pair in meshesByElement)
                {
                    string element = pair.Key;

                    Color color =CpkColors.TryGetValue( element,out var foundColor)
                            ? foundColor
                            : Colors.Magenta;

                    var material = MaterialHelper.CreateMaterial(color);

                    material.Freeze();

                    var mesh = pair.Value;

                    mesh.Freeze();

                    var geometryModel = new GeometryModel3D( mesh, material)
                        {
                            BackMaterial = material
                        };

                    result.Model.Children.Add( geometryModel);
                }
            }

            if (style != VisualizationStyle.SpheresOnly)
            {
                var bonds =BondDetector.DetectBonds(protein);

                var bondMesh = new MeshGeometry3D();

                double radius = style == VisualizationStyle.Wireframe
                        ? 0.05
                        : BondRadius;

                foreach (var bond in bonds)
                {
                    var p1 = new Point3D( bond.Atom1.X, bond.Atom1.Y, bond.Atom1.Z);

                    var p2 = new Point3D( bond.Atom2.X, bond.Atom2.Y, bond.Atom2.Z);

                    AddCylinderMesh( bondMesh, p1, p2, radius, cylinderSides);

                    if (atomToResidue.TryGetValue( bond.Atom1, out var residue1))
                    {
                        result.ResidueInfo[residue1].Bonds.Add(bond);
                    }

                    if (atomToResidue.TryGetValue( bond.Atom2, out var residue2))
                    {
                        if (residue2 != residue1)
                        {
                            result.ResidueInfo[residue2].Bonds.Add(bond);
                        }
                    }
                }

                var bondMaterial =  MaterialHelper.CreateMaterial(Colors.LightGray);

                bondMaterial.Freeze();

                bondMesh.Freeze();

                var bondModel = new GeometryModel3D( bondMesh, bondMaterial)
                    {
                        BackMaterial = bondMaterial
                    };

                result.Model.Children.Add(bondModel);
            }
            // ZAMROŻENIE CAŁEGO MODELU

            /*
             * Model główny jest statyczny.
             *
             * Po Freeze() WPF nie musi śledzić zmian właściwości
             * geometrii i materiałów.
             *
             * Jest to szczególnie korzystne podczas obracania
             * i powiększania dużych struktur.
             */

            result.Model.Freeze();

            return result;
        }
        /// <summary>
        /// Normalizuje nazwę pierwiastka.
        ///
        /// Dzięki temu np.:
        ///
        /// " C " -> "C"
        /// "c"   -> "C"
        /// "CA"  -> "CA"
        ///
        /// Nieznany pierwiastek trafia do "?",
        /// dzięki czemu otrzyma kolor magenta.
        /// </summary>
        private static string NormalizeElement(
            string? element)
        {
            if (string.IsNullOrWhiteSpace(element))
                return "?";

            string upper = element.Trim().ToUpperInvariant();

            if (CpkColors.ContainsKey(upper) ||AtomRadii.ContainsKey(upper))
            {
                return upper;
            }

            return "?";
        }
        /// <summary>
        /// Dobiera liczbę segmentów sfer w zależności
        /// od wielkości struktury.
        ///
        /// Małe struktury:
        ///     12 x 10
        ///
        /// Średnie:
        ///     8 x 6
        ///
        /// Bardzo duże:
        ///     6 x 4
        /// </summary>
        private static (int segments,int rings) GetSphereResolution(int atomCount)
        {
            if (atomCount > 15000)
                return (6, 4);

            if (atomCount > 5000)
                return (8, 6);

            return (12, 10);
        }
        /// <summary>
        /// Dobiera liczbę segmentów cylindrów.
        /// </summary>
        private static int GetCylinderSides(
            int atomCount)
        {
            if (atomCount > 15000)
                return 5;

            if (atomCount > 5000)
                return 6;

            return 8;
        }
    }
}