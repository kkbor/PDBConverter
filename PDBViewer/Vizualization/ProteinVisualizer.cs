using HelixToolkit.Wpf;
using PDBLibrary.Models;
using PDBLibrary.Parsers;
using System;
using System.Collections.Generic;
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
    /// Zamienia model Protein na Model3DGroup gotowy do wyswietlenia w HelixViewport3D.
    /// </summary>
    public class ProteinVisualizer
    {
        // Kolorowanie CPK (standard w wizualizacji molekularnej)
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
            { "H", 0.25 }, { "C", 0.40 }, { "N", 0.40 }, { "O", 0.40 },
            { "S", 0.45 }, { "P", 0.45 }
        };

        private const double DefaultAtomRadius = 0.40;
        private const double BondRadius = 0.12;

        public Model3DGroup Build(Protein protein, VisualizationStyle style = VisualizationStyle.BallAndStick)
        {
            var group = new Model3DGroup();
            if (protein == null) return group;

            if (style != VisualizationStyle.Wireframe)
            {
                foreach (var chain in protein.Chains)
                {
                    foreach (var residue in chain.Residues)
                    {
                        foreach (var atom in residue.Atoms)
                        {
                            group.Children.Add(BuildAtomSphere(atom));
                        }
                    }
                }
            }

            if (style != VisualizationStyle.SpheresOnly)
            {
                var bonds = BondDetector.DetectBonds(protein);
                foreach (var bond in bonds)
                {
                    group.Children.Add(BuildBondCylinder(bond, style == VisualizationStyle.Wireframe));
                }
            }

            return group;
        }

        private GeometryModel3D BuildAtomSphere(Atoms atom)
        {
            var mesh = new MeshGeometry3D();

            string element = atom.Element?.ToUpperInvariant() ?? "";
            double radius = AtomRadii.TryGetValue(element, out double r) 
                ? r 
                : DefaultAtomRadius;
            // Liczba segmentów sfery.
            // Większa wartość = gładsza sfera.
            const int segments = 12;
            const int rings = 12; 
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
                    mesh.Normals.Add( new Vector3D(x, y, z));
                }
            }
            for (int ring = 0; ring < rings; ring++)
            { 
                for (int segment = 0; segment < segments; segment++)
                { 
                    int current = ring * (segments + 1) + segment;
                    int next = current + segments + 1;

                    mesh.TriangleIndices.Add(current); 
                    mesh.TriangleIndices.Add(next);
                    mesh.TriangleIndices.Add(current + 1);
                    
                    mesh.TriangleIndices.Add(current + 1); 
                    mesh.TriangleIndices.Add(next); 
                    mesh.TriangleIndices.Add(next + 1);
                } 
            }
            Color color = CpkColors.TryGetValue( element, out Color c) 
                ? c 
                : Colors.Magenta; var material = MaterialHelper.CreateMaterial(color);
            return new GeometryModel3D( mesh, material)
            { 
                BackMaterial = material 
            }; 
        }

        private GeometryModel3D BuildBondCylinder(Bonds bond, bool thin) 
        { 
            var mesh = new MeshGeometry3D();
            var p1 = new Point3D(bond.Atom1.X, bond.Atom1.Y, bond.Atom1.Z); 
            var p2 = new Point3D(bond.Atom2.X, bond.Atom2.Y, bond.Atom2.Z); 
            double radius = thin 
                ? 0.05 
                : BondRadius;
            
            AddCylinderMesh(mesh, p1, p2, radius, 8);
            var material = MaterialHelper.CreateMaterial(Colors.LightGray);
            return new GeometryModel3D(mesh, material) 
            { 
                BackMaterial = material
            }; 
        } 
       /// <summary> 
       /// Dodaje cylinder pomiędzy dwoma punktami.
       /// </summary> 
       private void AddCylinderMesh( MeshGeometry3D mesh, Point3D p1, Point3D p2, double radius, int segments)
        {
            Vector3D direction = p2 - p1;
            double length = direction.Length;
            // Punkty są takie same.
            if (length < 0.0001) 
                return; 

            direction.Normalize(); 
            // Tworzymy wektor prostopadły 
            // do kierunku cylindra.
            Vector3D reference;
            if (Math.Abs(direction.Y) < 0.9) 
            { 
                reference = new Vector3D(0, 1, 0);
            } else 
            { 
                reference = new Vector3D(1, 0, 0); 
            }
            Vector3D u = Vector3D.CrossProduct( direction, reference);
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct( direction, u);
            v.Normalize();
            // Indeksy pierwszego i drugiego pierścienia.
            int startIndex = mesh.Positions.Count; 
            
            for (int i = 0; i < segments; i++)
            { 
                double angle = 2.0 * Math.PI * i / segments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                Vector3D offset = u * (radius * cos) + v * (radius * sin); 
                Point3D point1 = p1 + offset; Point3D point2 = p2 + offset; 
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

                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(c);
                
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
            for (int i = 0; i < segments; i++) 
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