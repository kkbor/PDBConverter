using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Geometry
{
    /// <summary>Prosty wektor 3D double-precision, niezalezny od WPF (biblioteka ma byc UI-agnostic).</summary>
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator *(double s, Vec3 a) => new(s * a.X, s * a.Y, s * a.Z);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3 Normalized()
        {
            double len = Length;
            return len < 1e-9 ? this : new Vec3(X / len, Y / len, Z / len);
        }

        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    /// <summary>
    /// Macierz 3x3 reprezentowana jako trzy kolumny (wektory bazowe).
    /// Uzywana do obracania ukladu wspolrzednych szablonu aminokwasu
    /// na rzeczywisty szkielet (N, CA, C) w strukturze.
    /// </summary>
    public readonly struct Mat3
    {
        public readonly Vec3 Col0, Col1, Col2;

        public Mat3(Vec3 col0, Vec3 col1, Vec3 col2)
        {
            Col0 = col0;
            Col1 = col1;
            Col2 = col2;
        }

        public static Vec3 operator *(Mat3 m, Vec3 v) => new(
            m.Col0.X * v.X + m.Col1.X * v.Y + m.Col2.X * v.Z,
            m.Col0.Y * v.X + m.Col1.Y * v.Y + m.Col2.Y * v.Z,
            m.Col0.Z * v.X + m.Col1.Z * v.Y + m.Col2.Z * v.Z);

        public static Mat3 operator *(Mat3 a, Mat3 b) => new(a * b.Col0, a * b.Col1, a * b.Col2);

        /// <summary>Dla macierzy ortonormalnej transpozycja = odwrotnosc - duzo tansze niz inwersja ogolna.</summary>
        public Mat3 Transposed() => new(
            new Vec3(Col0.X, Col1.X, Col2.X),
            new Vec3(Col0.Y, Col1.Y, Col2.Y),
            new Vec3(Col0.Z, Col1.Z, Col2.Z));
    }
}

