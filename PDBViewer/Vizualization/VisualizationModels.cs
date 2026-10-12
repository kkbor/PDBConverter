using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace PDBViewer.Vizualization
{
    public enum VisualizationStyle
    {
        BallAndStick,
        SpheresOnly,
        Wireframe,
        Cartoon
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
        public Dictionary<Residues, ResidueGeometryInfo> ResidueInfo { get; }= new();
    }
}
