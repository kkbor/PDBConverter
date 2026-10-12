using HelixToolkit.Wpf;
using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PDBViewer.Vizualization
{
    public partial class ProteinVisualizer
    {
        private void BuildCartoon(Protein protein, VisualizationResult result)
        {
            foreach (var chain in protein.Chains)
            {
                var residues = chain.Residues
                    .Where(r =>
                        AminoAcidCodes.IsStandardAminoAcid(r.Name))
                    .OrderBy(r => r.Id)
                    .ThenBy(r => r.InsertionCode)
                    .ToList();

                if (residues.Count < 2)
                    continue;

                // Każdej reszcie przypisujemy:
                // Coil / Helix / Sheet.
                var classified = new List<(Residues Residue, Atoms Ca, SecondaryStructureType Type)>();

                foreach (var residue in residues)
                {
                    var ca = residue.Atoms.FirstOrDefault(
                            a => a.Name.Equals(
                                "CA",
                                StringComparison.OrdinalIgnoreCase));

                    if (ca == null)
                        continue;

                    var type = GetSecondaryStructureType(protein, chain.Id, residue);

                    classified.Add((residue, ca, type));
                }

                if (classified.Count < 2)
                    continue;

                // Dzielimy łańcuch na ciągłe fragmenty
                // tego samego typu.
                int start = 0;

                while (start < classified.Count)
                {
                    SecondaryStructureType type = classified[start].Type;

                    int end = start;

                    while (end + 1 < classified.Count && classified[end + 1].Type == type)
                    {
                        end++;
                    }

                    BuildSecondaryStructurePart(classified, start, end, type, result);
                    // Jeżeli po tym fragmencie istnieje kolejny fragment
                    // innego typu, łączymy oba atomy CA krótką rurką.
                    if (end + 1 < classified.Count)
                    {
                        BuildCartoonConnector(classified[end].Ca, classified[end + 1].Ca, type, classified[end + 1].Type, result);
                    }
                    start = end + 1;
                }
            }
        }
        private void BuildCartoonConnector(Atoms firstCa, Atoms secondCa, SecondaryStructureType firstType, SecondaryStructureType secondType, VisualizationResult result)
        {
            var p1 = new Point3D(firstCa.X, firstCa.Y, firstCa.Z);

            var p2 = new Point3D(secondCa.X, secondCa.Y, secondCa.Z);

            // Jeżeli punkty są praktycznie identyczne,
            // nie ma czego łączyć.
            if ((p2 - p1).Length < 0.0001)
                return;

            var middle = new Point3D((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);

            AddCartoonConnectorPart(p1, middle, GetSecondaryStructureColor(firstType), result);

            AddCartoonConnectorPart(middle, p2, GetSecondaryStructureColor(secondType), result);
        }
        private void AddCartoonConnectorPart(Point3D p1, Point3D p2, Color color, VisualizationResult result)
        {
            var mesh = new MeshGeometry3D();

            AddCylinderMesh(mesh, p1, p2, radius: 0.18, segments: 8);

            var material = MaterialHelper.CreateMaterial(color);

            material.Freeze();
            mesh.Freeze();

            var model = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };

            result.Model.Children.Add(model);
        }
        private static Color GetSecondaryStructureColor(SecondaryStructureType type)
        {
            return type switch
            {
                SecondaryStructureType.Helix => Colors.Crimson,

                SecondaryStructureType.Sheet => Colors.Gold,

                _ => Colors.CornflowerBlue
            };
        }

        private static SecondaryStructureType GetSecondaryStructureType(Protein protein, char chainId, Residues residue)
        {
            foreach (var segment in protein.SecondaryStructures)
            {
                if (segment.ChainId != chainId)
                    continue;

                if (IsResidueInsideSegment(residue, segment))
                {
                    return segment.Type;
                }
            }

            return SecondaryStructureType.Coil;
        }
        private static bool IsResidueInsideSegment(Residues residue, SecondaryStructureSegment segment)
        {
            int startComparison = CompareResiduePosition(
                    residue.Id,
                    residue.InsertionCode,
                    segment.StartResidueId,
                    segment.StartInsertionCode);

            int endComparison = CompareResiduePosition(
                    residue.Id,
                    residue.InsertionCode,
                    segment.EndResidueId,
                    segment.EndInsertionCode);

            return startComparison >= 0 &&
                   endComparison <= 0;
        }
        private static int CompareResiduePosition(int idA, char insertionA, int idB, char insertionB)
        {
            int idComparison = idA.CompareTo(idB);

            if (idComparison != 0)
                return idComparison;

            char normalizedA = insertionA == '\0'
                    ? ' '
                    : insertionA;

            char normalizedB = insertionB == '\0'
                    ? ' '
                    : insertionB;

            return normalizedA.CompareTo(normalizedB);
        }
        private void BuildSecondaryStructurePart(
        List<(Residues Residue, Atoms Ca, SecondaryStructureType Type)> residues, int start, int end, SecondaryStructureType type, VisualizationResult result)
        {
            var points = new List<Point3D>();

            for (int i = start; i <= end; i++)
            {
                var ca = residues[i].Ca;
                points.Add(new Point3D(ca.X, ca.Y, ca.Z));
            }

            // Fragment z jedną resztą sam nie utworzy powierzchni.
            // Dodajemy sąsiedni punkt, jeśli istnieje.
            if (points.Count == 1)
            {
                if (end + 1 < residues.Count)
                {
                    var ca = residues[end + 1].Ca;

                    points.Add(new Point3D(ca.X, ca.Y, ca.Z));
                }
                else if (start > 0)
                {
                    var ca = residues[start - 1].Ca;
                    points.Insert(0, new Point3D(ca.X, ca.Y, ca.Z));
                }
            }

            if (points.Count < 2)
                return;

            var smoothPoints = CreateCatmullRomSpline(points, 6);

            if (smoothPoints.Count < 2)
                return;

            var mesh = new MeshGeometry3D();

            Color color;

            switch (type)
            {
                case SecondaryStructureType.Helix:

                    AddRibbonAlongPath(mesh, smoothPoints, width: 0.9, thickness: 0.16, arrowEnd: false);

                    color = Colors.Crimson;

                    break;

                case SecondaryStructureType.Sheet:

                    AddRibbonAlongPath(mesh, smoothPoints, width: 0.9, thickness: 0.12, arrowEnd: true);

                    color = Colors.Gold;

                    break;

                default:

                    AddTubeAlongPath(mesh, smoothPoints, radius: 0.18, sides: 8);

                    color = Colors.CornflowerBlue;

                    break;
            }

            if (mesh.Positions.Count == 0)
                return;

            var material = MaterialHelper.CreateMaterial(color);

            material.Freeze();
            mesh.Freeze();

            var model = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };

            result.Model.Children.Add(model);
        }
        private static void AddRibbonAlongPath(MeshGeometry3D mesh, List<Point3D> points, double width, double thickness, bool arrowEnd)
        {
            if (points.Count < 2)
                return;

            Vector3D previousSide = new Vector3D();

            for (int i = 0; i < points.Count; i++)
            {
                Vector3D tangent;

                if (i == 0)
                {
                    tangent = points[1] - points[0];
                }
                else if (i == points.Count - 1)
                {
                    tangent = points[i] - points[i - 1];
                }
                else
                {
                    tangent = points[i + 1] - points[i - 1];
                }

                if (tangent.Length < 0.0001)
                    continue;

                tangent.Normalize();

                Vector3D side;

                if (i == 0 || previousSide.Length < 0.0001)
                {
                    Vector3D reference = Math.Abs(tangent.Y) < 0.9
                            ? new Vector3D(0, 1, 0)
                            : new Vector3D(1, 0, 0);

                    side = Vector3D.CrossProduct(tangent, reference);

                    if (side.Length < 0.0001)
                        side = new Vector3D(1, 0, 0);

                    side.Normalize();
                }
                else
                {
                    // Przenosimy poprzedni kierunek wstęgi
                    // na nową płaszczyznę prostopadłą do tangensa.
                    side = previousSide - tangent * Vector3D.DotProduct(previousSide, tangent);

                    if (side.Length < 0.0001)
                    {
                        Vector3D reference = Math.Abs(tangent.Y) < 0.9
                                ? new Vector3D(0, 1, 0)
                                : new Vector3D(1, 0, 0);

                        side = Vector3D.CrossProduct(tangent, reference);
                    }

                    side.Normalize();
                }

                previousSide = side;

                Vector3D normal = Vector3D.CrossProduct(tangent, side);

                normal.Normalize();

                double currentWidth = width;

                if (arrowEnd)
                {
                    double progress = (double)i / (points.Count - 1);

                    // Przed końcem beta-strand robimy
                    // szerszą część strzałki.
                    if (progress >= 0.72 && progress < 0.95)
                    {
                        currentWidth = width * 1.7;
                    }

                    // Sam koniec zwęża się do grotu.
                    if (progress >= 0.95)
                    {
                        currentWidth = width * 0.08;
                    }
                }

                double halfWidth = currentWidth / 2.0;

                double halfThickness = thickness / 2.0;

                Point3D p = points[i];

                // Cztery narożniki przekroju wstęgi.
                Point3D topLeft = p - side * halfWidth + normal * halfThickness;

                Point3D topRight = p + side * halfWidth + normal * halfThickness;

                Point3D bottomLeft = p - side * halfWidth - normal * halfThickness;

                Point3D bottomRight = p + side * halfWidth - normal * halfThickness;

                mesh.Positions.Add(topLeft);
                mesh.Positions.Add(topRight);
                mesh.Positions.Add(bottomLeft);
                mesh.Positions.Add(bottomRight);

                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(-normal);
                mesh.Normals.Add(-normal);
            }

            int ringCount = mesh.Positions.Count / 4;

            for (int ring = 0;
                 ring < ringCount - 1;
                 ring++)
            {
                int a = ring * 4;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;

                int na = (ring + 1) * 4;
                int nb = na + 1;
                int nc = na + 2;
                int nd = na + 3;

                // Góra
                AddQuad(mesh, a, na, b, nb);
                // Dół
                AddQuad(mesh, c, d, nc, nd);
                // Lewy bok
                AddQuad(mesh, a, c, na, nc);
                // Prawy bok
                AddQuad(mesh, b, nb, d, nd);
            }
        }
        private static void AddQuad(MeshGeometry3D mesh, int a, int b, int c, int d)
        {
            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);

            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(d);
        }
        private static List<Point3D> CreateCatmullRomSpline(List<Point3D> points, int subdivisions)
        {
            var result = new List<Point3D>();

            if (points.Count < 2)
                return result;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Point3D p0 = i == 0
                        ? points[i]
                        : points[i - 1];

                Point3D p1 = points[i];
                Point3D p2 = points[i + 1];

                Point3D p3 = i + 2 < points.Count
                        ? points[i + 2]
                        : points[i + 1];

                for (int step = 0; step < subdivisions; step++)
                {
                    double t = (double)step / subdivisions;

                    result.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }

            result.Add(points[^1]);

            return result;
        }
        private static Point3D CatmullRom(Point3D p0, Point3D p1, Point3D p2, Point3D p3, double t)
        {
            double t2 = t * t;
            double t3 = t2 * t;

            double x = 0.5 *
                (
                    2.0 * p1.X +
                    (-p0.X + p2.X) * t +
                    (2.0 * p0.X - 5.0 * p1.X +
                     4.0 * p2.X - p3.X) * t2 +
                    (-p0.X + 3.0 * p1.X -
                     3.0 * p2.X + p3.X) * t3
                );

            double y = 0.5 *
                (
                    2.0 * p1.Y +
                    (-p0.Y + p2.Y) * t +
                    (2.0 * p0.Y - 5.0 * p1.Y +
                     4.0 * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3.0 * p1.Y -
                     3.0 * p2.Y + p3.Y) * t3
                );

            double z = 0.5 *
                (
                    2.0 * p1.Z +
                    (-p0.Z + p2.Z) * t +
                    (2.0 * p0.Z - 5.0 * p1.Z +
                     4.0 * p2.Z - p3.Z) * t2 +
                    (-p0.Z + 3.0 * p1.Z -
                     3.0 * p2.Z + p3.Z) * t3
                );

            return new Point3D(x, y, z);
        }
        private static void AddTubeAlongPath(MeshGeometry3D mesh, List<Point3D> points, double radius, int sides)
        {
            if (points.Count < 2)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3D tangent;

                if (i == 0)
                {
                    tangent = points[1] - points[0];
                }
                else if (i == points.Count - 1)
                {
                    tangent = points[i] - points[i - 1];
                }
                else
                {
                    tangent = points[i + 1] - points[i - 1];
                }

                if (tangent.Length < 0.0001)
                    continue;

                tangent.Normalize();

                Vector3D reference = Math.Abs(tangent.Y) < 0.9
                        ? new Vector3D(0, 1, 0)
                        : new Vector3D(1, 0, 0);

                Vector3D u = Vector3D.CrossProduct(tangent, reference);

                if (u.Length < 0.0001)
                    continue;

                u.Normalize();

                Vector3D v = Vector3D.CrossProduct(tangent, u);

                v.Normalize();

                for (int side = 0; side < sides; side++)
                {
                    double angle = 2.0 * Math.PI * side / sides;

                    Vector3D normal = u * Math.Cos(angle) + v * Math.Sin(angle);

                    normal.Normalize();

                    var position = points[i] + normal * radius;

                    mesh.Positions.Add(position);
                    mesh.Normals.Add(normal);
                }
            }

            int ringCount = mesh.Positions.Count / sides;

            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int nextSide = (side + 1) % sides;

                    int a = ring * sides + side;

                    int b = ring * sides + nextSide;

                    int c = (ring + 1) * sides + side;

                    int d = (ring + 1) * sides + nextSide;

                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(c);
                    mesh.TriangleIndices.Add(b);

                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(c);
                    mesh.TriangleIndices.Add(d);
                }
            }
        }
    
    }
}
