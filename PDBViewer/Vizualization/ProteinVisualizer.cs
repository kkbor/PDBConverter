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
    public enum VisualizationStyle
    {
        BallAndStick,
        SpheresOnly,
        Wireframe
    }

    /// <summary>
    /// Surowe dane pojedynczej reszty potrzebne do zbudowania
    /// małego modelu podświetlenia.
    ///
    /// Nie przechowujemy tutaj GeometryModel3D.
    /// Dzięki temu główny model może być scalony i zoptymalizowany.
    /// </summary>
    public class ResidueGeometryInfo
    {
        public List<Atoms> Atoms { get; } = new();

        public List<Bonds> Bonds { get; } = new();
    }

    /// <summary>
    /// Wynik budowy sceny 3D.
    ///
    /// Model zawiera scaloną geometrię całego białka.
    /// ResidueInfo zawiera tylko dane potrzebne do późniejszego
    /// zbudowania overlayu podświetlenia konkretnej reszty.
    /// </summary>
    public class VisualizationResult
    {
        /// <summary>
        /// Główny, scalony model całego białka.
        /// </summary>
        public Model3DGroup Model { get; } = new();

        /// <summary>
        /// Informacje potrzebne do podświetlania pojedynczych reszt.
        /// </summary>
        public Dictionary<Residues, ResidueGeometryInfo> ResidueInfo { get; } = new();
    }

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
    public class ProteinVisualizer
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

        // PODŚWIETLENIE RESZTY

        /// <summary>
        /// Buduje mały model overlay dla jednej reszty.
        ///
        /// Ten model zawiera wyłącznie atomy i wiązania należące
        /// do aktualnie podświetlanej reszty.
        ///
        /// Nie modyfikuje głównego, zamrożonego modelu.
        /// </summary>
        public Model3DGroup BuildHighlightModel(
            ResidueGeometryInfo info)
        {
            var group = new Model3DGroup();

            var mesh = new MeshGeometry3D();

            foreach (var atom in info.Atoms)
            {
                AddAtomSphere( mesh, atom, 8, 6);
            }

            foreach (var bond in info.Bonds)
            {
                var p1 = new Point3D( bond.Atom1.X, bond.Atom1.Y, bond.Atom1.Z);

                var p2 = new Point3D( bond.Atom2.X, bond.Atom2.Y, bond.Atom2.Z);

                AddCylinderMesh( mesh,  p1, p2, BondRadius * 1.4, 8);
            }

            var material = CreateHighlightMaterial();

            mesh.Freeze();

            var model = new GeometryModel3D( mesh, material)
                {
                    BackMaterial = material
                };

            group.Children.Add(model);

            return group;
        }

        /// <summary>
        /// Materiał używany do podświetlania reszty.
        /// </summary>
        private static Material CreateHighlightMaterial()
        {
            var group = new MaterialGroup();

            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Orange)));

            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(180,90,0))));

            group.Freeze();

            return group;
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

        /// <summary>
        /// Dodaje sferę pojedynczego atomu do istniejącego
        /// MeshGeometry3D.
        ///
        /// Metoda NIE tworzy GeometryModel3D.
        ///
        /// Dzięki temu wiele atomów może zostać połączonych
        /// w jeden wspólny mesh.
        /// </summary>
        private void AddAtomSphere(MeshGeometry3D mesh,Atoms atom,int segments,int rings)
        {
            string element = NormalizeElement(atom.Element);

            double radius = AtomRadii.TryGetValue(element,out double r)
                    ? r
                    : DefaultAtomRadius;

            int startIndex =mesh.Positions.Count;

          
            for (int ring = 0; ring <= rings; ring++)
            {
                double phi = Math.PI * ring / rings;

                for (int segment = 0; segment <= segments; segment++)
                {
                    double theta = 2.0 * Math.PI * segment / segments;

                    double x = Math.Sin(phi) * Math.Cos(theta);

                    double y = Math.Cos(phi);

                    double z = Math.Sin(phi) * Math.Sin(theta);

                    mesh.Positions.Add( new Point3D( atom.X + radius * x, atom.Y + radius * y, atom.Z + radius * z));

                    mesh.Normals.Add(new Vector3D(x, y, z));
                }
            }


            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int current = startIndex + ring * (segments + 1) + segment;

                    int next = current + segments + 1;

                    // Pierwszy trójkąt
                    mesh.TriangleIndices.Add(current);

                    mesh.TriangleIndices.Add(next);

                    mesh.TriangleIndices.Add(current + 1);

                    // Drugi trójkąt
                    mesh.TriangleIndices.Add(current + 1);

                    mesh.TriangleIndices.Add(next);

                    mesh.TriangleIndices.Add(next + 1);
                }
            }
        }

        /// <summary>
        /// Dodaje cylinder pomiędzy dwoma punktami
        /// do istniejącego MeshGeometry3D.
        ///
        /// Metoda nie tworzy osobnego GeometryModel3D.
        /// </summary>
        private void AddCylinderMesh(MeshGeometry3D mesh,Point3D p1,Point3D p2,double radius,int segments)
        {
            Vector3D direction = p2 - p1;

            double length = direction.Length;

            if (length < 0.0001)
                return;

            direction.Normalize();

            Vector3D reference;

            if (Math.Abs(direction.Y) < 0.9)
            {
                reference = new Vector3D(0,1,0);
            }
            else
            {
                reference = new Vector3D(1,0,0);
            }


            Vector3D u = Vector3D.CrossProduct(direction,reference);

            u.Normalize();

            Vector3D v = Vector3D.CrossProduct(direction,u);

            v.Normalize();

            int startIndex = mesh.Positions.Count;

            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;

                double cos = Math.Cos(angle);

                double sin = Math.Sin(angle);

                Vector3D offset = u * (radius * cos) + v * (radius * sin);

                Point3D point1 = p1 + offset;

                Point3D point2 = p2 + offset;

                mesh.Positions.Add(point1);
                mesh.Positions.Add(point2);

                mesh.Normals.Add(offset);
                mesh.Normals.Add(offset);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int a = startIndex + i * 2;

                int b = startIndex + next * 2;

                int c = startIndex + i * 2 + 1;

                int d = startIndex + next * 2 + 1;

                // Pierwszy trójkąt
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(c);

                // Drugi trójkąt
                mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(d);
            }

            int center1 = mesh.Positions.Count;

            mesh.Positions.Add(p1);
            mesh.Normals.Add(-direction);

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int current = startIndex + i * 2;

                int nextIndex = startIndex + next * 2;

                mesh.TriangleIndices.Add(center1);

                mesh.TriangleIndices.Add(nextIndex);

                mesh.TriangleIndices.Add(current);
            }


            int center2 = mesh.Positions.Count;

            mesh.Positions.Add(p2);
            mesh.Normals.Add(direction);

            for (int i = 0; i < segments;  i++)
            {
                int next = (i + 1) % segments;

                int current = startIndex + i * 2 + 1;

                int nextIndex = startIndex + next * 2 + 1;

                mesh.TriangleIndices.Add(center2);

                mesh.TriangleIndices.Add(current);

                mesh.TriangleIndices.Add(nextIndex);
            }
        }
    }
}