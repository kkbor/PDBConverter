// See https://aka.ms/new-console-template for more information
using PDBLibrary.Models;
using PDBLibrary.Parsers;
Console.Write("Podaj ścieżkę do pliku PDB: ");

string? path = Console.ReadLine();

if (string.IsNullOrWhiteSpace(path))
{
    Console.WriteLine("Nie podano ścieżki.");
    return;
}

if (!File.Exists(path))
{
    Console.WriteLine("Plik nie istnieje.");
    return;
}

PdbParsers parser = new();

Protein protein = parser.Load(path);

Console.WriteLine();

Console.WriteLine($"Liczba łańcuchów : {protein.Chains.Count}");

Console.WriteLine($"Liczba reszt : {protein.Chains.Sum(c => c.Residues.Count)}");

Console.WriteLine($"Liczba atomów : {protein.Chains.Sum(c => c.Residues.Sum(r => r.Atoms.Count))}");